Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Threading

''' <summary>
''' Ultra：游戏截屏（默认 Alt + W）。
''' 用 GDI 抓一帧屏幕 / 游戏窗口客户区，按设置缩放到目标高度后存成 PNG 或 JPG，
''' 保存在 PCL 目录下的 photo 文件夹。截完复用录制提示条弹「已截屏」。
''' 全程原生，不依赖 ffmpeg，也不占用录制用的 Media Foundation / D3D 管线。
''' </summary>
Public Module UltraScreenshot

    Private Const HotkeyId As Integer = &H5057
    Private Const MOD_ALT As Integer = 1
    Private Const MOD_CONTROL As Integer = 2
    Private Const MOD_SHIFT As Integer = 4
    Private Const MOD_WIN As Integer = 8
    Private Const WH_KEYBOARD_LL As Integer = 13

    ''' <summary>默认快捷键。</summary>
    Public Const DefaultHotkey As String = "Alt+W"

    Private HotkeyHwnd As IntPtr = IntPtr.Zero
    Private HotkeyMods As Integer = MOD_ALT
    Private HotkeyVK As Integer = &H57
    Private HotkeyIsRegistered As Boolean = False
    Private KeyboardHookHandle As IntPtr = IntPtr.Zero
    Private KeyboardHookCallback As Native.LowLevelKeyboardProc
    Private HotkeyHeld As Boolean = False
    Private HookMods As Integer = 0
    Private Busy As Boolean = False
    Private IsMuted As Boolean = False

    ''' <summary>最近一次截屏的文件路径。</summary>
    Public LastFile As String = ""
    ''' <summary>最近一次截屏的说明，例如「1920×1080 · 2.1 MB」。</summary>
    Public LastInfo As String = ""
    ''' <summary>截屏完成后触发（界面线程）。</summary>
    Public Event ScreenshotTaken As EventHandler

#Region "路径"

    ''' <summary>截屏图片文件夹：PCL 同目录下的 photo。</summary>
    Public ReadOnly Property PhotoFolder As String
        Get
            Return Paths.Base & "photo\"
        End Get
    End Property

    ''' <summary>确保 photo 文件夹存在。</summary>
    Public Sub EnsureFolder()
        Try
            If Not Directory.Exists(PhotoFolder) Then Directory.CreateDirectory(PhotoFolder)
        Catch ex As Exception
            Logger.Error(ex, "创建 photo 文件夹失败")
        End Try
    End Sub

#End Region

