Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Threading
Imports SharpDX
Imports SharpDX.Direct3D11
Imports SharpDX.DXGI
Imports SharpDX.Mathematics.Interop
Imports D3DDevice = SharpDX.Direct3D11.Device

''' <summary>
''' Ultra：原生录屏内核。
''' 采集走 Direct3D 11 桌面复制（画面全程在显存里，含系统光标，不会闪）；
''' 缩放与 YUV 转换走 D3D11 视频处理器（显卡完成）；
''' 编码走 Media Foundation 的 H.264，优先使用硬件编码器。不依赖任何外部程序。
'''
''' 注意：所有 D3D / Media Foundation 对象都在同一根采集线程里创建和释放。
''' 跨线程使用 COM 对象会因为单元（apartment）不匹配报 E_NOINTERFACE。
''' </summary>
Public Module UltraVideoNative

    Private Const PoolSize As Integer = 4

    '会话参数
    Private OutputFile As String = ""
    Private ArgLeft As Integer, ArgTop As Integer, ArgWidth As Integer, ArgHeight As Integer
    Private ArgTargetHeight As Integer, ArgFps As Integer, ArgBitrate As Integer
    Private ArgCodec As Integer

    '运行状态
    Private Worker As Thread
    Private StopFlag As Boolean
    Private Running As Boolean
    Private FailureMessage As String = ""
    Private EncoderDesc As String = ""
    Private InitSignal As New ManualResetEventSlim(False)

    ''' <summary>录制线程意外结束（编码器出错、桌面复制中断等）。参数是原因。</summary>
    Public Event CaptureFailed As EventHandler

    '线程内对象
    Private Device As D3DDevice
    Private Context As SharpDX.Direct3D11.DeviceContext
    Private VideoDevice As SharpDX.Direct3D11.VideoDevice
    Private VideoContext As SharpDX.Direct3D11.VideoContext
    Private Enumerator As VideoProcessorEnumerator
    Private Processor As VideoProcessor
    Private Duplication As OutputDuplication
    Private OutputDesc As OutputDescription
    Private InputView As VideoProcessorInputView
    Private OutTextures As Texture2D()
    Private OutViews As VideoProcessorOutputView()
    Private Writer As IMFSinkWriter
    Private D3DManager As IMFDXGIDeviceManager
    Private PoolIndex As Integer
    Private StagingTexture As Texture2D
    Private UseCpuSamples As Boolean = False
    '用来确认显卡真的把这一帧画完了，再释放桌面帧（Map 会阻塞到绘制完成）
    Private SyncTexture As Texture2D = Nothing
    Private LastOutputTexture As Texture2D = Nothing

    '声音：WASAPI 回环。所有 COM 对象都跟视频一样在采集线程里创建/使用/释放，
    '不另开线程，避免单元不匹配，也让音频时间戳和视频共用同一个时钟起点。
    Private ArgRecordAudio As Boolean = False
    Private AudioWanted As Boolean = False
    Private AudioActive As Boolean = False
    Private AudioStreamIndex As Integer = -1
    Private AudioRate As Integer = 0
    Private AudioWaveChannels As Integer = 0
    Private AudioWaveBits As Integer = 0
    Private AudioWaveFloat As Boolean = False
    Private AudioFramesWritten As Long = 0
    Private AudioMixFormat As IntPtr = IntPtr.Zero
    Private AudioEnumerator As UltraWasapi.IMMDeviceEnumerator = Nothing
    Private AudioEndpoint As UltraWasapi.IMMDevice = Nothing
    Private AudioClient As UltraWasapi.IAudioClient = Nothing
    Private AudioCapture As UltraWasapi.IAudioCaptureClient = Nothing
    Private AudioPollFailed As Boolean = False

    Private CropLeft As Integer, CropTop As Integer
    Private SrcWidth As Integer, SrcHeight As Integer
    Private OutWidth As Integer, OutHeightPx As Integer
    Private FrameRate As Integer, BitrateMbps As Integer
    Private ActualFps As Integer
    Private ActualCodecValue As Integer
    Private ReadOnly SpecCache As New Dictionary(Of String, Boolean)

    ''' <summary>是否正在录制。</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Running
        End Get
    End Property

    ''' <summary>上一次失败的原因。</summary>
    Public ReadOnly Property LastError As String
        Get
            Return FailureMessage
        End Get
    End Property

    ''' <summary>实际使用的编码格式：0 = H.264，1 = H.265。</summary>
    Public ReadOnly Property ActualCodec As Integer
        Get
            Return ActualCodecValue
        End Get
    End Property

    ''' <summary>实际使用的帧率（可能因为分辨率太高被自动降低）。</summary>
    Public ReadOnly Property ActualFrameRate As Integer
        Get
            Return ActualFps
        End Get
    End Property

    ''' <summary>当前使用的编码参数说明。</summary>
    Public ReadOnly Property EncoderName As String
        Get
            Return EncoderDesc
        End Get
    End Property

    ''' <summary>本次录制是否真的写入了系统声音（设备不可用或音频流建立失败时为 False）。</summary>
    Public ReadOnly Property IsAudioActive As Boolean
        Get
            Return AudioActive
        End Get
    End Property

