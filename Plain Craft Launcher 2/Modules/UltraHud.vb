Imports System.Diagnostics
Imports System.IO
Imports System.Management
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Threading

''' <summary>
''' Ultra：UHD（微星小飞机式游戏内 OSD）。
'''
''' 实现方式是原生注入：把 PCL_OSD64.dll 注入 Minecraft 的进程，钩住 gdi32!SwapBuffers，
''' 在游戏自己换页之前用 OpenGL 把字画进后缓冲。所以：
'''   帧率 / 帧时间 / 1% Low / 0.1% Low 全部来自游戏真实的换页节奏，不是桌面合成帧；
'''   窗口化、无边框、独占全屏都跟着游戏窗口走，不用额外跟随逻辑；
'''   画面直接画在游戏里，不会被别的窗口盖住，也不用置顶抢焦点。
'''
''' 本程序（PCL）与游戏之间靠一块共享内存通信：本程序写「显示哪些项、CPU/GPU/显存/内存读数」，
''' 游戏里的内核回传「帧率、帧时间、1% Low、渲染分辨率」。
''' </summary>
Public Module UltraHud

#Region "常量"

    '热键（默认 ALT+E，可在设置里改）
    Private Const HotkeyId As Integer = &H505A
    Public Const DefaultHotkey As String = "Alt+E"
    '旧版本的默认值是 Alt + H，设置里存着这个值就当没改过，自动跟到新默认
    Private Const LegacyDefaultHotkey As String = "Alt+H"
    Private Const MOD_ALT As Integer = 1

    '数值字号（1080p 基准下的数字高度），可连续调节
    Public Const FontPxMin As Integer = 18
    Public Const FontPxMax As Integer = 72
    Public Const FontPxDefault As Integer = 36

    ''' <summary>HUD 贴在屏幕的哪个角：左上 / 右上 / 左下 / 右下。</summary>
    Public Const PositionLeftTop As Integer = 0
    Public Const PositionRightTop As Integer = 1
    Public Const PositionLeftBottom As Integer = 2
    Public Const PositionRightBottom As Integer = 3

    '可显示的参数：设置里用逗号分隔存成一行，顺序固定按 AllItems
    Public Const ItemCpu As String = "cpu"
    Public Const ItemGpu As String = "gpu"
    Public Const ItemVram As String = "vram"
    Public Const ItemRam As String = "ram"
    Public Const ItemFps As String = "fps"
    Public Const ItemFrameTime As String = "ft"
    Public Const ItemLow1 As String = "low1"
    Public Const ItemLow01 As String = "low01"
    Public Const DefaultItems As String = "gpu,vram,ram,fps,low1"
    '老版本的默认值（帧时间跟帧率挤一行，用户嫌挤），读到它就当没改过
    Private Const LegacyDefaultItems As String = "gpu,vram,ram,fps,ft,low1"

    '显示项目对应的位掩码（与原生内核一致）
    Private Const MaskCpu As Long = &H1
    Private Const MaskGpu As Long = &H2
    Private Const MaskVram As Long = &H4
    Private Const MaskRam As Long = &H8
    Private Const MaskFps As Long = &H10
    Private Const MaskFrameTime As Long = &H20
    Private Const MaskLow1 As Long = &H40
    Private Const MaskLow01 As Long = &H80

    ' ---- 共享内存协议（与 PclOsd.cpp 的 OsdShared 逐字段一致） ----
    Private Const OsdShmName As String = "Local\PCL_OSD_Shared"
    Private Const OsdMagic As Integer = &H4F534C50
    Private Const OsdVersion As Integer = 3
    Private Const OsdShmSize As Long = 128

    Private Const OffMagic As Integer = 0
    Private Const OffVersion As Integer = 4
    Private Const OffEnabled As Integer = 8
    Private Const OffPosition As Integer = 12
    Private Const OffScale As Integer = 16
    Private Const OffItems As Integer = 20
    Private Const OffCpu As Integer = 24
    Private Const OffGpu As Integer = 28
    Private Const OffVram As Integer = 32
    Private Const OffRamUsed As Integer = 36
    Private Const OffRamTotal As Integer = 40
    Private Const OffFps As Integer = 44
    Private Const OffFrameMs10 As Integer = 48
    Private Const OffLow1 As Integer = 52
    Private Const OffLow01 As Integer = 56
    Private Const OffFrames As Integer = 60
    Private Const OffStatus As Integer = 64
    Private Const OffWidth As Integer = 68
    Private Const OffHeight As Integer = 72
    Private Const OffLastError As Integer = 76
    Private Const OffRefreshMs As Integer = 80
    Private Const OffHookCount As Integer = 84
    Private Const OffFontPx As Integer = 88

    '内核状态码
    Private Const StatusNotHooked As Integer = 0
    Private Const StatusRunning As Integer = 1

    '内嵌内核的资源名与落地文件名
    Private Const DllResourceName As String = "PCL_OSD64.dll"
    Private Const DllFileName As String = "PCL_OSD64.dll"

#End Region

#Region "运行状态"

    '共享内存
    Private ShmMap As IntPtr = IntPtr.Zero
    Private ShmView As IntPtr = IntPtr.Zero
    Private ReadOnly ShmGate As New Object()

    '已经注入的游戏进程号（换了游戏要重新注入）
    Private InjectedPid As Integer = 0

    '采样线程：把 CPU / 内存 / GPU / 显存写进共享内存
    Private SamplerWorker As Thread
    Private SamplerStop As Boolean
    Private ReadOnly Gate As New Object()
    Private CpuPercent As Integer = -1
    Private GpuPercent As Integer = -1
    Private VramMb As Integer = -1
    Private RamUsed As Integer = 0
    Private RamTotal As Integer = 0
    Private GpuOk As Boolean = True
    Private GpuFailLogged As Boolean = False

    '热键
    Private HotkeyHwnd As IntPtr = IntPtr.Zero
    Private HotkeyMods As Integer = MOD_ALT
    Private HotkeyVK As Integer = &H45
    Private HotkeyIsRegistered As Boolean = False

#End Region

#Region "对外状态"

    ''' <summary>UHD 现在是否开着。</summary>
    Public ReadOnly Property IsShowing As Boolean
        Get
            Return ReadShared(OffEnabled) <> 0
        End Get
    End Property

    ''' <summary>
    ''' 当前正在运行的 Minecraft 游戏窗口句柄，没开游戏时是 IntPtr.Zero。
    ''' UHD 只在游戏跑起来之后才允许打开——桌面上没有「游戏帧率」这回事。
    ''' </summary>
    Public ReadOnly Property GameWindow As IntPtr
        Get
            Try
                '调试用：拿任意窗口冒充游戏窗口（PCL_OSD_TEST_WINDOW=句柄），
                '不开 Minecraft 也能验证「释放内核 → 注入 → 画进画面」整条链路
                Dim Fake As String = Environment.GetEnvironmentVariable("PCL_OSD_TEST_WINDOW")
                If Not String.IsNullOrEmpty(Fake) Then
                    Dim Value As Long
                    If Long.TryParse(Fake, Value) AndAlso Value <> 0 Then
                        Dim H As New IntPtr(Value)
                        '窗口已经没了就当游戏已退出
                        If NativeWindow.IsWindow(H) Then Return H
                        Return IntPtr.Zero
                    End If
                End If
                Return UltraVideo.GetGameWindow()
            Catch ex As Exception
                Return IntPtr.Zero
            End Try
        End Get
    End Property

    ''' <summary>游戏是否正在运行（已经拿到游戏窗口）。</summary>
    Public ReadOnly Property GameRunning As Boolean
        Get
            Return GameWindow <> IntPtr.Zero
        End Get
    End Property

    ''' <summary>原生内核是否已经挂到游戏上。</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            Return ReadShared(OffStatus) = StatusRunning
        End Get
    End Property

    ''' <summary>当前帧率（整数，直接显示用）。</summary>
    Public ReadOnly Property CurrentFps As Integer
        Get
            Return ReadShared(OffFps)
        End Get
    End Property

    ''' <summary>当前帧间隔（毫秒），没有数据时为 0。</summary>
    Public ReadOnly Property FrameTimeMs As Double
        Get
            Return ReadShared(OffFrameMs10) / 10.0
        End Get
    End Property

    ''' <summary>1% Low：最近 10 秒里最慢的那 1% 帧换算出来的帧率，0 表示还没有数据。</summary>
    Public ReadOnly Property Low1Fps As Integer
        Get
            Return ReadShared(OffLow1)
        End Get
    End Property

    ''' <summary>0.1% Low：只取最慢的 0.1% 帧，卡顿更敏感。</summary>
    Public ReadOnly Property Low01Fps As Integer
        Get
            Return ReadShared(OffLow01)
        End Get
    End Property

    ''' <summary>游戏当前的渲染分辨率（像素）。</summary>
    Public ReadOnly Property RenderWidth As Integer
        Get
            Return ReadShared(OffWidth)
        End Get
    End Property

    Public ReadOnly Property RenderHeight As Integer
        Get
            Return ReadShared(OffHeight)
        End Get
    End Property

    ''' <summary>GPU 使用率（3D 引擎，0~100），读不到为 -1。</summary>
    Public ReadOnly Property GpuUsage As Integer
        Get
            SyncLock Gate
                Return GpuPercent
            End SyncLock
        End Get
    End Property

    ''' <summary>显存占用（MB），读不到为 -1。</summary>
    Public ReadOnly Property VramUsedMb As Integer
        Get
            SyncLock Gate
                Return VramMb
            End SyncLock
        End Get
    End Property

    ''' <summary>已用物理内存（MB）。</summary>
    Public ReadOnly Property RamUsedMb As Integer
        Get
            SyncLock Gate
                Return RamUsed
            End SyncLock
        End Get
    End Property

    ''' <summary>物理内存总量（MB）。</summary>
    Public ReadOnly Property RamTotalMb As Integer
        Get
            SyncLock Gate
                Return RamTotal
            End SyncLock
        End Get
    End Property

    ''' <summary>CPU 使用率（0~100），还没采到时为 -1。</summary>
    Public ReadOnly Property CpuUsage As Integer
        Get
            SyncLock Gate
                Return CpuPercent
            End SyncLock
        End Get
    End Property

    ''' <summary>这台机器能不能读到 GPU 数据（性能计数器不存在时为 False）。</summary>
    Public ReadOnly Property GpuAvailable As Boolean
        Get
            SyncLock Gate
                Return GpuOk
            End SyncLock
        End Get
    End Property

    ''' <summary>统计不了时的原因，正常时为空字符串。</summary>
    Public ReadOnly Property LastError As String
        Get
            Dim Code As Integer = ReadShared(OffLastError)
            If Code <= 0 Then Return ""
            Select Case Code
                Case 2
                    Return "共享内存版本不匹配"
                Case 3
                    Return "游戏里的 OpenGL 上下文不支持（需要 OpenGL 3.0 以上）"
                Case 10, 11, 12
                    Return "字体图集生成失败"
                Case 14
                    Return "没有可显示的项目"
                Case 15
                    Return "没有绘制任何内容"
                Case Is >= 100
                    Return "OpenGL 报错（代码 " & (Code - 100) & "）"
                Case Else
                    Return "内核错误（代码 " & Code & "）"
            End Select
        End Get
    End Property

    ''' <summary>一句话状态，给界面直接显示。</summary>
    Public ReadOnly Property StateText As String
        Get
            If Not GameRunning Then Return "游戏未启动"
            If Not IsShowing Then Return "未开启"
            Dim St As Integer = ReadShared(OffStatus)
            If St = StatusRunning Then Return "运行中（已挂接到游戏）"
            If InjectedPid <= 0 Then Return "正在准备内核……"
            Return "正在挂接游戏……"
        End Get
    End Property

#End Region

#Region "显示项目与硬件读数"

    ''' <summary>所有可显示的项目，界面和内核都按这个顺序排。</summary>
    Public ReadOnly Property AllItems As String()
        Get
            Return New String() {ItemCpu, ItemGpu, ItemVram, ItemRam, ItemFps, ItemFrameTime, ItemLow1, ItemLow01}
        End Get
    End Property

    ''' <summary>当前要显示的项目（按固定顺序排好、去重）。存在 HudItems 里，逗号分隔。</summary>
    Public ReadOnly Property SelectedItems As List(Of String)
        Get
            Dim Raw As String = Settings.Get(Of String)("HudItems")
            If String.IsNullOrWhiteSpace(Raw) Then Raw = DefaultItems
            '老默认值里有 ft（帧时间跟帧率挤在同一行），当成没改过，换成新默认
            If Raw.Replace(" ", "").Equals(LegacyDefaultItems, StringComparison.OrdinalIgnoreCase) Then Raw = DefaultItems
            Dim Wanted As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each Part As String In Raw.Split(","c)
                Dim Token As String = Part.Trim().ToLowerInvariant()
                If Token <> "" Then Wanted.Add(Token)
            Next
            Dim Result As New List(Of String)
            For Each Item As String In AllItems
                If Wanted.Contains(Item) Then Result.Add(Item)
            Next
            Return Result
        End Get
    End Property

    ''' <summary>把选中的项目翻译成内核用的位掩码。</summary>
    Private Function ItemsMask() As Long
        Dim Mask As Long = 0
        For Each Token As String In SelectedItems
            Select Case Token
                Case ItemCpu
                    Mask = Mask Or MaskCpu
                Case ItemGpu
                    Mask = Mask Or MaskGpu
                Case ItemVram
                    Mask = Mask Or MaskVram
                Case ItemRam
                    Mask = Mask Or MaskRam
                Case ItemFps
                    Mask = Mask Or MaskFps
                Case ItemFrameTime
                    Mask = Mask Or MaskFrameTime
                Case ItemLow1
                    Mask = Mask Or MaskLow1
                Case ItemLow01
                    Mask = Mask Or MaskLow01
            End Select
        Next
        If Mask = 0 Then Mask = MaskFps
        Return Mask
    End Function

    ''' <summary>项目的短标签，和微星小飞机一样用英文缩写。</summary>
    Public Function ItemLabel(Token As String) As String
        Select Case Token
            Case ItemCpu
                Return "CPU"
            Case ItemGpu
                Return "GPU"
            Case ItemVram
                Return "MEM"
            Case ItemRam
                Return "RAM"
            Case ItemLow1
                Return "1% Low"
            Case ItemLow01
                Return "0.1% Low"
            Case ItemFrameTime
                Return "FT"
            Case Else
                Return "FPS"
        End Select
    End Function

    ''' <summary>项目的单位。帧率本身就用「FPS」当标签，不再重复写单位。</summary>
    Public Function ItemUnit(Token As String) As String
        Select Case Token
            Case ItemCpu, ItemGpu
                Return "%"
            Case ItemVram
                Return "MB"
            Case ItemRam
                Return "GB"
            Case ItemFrameTime
                Return "ms"
            Case ItemFps
                Return ""
            Case Else
                Return "FPS"
        End Select
    End Function

