Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Threading

''' <summary>
''' Ultra：游戏录制。
''' 用原生桌面复制 + Media Foundation 采集，支持全屏 / 锁定游戏窗口 / 框选区域、
''' 分辨率 / 帧率 / 码率调节、游戏启动自动录制；录制状态用悬浮窗提示在游戏右上角。
''' 录制范围由设置决定，全局快捷键只负责开始与结束。
''' </summary>
Public Module UltraVideo

    '热键
    Private Const HotkeyId As Integer = &H5056
    '区域录制热键（默认 Alt + 1，可在设置里改）
    Private Const RegionHotkeyId As Integer = &H5058
    Public Const WM_HOTKEY As Integer = &H312
    Private Const MOD_ALT As Integer = 1
    Public Const MOD_CONTROL As Integer = 2
    Public Const MOD_SHIFT As Integer = 4
    Public Const MOD_WIN As Integer = 8
    Private Const WH_KEYBOARD_LL As Integer = 13
    Private Const LLKHF_ALTDOWN As Integer = &H20

    '状态
    Private RecordingFile As String = ""
    Private RecordingStart As Date
    Private RecordingAuto As Boolean
    Private RecordingTarget As IntPtr
    Private RecordingDetail As String = ""
    '本次录制是否为区域录制（区域录制用红框计时器代替提示条）
    Private RecordingIsRegion As Boolean
    Private IsMuted As Boolean = False
    Private KeyboardHookHandle As IntPtr = IntPtr.Zero
    Private KeyboardHookCallback As Native.LowLevelKeyboardProc
    Private HotkeyHeld As Boolean = False
    Private HotkeyHwnd As IntPtr = IntPtr.Zero
    Private HotkeyMods As Integer = MOD_ALT
    Private HotkeyVK As Integer = &H51
    Private HotkeyIsRegistered As Boolean = False
    '区域录制热键（默认 Alt + 1）
    Private RegionHotkeyMods As Integer = MOD_ALT
    Private RegionHotkeyVK As Integer = &H31
    Private RegionHotkeyIsRegistered As Boolean = False
    Private RegionHotkeyHeld As Boolean = False
    Private HookMods As Integer = 0

    ''' <summary>录制状态发生变化（开始 / 结束）时触发。</summary>
    Public Event StatusChanged As EventHandler

#Region "路径"

    ''' <summary>
    ''' 录制视频的保存文件夹：PCL 同目录下的 video。
    ''' </summary>
    Public ReadOnly Property VideoFolder As String
        Get
            Return Paths.Base & "video\"
        End Get
    End Property

    ''' <summary>
    ''' 确保 video 文件夹存在。
    ''' </summary>
    Public Sub EnsureFolder()
        Try
            If Not Directory.Exists(VideoFolder) Then Directory.CreateDirectory(VideoFolder)
        Catch ex As Exception
            Logger.Error(ex, "创建录制文件夹失败")
        End Try
    End Sub

#End Region

#Region "热键"

    ''' <summary>当前录制快捷键的显示文本，例如「Alt + Q」。</summary>
    Public ReadOnly Property HotkeyText As String
        Get
            Return FormatHotkey(HotkeyMods, HotkeyVK)
        End Get
    End Property

    ''' <summary>区域录制快捷键的显示文本，例如「Alt + 1」。</summary>
    Public ReadOnly Property RegionHotkeyText As String
        Get
            Return FormatHotkey(RegionHotkeyMods, RegionHotkeyVK)
        End Get
    End Property

    ''' <summary>快捷键是否成功注册为系统热键（False 表示用的是键盘钩子兜底）。</summary>
    Public ReadOnly Property HotkeyRegistered As Boolean
        Get
            Return HotkeyIsRegistered
        End Get
    End Property

    ''' <summary>
    ''' 在主窗口上注册全局热键（Alt + Z 的老逻辑改成读设置，默认 Alt + Q）。
    ''' </summary>
    Public Function RegisterHotkey(Hwnd As IntPtr) As Boolean
        If Hwnd = IntPtr.Zero Then Return False
        AddHandler UltraVideoNative.CaptureFailed, AddressOf OnNativeCaptureFailed
        HotkeyHwnd = Hwnd
        'ApplyHotkey 里会顺带把区域录制热键一起注册好
        Return ApplyHotkey()
    End Function

    ''' <summary>按设置重新注册快捷键。改完快捷键后调用。</summary>
    Public Function ApplyHotkey() As Boolean
        ParseHotkey(Settings.Get(Of String)("VideoHotkey"), HotkeyMods, HotkeyVK)
        If HotkeyHwnd = IntPtr.Zero Then Return False
        '先把区域录制那个摘掉：主热键可能刚被改成和它一样，不摘会一直抢不到
        Try
            Native.UnregisterHotKey(HotkeyHwnd, RegionHotkeyId)
        Catch
        End Try
        RegionHotkeyIsRegistered = False
        Try
            Native.UnregisterHotKey(HotkeyHwnd, HotkeyId)
        Catch
        End Try
        HotkeyIsRegistered = False
        Dim Ok As Boolean
        Try
            If Native.RegisterHotKey(HotkeyHwnd, HotkeyId, HotkeyMods, HotkeyVK) Then
                HotkeyIsRegistered = True
                Ok = True
                Logger.Info($"已注册录屏热键：{HotkeyText}")
            Else
                '被别的软件占用了，退回低级键盘钩子，热键照样可以用
                Logger.Warn($"注册录屏热键 {HotkeyText} 失败（错误码 {Marshal.GetLastWin32Error()}），改用键盘钩子监听")
                Ok = InstallKeyboardHook()
            End If
        Catch ex As Exception
            Logger.Error(ex, "注册录屏热键失败")
            Ok = False
        End Try
        '主热键落定后，再把区域录制热键补回来（撞车的话它自己会跳过）
        ApplyRegionHotkey()
        Return Ok
    End Function

    ''' <summary>
    ''' 注册区域录制热键（默认 Alt + 1）。注册失败不抛异常，交给键盘钩子兜底。
    ''' </summary>
    Public Function ApplyRegionHotkey() As Boolean
        ParseHotkey(Settings.Get(Of String)("VideoRegionHotkey"), RegionHotkeyMods, RegionHotkeyVK)
        If HotkeyHwnd = IntPtr.Zero Then Return False
        Try
            Native.UnregisterHotKey(HotkeyHwnd, RegionHotkeyId)
        Catch
        End Try
        RegionHotkeyIsRegistered = False
        '和主热键撞了就不注册，免得一次按键触发两遍
        If RegionHotkeyMods = HotkeyMods AndAlso RegionHotkeyVK = HotkeyVK Then
            Logger.Warn($"区域录制快捷键 {RegionHotkeyText} 与录制快捷键重复，已跳过注册")
            Return False
        End If
        Try
            If Native.RegisterHotKey(HotkeyHwnd, RegionHotkeyId, RegionHotkeyMods, RegionHotkeyVK) Then
                RegionHotkeyIsRegistered = True
                Logger.Info($"已注册区域录制热键：{RegionHotkeyText}")
                Return True
            End If
            Logger.Warn($"注册区域录制热键 {RegionHotkeyText} 失败（错误码 {Marshal.GetLastWin32Error()}），由键盘钩子兜底")
            Return False
        Catch ex As Exception
            Logger.Error(ex, "注册区域录制热键失败")
            Return False
        End Try
    End Function

    ''' <summary>录制范围：全屏。</summary>
    Public Const VideoModeFullScreen As Integer = 0
    ''' <summary>录制范围：锁定游戏窗口。</summary>
    Public Const VideoModeGameWindow As Integer = 1
    ''' <summary>录制范围：自定义区域（每次开始录制时框选）。</summary>
    Public Const VideoModeRegion As Integer = 2

    ''' <summary>
    ''' 兜底方案：低级键盘钩子监听配置的快捷键。
    ''' 用于 RegisterHotKey 返回 1409（已被其他软件注册）的情况。
    ''' </summary>
    Private Function InstallKeyboardHook() As Boolean
        Try
            If KeyboardHookHandle <> IntPtr.Zero Then Return True
            KeyboardHookCallback = AddressOf OnKeyboardHook
            KeyboardHookHandle = Native.SetWindowsHookEx(WH_KEYBOARD_LL, KeyboardHookCallback,
                Native.GetModuleHandle(Nothing), 0)
            If KeyboardHookHandle = IntPtr.Zero Then
                Logger.Warn($"安装键盘钩子失败（错误码 {Marshal.GetLastWin32Error()}），{HotkeyText} 将不可用")
                Return False
            End If
            Logger.Info($"已用键盘钩子监听录屏热键：{HotkeyText}")
            Return True
        Catch ex As Exception
            Logger.Error(ex, "安装键盘钩子失败")
            Return False
        End Try
    End Function

    Private Sub UpdateHookMods(VK As Integer, Down As Boolean)
        Dim Bit As Integer = 0
        Select Case VK
            Case &H12, &HA4, &HA5 : Bit = MOD_ALT
            Case &H11, &HA2, &HA3 : Bit = MOD_CONTROL
            Case &H10, &HA0, &HA1 : Bit = MOD_SHIFT
            Case &H5B, &H5C : Bit = MOD_WIN
            Case Else : Return
        End Select
        If Down Then
            HookMods = HookMods Or Bit
        Else
            HookMods = HookMods And Not Bit
        End If
    End Sub

    Private Function OnKeyboardHook(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        Try
            If nCode >= 0 Then
                Dim Message As Integer = wParam.ToInt32()
                Dim Info As Native.KBDLLHOOKSTRUCT = Marshal.PtrToStructure(Of Native.KBDLLHOOKSTRUCT)(lParam)
                If Message = &H100 OrElse Message = &H104 Then 'WM_KEYDOWN / WM_SYSKEYDOWN
                    UpdateHookMods(Info.vkCode, True)
                    If Info.vkCode = HotkeyVK AndAlso HookMods = HotkeyMods Then
                        If Not HotkeyHeld Then
                            HotkeyHeld = True
                            ToggleRecording()
                        End If
                    ElseIf Info.vkCode = RegionHotkeyVK AndAlso HookMods = RegionHotkeyMods AndAlso Not RegionHotkeyIsRegistered Then
                        '只有 RegisterHotKey 没抢到区域键时才由钩子处理，避免一次按键触发两遍
                        If Not RegionHotkeyHeld Then
                            RegionHotkeyHeld = True
                            ToggleRegionRecording()
                        End If
                    End If
                ElseIf Message = &H101 OrElse Message = &H105 Then 'WM_KEYUP / WM_SYSKEYUP
                    UpdateHookMods(Info.vkCode, False)
                    If Info.vkCode = HotkeyVK Then HotkeyHeld = False
                    If Info.vkCode = RegionHotkeyVK Then RegionHotkeyHeld = False
                End If
            End If
        Catch ex As Exception
            Logger.Error(ex, "录屏热键钩子出错")
        End Try
        Return Native.CallNextHookEx(KeyboardHookHandle, nCode, wParam, lParam)
    End Function

    ''' <summary>
    ''' 主窗口收到 WM_HOTKEY 时调用。返回是否吃掉了该消息。
    ''' </summary>
    Public Function OnHotkey(WParam As IntPtr) As Boolean
        Dim Id As Integer = WParam.ToInt32()
        If Id = HotkeyId Then
            ToggleRecording()
            Return True
        End If
        If Id = RegionHotkeyId Then
            ToggleRegionRecording()
            Return True
        End If
        Return False
    End Function

    ''' <summary>把「Alt+Q」这类文本解析成修饰键与虚拟键码，失败时保持原值。</summary>
    Public Sub ParseHotkey(Spec As String, ByRef Mods As Integer, ByRef VK As Integer)
        If String.IsNullOrWhiteSpace(Spec) Then Return
        Dim NewMods As Integer = 0
        Dim NewVK As Integer = 0
        For Each Raw As String In Spec.Split("+"c)
            Dim Part As String = Raw.Trim().ToUpperInvariant()
            Select Case Part
                Case "ALT" : NewMods = NewMods Or MOD_ALT
                Case "CTRL", "CONTROL" : NewMods = NewMods Or MOD_CONTROL
                Case "SHIFT" : NewMods = NewMods Or MOD_SHIFT
                Case "WIN", "WINDOWS" : NewMods = NewMods Or MOD_WIN
                Case Else
                    Dim Value As Integer = KeyNameToVK(Part)
                    If Value > 0 Then NewVK = Value
            End Select
        Next
        If NewVK > 0 Then
            Mods = NewMods
            VK = NewVK
        End If
    End Sub

    ''' <summary>按键名（A / 0 / F5 / SPACE）转虚拟键码，认不出返回 0。</summary>
    Public Function KeyNameToVK(Name As String) As Integer
        If String.IsNullOrEmpty(Name) Then Return 0
        Name = Name.Trim().ToUpperInvariant()
        If Name.Length = 1 Then
            Dim C As Char = Name(0)
            If (C >= "A"c AndAlso C <= "Z"c) OrElse (C >= "0"c AndAlso C <= "9"c) Then Return AscW(C)
        End If
        If Name.Length <= 3 AndAlso Name.StartsWith("F") Then
            Dim N As Integer
            If Integer.TryParse(Name.Substring(1), N) AndAlso N >= 1 AndAlso N <= 12 Then Return &H6F + N
        End If
        Select Case Name
            Case "SPACE", "空格" : Return &H20
            Case "TAB" : Return &H09
            Case "ENTER" : Return &H0D
            Case "UP", "↑" : Return &H26
            Case "DOWN", "↓" : Return &H28
            Case "LEFT", "←" : Return &H25
            Case "RIGHT", "→" : Return &H27
            Case "INSERT" : Return &H2D
            Case "DELETE" : Return &H2E
            Case "HOME" : Return &H24
            Case "END" : Return &H23
        End Select
        Return 0
    End Function

    ''' <summary>虚拟键码转显示名。</summary>
    Public Function VKToKeyName(VK As Integer) As String
        If VK >= &H41 AndAlso VK <= &H5A Then Return ChrW(VK)
        If VK >= &H30 AndAlso VK <= &H39 Then Return ChrW(VK)
        If VK >= &H70 AndAlso VK <= &H7B Then Return "F" & (VK - &H6F)
        Select Case VK
            Case &H20 : Return "Space"
            Case &H09 : Return "Tab"
            Case &H0D : Return "Enter"
            Case &H26 : Return "Up"
            Case &H28 : Return "Down"
            Case &H25 : Return "Left"
            Case &H27 : Return "Right"
            Case &H2D : Return "Insert"
            Case &H2E : Return "Delete"
            Case &H24 : Return "Home"
            Case &H23 : Return "End"
        End Select
        Return "Key" & VK
    End Function

    ''' <summary>修饰键 + 虚拟键码拼成显示文本（Alt + Q）。</summary>
    Public Function FormatHotkey(Mods As Integer, VK As Integer) As String
        Dim Parts As New List(Of String)
        If (Mods And MOD_CONTROL) <> 0 Then Parts.Add("Ctrl")
        If (Mods And MOD_ALT) <> 0 Then Parts.Add("Alt")
        If (Mods And MOD_SHIFT) <> 0 Then Parts.Add("Shift")
        If (Mods And MOD_WIN) <> 0 Then Parts.Add("Win")
        Parts.Add(VKToKeyName(VK))
        Return String.Join(" + ", Parts)
    End Function

    ''' <summary>修饰键 + 虚拟键码拼成设置里存的文本（Alt+Q）。</summary>
    Public Function BuildHotkeySpec(Mods As Integer, VK As Integer) As String
        Dim Parts As New List(Of String)
        If (Mods And MOD_CONTROL) <> 0 Then Parts.Add("Ctrl")
        If (Mods And MOD_ALT) <> 0 Then Parts.Add("Alt")
        If (Mods And MOD_SHIFT) <> 0 Then Parts.Add("Shift")
        If (Mods And MOD_WIN) <> 0 Then Parts.Add("Win")
        Parts.Add(VKToKeyName(VK))
        Return String.Join("+", Parts)
    End Function

#End Region

#Region "录制状态"

    ''' <summary>当前是否正在录制。</summary>
    Public ReadOnly Property IsRecording As Boolean
        Get
            Return UltraVideoNative.IsRunning
        End Get
    End Property

    ''' <summary>当前录制的信息，例如「1920×1080 · 60 FPS · 12 Mbps」。</summary>
    Public ReadOnly Property RecordingInfo As String
        Get
            Return RecordingDetail
        End Get
    End Property

    ''' <summary>当前录制的文件名。</summary>
    Public ReadOnly Property RecordingName As String
        Get
            Return If(RecordingFile = "", "", Path.GetFileName(RecordingFile))
        End Get
    End Property

    ''' <summary>已录制的时长。</summary>
    Public ReadOnly Property RecordingElapsed As TimeSpan
        Get
            If Not UltraVideoNative.IsRunning Then Return TimeSpan.Zero
            Return Date.Now - RecordingStart
        End Get
    End Property

#End Region

#Region "开始与结束"

    Private Stopping As Boolean = False

    ''' <summary>
    ''' 开始录制。
    ''' </summary>
    ''' <param name="Auto">是否由「游戏启动自动录制」触发。</param>
    ''' <param name="Region">显式指定的录制区域（Alt + 1 区域录制）。传 Nothing 时按「录制范围」设置决定全屏、锁定窗口还是复用上次框选的区域。</param>
    Public Sub StartRecording(Optional Auto As Boolean = False, Optional Region As GameArea? = Nothing)
        If IsRecording OrElse Stopping Then Return
        EnsureFolder()

        '录制区域：显式传入的区域优先（Alt + 1 区域录制）
        Dim GameWindow As IntPtr = GetGameWindow()
        Dim Mode As Integer = Settings.Get(Of Integer)("VideoMode")
        '区域录制才画红框与计时器；「锁定游戏窗口」不画，免得盖在游戏画面上
        Dim IsRegion As Boolean = Region.HasValue
        Dim Area As GameArea? = Region
        If Not Area.HasValue Then
            Select Case Mode
                Case VideoModeGameWindow
                    If GameWindow <> IntPtr.Zero Then
                        Area = GetWindowArea(GameWindow)
                    Else
                        Hint("没有找到正在运行的游戏窗口，本次改为全屏录制", HintType.Blue, False)
                    End If
                Case VideoModeRegion
                    '录制范围选了「区域」：每次开始录制时重新框一次
                    IsRegion = True
                    If Auto Then
                        '自动录制不该弹全屏浮层抢游戏焦点，没有区域就退回全屏
                        Hint("自动录制不能框选区域，本次改为全屏录制", HintType.Blue, False)
                    Else
                        Dim Picked As GameArea? = WindowRegionSelect.Pick("录制")
                        If Not Picked.HasValue Then Return
                        Area = Picked
                    End If
            End Select
        End If
        If Area.HasValue AndAlso (Area.Value.Width < 16 OrElse Area.Value.Height < 16) Then Area = Nothing
        '退回全屏就不是区域录制了，别再画红框
        If Not Area.HasValue Then IsRegion = False

        Dim Left As Integer, Top As Integer, Width As Integer, Height As Integer
        If Area.HasValue Then
            Left = Area.Value.X
            Top = Area.Value.Y
            Width = Area.Value.Width
            Height = Area.Value.Height
        Else
            Left = 0
            Top = 0
            Width = ScreenWidth
            Height = ScreenHeight
        End If

        '设置先在界面线程读，免得后台线程和设置文件打架
        Dim TargetHeight As Integer = Settings.Get(Of Integer)("VideoHeight")
        '区域录制按框选原尺寸输出，不放大也不缩小
        If IsRegion Then TargetHeight = 0
        Dim Fps As Integer = Settings.Get(Of Integer)("VideoFps")
        Dim Bitrate As Integer = Settings.Get(Of Integer)("VideoBitrate")
        Dim Codec As Integer = Settings.Get(Of Integer)("VideoCodec")
        Dim RecordAudio As Boolean = Settings.Get(Of Boolean)("VideoAudio")
        Dim OutputFile As String = VideoFolder & $"MC_{Date.Now:yyyyMMdd_HHmmss}.mp4"

        '初始化录制内核要一两秒，放后台跑，界面不会被卡住
        RunInNewThread(
        Sub()
            Dim Result As String = UltraVideoNative.StartRecording(OutputFile, Left, Top, Width, Height,
                TargetHeight, Fps, Bitrate, Codec, RecordAudio)
            Dispatch(
            Sub()
                If Result <> "" Then
                    Hint("录制启动失败：" & Result, HintType.Red, True)
                    Return
                End If
                If Codec = 1 AndAlso UltraVideoNative.ActualCodec = 0 Then
                    Hint("本机没有可用的 H.265 编码器，本次用 H.264 录制", HintType.Blue, False)
                End If
                If UltraVideoNative.ActualFrameRate > 0 AndAlso UltraVideoNative.ActualFrameRate < Fps Then
                    Hint($"这个分辨率下最高只能录 {UltraVideoNative.ActualFrameRate} 帧，本次按 {UltraVideoNative.ActualFrameRate} 帧录制", HintType.Blue, False)
                End If
                If RecordAudio AndAlso Not UltraVideoNative.IsAudioActive Then
                    Hint("当前播放设备无法录制声音，本次仅录画面", HintType.Blue, False)
                End If
                RecordingFile = OutputFile
                RecordingStart = Date.Now
                RecordingAuto = Auto
                RecordingIsRegion = IsRegion
                RecordingTarget = If(Area.HasValue AndAlso Not IsRegion, GameWindow, IntPtr.Zero)
                RecordingDetail = UltraVideoNative.EncoderName
                Dim AreaText As String = If(IsRegion, "区域 " & $"{Width}×{Height}",
                                             If(Area.HasValue, "锁定游戏窗口", "全屏"))
                Logger.Info($"开始录制：{OutputFile}（{AreaText}，{RecordingDetail}）")
                If RecordingIsRegion Then
                    WindowRecordFrame.ShowFrame(Area.Value)
                Else
                    '全屏 / 锁定游戏窗口：画面右上角只显示录制时长，靠 WDA 排除不进画面
                    WindowRecordFrame.ShowTimerOnly(New GameArea With {
                        .X = Left, .Y = Top, .Width = Width, .Height = Height})
                End If
                '提示条已经排除在捕获之外，区域录制也照常弹
                ShowToast("录制已开始", RecordingTarget)
                RaiseStatus()
            End Sub)
        End Sub, "VideoStart")
    End Sub

    ''' <summary>
    ''' 结束录制。文件会在后台写完，界面不会卡。
    ''' </summary>
    ''' <param name="Silent">不弹出结束提示。</param>
    Public Sub StopRecording(Optional Silent As Boolean = False)
        If Not UltraVideoNative.IsRunning Then Return
        If Stopping Then Return
        Stopping = True
        Dim OutputPath As String = RecordingFile
        Dim Elapsed As TimeSpan = Date.Now - RecordingStart
        Dim Target As IntPtr = RecordingTarget
        RecordingFile = ""
        RecordingTarget = IntPtr.Zero
        RecordingAuto = False
        RecordingIsRegion = False
        '结束后收掉红框与右上角计时器
        WindowRecordFrame.HideFrame()
        If Not Silent Then ShowToast("录制已结束", Target)
        RunInNewThread(
        Sub()
            Try
                UltraVideoNative.StopRecording()
            Catch ex As Exception
                Logger.Error(ex, "结束录制时出错")
            End Try
            Dim SizeText As String = ""
            Try
                If System.IO.File.Exists(OutputPath) Then
                    SizeText = $"，{New FileInfo(OutputPath).Length / 1024 / 1024} MB"
                End If
            Catch
            End Try
            Logger.Info($"录制已保存：{OutputPath}（时长 {CInt(Elapsed.TotalMinutes)} 分 {Elapsed.Seconds} 秒{SizeText}）")
            Stopping = False
            Dispatch(
            Sub()
                RaiseStatus()
            End Sub)
        End Sub, "VideoStop")
    End Sub

    ''' <summary>
    ''' 开始或结束录制（Alt + Z 与页面按钮共用）。
    ''' </summary>
    Public Sub ToggleRecording()
        Dispatch(
        Sub()
            If IsRecording Then StopRecording() Else StartRecording()
        End Sub)
    End Sub

    ''' <summary>
    ''' 区域录制快捷键：正在录制就停止；否则先弹出框选浮层，按框出的区域开始录制。
    ''' 这个键不分「录制范围」设置，永远走框选。
    ''' </summary>
    Public Sub ToggleRegionRecording()
        Dispatch(
        Sub()
            If IsRecording Then
                StopRecording()
                Return
            End If
            Dim Area As GameArea? = WindowRegionSelect.Pick("录制")
            If Not Area.HasValue Then Return
            StartRecording(Region:=Area)
        End Sub)
    End Sub

    ''' <summary>
    ''' 游戏窗口已就绪（由 Watcher 调用）。若开启了自动录制则稍等片刻后开始。
    ''' </summary>
    Public Sub OnGameWindowReady(Window As IntPtr)
        Try
            If Window = IntPtr.Zero Then Return
            If Not Settings.Get(Of Boolean)("VideoAutoRecord") Then Return
            If IsRecording Then Return
            RunInNewThread(
            Sub()
                '等游戏把画面画出来，不然前几秒是黑屏
                Thread.Sleep(2500)
                If McLaunchProcess Is Nothing OrElse McLaunchProcess.HasExited Then Return
                Dispatch(
                Sub()
                    If IsRecording Then Return
                    StartRecording(Auto:=True)
                End Sub)
            End Sub, "VideoAutoRecord")
        Catch ex As Exception
            Logger.Error(ex, "触发自动录制失败")
        End Try
    End Sub

    ''' <summary>
    ''' 游戏已退出：如果是自动录制，就跟着停。
    ''' </summary>
    Public Sub OnGameExited()
        Try
            If Not IsRecording Then Return
            If Not RecordingAuto Then Return
            Dispatch(Sub() StopRecording())
        Catch ex As Exception
            Logger.Error(ex, "游戏退出后停止录制失败")
        End Try
    End Sub

#End Region

#Region "悬浮提示"

    ''' <summary>录制线程自己停了（编码器出错 / 桌面复制中断），把状态收干净。</summary>
    Private Sub OnNativeCaptureFailed(sender As Object, e As EventArgs)
        If RecordingFile = "" Then Return
        RecordingFile = ""
        RecordingAuto = False
        RecordingIsRegion = False
        RecordingTarget = IntPtr.Zero
        '录制中断了，红框和计时器也得跟着收掉
        WindowRecordFrame.HideFrame()
        Dim Reason As String = UltraVideoNative.LastError
        Logger.Warn("录制意外结束：" & Reason)
        Dispatch(
        Sub()
            RaiseStatus()
            Hint("录制意外结束：" & If(Reason = "", "未知原因", Reason), HintType.Red, False)
        End Sub)
    End Sub

    Private Sub ShowToast(Text As String, Target As IntPtr)
        If IsMuted Then Return
        Try
            WindowVideoToast.ShowTip(Text, Target)
        Catch ex As Exception
            Logger.Error(ex, "显示录制提示失败")
        End Try
    End Sub

    ''' <summary>静音所有录屏提示（程序退出时使用）。</summary>
    Public Sub Mute()
        IsMuted = True
        WindowRecordFrame.HideFrame()
    End Sub

#End Region

#Region "工具"

    ''' <summary>
    ''' 当前正在运行的游戏窗口句柄。没找到时返回 IntPtr.Zero。
    ''' </summary>
    Public Function GetGameWindow() As IntPtr
        Try
            For i As Integer = McWatcherList.Count - 1 To 0 Step -1
                Dim Item As Watcher = McWatcherList(i)
                If Item.State = Watcher.MinecraftState.Running AndAlso Item.MinecraftWindowHandle <> IntPtr.Zero Then
                    Return Item.MinecraftWindowHandle
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取游戏窗口句柄失败")
        End Try
        Try
            If McLaunchProcess IsNot Nothing AndAlso Not McLaunchProcess.HasExited Then
                Dim Handle As IntPtr = McLaunchProcess.MainWindowHandle
                If Handle <> IntPtr.Zero Then Return Handle
            End If
        Catch ex As Exception
            Logger.Warn(ex, "从游戏进程读取窗口句柄失败")
        End Try
        Return IntPtr.Zero
    End Function

    ''' <summary>
    ''' 取窗口客户区在屏幕上的位置与大小（不含标题栏与边框）。
    ''' </summary>
    Public Function GetWindowArea(Window As IntPtr) As GameArea?
        Try
            Dim ClientRect As New Native.RECT
            If Not Native.GetClientRect(Window, ClientRect) Then Return Nothing
            Dim Origin As New Native.POINT With {.X = 0, .Y = 0}
            If Not Native.ClientToScreen(Window, Origin) Then Return Nothing
            Dim Width As Integer = ClientRect.Right - ClientRect.Left
            Dim Height As Integer = ClientRect.Bottom - ClientRect.Top
            If Width <= 0 OrElse Height <= 0 Then Return Nothing
            Return New GameArea With {.X = Origin.X, .Y = Origin.Y, .Width = Width, .Height = Height}
        Catch ex As Exception
            Logger.Warn(ex, "读取游戏窗口位置失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>主屏幕的物理像素宽度。</summary>
    Public ReadOnly Property ScreenWidth As Integer
        Get
            Return Native.GetSystemMetrics(0)
        End Get
    End Property

    ''' <summary>主屏幕的物理像素高度。</summary>
    Public ReadOnly Property ScreenHeight As Integer
        Get
            Return Native.GetSystemMetrics(1)
        End Get
    End Property

    Private Sub RaiseStatus()
        RaiseEvent StatusChanged(Nothing, EventArgs.Empty)
    End Sub

    Private Sub Dispatch(Action As Action)
        Try
            If FrmMain Is Nothing Then Return
            Dim Dispatcher As Dispatcher = FrmMain.Dispatcher
            If Dispatcher Is Nothing Then Return
            If Dispatcher.CheckAccess() Then
                Action()
            Else
                Dispatcher.BeginInvoke(Action, DispatcherPriority.Normal)
            End If
        Catch ex As Exception
            Logger.Error(ex, "调度录屏操作失败")
        End Try
    End Sub

    ''' <summary>
    ''' 是否给浮层加「不参与截屏 / 录屏」。设了环境变量 PCL_FRAME_NO_WDA 就关掉，便于截图验证浮层。
    ''' </summary>
    Private UseCaptureExcludeCache As Boolean? = Nothing
    Public ReadOnly Property UseCaptureExclude As Boolean
        Get
            If Not UseCaptureExcludeCache.HasValue Then
                UseCaptureExcludeCache = (Environment.GetEnvironmentVariable("PCL_FRAME_NO_WDA") Is Nothing)
            End If
            Return UseCaptureExcludeCache.Value
        End Get
    End Property

    ''' <summary>
    ''' 标记窗口「不参与截屏 / 录屏」（WDA_EXCLUDEFROMCAPTURE，需要 Win10 2004 以上）。
    ''' 失败只记一条警告，窗口照常显示。
    ''' </summary>
    Public Sub SetWindowCaptureExclude(Win As Window, Exclude As Boolean)
        If Win Is Nothing Then Return
        Try
            Dim Hwnd As IntPtr = New Interop.WindowInteropHelper(Win).Handle
            If Hwnd = IntPtr.Zero Then Return
            Dim Affinity As UInteger = If(Exclude, &H11UI, 0UI)
            If Not Native.SetWindowDisplayAffinity(Hwnd, Affinity) Then
                Logger.Warn($"设置窗口捕获排除失败（affinity={Affinity}，错误码 {Marshal.GetLastWin32Error()}）")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "设置窗口捕获排除时出错")
        End Try
    End Sub

    ''' <summary>
    ''' 把窗口标记成不抢焦点、不占任务栏、可穿透鼠标的浮层。
    ''' </summary>
    Public Sub MarkOverlayWindow(Hwnd As IntPtr)
        Try
            Const GWL_EXSTYLE As Integer = -20
            Const WS_EX_TRANSPARENT As Integer = &H20
            Const WS_EX_TOOLWINDOW As Integer = &H80
            Const WS_EX_NOACTIVATE As Integer = &H8000000
            Dim Style As Integer = Native.GetWindowLong(Hwnd, GWL_EXSTYLE)
            Native.SetWindowLong(Hwnd, GWL_EXSTYLE, Style Or WS_EX_TRANSPARENT Or WS_EX_TOOLWINDOW Or WS_EX_NOACTIVATE)
        Catch ex As Exception
            Logger.Warn(ex, "设置录制浮窗样式失败")
        End Try
    End Sub


#End Region

#Region "Win32"

    ''' <summary>屏幕上的矩形区域（物理像素）。</summary>
    Public Structure GameArea
        Public X As Integer
        Public Y As Integer
        Public Width As Integer
        Public Height As Integer
    End Structure

    Private NotInheritable Class Native

        <StructLayout(LayoutKind.Sequential)>
        Public Structure RECT
            Public Left As Integer
            Public Top As Integer
            Public Right As Integer
            Public Bottom As Integer
        End Structure

        Public Delegate Function LowLevelKeyboardProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

        <StructLayout(LayoutKind.Sequential)>
        Public Structure KBDLLHOOKSTRUCT
            Public vkCode As Integer
            Public scanCode As Integer
            Public flags As Integer
            Public time As Integer
            Public dwExtraInfo As IntPtr
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure POINT
            Public X As Integer
            Public Y As Integer
        End Structure

        <DllImport("user32.dll")>
        Public Shared Function GetClientRect(hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function ClientToScreen(hWnd As IntPtr, ByRef lpPoint As POINT) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function RegisterHotKey(hWnd As IntPtr, id As Integer, fsModifiers As Integer, vk As Integer) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function UnregisterHotKey(hWnd As IntPtr, id As Integer) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function GetSystemMetrics(nIndex As Integer) As Integer
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function SetWindowsHookEx(idHook As Integer, lpfn As LowLevelKeyboardProc, hMod As IntPtr, dwThreadId As Integer) As IntPtr
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function CallNextHookEx(hhk As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function UnhookWindowsHookEx(hhk As IntPtr) As Boolean
        End Function

        <DllImport("kernel32.dll", CharSet:=CharSet.Auto)>
        Public Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
        End Function

        <DllImport("user32.dll")>
        Public Shared Function GetWindowLong(hWnd As IntPtr, nIndex As Integer) As Integer
        End Function

        <DllImport("user32.dll")>
        Public Shared Function SetWindowLong(hWnd As IntPtr, nIndex As Integer, dwNewLong As Integer) As Integer
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function SetWindowDisplayAffinity(hWnd As IntPtr, dwAffinity As UInteger) As Boolean
        End Function

    End Class

#End Region

End Module