#Region "自检"

    ''' <summary>
    ''' 自检：创建 D3D11 设备、枚举显卡、尝试桌面复制与编码器。返回一行说明。
    ''' </summary>
    Public Function Probe() As String
        Dim Result As String = ""
        Dim TestDevice As D3DDevice = Nothing
        Dim Dup As OutputDuplication = Nothing
        Try
            TestDevice = New D3DDevice(SharpDX.Direct3D.DriverType.Hardware,
                DeviceCreationFlags.BgraSupport Or DeviceCreationFlags.VideoSupport)
            Dim DxgiDev As SharpDX.DXGI.Device = TestDevice.QueryInterface(Of SharpDX.DXGI.Device)()
            Dim Adapter As Adapter = DxgiDev.Adapter
            Result = "显卡：" & Adapter.Description.Description.Trim()
            Dim Out As SharpDX.DXGI.Output = FindOutput(Adapter, Nothing)
            If Out Is Nothing Then Return Result & "；没有已连接的显示器"
            Dim Desc As OutputDescription = Out.Description
            Result &= $"；输出：{Desc.DeviceName} {Desc.DesktopBounds.Right - Desc.DesktopBounds.Left}×{Desc.DesktopBounds.Bottom - Desc.DesktopBounds.Top}"
            Dim Out1 As Output1 = Out.QueryInterface(Of Output1)()
            Dup = Out1.DuplicateOutput(TestDevice)
            Result &= "；桌面复制：可用"
            Result &= "；" & ProbeEncoder()
        Catch ex As Exception
            Result &= "；异常：" & ex.GetType().Name & " " & ex.Message
        Finally
            If Dup IsNot Nothing Then Dup.Dispose()
            If TestDevice IsNot Nothing Then TestDevice.Dispose()
        End Try
        Return Result
    End Function

    ''' <summary>自检系统里 H.264 / H.265 编码器是否可用。</summary>
    Private Function ProbeEncoder() As String
        Return $"H.264：{If(ProbeSpec(1280, 720, 30, 0), "可用", "不可用")}；H.265：{If(ProbeSpec(1280, 720, 30, 1), "可用", "不可用")}"
    End Function

    ''' <summary>
    ''' 这个「分辨率 × 帧率 × 编码」编码器能不能吃下来。结果会缓存。
    ''' Height 传 0 表示跟随原画（按屏幕高度算）。
    ''' </summary>
    Public Function IsSupported(Height As Integer, Fps As Integer, Codec As Integer) As Boolean
        Try
            Dim H As Integer = If(Height > 0, Height, ScreenHeight)
            H = H - (H Mod 2)
            Dim W As Integer = Math.Max(16, CInt(Math.Round(ScreenWidth * H / ScreenHeight / 2)) * 2)
            Dim Key As String = $"{Codec}|{W}x{H}@{Fps}"
            SyncLock SpecCache
                Dim Cached As Boolean
                If SpecCache.TryGetValue(Key, Cached) Then Return Cached
            End SyncLock
            Dim Ok As Boolean = ProbeSpec(W, H, Fps, Codec)
            SyncLock SpecCache
                SpecCache(Key) = Ok
            End SyncLock
            Return Ok
        Catch ex As Exception
            Logger.Warn(ex, "检测编码能力失败")
            Return True
        End Try
    End Function

    ''' <summary>试着用指定规格建一个 SinkWriter。</summary>
    Private ReadOnly ProbeLock As New Object()

    Private Function ProbeSpec(Width As Integer, Height As Integer, Fps As Integer, Codec As Integer) As Boolean
        '多个探测线程会同时进来，串行化 + 各用各的临时文件，否则会互相删文件导致误判
        SyncLock ProbeLock
            Return ProbeSpecInner(Width, Height, Fps, Codec)
        End SyncLock
    End Function

    Private Function ProbeSpecInner(Width As Integer, Height As Integer, Fps As Integer, Codec As Integer) As Boolean
        Dim TempFile As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pcl_video_probe_" & Guid.NewGuid().ToString("N") & ".mp4")
        Dim Attr As IMFAttributes = Nothing
        Dim W As IMFSinkWriter = Nothing
        Dim OutType As IMFMediaType = Nothing
        Try
            If MFStartup_() <> 0 Then Return False
            Dim Hr = UltraMf.MFCreateAttributes(Attr, 3)
            If Hr <> 0 Then Return False
            SetUInt(Attr, UltraMf.KeyEnableHardwareTransforms, 1)
            Hr = UltraMf.MFCreateMediaType(OutType)
            If Hr <> 0 Then Return False
            SetGuid(OutType, UltraMf.KeyMajorType, UltraMf.GuidVideo)
            SetGuid(OutType, UltraMf.KeySubType, If(Codec = 1, UltraMf.GuidHevc, UltraMf.GuidH264))
            SetUInt(OutType, UltraMf.KeyAvgBitrate, 8000000)
            SetUInt(OutType, UltraMf.KeyInterlaceMode, 2)
            SetUInt64(OutType, UltraMf.KeyFrameSize, MakeLong(Width, Height))
            SetUInt64(OutType, UltraMf.KeyFrameRate, MakeLong(Fps, 1))
            SetUInt64(OutType, UltraMf.KeyPixelAspect, MakeLong(1, 1))
            Hr = UltraMf.MFCreateSinkWriterFromURL(TempFile, IntPtr.Zero, Attr, W)
            If Hr <> 0 Then Return False
            Dim Index As Integer = 0
            Hr = W.AddStream(OutType, Index)
            If Hr <> 0 Then Return False
            '关键：真正决定能不能录的是 SetInputMediaType（分辨率×帧率超限时这里返回 0xC00D36B4）
            Dim InType As IMFMediaType = Nothing
            Hr = UltraMf.MFCreateMediaType(InType)
            If Hr <> 0 Then Return False
            Try
                SetGuid(InType, UltraMf.KeyMajorType, UltraMf.GuidVideo)
                SetGuid(InType, UltraMf.KeySubType, UltraMf.GuidNv12)
                SetUInt(InType, UltraMf.KeyInterlaceMode, 2)
                SetUInt64(InType, UltraMf.KeyFrameSize, MakeLong(Width, Height))
                SetUInt64(InType, UltraMf.KeyFrameRate, MakeLong(Fps, 1))
                SetUInt64(InType, UltraMf.KeyPixelAspect, MakeLong(1, 1))
                Return W.SetInputMediaType(0, InType, Nothing) = 0
            Finally
                Marshal.ReleaseComObject(InType)
            End Try
        Catch ex As Exception
            Logger.Warn(ex, "检测编码规格失败")
            Return False
        Finally
            If W IsNot Nothing Then
                Try
                    W.FinalizeWriter()
                Catch
                End Try
                Marshal.ReleaseComObject(W)
            End If
            If OutType IsNot Nothing Then Marshal.ReleaseComObject(OutType)
            If Attr IsNot Nothing Then Marshal.ReleaseComObject(Attr)
            Try
                If System.IO.File.Exists(TempFile) Then System.IO.File.Delete(TempFile)
            Catch
            End Try
        End Try
    End Function

    Private Function MFStartup_() As Integer
        Static Started As Boolean = False
        Static Hr As Integer = 0
        If Not Started Then
            Hr = UltraMf.MFStartup(UltraMf.MfVersion, 0)
            Started = True
        End If
        Return Hr
    End Function

#End Region

#Region "启动 / 停止"

    ''' <summary>
    ''' 开始录制。返回空字符串表示成功，否则是错误原因。
    ''' </summary>
    Public Function StartRecording(File As String, Left As Integer, Top As Integer, Width As Integer,
                                   Height As Integer, TargetHeight As Integer, Fps As Integer, Bitrate As Integer,
                                   Codec As Integer, Optional RecordAudio As Boolean = False) As String
        If Running Then Return "已经在录制了"
        FailureMessage = ""
        EncoderDesc = ""
        UseCpuSamples = False
        StopFlag = False
        ArgRecordAudio = RecordAudio
        AudioWanted = RecordAudio
        AudioActive = False
        AudioStreamIndex = -1
        AudioFramesWritten = 0
        AudioPollFailed = False
        OutputFile = File
        ArgLeft = Left
        ArgTop = Top
        ArgWidth = Width
        ArgHeight = Height
        ArgTargetHeight = TargetHeight
        ArgFps = Fps
        ArgBitrate = Bitrate
        ArgCodec = Codec
        InitSignal.Reset()
        Worker = New Thread(AddressOf WorkerMain) With {
            .IsBackground = True, .Priority = ThreadPriority.AboveNormal, .Name = "VideoNative"}
        Try
            Worker.SetApartmentState(ApartmentState.MTA)
        Catch
        End Try
        Worker.Start()
        If Not InitSignal.Wait(8000) Then
            StopFlag = True
            FailureMessage = "录制内核初始化超时"
            Return FailureMessage
        End If
        If FailureMessage <> "" Then Return FailureMessage
        Return ""
    End Function

    ''' <summary>结束录制。必须在后台线程调用（会等待编码器收尾）。</summary>
    Public Sub StopRecording()
        StopFlag = True
        If Worker IsNot Nothing Then
            Try
                Worker.Join(6000)
            Catch
            End Try
            Worker = Nothing
        End If
        Running = False
    End Sub

    Private Sub WorkerMain()
        Dim Ok As Boolean = False
        Try
            Ok = InitEngine()
        Catch ex As Exception
            FailureMessage = ex.Message
            Logger.Error(ex, "初始化录制内核失败")
        End Try
        If Not Ok Then
            If FailureMessage = "" Then FailureMessage = "录制内核初始化失败"
            Cleanup()
            InitSignal.Set()
            Return
        End If
        Running = True
        InitSignal.Set()
        Try
            CaptureLoop()
        Catch ex As Exception
            FailureMessage = ex.Message
            Logger.Error(ex, "录制线程异常退出")
        End Try
        Running = False
        Cleanup()
        If FailureMessage <> "" Then
            Logger.Warn("录制意外结束：" & FailureMessage)
            Try
                RaiseEvent CaptureFailed(Nothing, EventArgs.Empty)
            Catch
            End Try
        End If
    End Sub

    Private Function InitEngine() As Boolean
        '1. 设备
        Device = New D3DDevice(SharpDX.Direct3D.DriverType.Hardware,
            DeviceCreationFlags.BgraSupport Or DeviceCreationFlags.VideoSupport)
        Context = Device.ImmediateContext

        '2. 找到覆盖录制区域的显示器
        Dim DxgiDev As SharpDX.DXGI.Device = Device.QueryInterface(Of SharpDX.DXGI.Device)()
        Dim Out As SharpDX.DXGI.Output = FindOutput(DxgiDev.Adapter,
            New RawRectangle(ArgLeft, ArgTop, ArgLeft + ArgWidth, ArgTop + ArgHeight))
        If Out Is Nothing Then Throw New Exception("找不到覆盖录制区域的显示器")
        OutputDesc = Out.Description
        Dim Out1 As Output1 = Out.QueryInterface(Of Output1)()
        Duplication = Out1.DuplicateOutput(Device)

        '3. 裁切与输出尺寸
        CropLeft = Math.Max(0, ArgLeft - OutputDesc.DesktopBounds.Left)
        CropTop = Math.Max(0, ArgTop - OutputDesc.DesktopBounds.Top)
        SrcWidth = Math.Min(ArgWidth, OutputDesc.DesktopBounds.Right - OutputDesc.DesktopBounds.Left - CropLeft)
        SrcHeight = Math.Min(ArgHeight, OutputDesc.DesktopBounds.Bottom - OutputDesc.DesktopBounds.Top - CropTop)
        If SrcWidth < 16 OrElse SrcHeight < 16 Then Throw New Exception($"录制区域太小（{SrcWidth}×{SrcHeight}）")
        FrameRate = Math.Max(10, Math.Min(240, ArgFps))
        BitrateMbps = Math.Max(1, Math.Min(200, ArgBitrate))
        If ArgTargetHeight > 0 Then
            '选了多少就输出多少，哪怕比屏幕大（由视频处理器放大）
            OutHeightPx = ArgTargetHeight - (ArgTargetHeight Mod 2)
            OutWidth = CInt(Math.Round(SrcWidth * OutHeightPx / SrcHeight / 2)) * 2
        Else
            OutWidth = SrcWidth - (SrcWidth Mod 2)
            OutHeightPx = SrcHeight - (SrcHeight Mod 2)
        End If

        '4. 视频处理器：裁切 + 缩放 + BGRA→NV12 全在显卡上做
        VideoDevice = Device.QueryInterface(Of SharpDX.Direct3D11.VideoDevice)()
        VideoContext = Context.QueryInterface(Of SharpDX.Direct3D11.VideoContext)()
        Dim Content As New VideoProcessorContentDescription
        Content.InputFrameFormat = VideoFrameFormat.Progressive
        Content.InputFrameRate = New SharpDX.DXGI.Rational(FrameRate, 1)
        '输入纹理是「整块桌面」，不是裁出来的那一小块：
        '这里必须写显示器的全尺寸，裁切交给下面的 StreamSourceRect。
        '写成 SrcWidth / SrcHeight 会让裁切坐标落在这个假想尺寸之外，录出来是大片绿色。
        Content.InputWidth = OutputDesc.DesktopBounds.Right - OutputDesc.DesktopBounds.Left
        Content.InputHeight = OutputDesc.DesktopBounds.Bottom - OutputDesc.DesktopBounds.Top
        Content.OutputFrameRate = New SharpDX.DXGI.Rational(FrameRate, 1)
        Content.OutputWidth = OutWidth
        Content.OutputHeight = OutHeightPx
        Content.Usage = VideoUsage.PlaybackNormal
        Dim EnumOut As VideoProcessorEnumerator = Nothing
        VideoDevice.CreateVideoProcessorEnumerator(Content, EnumOut)
        Enumerator = EnumOut
        Dim ProcOut As VideoProcessor = Nothing
        VideoDevice.CreateVideoProcessor(Enumerator, 0, ProcOut)
        Processor = ProcOut
        VideoContext.VideoProcessorSetStreamSourceRect(Processor, 0, True,
            New RawRectangle(CropLeft, CropTop, CropLeft + SrcWidth, CropTop + SrcHeight))
        VideoContext.VideoProcessorSetStreamDestRect(Processor, 0, True,
            New RawRectangle(0, 0, OutWidth, OutHeightPx))
        VideoContext.VideoProcessorSetOutputTargetRect(Processor, True, New RawRectangle(0, 0, OutWidth, OutHeightPx))

        '0. 同步用的小纹理：区域录制要缩放，Blt 耗时更长，
        '必须等它画完再放掉桌面帧，否则录出来是大片绿色（NV12 全零）
        SyncTexture = New Texture2D(Device, New Texture2DDescription With {
            .Width = 16, .Height = 16, .MipLevels = 1, .ArraySize = 1,
            .Format = Format.NV12, .SampleDescription = New SampleDescription(1, 0),
            .Usage = ResourceUsage.Staging, .BindFlags = BindFlags.None,
            .CpuAccessFlags = CpuAccessFlags.Read, .OptionFlags = ResourceOptionFlags.None})

        '5. 输出纹理池
        OutTextures = New Texture2D(PoolSize - 1) {}
        OutViews = New VideoProcessorOutputView(PoolSize - 1) {}
        Dim TexDesc As New Texture2DDescription With {
            .Width = OutWidth, .Height = OutHeightPx, .MipLevels = 1, .ArraySize = 1,
            .Format = Format.NV12, .SampleDescription = New SampleDescription(1, 0),
            .Usage = ResourceUsage.Default, .BindFlags = BindFlags.RenderTarget,
            .CpuAccessFlags = CpuAccessFlags.None, .OptionFlags = ResourceOptionFlags.None}
        Dim OutDesc As New VideoProcessorOutputViewDescription
        OutDesc.Dimension = VpovDimension.Texture2D
        OutDesc.Texture2D = New Texture2DVpov With {.MipSlice = 0}
        For i As Integer = 0 To PoolSize - 1
            OutTextures(i) = New Texture2D(Device, TexDesc)
            Dim ViewOut As VideoProcessorOutputView = Nothing
            VideoDevice.CreateVideoProcessorOutputView(OutTextures(i), Enumerator, OutDesc, ViewOut)
            OutViews(i) = ViewOut
        Next

        '6. 编码器
        Dim Hr As Integer = MFStartup_()
        If Hr <> 0 Then Throw New Exception($"Media Foundation 初始化失败（0x{Hr:X8}）")

        '音频媒体类型要用真实的采样率/声道数，所以声音必须比编码器先准备好。
        '任何一步失败都只降级为「仅录画面」，绝不让整个录制失败。
        If AudioWanted Then
            Try
                InitAudio()
            Catch ex As Exception
                Logger.Warn(ex, "初始化声音回环失败，本次仅录画面")
                StopAudioClient()
                ReleaseAudio()
                AudioWanted = False
            End Try
        End If

        Dim Attr As IMFAttributes = Nothing
        Hr = UltraMf.MFCreateAttributes(Attr, 3)
        If Hr <> 0 Then Throw New Exception($"创建属性失败（0x{Hr:X8}）")
        SetUInt(Attr, UltraMf.KeyEnableHardwareTransforms, 1)
        Hr = UltraMf.MFCreateDXGIDeviceManager(0, D3DManager)
        If Hr = 0 Then
            If D3DManager.ResetDevice(Device.NativePointer, 0) = 0 Then
                SetUnknown(Attr, UltraMf.KeySinkWriterD3DManager, D3DManager)
            End If
        End If

        '这个分辨率带不动这么高的帧率时（H.264 / H.265 对「分辨率 × 帧率」都有上限），自动往下降；
        '选的编码格式不可用时，也会自动退回另一种
        Dim Candidates As Integer() = {FrameRate, 120, 90, 60, 48, 30, 24}
        Dim Codecs As Integer() = If(ArgCodec = 1, New Integer() {1, 0}, New Integer() {0, 1})
        Dim LastHr As Integer = 0
        Dim UsedFps As Integer = 0
        Dim UsedCodec As Integer = 0
        For Each Codec As Integer In Codecs
            For Each Candidate As Integer In Candidates
                If Candidate > FrameRate Then Continue For
                LastHr = CreateEncoder(Attr, Candidate, Codec)
                If Writer IsNot Nothing Then
                    UsedFps = Candidate
                    UsedCodec = Codec
                    Exit For
                End If
                Logger.Warn($"编码器不接受 {OutWidth}×{OutHeightPx}@{Candidate}/{If(Codec = 1, "H.265", "H.264")}（0x{LastHr:X8}），尝试更低参数")
            Next
            If Writer IsNot Nothing Then Exit For
        Next
        Marshal.ReleaseComObject(Attr)
        If Writer Is Nothing Then
            Throw New Exception($"当前分辨率下编码器无法工作（0x{LastHr:X8}），请降低分辨率")
        End If
        FrameRate = UsedFps
        ActualFps = UsedFps
        ActualCodecValue = UsedCodec

        EncoderDesc = $"{OutWidth}×{OutHeightPx} · {FrameRate} FPS · {BitrateMbps} Mbps · {If(UsedCodec = 1, "H.265", "H.264")}" &
                      If(AudioActive, " · 系统声音", "")
        Return True
    End Function

    ''' <summary>按指定帧率创建编码器。成功时 Writer 会被赋值，返回 0。</summary>
    Private Function CreateEncoder(Attr As IMFAttributes, Fps As Integer, Codec As Integer) As Integer
        Dim OutType As IMFMediaType = Nothing
        Dim InType As IMFMediaType = Nothing
        Dim W As IMFSinkWriter = Nothing
        Try
            Dim Hr = UltraMf.MFCreateMediaType(OutType)
            If Hr <> 0 Then Return Hr
            SetGuid(OutType, UltraMf.KeyMajorType, UltraMf.GuidVideo)
            SetGuid(OutType, UltraMf.KeySubType, If(Codec = 1, UltraMf.GuidHevc, UltraMf.GuidH264))
            SetUInt(OutType, UltraMf.KeyAvgBitrate, BitrateMbps * 1000000)
            SetUInt(OutType, UltraMf.KeyInterlaceMode, 2)
            SetUInt64(OutType, UltraMf.KeyFrameSize, MakeLong(OutWidth, OutHeightPx))
            SetUInt64(OutType, UltraMf.KeyFrameRate, MakeLong(Fps, 1))
            SetUInt64(OutType, UltraMf.KeyPixelAspect, MakeLong(1, 1))

            Hr = UltraMf.MFCreateSinkWriterFromURL(OutputFile, IntPtr.Zero, Attr, W)
            If Hr <> 0 Then Return Hr
            Dim StreamIndex As Integer = 0
            Hr = W.AddStream(OutType, StreamIndex)
            If Hr <> 0 Then Return Hr

            Hr = UltraMf.MFCreateMediaType(InType)
            If Hr <> 0 Then Return Hr
            SetGuid(InType, UltraMf.KeyMajorType, UltraMf.GuidVideo)
            SetGuid(InType, UltraMf.KeySubType, UltraMf.GuidNv12)
            SetUInt(InType, UltraMf.KeyInterlaceMode, 2)
            SetUInt64(InType, UltraMf.KeyFrameSize, MakeLong(OutWidth, OutHeightPx))
            SetUInt64(InType, UltraMf.KeyFrameRate, MakeLong(Fps, 1))
            SetUInt64(InType, UltraMf.KeyPixelAspect, MakeLong(1, 1))
            Hr = W.SetInputMediaType(0, InType, Nothing)
            If Hr <> 0 Then Return Hr

            '音频流必须加在 BeginWriting 之前，而且加进去之后没法从 Writer 里摘掉。
            '所以一旦失败就返回错误，让外层重建一个纯视频的 Writer，而不是留个哑流。
            AudioActive = False
            If AudioWanted Then
                Dim AudioHr As Integer = SetupAudioStream(W)
                If AudioHr <> 0 Then
                    Logger.Warn($"音频流建立失败（0x{AudioHr:X8}），本次仅录画面")
                    StopAudioClient()
                    ReleaseAudio()
                    Return AudioHr
                End If
            End If
            Hr = W.BeginWriting()
            If Hr <> 0 Then Return Hr
            If AudioWanted Then
                Dim AudioHr As Integer = AudioClient.Start()
                If AudioHr <> 0 Then
                    Logger.Warn($"启动声音采集失败（0x{AudioHr:X8}），本次仅录画面")
                    StopAudioClient()
                    ReleaseAudio()
                    Return AudioHr
                End If
                AudioActive = True
            End If

            Writer = W
            W = Nothing
            Return 0
        Finally
            If W IsNot Nothing Then
                Try
                    Marshal.ReleaseComObject(W)
                Catch
                End Try
            End If
            If OutType IsNot Nothing Then Marshal.ReleaseComObject(OutType)
            If InType IsNot Nothing Then Marshal.ReleaseComObject(InType)
        End Try
    End Function

    Private Sub Cleanup()
        '收尾顺序：写掉最后一批声音 → 停采集 → 封口文件 → 释放所有 COM 对象
        If Writer IsNot Nothing AndAlso AudioActive Then PollAudio()
        StopAudioClient()
        If Writer IsNot Nothing Then
            Try
                Writer.FinalizeWriter()
            Catch ex As Exception
                Logger.Warn(ex, "结束录制文件失败")
            End Try
            Try
                Marshal.ReleaseComObject(Writer)
            Catch
            End Try
            Writer = Nothing
        End If
        ReleaseAudio()
        If OutViews IsNot Nothing Then
            For i As Integer = 0 To OutViews.Length - 1
                If OutViews(i) IsNot Nothing Then OutViews(i).Dispose()
            Next
            OutViews = Nothing
        End If
        If OutTextures IsNot Nothing Then
            For i As Integer = 0 To OutTextures.Length - 1
                If OutTextures(i) IsNot Nothing Then OutTextures(i).Dispose()
            Next
            OutTextures = Nothing
        End If
        If StagingTexture IsNot Nothing Then
            Try
                StagingTexture.Dispose()
            Catch
            End Try
            StagingTexture = Nothing
        End If
        If SyncTexture IsNot Nothing Then
            Try
                SyncTexture.Dispose()
            Catch
            End Try
            SyncTexture = Nothing
        End If
        If InputView IsNot Nothing Then
            Try
                InputView.Dispose()
            Catch
            End Try
            InputView = Nothing
        End If
        If Processor IsNot Nothing Then
            Processor.Dispose()
            Processor = Nothing
        End If
        If Enumerator IsNot Nothing Then
            Enumerator.Dispose()
            Enumerator = Nothing
        End If
        If Duplication IsNot Nothing Then
            Try
                Duplication.ReleaseFrame()
            Catch
            End Try
            Try
                Duplication.Dispose()
            Catch
            End Try
            Duplication = Nothing
        End If
        If D3DManager IsNot Nothing Then
            Try
                Marshal.ReleaseComObject(D3DManager)
            Catch
            End Try
            D3DManager = Nothing
        End If
        If VideoContext IsNot Nothing Then
            Try
                VideoContext.Dispose()
            Catch
            End Try
            VideoContext = Nothing
        End If
        If VideoDevice IsNot Nothing Then
            Try
                VideoDevice.Dispose()
            Catch
            End Try
            VideoDevice = Nothing
        End If
        If Context IsNot Nothing Then
            Try
                Context.Dispose()
            Catch
            End Try
            Context = Nothing
        End If
        If Device IsNot Nothing Then
            Try
                Device.Dispose()
            Catch
            End Try
            Device = Nothing
        End If
    End Sub