#End Region

#Region "共享内存"

    ''' <summary>读共享内存里的一个 32 位字段。</summary>
    Private Function ReadShared(Offset As Integer) As Integer
        Try
            SyncLock ShmGate
                If ShmView = IntPtr.Zero Then Return 0
                Return Marshal.ReadInt32(ShmView, Offset)
            End SyncLock
        Catch ex As Exception
            Return 0
        End Try
    End Function

    ''' <summary>写共享内存里的一个 32 位字段。</summary>
    Private Sub WriteShared(Offset As Integer, Value As Integer)
        Try
            SyncLock ShmGate
                If ShmView = IntPtr.Zero Then Return
                Marshal.WriteInt32(ShmView, Offset, Value)
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, "写 OSD 共享内存失败")
        End Try
    End Sub

    ''' <summary>
    ''' 建立（或重新取得）共享内存。PCL 重启后游戏里还挂着内核时，
    ''' 同名映射对象会被复用，这里只要校验一下 magic 就好。
    ''' </summary>
    Private Function EnsureShared() As Boolean
        SyncLock ShmGate
            If ShmView <> IntPtr.Zero Then
                If ReadShared(OffMagic) = OsdMagic Then Return True
                '映射还在但内容不对（别的程序占了这个名字），重建
                Try
                    Native.UnmapViewOfFile(ShmView)
                Catch
                End Try
                ShmView = IntPtr.Zero
                If ShmMap <> IntPtr.Zero Then
                    Native.CloseHandle(ShmMap)
                    ShmMap = IntPtr.Zero
                End If
            End If
            Try
                ShmMap = Native.CreateFileMapping(Native.InvalidHandleValue, IntPtr.Zero,
                                                  Native.PageReadWrite, 0, OsdShmSize, OsdShmName)
                If ShmMap = IntPtr.Zero Then
                    Logger.Warn($"创建 OSD 共享内存失败（错误码 {Marshal.GetLastWin32Error()}）")
                    Return False
                End If
                ShmView = Native.MapViewOfFile(ShmMap, Native.FileMapAllAccess, 0, 0, OsdShmSize)
                If ShmView = IntPtr.Zero Then
                    Logger.Warn("映射 OSD 共享内存失败")
                    Native.CloseHandle(ShmMap)
                    ShmMap = IntPtr.Zero
                    Return False
                End If
                Marshal.WriteInt32(ShmView, OffMagic, OsdMagic)
                Marshal.WriteInt32(ShmView, OffVersion, OsdVersion)
                Marshal.WriteInt32(ShmView, OffEnabled, 0)
                Marshal.WriteInt32(ShmView, OffStatus, 0)
                Marshal.WriteInt32(ShmView, OffCpu, -1)
                Marshal.WriteInt32(ShmView, OffGpu, -1)
                Marshal.WriteInt32(ShmView, OffVram, -1)
                Marshal.WriteInt32(ShmView, OffRamUsed, 0)
                Marshal.WriteInt32(ShmView, OffRamTotal, 0)
                Return True
            Catch ex As Exception
                Logger.Error(ex, "创建 OSD 共享内存失败")
                Return False
            End Try
        End SyncLock
    End Function

    ''' <summary>当前数值字号（1080p 基准像素），越界会被拉回合法范围。</summary>
    Public ReadOnly Property FontSizePx As Integer
        Get
            Dim V As Integer = Settings.Get(Of Integer)("HudFontPx")
            If V < FontPxMin Then V = FontPxMin
            If V > FontPxMax Then V = FontPxMax
            Return V
        End Get
    End Property

    ''' <summary>把设置（位置、字号、显示项目、刷新间隔）同步给内核。</summary>
    Public Sub ApplySettings()
        If Not EnsureShared() Then Return
        WriteShared(OffPosition, Settings.Get(Of Integer)("HudPosition"))
        '旧的三档（小/中/大）内核仍认，保留做兜底；真正生效的是下面的连续字号
        WriteShared(OffScale, 1)
        WriteShared(OffFontPx, FontSizePx)
        WriteShared(OffItems, CInt(ItemsMask()))
        WriteShared(OffRefreshMs, Settings.Get(Of Integer)("HudRefresh"))
    End Sub