#Region "热键"

    ''' <summary>当前截屏快捷键的显示文本，例如「Alt + W」。</summary>
    Public ReadOnly Property HotkeyText As String
        Get
            Return UltraVideo.FormatHotkey(HotkeyMods, HotkeyVK)
        End Get
    End Property

    ''' <summary>在主窗口上注册全局截屏热键。</summary>
    Public Function RegisterHotkey(Hwnd As IntPtr) As Boolean
        If Hwnd = IntPtr.Zero Then Return False
        HotkeyHwnd = Hwnd
        Return ApplyHotkey()
    End Function

    ''' <summary>按设置重新注册快捷键。</summary>
    Public Function ApplyHotkey() As Boolean
        Dim Spec As String = Settings.Get(Of String)("ScreenshotHotkey")
        If String.IsNullOrWhiteSpace(Spec) Then Spec = DefaultHotkey
        HotkeyMods = MOD_ALT
        HotkeyVK = &H57
        UltraVideo.ParseHotkey(Spec, HotkeyMods, HotkeyVK)
        If HotkeyHwnd = IntPtr.Zero Then Return False
        Try
            Native.UnregisterHotKey(HotkeyHwnd, HotkeyId)
        Catch
        End Try
        HotkeyIsRegistered = False
        Try
            If Native.RegisterHotKey(HotkeyHwnd, HotkeyId, HotkeyMods, HotkeyVK) Then
                HotkeyIsRegistered = True
                Logger.Info($"已注册截屏热键：{HotkeyText}")
                Return True
            End If
            Logger.Warn($"注册截屏热键 {HotkeyText} 失败（错误码 {Marshal.GetLastWin32Error()}），改用键盘钩子监听")
            Return InstallKeyboardHook()
        Catch ex As Exception
            Logger.Error(ex, "注册截屏热键失败")
            Return False
        End Try
    End Function

    ''' <summary>RegisterHotKey 被别的软件占用时的兜底。</summary>
    Private Function InstallKeyboardHook() As Boolean
        Try
            If KeyboardHookHandle <> IntPtr.Zero Then Return True
            KeyboardHookCallback = AddressOf OnKeyboardHook
            KeyboardHookHandle = Native.SetWindowsHookEx(WH_KEYBOARD_LL, KeyboardHookCallback,
                Native.GetModuleHandle(Nothing), 0)
            If KeyboardHookHandle = IntPtr.Zero Then
                Logger.Warn($"安装截屏键盘钩子失败（错误码 {Marshal.GetLastWin32Error()}），{HotkeyText} 将不可用")
                Return False
            End If
            Logger.Info($"已用键盘钩子监听截屏热键：{HotkeyText}")
            Return True
        Catch ex As Exception
            Logger.Error(ex, "安装截屏键盘钩子失败")
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
                If Message = &H100 OrElse Message = &H104 Then
                    UpdateHookMods(Info.vkCode, True)
                    If Info.vkCode = HotkeyVK AndAlso HookMods = HotkeyMods Then
                        If Not HotkeyHeld Then
                            HotkeyHeld = True
                            TakeScreenshot()
                        End If
                    End If
                ElseIf Message = &H101 OrElse Message = &H105 Then
                    UpdateHookMods(Info.vkCode, False)
                    If Info.vkCode = HotkeyVK Then HotkeyHeld = False
                End If
            End If
        Catch ex As Exception
            Logger.Error(ex, "截屏热键钩子出错")
        End Try
        Return Native.CallNextHookEx(KeyboardHookHandle, nCode, wParam, lParam)
    End Function

    ''' <summary>主窗口收到 WM_HOTKEY 时调用。返回是否吃掉了该消息。</summary>
    Public Function OnHotkey(WParam As IntPtr) As Boolean
        If WParam.ToInt32() <> HotkeyId Then Return False
        TakeScreenshot()
        Return True
    End Function

#End Region

#Region "截屏"

    ''' <summary>截一张图（热键、页面按钮共用）。</summary>
    Public Sub TakeScreenshot()
        Dispatch(Sub() TakeScreenshotInternal())
    End Sub

    Private Sub TakeScreenshotInternal()
        If Busy Then Return
        Busy = True
        Try
            '设置与窗口位置都在界面线程读，免得后台线程和设置文件、UI 打架
            Dim Mode As Integer = Settings.Get(Of Integer)("ScreenshotMode")
            Dim TargetH As Integer = Settings.Get(Of Integer)("ScreenshotHeight")
            Dim Fmt As Integer = Settings.Get(Of Integer)("ScreenshotFormat")
            Dim WithCursor As Boolean = Settings.Get(Of Boolean)("ScreenshotCursor")
            Dim ShowToast As Boolean = Settings.Get(Of Boolean)("ScreenshotToast")

            Dim X As Integer = 0, Y As Integer = 0
            Dim W As Integer = UltraVideo.ScreenWidth, H As Integer = UltraVideo.ScreenHeight
            Dim Target As IntPtr = IntPtr.Zero
            If Mode = 1 Then
                Target = UltraVideo.GetGameWindow()
                Dim Area As UltraVideo.GameArea? = Nothing
                If Target <> IntPtr.Zero Then Area = UltraVideo.GetWindowArea(Target)
                If Area.HasValue AndAlso Area.Value.Width >= 16 AndAlso Area.Value.Height >= 16 Then
                    X = Area.Value.X
                    Y = Area.Value.Y
                    W = Area.Value.Width
                    H = Area.Value.Height
                Else
                    Target = IntPtr.Zero
                    Hint("没有找到正在运行的游戏窗口，本次截取全屏", HintType.Blue, False)
                End If
            End If
            If W < 16 OrElse H < 16 Then
                Hint("屏幕尺寸读取失败，无法截屏", HintType.Red, False)
                Busy = False
                Return
            End If

            EnsureFolder()
            Dim Ext As String = If(Fmt = 1, ".jpg", ".png")
            Dim OutFile As String = PhotoFolder & $"MC_{Date.Now:yyyyMMdd_HHmmss_fff}{Ext}"

            '提示条是置顶窗口，抓屏前先立刻藏起来，别把它截进图片里
            Try
                WindowVideoToast.HideNow()
            Catch
            End Try

            RunInNewThread(
            Sub()
                '等浮窗真正从屏幕上消失再抓
                Thread.Sleep(150)
                Dim Info As String = ""
                Dim ErrorText As String = ""
                Dim OutW As Integer = W, OutH As Integer = H
                Try
                    Using Raw As New Bitmap(W, H, PixelFormat.Format32bppArgb)
                        Using G As Graphics = Graphics.FromImage(Raw)
                            G.CopyFromScreen(X, Y, 0, 0, New Size(W, H), CopyPixelOperation.SourceCopy)
                            If WithCursor Then DrawCursor(G, X, Y)
                        End Using
                        If TargetH > 0 AndAlso TargetH <> H Then
                            OutH = TargetH
                            OutW = CInt(Math.Round(W * CDbl(TargetH) / H))
                            If OutW < 2 Then OutW = 2
                        End If
                        Using Final As Bitmap = If(OutW = W AndAlso OutH = H,
                                                   New Bitmap(Raw),
                                                   ScaleImage(Raw, OutW, OutH))
                            SaveImage(Final, OutFile, Fmt)
                        End Using
                    End Using
                    Dim SizeKb As Double = New FileInfo(OutFile).Length / 1024
                    Info = $"{OutW}×{OutH} · " & If(SizeKb >= 1024, $"{SizeKb / 1024:F1} MB", $"{SizeKb:F0} KB")
                Catch ex As Exception
                    ErrorText = ex.Message
                    Logger.Error(ex, "截屏失败")
                End Try
                Dispatch(
                Sub()
                    Busy = False
                    If ErrorText <> "" Then
                        Hint("截屏失败：" & ErrorText, HintType.Red, True)
                        Return
                    End If
                    LastFile = OutFile
                    LastInfo = Info
                    Logger.Info($"截屏已保存：{OutFile}（{Info}）")
                    If ShowToast AndAlso Not IsMuted Then
                        Try
                            WindowVideoToast.ShowTip("已截屏", Target, True)
                        Catch ex As Exception
                            Logger.Error(ex, "显示截屏提示失败")
                        End Try
                    End If
                    RaiseEvent ScreenshotTaken(Nothing, EventArgs.Empty)
                End Sub)
            End Sub, "Screenshot")
        Catch ex As Exception
            Busy = False
            Logger.Error(ex, "截屏启动失败")
            Hint("截屏失败：" & ex.Message, HintType.Red, True)
        End Try
    End Sub

    ''' <summary>高质量缩放。</summary>
    Private Function ScaleImage(Src As Bitmap, W As Integer, H As Integer) As Bitmap
        Dim Out As New Bitmap(W, H, PixelFormat.Format32bppArgb)
        Using G As Graphics = Graphics.FromImage(Out)
            G.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
            G.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality
            G.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality
            G.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality
            G.DrawImage(Src, New Rectangle(0, 0, W, H), New Rectangle(0, 0, Src.Width, Src.Height), GraphicsUnit.Pixel)
        End Using
        Return Out
    End Function

    Private Sub SaveImage(Bmp As Bitmap, File As String, Fmt As Integer)
        If Fmt = 1 Then
            Dim Codec As ImageCodecInfo = Nothing
            For Each Item As ImageCodecInfo In ImageCodecInfo.GetImageEncoders()
                If Item.MimeType = "image/jpeg" Then
                    Codec = Item
                    Exit For
                End If
            Next
            If Codec IsNot Nothing Then
                Using Params As New EncoderParameters(1)
                    Params.Param(0) = New EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L)
                    Bmp.Save(File, Codec, Params)
                End Using
            Else
                Bmp.Save(File, ImageFormat.Jpeg)
            End If
        Else
            Bmp.Save(File, ImageFormat.Png)
        End If
    End Sub

    ''' <summary>CopyFromScreen 不含鼠标指针，按需补画一个。</summary>
    Private Sub DrawCursor(G As Graphics, OffsetX As Integer, OffsetY As Integer)
        Try
            Dim Info As New Native.CURSORINFO With {.cbSize = Marshal.SizeOf(GetType(Native.CURSORINFO))}
            If Not Native.GetCursorInfo(Info) Then Return
            If Info.flags <> 1 OrElse Info.hCursor = IntPtr.Zero Then Return
            Dim IconInfo As New Native.ICONINFO
            If Not Native.GetIconInfo(Info.hCursor, IconInfo) Then Return
            Try
                Dim Hdc As IntPtr = G.GetHdc()
                Try
                    Native.DrawIcon(Hdc, Info.ptScreenPos.X - OffsetX - IconInfo.xHotspot,
                                    Info.ptScreenPos.Y - OffsetY - IconInfo.yHotspot, Info.hCursor)
                Finally
                    G.ReleaseHdc(Hdc)
                End Try
            Finally
                If IconInfo.hbmMask <> IntPtr.Zero Then Native.DeleteObject(IconInfo.hbmMask)
                If IconInfo.hbmColor <> IntPtr.Zero Then Native.DeleteObject(IconInfo.hbmColor)
            End Try
        Catch ex As Exception
            Logger.Warn(ex, "绘制鼠标指针失败")
        End Try
    End Sub

    ''' <summary>静音截屏提示（程序退出时使用）。</summary>
    Public Sub Mute()
        IsMuted = True
    End Sub

#End Region

#Region "工具"

    Private Sub Dispatch(Action As Action)
        Try
            If FrmMain Is Nothing Then Return
            Dim D As Dispatcher = FrmMain.Dispatcher
            If D Is Nothing Then Return
            If D.CheckAccess() Then
                Action()
            Else
                D.BeginInvoke(Action, DispatcherPriority.Normal)
            End If
        Catch ex As Exception
            Logger.Error(ex, "调度截屏操作失败")
        End Try
    End Sub

#End Region

#Region "Win32"

    Private NotInheritable Class Native

        <StructLayout(LayoutKind.Sequential)>
        Public Structure POINT
            Public X As Integer
            Public Y As Integer
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure CURSORINFO
            Public cbSize As Integer
            Public flags As Integer
            Public hCursor As IntPtr
            Public ptScreenPos As POINT
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure ICONINFO
            Public fIcon As Boolean
            Public xHotspot As Integer
            Public yHotspot As Integer
            Public hbmMask As IntPtr
            Public hbmColor As IntPtr
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure KBDLLHOOKSTRUCT
            Public vkCode As Integer
            Public scanCode As Integer
            Public flags As Integer
            Public time As Integer
            Public dwExtraInfo As IntPtr
        End Structure

        Public Delegate Function LowLevelKeyboardProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function RegisterHotKey(hWnd As IntPtr, id As Integer, fsModifiers As Integer, vk As Integer) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function UnregisterHotKey(hWnd As IntPtr, id As Integer) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function SetWindowsHookEx(idHook As Integer, lpfn As LowLevelKeyboardProc, hMod As IntPtr, dwThreadId As Integer) As IntPtr
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function CallNextHookEx(hhk As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        End Function

        <DllImport("kernel32.dll", CharSet:=CharSet.Auto)>
        Public Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
        End Function

        <DllImport("user32.dll")>
        Public Shared Function GetCursorInfo(ByRef pci As CURSORINFO) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function GetIconInfo(hIcon As IntPtr, ByRef piconinfo As ICONINFO) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function DrawIcon(hDC As IntPtr, X As Integer, Y As Integer, hIcon As IntPtr) As Boolean
        End Function

        <DllImport("gdi32.dll")>
        Public Shared Function DeleteObject(hObject As IntPtr) As Boolean
        End Function

    End Class

#End Region

End Module