#End Region

#Region "采集循环"

    Private Sub CaptureLoop()
        Dim Clock As Stopwatch = Stopwatch.StartNew()
        Dim IntervalMs As Double = 1000.0 / FrameRate
        Dim LastWriteMs As Double = -1000
        Dim LastTime As Long = 0
        Dim FrameDuration As Long = 10000000L \ FrameRate
        Dim FailCount As Integer = 0
        While Not StopFlag
            '先排空音频包。TryAcquireNextFrame 最多等 120ms，回环缓冲有 200ms；
            '放在循环开头能覆盖后面所有 continue（超时、重建桌面复制、跳帧）。
            PollAudio()
            Dim Resource As SharpDX.DXGI.Resource = Nothing
            Dim Info As OutputDuplicateFrameInformation
            Dim Result As Result
            Try
                Result = Duplication.TryAcquireNextFrame(120, Info, Resource)
            Catch ex As Exception
                FailureMessage = "等待桌面帧失败：" & ex.Message
                Exit While
            End Try
            If Result.Failure Then
                If CLng(Result.Code) = -2005270489L Then Continue While 'DXGI_ERROR_WAIT_TIMEOUT
                If CLng(Result.Code) = -2005270494L Then 'DXGI_ERROR_ACCESS_LOST
                    Try
                        Duplication.Dispose()
                    Catch
                    End Try
                    Thread.Sleep(250)
                    Try
                        Dim DxgiDev As SharpDX.DXGI.Device = Device.QueryInterface(Of SharpDX.DXGI.Device)()
                        Dim Out As SharpDX.DXGI.Output = FindOutput(DxgiDev.Adapter,
                            New RawRectangle(ArgLeft, ArgTop, ArgLeft + 1, ArgTop + 1))
                        Dim Out1 As Output1 = Out.QueryInterface(Of Output1)()
                        Duplication = Out1.DuplicateOutput(Device)
                        FailCount = 0
                    Catch ex As Exception
                        Logger.Warn(ex, "重建桌面复制失败")
                        FailCount += 1
                        If FailCount > 8 Then
                            FailureMessage = "桌面复制中断（可能切换了分辨率或进入了独占全屏）"
                            Exit While
                        End If
                    End Try
                End If
                Continue While
            End If

            Dim NowMs As Double = Clock.Elapsed.TotalMilliseconds
            If NowMs - LastWriteMs < IntervalMs - 3 Then
                Resource.Dispose()
                Try
                    Duplication.ReleaseFrame()
                Catch
                End Try
                Continue While
            End If

            Try
                Dim Source As Texture2D = Resource.QueryInterface(Of Texture2D)
                PushFrame(Source, NowMs, LastTime, FrameDuration)
                LastWriteMs = NowMs
                LastTime = CLng(NowMs * 10000.0)
            Catch ex As Exception
                Logger.Error(ex, "写入录制帧失败")
                FailCount += 1
            Finally
                If Resource IsNot Nothing Then Resource.Dispose()
                Try
                    Duplication.ReleaseFrame()
                Catch
                End Try
            End Try
            If FailCount > 30 Then
                FailureMessage = "连续写入失败，录制已停止"
                Exit While
            End If
        End While
    End Sub

    ''' <summary>
    ''' 等待显卡把已提交的绘制真正做完。
    ''' 做法：把输出纹理左上角一小块拷进 staging 纹理，再 Map 读它——
    ''' D3D11 会在 Map 时等前面所有绘制完成，等价于一次轻量同步。
    ''' </summary>
    Private Sub WaitForGpu()
        If SyncTexture Is Nothing Then Return
        Try
            Context.CopySubresourceRegion(SyncTexture, 0, New ResourceRegion(0, 0, 0, 16, 16, 1),
                                          LastOutputTexture, 0)
            Context.MapSubresource(SyncTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None)
            Context.UnmapSubresource(SyncTexture, 0)
        Catch ex As Exception
            Logger.Warn(ex, "等待显卡绘制完成失败，本次录制改为不同步")
            Try
                SyncTexture.Dispose()
            Catch
            End Try
            SyncTexture = Nothing
        End Try
    End Sub

    Private Sub PushFrame(Source As Texture2D, NowMs As Double, ByRef LastTime As Long, FrameDuration As Long)
        '输入视图必须绑定固定纹理，每帧按当前桌面纹理重建一次（很便宜）
        If InputView IsNot Nothing Then
            Try
                InputView.Dispose()
            Catch
            End Try
            InputView = Nothing
        End If
        Dim InDesc As New VideoProcessorInputViewDescription
        InDesc.FourCC = 0
        InDesc.Dimension = VpivDimension.Texture2D
        InDesc.Texture2D = New Texture2DVpiv With {.MipSlice = 0, .ArraySlice = 0}
        Dim ViewOut As VideoProcessorInputView = Nothing
        VideoDevice.CreateVideoProcessorInputView(Source, Enumerator, InDesc, ViewOut)
        InputView = ViewOut

        Dim Index As Integer = PoolIndex
        PoolIndex = (PoolIndex + 1) Mod PoolSize

        Dim OneStream As New VideoProcessorStream
        OneStream.Enable = True
        OneStream.OutputIndex = 0
        OneStream.InputFrameOrField = 0
        OneStream.PastFrames = 0
        OneStream.FutureFrames = 0
        OneStream.PInputSurface = InputView.NativePointer
        LastOutputTexture = OutTextures(Index)
        VideoContext.VideoProcessorBlt(Processor, OutViews(Index), 0, 1,
                                       New VideoProcessorStream() {OneStream})

        Dim NowTime As Long = CLng(NowMs * 10000.0)
        Dim Duration As Long = If(LastTime > 0, Math.Max(1000, NowTime - LastTime), FrameDuration)

        '回读模式也必须先 Blt：这一步负责把桌面画面画进输出纹理，
        '跳过它只会读到纹理池里的旧数据（表现为整段视频纯绿）。
        '回读里的 Map 本身就会等显卡，所以不需要额外同步。
        If UseCpuSamples Then
            WriteSampleFromCpu(OutTextures(Index), NowTime, Duration)
            Return
        End If

        '显存样本不会再读回内存，必须自己确认这一帧画完了，
        '否则调用方随后 ReleaseFrame 会把还没读完的桌面纹理放掉。
        WaitForGpu()

        Dim Buffer As IMFMediaBuffer = Nothing
        Dim Hr = UltraMf.MFCreateDXGISurfaceBuffer(UltraMf.IidTexture2D, OutTextures(Index).NativePointer, 0, False, Buffer)
        If Hr <> 0 Then Throw New Exception($"创建帧缓冲失败（0x{Hr:X8}）")
        Dim Sample As IMFSample = Nothing
        Hr = UltraMf.MFCreateSample(Sample)
        If Hr <> 0 Then Throw New Exception($"创建帧失败（0x{Hr:X8}）")
        Sample.AddBuffer(Buffer)
        Sample.SetSampleTime(NowTime)
        Sample.SetSampleDuration(Duration)
        Hr = Writer.WriteSample(0, Sample)
        Marshal.ReleaseComObject(Sample)
        Marshal.ReleaseComObject(Buffer)
        If Hr = 0 Then Return
        If Hr <> &H80070057 AndAlso Hr <> &H80004005 Then Throw New Exception($"编码失败（0x{Hr:X8}）")
        '这台机器上的编码器不吃显存纹理，改用 CPU 回读，之后一直用它
        UseCpuSamples = True
        Logger.Info("编码器不接受显存纹理，已切换为 CPU 回读模式")
        WriteSampleFromCpu(OutTextures(Index), NowTime, Duration)
    End Sub

    ''' <summary>把 NV12 纹理回读到内存，做成普通样本交给编码器。</summary>
    Private Sub WriteSampleFromCpu(Target As Texture2D, NowTime As Long, Duration As Long)
        If StagingTexture Is Nothing Then
            StagingTexture = New Texture2D(Device, New Texture2DDescription With {
                .Width = OutWidth, .Height = OutHeightPx, .MipLevels = 1, .ArraySize = 1,
                .Format = Format.NV12, .SampleDescription = New SampleDescription(1, 0),
                .Usage = ResourceUsage.Staging, .BindFlags = BindFlags.None,
                .CpuAccessFlags = CpuAccessFlags.Read, .OptionFlags = ResourceOptionFlags.None})
        End If
        Context.CopyResource(Target, StagingTexture)
        Dim Box As DataBox = Context.MapSubresource(StagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None)
        Dim Buffer As IMFMediaBuffer = Nothing
        Try
            Dim FrameSize As Integer = OutWidth * OutHeightPx * 3 \ 2
            Dim Hr = UltraMf.MFCreateMemoryBuffer(FrameSize, Buffer)
            If Hr <> 0 Then Throw New Exception($"创建内存缓冲失败（0x{Hr:X8}）")
            Dim Data As IntPtr = IntPtr.Zero
            Dim MaxLen As Integer = 0, CurLen As Integer = 0
            Buffer.Lock(Data, MaxLen, CurLen)
            Try
                For Row As Integer = 0 To OutHeightPx - 1
                    CopyMemory(IntPtr.Add(Data, Row * OutWidth),
                               IntPtr.Add(Box.DataPointer, Row * Box.RowPitch), OutWidth)
                Next
                Dim UvDst As IntPtr = IntPtr.Add(Data, OutWidth * OutHeightPx)
                Dim UvSrc As IntPtr = IntPtr.Add(Box.DataPointer, Box.RowPitch * OutHeightPx)
                For Row As Integer = 0 To OutHeightPx \ 2 - 1
                    CopyMemory(IntPtr.Add(UvDst, Row * OutWidth),
                               IntPtr.Add(UvSrc, Row * Box.RowPitch), OutWidth)
                Next
            Finally
                Buffer.Unlock()
            End Try
            Buffer.SetCurrentLength(FrameSize)
            Dim Smp As IMFSample = Nothing
            Hr = UltraMf.MFCreateSample(Smp)
            If Hr <> 0 Then Throw New Exception($"创建帧失败（0x{Hr:X8}）")
            Smp.AddBuffer(Buffer)
            Smp.SetSampleTime(NowTime)
            Smp.SetSampleDuration(Duration)
            Hr = Writer.WriteSample(0, Smp)
            Marshal.ReleaseComObject(Smp)
            If Hr <> 0 Then Throw New Exception($"编码失败（0x{Hr:X8}）")
        Finally
            Try
                Context.UnmapSubresource(StagingTexture, 0)
            Catch
            End Try
            If Buffer IsNot Nothing Then Marshal.ReleaseComObject(Buffer)
        End Try
    End Sub