#End Region

#Region "显示 / 隐藏"

    ''' <summary>
    ''' 显示 UHD 并把内核挂到游戏上。只有检测到游戏窗口时才允许——
    ''' 桌面上没有「游戏帧率」这回事。
    ''' </summary>
    Public Sub ShowHud()
        Try
            If FrmMain Is Nothing Then Return
            If Not GameRunning Then
                Hint("还没检测到游戏窗口，进游戏后再按 " & HotkeyText & " 打开 UHD", HintType.Red)
                Logger.Info("未检测到游戏窗口，UHD 不启动")
                Return
            End If
            If Not EnsureShared() Then
                Hint("无法创建 OSD 共享内存", HintType.Red)
                Return
            End If
            Dim Pid As Integer = GetGamePid()
            If Pid <= 0 Then
                Hint("找不到游戏进程，无法挂接 OSD 内核", HintType.Red)
                Logger.Warn("UHD：找不到游戏进程号")
                Return
            End If
            If InjectedPid <> Pid Then
                Dim DllPath As String = EnsureDllFile()
                If DllPath = "" Then
                    Hint("找不到 OSD 内核文件", HintType.Red)
                    Logger.Warn("UHD：内嵌的 OSD 内核释放失败")
                    Return
                End If
                Dim Reason As String = InjectDll(Pid, DllPath)
                If Reason <> "" Then
                    Hint("挂接 OSD 内核失败：" & Reason, HintType.Red)
                    Logger.Warn("UHD：注入内核失败 —— " & Reason)
                    Return
                End If
                InjectedPid = Pid
                Logger.Info($"UHD：已向游戏进程 {Pid} 挂接 OSD 内核")
            End If
            WriteShared(OffEnabled, 1)
            ApplySettings()
            StartSampler()
            Hint("UHD 已开启，在游戏画面左上角显示", HintType.Green)
            Logger.Info("UHD 已开启")
        Catch ex As Exception
            Logger.Error(ex, "开启 UHD 失败")
        End Try
    End Sub

    ''' <summary>隐藏 UHD（内核留在游戏里但不再绘制，下次开不用重新注入）。</summary>
    Public Sub HideHud()
        WriteShared(OffEnabled, 0)
        StopSampler()
        Logger.Info("UHD 已隐藏")
    End Sub

    ''' <summary>
    ''' 游戏退出：内核随游戏进程一起没了，这里把状态清掉，下次进游戏重新注入。
    ''' </summary>
    Public Sub OnGameExited()
        Try
            WriteShared(OffEnabled, 0)
            StopSampler()
            InjectedPid = 0
            Logger.Info("游戏已退出，UHD 内核已卸载")
        Catch ex As Exception
            Logger.Warn(ex, "游戏退出后清理 UHD 失败")
        End Try
    End Sub

    ''' <summary>开关切换，供全局快捷键用。</summary>
    Public Sub ToggleHud()
        If IsShowing Then HideHud() Else ShowHud()
    End Sub

    ''' <summary>位置 / 字号等设置变了以后重新同步给内核。</summary>
    Public Sub RefreshLayout()
        ApplySettings()
    End Sub

    ''' <summary>程序退出时收尾。</summary>
    Public Sub Shutdown()
        Try
            WriteShared(OffEnabled, 0)
            StopSampler()
            SyncLock ShmGate
                If ShmView <> IntPtr.Zero Then
                    Try
                        Native.UnmapViewOfFile(ShmView)
                    Catch
                    End Try
                    ShmView = IntPtr.Zero
                End If
                If ShmMap <> IntPtr.Zero Then
                    Native.CloseHandle(ShmMap)
                    ShmMap = IntPtr.Zero
                End If
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, "关闭 UHD 失败")
        End Try
    End Sub

    ''' <summary>
    ''' 录制期间不需要让出什么：UHD 是画在游戏进程里的，
    ''' 和本程序的桌面复制录制完全不冲突，录进去的画面本来就带 HUD。
    ''' </summary>
    Public Sub SuspendForRecording()
        '保留空实现，录制时只跳过 GPU 采样（WMI 查询有点吃 CPU）
    End Sub

    ''' <summary>录制结束后恢复采样。</summary>
    Public Sub ResumeAfterRecording()
        '保留空实现
    End Sub

#End Region

#Region "注入"

    ''' <summary>
    ''' 游戏刚启动时就把内核挂上去。
    ''' 必须赶在游戏创建窗口之前：LWJGL 是运行时用 GetProcAddress 解析 SwapBuffers 的，
    ''' 窗口一旦建好，那个地址就被缓存下来，那时候再挂就晚了。
    ''' </summary>
    Public Sub AutoAttach(Proc As Process)
        Try
            If Proc Is Nothing Then Return
            If Not Settings.Get(Of Boolean)("HudAutoAttach") Then Return
            If Not EnsureShared() Then Return
            Dim DllPath As String = EnsureDllFile()
            If DllPath = "" Then Return
            Dim Pid As Integer = Proc.Id
            If Pid <= 0 Then Return
            If InjectedPid = Pid Then Return
            Dim Reason As String = InjectDll(Pid, DllPath)
            If Reason <> "" Then
                Logger.Warn("UHD：游戏启动时挂接内核失败 —— " & Reason)
                Return
            End If
            InjectedPid = Pid
            Logger.Info($"UHD：已在游戏启动时挂接内核（进程 {Pid}）")
        Catch ex As Exception
            Logger.Warn(ex, "游戏启动时挂接 UHD 内核失败")
        End Try
    End Sub

    ''' <summary>取当前 Minecraft 进程号：优先用游戏窗口所属的进程。</summary>
    Private Function GetGamePid() As Integer
        Try
            Dim H As IntPtr = GameWindow
            If H <> IntPtr.Zero Then
                Dim Pid As Integer = 0
                Dim Tid As Integer = Native.GetWindowThreadProcessId(H, Pid)
                Logger.Info($"UHD：窗口 {H.ToInt64()} → 线程 {Tid}，进程 {Pid}")
                If Tid <> 0 AndAlso Pid > 0 Then Return Pid
                '退路：直接枚举进程，按主窗口句柄认人
                For Each P As Process In Process.GetProcesses()
                    Try
                        If P.MainWindowHandle = H Then
                            Logger.Info($"UHD：枚举进程认出 {P.ProcessName}（{P.Id}）")
                            Return P.Id
                        End If
                    Catch
                    End Try
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取游戏进程号失败")
        End Try
        Try
            If McLaunchProcess IsNot Nothing AndAlso Not McLaunchProcess.HasExited Then
                Return McLaunchProcess.Id
            End If
        Catch ex As Exception
            Logger.Warn(ex, "从游戏进程对象读取进程号失败")
        End Try
        Return 0
    End Function

    ''' <summary>
    ''' 把内嵌的原生内核释放到缓存目录。内容变了就重写（升级后自动换新）。
    ''' </summary>
    Private Function EnsureDllFile() As String
        Dim Data As Byte() = Nothing
        Try
            Using S As Stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DllResourceName)
                If S Is Nothing Then
                    Logger.Warn($"程序里找不到内嵌资源 {DllResourceName}")
                    Return ""
                End If
                Using Ms As New MemoryStream()
                    S.CopyTo(Ms)
                    Data = Ms.ToArray()
                End Using
            End Using
        Catch ex As Exception
            Logger.Error(ex, "读取内嵌的 OSD 内核失败")
            Return ""
        End Try
        If Data Is Nothing OrElse Data.Length < 1024 Then
            Logger.Warn("内嵌的 OSD 内核不完整")
            Return ""
        End If

        Dim Target As String = Path.Combine(PathTemp, DllFileName)
        Try
            Directory.CreateDirectory(PathTemp)
            Dim NewHash As String = BitConverter.ToString(
                System.Security.Cryptography.SHA256.Create().ComputeHash(Data)).Replace("-", "")
            Dim OldHash As String = ""
            If File.Exists(Target) Then
                Using S As FileStream = File.OpenRead(Target)
                    OldHash = BitConverter.ToString(
                        System.Security.Cryptography.SHA256.Create().ComputeHash(S)).Replace("-", "")
                End Using
            End If
            If OldHash <> NewHash Then File.WriteAllBytes(Target, Data)
            Return Target
        Catch ex As Exception
            Logger.Error(ex, "释放 OSD 内核到缓存目录失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 往目标进程注入 DLL。返回空字符串表示成功，否则是失败原因。
    ''' 做法和经典的远程线程注入一样：把 DLL 路径写进目标进程，
    ''' 再远程调一次 LoadLibraryW。
    ''' </summary>
    Private Function InjectDll(Pid As Integer, DllPath As String) As String
        Dim H As IntPtr = IntPtr.Zero
        Dim Mem As IntPtr = IntPtr.Zero
        Try
            H = Native.OpenProcess(Native.ProcessAllAccess, False, Pid)
            If H = IntPtr.Zero Then
                Dim Err1 As Integer = Marshal.GetLastWin32Error()
                Return "无法打开游戏进程（错误码 " & Err1 & "），请以管理员身份运行本程序"
            End If
            Dim Bytes As Byte() = Encoding.Unicode.GetBytes(DllPath & ChrW(0))
            Mem = Native.VirtualAllocEx(H, IntPtr.Zero, CUInt(Bytes.Length),
                                        Native.MemCommit Or Native.MemReserve, Native.PageReadWrite)
            If Mem = IntPtr.Zero Then Return "无法在游戏进程里分配内存"
            Dim Written As IntPtr
            If Not Native.WriteProcessMemory(H, Mem, Bytes, CUInt(Bytes.Length), Written) Then
                Return "无法写入游戏进程"
            End If
            Dim Kernel As IntPtr = Native.GetModuleHandle("kernel32.dll")
            If Kernel = IntPtr.Zero Then Return "找不到 kernel32.dll"
            Dim LoadLib As IntPtr = Native.GetProcAddress(Kernel, "LoadLibraryW")
            If LoadLib = IntPtr.Zero Then Return "找不到 LoadLibraryW"
            Dim Thread As IntPtr = Native.CreateRemoteThread(H, IntPtr.Zero, 0, LoadLib, Mem, 0, IntPtr.Zero)
            If Thread = IntPtr.Zero Then Return "无法在游戏进程里启动加载线程"
            Dim Wait As Integer = Native.WaitForSingleObject(Thread, 5000)
            Native.CloseHandle(Thread)
            If Wait = 258 Then Return "注入超时"
            Return ""
        Catch ex As Exception
            Return ex.Message
        Finally
            If Mem <> IntPtr.Zero AndAlso H <> IntPtr.Zero Then
                Try
                    Native.VirtualFreeEx(H, Mem, 0, Native.MemRelease)
                Catch
                End Try
            End If
            If H <> IntPtr.Zero Then Native.CloseHandle(H)
        End Try
    End Function