#End Region

#Region "声音回环（WASAPI）"

    ''' <summary>准备回环采集：读取播放设备混音格式并以 LOOPBACK 模式初始化，此时还不 Start。</summary>
    Private Sub InitAudio()
        '采集线程已设为 MTA，再显式初始化一次；已初始化时返回 S_FALSE，忽略即可
        UltraWasapi.CoInitializeEx(IntPtr.Zero, 0)

        Dim Clsid As Guid = UltraWasapi.ClsidDeviceEnumerator
        Dim Iid As Guid = UltraWasapi.IidDeviceEnumerator
        Dim Hr As Integer = UltraWasapi.CoCreateInstance(Clsid, IntPtr.Zero, UltraWasapi.ClsCtxAll, Iid, AudioEnumerator)
        If Hr <> 0 Then Throw New Exception($"创建音频设备枚举器失败（0x{Hr:X8}）")

        Hr = AudioEnumerator.GetDefaultAudioEndpoint(UltraWasapi.RenderEndpoint, UltraWasapi.ConsoleRole, AudioEndpoint)
        If Hr <> 0 Then Throw New Exception($"找不到默认播放设备（0x{Hr:X8}）")

        Dim IidClient As Guid = UltraWasapi.IidAudioClient
        Hr = AudioEndpoint.Activate(IidClient, UltraWasapi.ClsCtxAll, IntPtr.Zero, AudioClient)
        If Hr <> 0 Then Throw New Exception($"打开音频设备失败（0x{Hr:X8}）")

        Hr = AudioClient.GetMixFormat(AudioMixFormat)
        If Hr <> 0 Then Throw New Exception($"读取播放设备格式失败（0x{Hr:X8}）")
        Dim Wf = CType(Marshal.PtrToStructure(AudioMixFormat, GetType(UltraWasapi.WaveFormatEx)), UltraWasapi.WaveFormatEx)

        Dim IsFloat As Boolean
        If Wf.FormatTag = UltraWasapi.WaveFormatExtensible Then
            'WAVEFORMATEXTENSIBLE 的 SubFormat 在 wfx(18) + wValidBits(2) + dwChannelMask(4) 之后
            Dim Raw(39) As Byte
            Marshal.Copy(AudioMixFormat, Raw, 0, 40)
            Dim SubBytes(15) As Byte
            Array.Copy(Raw, 24, SubBytes, 0, 16)
            Dim SubFmt As New Guid(SubBytes)
            If SubFmt = UltraWasapi.SubtypeFloat Then
                IsFloat = True
            ElseIf SubFmt = UltraWasapi.SubtypePcm Then
                IsFloat = False
            Else
                Throw New Exception("播放设备使用了不支持的音频格式")
            End If
        ElseIf Wf.FormatTag = UltraWasapi.WaveFormatIeeeFloat Then
            IsFloat = True
        ElseIf Wf.FormatTag = UltraWasapi.WaveFormatPcm Then
            IsFloat = False
        Else
            Throw New Exception("播放设备使用了不支持的音频格式")
        End If

        'AAC 编码器只接受 44.1 / 48 kHz、16 bit 的 PCM 输入
        If Wf.SamplesPerSec <> CUInt(44100) AndAlso Wf.SamplesPerSec <> CUInt(48000) Then
            Throw New Exception($"播放设备采样率 {Wf.SamplesPerSec} Hz 无法直接编码")
        End If
        If Not ((IsFloat AndAlso Wf.BitsPerSample = 32) OrElse (Not IsFloat AndAlso Wf.BitsPerSample = 16)) Then
            Throw New Exception($"播放设备位深 {Wf.BitsPerSample} 无法直接编码")
        End If
        If Wf.Channels < 1 OrElse Wf.Channels > 8 Then Throw New Exception("播放设备声道数异常")

        AudioRate = CInt(Wf.SamplesPerSec)
        AudioWaveChannels = CInt(Wf.Channels)
        AudioWaveBits = CInt(Wf.BitsPerSample)
        AudioWaveFloat = IsFloat

        Dim Session As Guid = Guid.Empty
        Hr = AudioClient.Initialize(UltraWasapi.ShareModeShared, UltraWasapi.StreamFlagsLoopback,
                                    2000000L, 0L, AudioMixFormat, Session)
        If Hr <> 0 Then Throw New Exception($"打开回环采集失败（0x{Hr:X8}）")

        Dim IidCap As Guid = UltraWasapi.IidAudioCaptureClient
        Hr = AudioClient.GetService(IidCap, AudioCapture)
        If Hr <> 0 Then Throw New Exception($"获取音频采集接口失败（0x{Hr:X8}）")
        Logger.Info($"声音回环就绪：{AudioRate} Hz / {AudioWaveChannels} 声道 / {If(IsFloat, "Float32", "PCM16")}")
    End Sub

    ''' <summary>给 SinkWriter 加一条 AAC 输出流和对应的 16bit 立体声 PCM 输入流。</summary>
    Private Function SetupAudioStream(W As IMFSinkWriter) As Integer
        Dim OutType As IMFMediaType = Nothing
        Dim InType As IMFMediaType = Nothing
        Try
            Dim Hr As Integer = UltraMf.MFCreateMediaType(OutType)
            If Hr <> 0 Then Return Hr
            SetGuid(OutType, UltraMf.KeyMajorType, UltraMf.GuidAudio)
            SetGuid(OutType, UltraMf.KeySubType, UltraMf.GuidAac)
            SetUInt(OutType, UltraMf.KeyAudioNumChannels, 2)
            SetUInt(OutType, UltraMf.KeyAudioSamplesPerSecond, AudioRate)
            SetUInt(OutType, UltraMf.KeyAudioBitsPerSample, 16)
            SetUInt(OutType, UltraMf.KeyAudioAvgBytesPerSecond, 24000) '192 kbps，AAC 编码器支持值之一

            Dim Index As Integer = 0
            Hr = W.AddStream(OutType, Index)
            If Hr <> 0 Then Return Hr

            Hr = UltraMf.MFCreateMediaType(InType)
            If Hr <> 0 Then Return Hr
            SetGuid(InType, UltraMf.KeyMajorType, UltraMf.GuidAudio)
            SetGuid(InType, UltraMf.KeySubType, UltraMf.GuidPcm)
            SetUInt(InType, UltraMf.KeyAudioNumChannels, 2)
            SetUInt(InType, UltraMf.KeyAudioSamplesPerSecond, AudioRate)
            SetUInt(InType, UltraMf.KeyAudioBitsPerSample, 16)
            SetUInt(InType, UltraMf.KeyAudioBlockAlignment, 4)
            SetUInt(InType, UltraMf.KeyAudioAvgBytesPerSecond, AudioRate * 4)
            Hr = W.SetInputMediaType(Index, InType, Nothing)
            If Hr <> 0 Then Return Hr

            AudioStreamIndex = Index
            Return 0
        Finally
            If OutType IsNot Nothing Then Marshal.ReleaseComObject(OutType)
            If InType IsNot Nothing Then Marshal.ReleaseComObject(InType)
        End Try
    End Function

    ''' <summary>把回环缓冲里累积的音频包全部写成样本。出错只关掉声音，绝不影响画面。</summary>
    Private Sub PollAudio()
        If Not AudioActive OrElse AudioPollFailed OrElse AudioCapture Is Nothing Then Return
        Try
            Dim Packet As Integer = 0
            Dim Hr As Integer = AudioCapture.GetNextPacketSize(Packet)
            Dim Guard As Integer = 0
            While Hr = 0 AndAlso Packet > 0 AndAlso Guard < 64
                Guard += 1
                Dim Data As IntPtr = IntPtr.Zero
                Dim Frames As Integer = 0, Flags As Integer = 0
                Dim DevPos As Long = 0, QpcPos As Long = 0
                Dim Hr2 As Integer = AudioCapture.GetBuffer(Data, Frames, Flags, DevPos, QpcPos)
                If Hr2 <> 0 Then Exit While
                Try
                    If Frames > 0 Then WriteAudioSample(Data, Frames, (Flags And UltraWasapi.BufferSilent) <> 0)
                Finally
                    AudioCapture.ReleaseBuffer(Frames)
                End Try
                Hr = AudioCapture.GetNextPacketSize(Packet)
            End While
        Catch ex As Exception
            AudioPollFailed = True
            AudioActive = False
            Logger.Warn(ex, "采集系统声音失败，已切换为仅录画面")
        End Try
    End Sub

    ''' <summary>一包回环数据 → 16bit 立体声 PCM 样本 → 音频流。</summary>
    Private Sub WriteAudioSample(Src As IntPtr, Frames As Integer, Silent As Boolean)
        Dim Buffer As IMFMediaBuffer = Nothing
        Dim Sample As IMFSample = Nothing
        Try
            Dim ByteCount As Integer = Frames * 4
            Dim Hr As Integer = UltraMf.MFCreateMemoryBuffer(ByteCount, Buffer)
            If Hr <> 0 Then Throw New Exception($"创建音频缓冲失败（0x{Hr:X8}）")
            Dim Dest As IntPtr = IntPtr.Zero
            Dim MaxLen As Integer = 0, CurLen As Integer = 0
            Hr = Buffer.Lock(Dest, MaxLen, CurLen)
            If Hr <> 0 Then Throw New Exception($"锁定音频缓冲失败（0x{Hr:X8}）")
            Try
                Dim Pcm(Frames * 2 - 1) As Short
                If Not Silent AndAlso Src <> IntPtr.Zero Then FillPcm(Src, Frames, Pcm)
                Marshal.Copy(Pcm, 0, Dest, Frames * 2)
            Finally
                Buffer.Unlock()
            End Try
            Buffer.SetCurrentLength(ByteCount)

            Hr = UltraMf.MFCreateSample(Sample)
            If Hr <> 0 Then Throw New Exception($"创建音频样本失败（0x{Hr:X8}）")
            Sample.AddBuffer(Buffer)
            Dim Time As Long = AudioFramesWritten * 10000000L \ AudioRate
            Dim Duration As Long = CLng(Frames) * 10000000L \ AudioRate
            Sample.SetSampleTime(Time)
            Sample.SetSampleDuration(Duration)
            AudioFramesWritten += Frames
            Hr = Writer.WriteSample(AudioStreamIndex, Sample)
            If Hr <> 0 Then Throw New Exception($"写入音频失败（0x{Hr:X8}）")
        Finally
            If Sample IsNot Nothing Then
                Try
                    Marshal.ReleaseComObject(Sample)
                Catch
                End Try
            End If
            If Buffer IsNot Nothing Then
                Try
                    Marshal.ReleaseComObject(Buffer)
                Catch
                End Try
            End If
        End Try
    End Sub

    ''' <summary>Float32 / PCM16 → 16bit 立体声。单声道复制一份，多声道取前两路。</summary>
    Private Sub FillPcm(Src As IntPtr, Frames As Integer, Pcm As Short())
        Dim SrcCount As Integer = Frames * AudioWaveChannels
        Dim RightIndex As Integer = Math.Min(1, AudioWaveChannels - 1)
        If AudioWaveFloat AndAlso AudioWaveBits = 32 Then
            Dim F(SrcCount - 1) As Single
            Marshal.Copy(Src, F, 0, SrcCount)
            For i As Integer = 0 To Frames - 1
                Dim Base As Integer = i * AudioWaveChannels
                Pcm(i * 2) = ClampPcm(F(Base) * 32767.0F)
                Pcm(i * 2 + 1) = ClampPcm(F(Base + RightIndex) * 32767.0F)
            Next
        ElseIf Not AudioWaveFloat AndAlso AudioWaveBits = 16 Then
            Dim S(SrcCount - 1) As Short
            Marshal.Copy(Src, S, 0, SrcCount)
            For i As Integer = 0 To Frames - 1
                Dim Base As Integer = i * AudioWaveChannels
                Pcm(i * 2) = S(Base)
                Pcm(i * 2 + 1) = S(Base + RightIndex)
            Next
        End If
        '其它位深在 InitAudio 里已挡掉，走到这里保持静音
    End Sub

    Private Function ClampPcm(Value As Single) As Short
        If Single.IsNaN(Value) Then Return 0
        If Value > 32767.0F Then Return Short.MaxValue
        If Value < -32768.0F Then Return Short.MinValue
        Return CShort(Value)
    End Function

    Private Sub StopAudioClient()
        If AudioClient IsNot Nothing Then
            Try
                AudioClient.StopStream()
            Catch
            End Try
        End If
    End Sub

    Private Sub ReleaseAudio()
        If AudioCapture IsNot Nothing Then
            Try
                Marshal.ReleaseComObject(AudioCapture)
            Catch
            End Try
            AudioCapture = Nothing
        End If
        If AudioClient IsNot Nothing Then
            Try
                Marshal.ReleaseComObject(AudioClient)
            Catch
            End Try
            AudioClient = Nothing
        End If
        If AudioEndpoint IsNot Nothing Then
            Try
                Marshal.ReleaseComObject(AudioEndpoint)
            Catch
            End Try
            AudioEndpoint = Nothing
        End If
        If AudioEnumerator IsNot Nothing Then
            Try
                Marshal.ReleaseComObject(AudioEnumerator)
            Catch
            End Try
            AudioEnumerator = Nothing
        End If
        If AudioMixFormat <> IntPtr.Zero Then
            UltraWasapi.CoTaskMemFree(AudioMixFormat)
            AudioMixFormat = IntPtr.Zero
        End If
        AudioWanted = False
        AudioActive = False
        AudioStreamIndex = -1
    End Sub

#End Region

#Region "工具"

    Private Function FindOutput(Adapter As Adapter, Area As RawRectangle?) As SharpDX.DXGI.Output
        Dim Count As Integer = Adapter.GetOutputCount()
        Dim First As SharpDX.DXGI.Output = Nothing
        For i As Integer = 0 To Count - 1
            Dim Out As SharpDX.DXGI.Output = Adapter.GetOutput(i)
            Dim Attached As Boolean = Out.Description.IsAttachedToDesktop
            If Not Attached Then
                Out.Dispose()
                Continue For
            End If
            If First Is Nothing Then
                First = Out
            Else
                Out.Dispose()
                Continue For
            End If
            If Area Is Nothing Then Return First
            Dim B = Out.Description.DesktopBounds
            Dim Cx As Integer = (Area.Value.Left + Area.Value.Right) \ 2
            Dim Cy As Integer = (Area.Value.Top + Area.Value.Bottom) \ 2
            If Cx >= B.Left AndAlso Cx < B.Right AndAlso Cy >= B.Top AndAlso Cy < B.Bottom Then Return Out
        Next
        Return First
    End Function

    <DllImport("kernel32.dll", EntryPoint:="RtlMoveMemory")>
    Private Sub CopyMemory(Destination As IntPtr, Source As IntPtr, Length As Integer)
    End Sub

    Private Function MakeLong(High As Integer, Low As Integer) As Long
        Return (CLng(High) << 32) Or (CLng(Low And &HFFFFFFFFL))
    End Function

    Private Sub SetUInt(Target As IMFAttributes, Key As Guid, Value As Integer)
        Dim K As Guid = Key
        Target.SetUINT32(K, Value)
    End Sub

    Private Sub SetUInt64(Target As IMFAttributes, Key As Guid, Value As Long)
        Dim K As Guid = Key
        Target.SetUINT64(K, Value)
    End Sub

    Private Sub SetGuid(Target As IMFAttributes, Key As Guid, Value As Guid)
        Dim K As Guid = Key
        Dim V As Guid = Value
        Target.SetGUID(K, V)
    End Sub

    Private Sub SetUnknown(Target As IMFAttributes, Key As Guid, Value As Object)
        Dim K As Guid = Key
        Target.SetUnknown(K, Value)
    End Sub

#End Region

End Module