#End Region

#Region "硬件采样"

    ''' <summary>采样线程：CPU、内存每秒一次；GPU / 显存走 WMI，三秒一次。</summary>
    Private Sub StartSampler()
        If SamplerWorker IsNot Nothing AndAlso SamplerWorker.IsAlive Then Return
        SamplerStop = False
        SyncLock Gate
            CpuPercent = -1
            RamUsed = 0
            RamTotal = 0
            If GpuOk Then
                GpuPercent = -1
                VramMb = -1
            End If
        End SyncLock
        SamplerWorker = New Thread(AddressOf RunSampler) With {
            .IsBackground = True,
            .Name = "UltraHudSample",
            .Priority = ThreadPriority.BelowNormal}
        SamplerWorker.Start()
    End Sub

    Private Sub StopSampler()
        SamplerStop = True
        Dim W As Thread = SamplerWorker
        SamplerWorker = Nothing
        If W IsNot Nothing Then
            Try
                W.Join(1500)
            Catch
            End Try
        End If
    End Sub

    Private Sub RunSampler()
        Dim PrevIdle As Long, PrevKernel As Long, PrevUser As Long
        If Not ReadSystemTimes(PrevIdle, PrevKernel, PrevUser) Then
            PrevIdle = 0
            PrevKernel = 0
            PrevUser = 0
        End If
        Dim Tick As Integer = 0
        While Not SamplerStop
            Thread.Sleep(1000)
            If SamplerStop Then Exit While
            'CPU：和上一秒的差值
            Dim Idle As Long, Kernel As Long, User As Long
            If PrevKernel > 0 AndAlso ReadSystemTimes(Idle, Kernel, User) Then
                Dim DIdle As Long = Idle - PrevIdle
                Dim DTotal As Long = (Kernel - PrevKernel) + (User - PrevUser)
                If DTotal > 0 Then
                    Dim Load As Integer = CInt(Math.Round((1.0 - DIdle / CDbl(DTotal)) * 100))
                    If Load < 0 Then Load = 0
                    If Load > 100 Then Load = 100
                    SyncLock Gate
                        '轻度平滑，读数不跳
                        CpuPercent = If(CpuPercent < 0, Load, CInt(Math.Round(CpuPercent * 0.4 + Load * 0.6)))
                    End SyncLock
                End If
                PrevIdle = Idle
                PrevKernel = Kernel
                PrevUser = User
            Else
                ReadSystemTimes(PrevIdle, PrevKernel, PrevUser)
            End If
            '内存：随取随有
            Dim TotalMb As Integer, UsedMb As Integer
            If ReadMemory(TotalMb, UsedMb) Then
                SyncLock Gate
                    RamTotal = TotalMb
                    RamUsed = UsedMb
                End SyncLock
            End If
            'GPU / 显存：WMI 查询要几百毫秒，三秒一次，而且没勾选就完全不做
            Tick += 1
            If Tick Mod 3 = 0 Then
                Dim Items As List(Of String) = SelectedItems
                If Items.Contains(ItemGpu) OrElse Items.Contains(ItemVram) Then SampleGpu()
            End If
            '把读数写给游戏里的内核
            PublishStats()
        End While
    End Sub

    ''' <summary>把采到的硬件读数写进共享内存。</summary>
    Private Sub PublishStats()
        Dim Cpu As Integer, Gpu As Integer, Vram As Integer, Used As Integer, Total As Integer
        SyncLock Gate
            Cpu = CpuPercent
            Gpu = GpuPercent
            Vram = VramMb
            Used = RamUsed
            Total = RamTotal
        End SyncLock
        WriteShared(OffCpu, Cpu)
        WriteShared(OffGpu, Gpu)
        WriteShared(OffVram, Vram)
        WriteShared(OffRamUsed, Used)
        WriteShared(OffRamTotal, Total)
    End Sub

    ''' <summary>读 GPU 3D 引擎占用率与显存占用（Windows 自带的 GPU 性能计数器）。</summary>
    Private Sub SampleGpu()
        '录制期间别抢 CPU：WMI 查询要几百毫秒，这时让 GPU / 显存停在上一帧
        If UltraVideo.IsRecording Then Return
        SyncLock Gate
            If Not GpuOk Then Return
        End SyncLock
        Try
            Dim GpuSum As Double = 0
            Dim VramMax As Double = 0
            Using Searcher As New ManagementObjectSearcher(
                "root\CIMV2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")
                Using Results As ManagementObjectCollection = Searcher.Get()
                    For Each Obj As ManagementObject In Results
                        Try
                            Dim Name As String = TryCast(Obj("Name"), String)
                            If Name IsNot Nothing AndAlso Name.Contains("engtype_3D") Then
                                GpuSum += Convert.ToDouble(Obj("UtilizationPercentage"))
                            End If
                        Finally
                            Obj.Dispose()
                        End Try
                    Next
                End Using
            End Using
            Using Searcher As New ManagementObjectSearcher(
                "root\CIMV2",
                "SELECT DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory")
                Using Results As ManagementObjectCollection = Searcher.Get()
                    For Each Obj As ManagementObject In Results
                        Try
                            Dim Bytes As Double = Convert.ToDouble(Obj("DedicatedUsage"))
                            If Bytes > VramMax Then VramMax = Bytes
                        Finally
                            Obj.Dispose()
                        End Try
                    Next
                End Using
            End Using
            Dim G As Integer = CInt(Math.Round(GpuSum))
            If G < 0 Then G = 0
            If G > 100 Then G = 100
            SyncLock Gate
                GpuPercent = G
                VramMb = CInt(Math.Round(VramMax / 1048576.0))
            End SyncLock
        Catch ex As Exception
            SyncLock Gate
                GpuOk = False
                GpuPercent = -1
                VramMb = -1
            End SyncLock
            If Not GpuFailLogged Then
                GpuFailLogged = True
                Logger.Warn(ex, "读不到 GPU 性能计数器，UHD 的 GPU / 显存项会显示「—」")
            End If
        End Try
    End Sub

    ''' <summary>读系统累计的空闲 / 内核 / 用户态时间（单位 100 纳秒）。</summary>
    Private Function ReadSystemTimes(ByRef Idle As Long, ByRef Kernel As Long, ByRef User As Long) As Boolean
        Dim I, K, U As NativeSample.FileTime
        If Not NativeSample.GetSystemTimes(I, K, U) Then Return False
        Idle = NativeSample.ToTicks(I)
        Kernel = NativeSample.ToTicks(K)
        User = NativeSample.ToTicks(U)
        Return True
    End Function

    ''' <summary>读物理内存总量与已用量（MB）。</summary>
    Private Function ReadMemory(ByRef TotalMb As Integer, ByRef UsedMb As Integer) As Boolean
        Dim Status As New NativeSample.MemoryStatusEx
        Status.Length = CUInt(Marshal.SizeOf(GetType(NativeSample.MemoryStatusEx)))
        If Not NativeSample.GlobalMemoryStatusEx(Status) Then Return False
        TotalMb = CInt(Status.TotalPhys \ 1048576UL)
        UsedMb = CInt((Status.TotalPhys - Status.AvailPhys) \ 1048576UL)
        Return True
    End Function

#End Region

#Region "热键"

    ''' <summary>当前快捷键的显示文本，例如「ALT+E」。</summary>
    Public ReadOnly Property HotkeyText As String
        Get
            Return UltraVideo.FormatHotkey(HotkeyMods, HotkeyVK)
        End Get
    End Property

    ''' <summary>在主窗口上注册全局快捷键。</summary>
    Public Function RegisterHotkey(Hwnd As IntPtr) As Boolean
        If Hwnd = IntPtr.Zero Then Return False
        HotkeyHwnd = Hwnd
        Return ApplyHotkey()
    End Function

    ''' <summary>按设置重新注册快捷键。</summary>
    Public Function ApplyHotkey() As Boolean
        If HotkeyHwnd = IntPtr.Zero Then Return False
        Dim Spec As String = Settings.Get(Of String)("HudHotkey")
        If String.IsNullOrWhiteSpace(Spec) Then Spec = DefaultHotkey
        '老版本默认是 Alt+H，用户没改过就跟着换成新的 Alt+E
        If Spec.Replace(" ", "").Equals(LegacyDefaultHotkey, StringComparison.OrdinalIgnoreCase) Then Spec = DefaultHotkey
        HotkeyMods = MOD_ALT
        HotkeyVK = &H45
        UltraVideo.ParseHotkey(Spec, HotkeyMods, HotkeyVK)
        Try
            NativeHotkey.UnregisterHotKey(HotkeyHwnd, HotkeyId)
        Catch
        End Try
        HotkeyIsRegistered = False
        '和录制 / 截屏的快捷键撞了就跳过注册，免得一次按键触发两遍
        Dim MyText As String = HotkeyText
        If MyText = UltraVideo.HotkeyText OrElse MyText = UltraVideo.RegionHotkeyText OrElse
           MyText = UltraScreenshot.HotkeyText OrElse MyText = UltraScreenshot.RegionHotkeyText Then
            Logger.Warn($"UHD 快捷键 {MyText} 与其他快捷键重复，已跳过注册")
            Return False
        End If
        Try
            If NativeHotkey.RegisterHotKey(HotkeyHwnd, HotkeyId, HotkeyMods, HotkeyVK) Then
                HotkeyIsRegistered = True
                Logger.Info($"已注册 UHD 热键：{MyText}")
                Return True
            End If
            Logger.Warn($"注册 UHD 热键 {MyText} 失败（错误码 {Marshal.GetLastWin32Error()}），可能被其他软件占用")
        Catch ex As Exception
            Logger.Error(ex, "注册 UHD 热键失败")
        End Try
        Return False
    End Function

    ''' <summary>主窗口收到 WM_HOTKEY 时调用，返回是否由本模块处理。</summary>
    Public Function OnHotkey(WParam As IntPtr) As Boolean
        If WParam.ToInt64() <> HotkeyId Then Return False
        ToggleHud()
        Return True
    End Function

    Private NotInheritable Class NativeWindow
        <DllImport("user32.dll")>
        Public Shared Function IsWindow(hWnd As IntPtr) As Boolean
        End Function
    End Class

    Private NotInheritable Class NativeHotkey
        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function RegisterHotKey(hWnd As IntPtr, id As Integer, fsModifiers As Integer, vk As Integer) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function UnregisterHotKey(hWnd As IntPtr, id As Integer) As Boolean
        End Function
    End Class

#End Region

#Region "原生接口"

    Private NotInheritable Class NativeSample
        <StructLayout(LayoutKind.Sequential)>
        Public Structure FileTime
            Public Low As UInteger
            Public High As UInteger
        End Structure

        Public Shared Function ToTicks(T As FileTime) As Long
            Return (CLng(T.High) << 32) Or T.Low
        End Function

        <StructLayout(LayoutKind.Sequential)>
        Public Structure MemoryStatusEx
            Public Length As UInteger
            Public MemoryLoad As UInteger
            Public TotalPhys As ULong
            Public AvailPhys As ULong
            Public TotalPageFile As ULong
            Public AvailPageFile As ULong
            Public TotalVirtual As ULong
            Public AvailVirtual As ULong
            Public AvailExtendedVirtual As ULong
        End Structure

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function GetSystemTimes(ByRef IdleTime As FileTime, ByRef KernelTime As FileTime,
                                              ByRef UserTime As FileTime) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function GlobalMemoryStatusEx(ByRef Buffer As MemoryStatusEx) As Boolean
        End Function
    End Class

    ''' <summary>共享内存与进程注入要用到的原生接口。</summary>
    Private NotInheritable Class Native
        Public Const ProcessAllAccess As Long = &H1FFFFF
        Public Const MemCommit As Long = &H1000
        Public Const MemReserve As Long = &H2000
        Public Const MemRelease As Long = &H8000
        Public Const PageReadWrite As Long = &H4
        Public Const FileMapAllAccess As Long = &H1F001F

        Public Shared ReadOnly InvalidHandleValue As IntPtr = New IntPtr(-1)

        <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
        Public Shared Function CreateFileMapping(hFile As IntPtr, lpFileMappingAttributes As IntPtr,
                                                 flProtect As Long, dwMaximumSizeHigh As Long,
                                                 dwMaximumSizeLow As Long, lpName As String) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function MapViewOfFile(hFileMappingObject As IntPtr, dwDesiredAccess As Long,
                                             dwFileOffsetHigh As Long, dwFileOffsetLow As Long,
                                             dwNumberOfBytesToMap As Long) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function CloseHandle(hObject As IntPtr) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function UnmapViewOfFile(hBaseAddress As IntPtr) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function OpenProcess(dwDesiredAccess As Long, bInheritHandle As Boolean,
                                           dwProcessId As Long) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function VirtualAllocEx(hProcess As IntPtr, lpAddress As IntPtr, dwSize As UInteger,
                                              flAllocationType As Long, flProtect As Long) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function VirtualFreeEx(hProcess As IntPtr, lpAddress As IntPtr, dwSize As UInteger,
                                             dwFreeType As Long) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function WriteProcessMemory(hProcess As IntPtr, lpBaseAddress As IntPtr,
                                                   lpBuffer As Byte(), nSize As UInteger,
                                                   lpNumberOfBytesWritten As IntPtr) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function CreateRemoteThread(hProcess As IntPtr, lpThreadAttributes As IntPtr,
                                                  dwStackSize As UInteger, lpStartAddress As IntPtr,
                                                  lpParameter As IntPtr, dwCreationFlags As Long,
                                                  lpThreadId As IntPtr) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function WaitForSingleObject(hHandle As IntPtr, dwMilliseconds As Long) As Integer
        End Function

        <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
        Public Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Ansi)>
        Public Shared Function GetProcAddress(hModule As IntPtr, lpProcName As String) As IntPtr
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As Integer) As Integer
        End Function
    End Class

#End Region

End Module
