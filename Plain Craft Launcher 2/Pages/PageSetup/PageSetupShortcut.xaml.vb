Imports System.Windows.Input

''' <summary>
''' 设置 → 快捷键：修改录制开关的全局快捷键。
''' </summary>
Public Class PageSetupShortcut

    Private IsCapturing As Boolean = False

    Private Sub PageSetupShortcut_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        RefreshHotkey()
    End Sub

    Private Sub PageSetupShortcut_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        StopCapture()
    End Sub

    Private Sub RefreshHotkey()
        LabHotkey.Text = UltraVideo.HotkeyText
        LabShotHotkey.Text = UltraScreenshot.HotkeyText
    End Sub

    Private Sub BtnChange_Click() Handles BtnChange.Click
        If IsCapturing Then StopCapture() Else StartCapture()
    End Sub

    Private Sub BtnReset_Click() Handles BtnReset.Click
        Settings.Set("VideoHotkey", "Alt+Q")
        UltraVideo.ApplyHotkey()
        RefreshHotkey()
        Hint($"录制快捷键已恢复为 {UltraVideo.HotkeyText}", HintType.Green, False)
    End Sub

    Private Sub StartCapture()
        IsCapturing = True
        BtnChange.Text = "按下组合键…"
        LabHotkey.Text = "…"
        AddHandler FrmMain.PreviewKeyDown, AddressOf OnCaptureKey
        Try
            FrmMain.Focus()
        Catch
        End Try
    End Sub

    Private Sub StopCapture()
        If Not IsCapturing Then Return
        IsCapturing = False
        BtnChange.Text = "修改"
        Try
            RemoveHandler FrmMain.PreviewKeyDown, AddressOf OnCaptureKey
        Catch
        End Try
        RefreshHotkey()
    End Sub

    Private Sub OnCaptureKey(sender As Object, e As KeyEventArgs)
        If Not IsCapturing Then Return
        e.Handled = True
        'Alt 组合键在 WPF 里会被包成 System，真实键在 SystemKey 里
        Dim RealKey As Key = If(e.Key = Key.System, e.SystemKey, e.Key)
        If RealKey = Key.Escape Then
            StopCapture()
            Return
        End If
        Select Case RealKey
            Case Key.LeftAlt, Key.RightAlt, Key.LeftCtrl, Key.RightCtrl,
                 Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin, Key.None
                Return '只按修饰键不算
        End Select

        Dim Mods As Integer = 0
        If Keyboard.Modifiers.HasFlag(ModifierKeys.Control) Then Mods = Mods Or UltraVideo.MOD_CONTROL
        If Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) Then Mods = Mods Or 1
        If Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) Then Mods = Mods Or UltraVideo.MOD_SHIFT
        If Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) Then Mods = Mods Or UltraVideo.MOD_WIN
        If Mods = 0 Then
            Hint("快捷键至少要带一个 Alt / Ctrl / Shift", HintType.Blue, False)
            Return
        End If

        Dim VK As Integer = KeyInterop.VirtualKeyFromKey(RealKey)
        If VK <= 0 Then Return
        '只接受 A-Z / 0-9 / F1-F12 这类能存成文本的键
        Dim KeyName As String = UltraVideo.VKToKeyName(VK)
        If UltraVideo.KeyNameToVK(KeyName) = 0 Then
            Hint("这个键暂不支持，换一个字母、数字或 F1~F12 试试", HintType.Blue, False)
            Return
        End If

        Dim Spec As String = UltraVideo.BuildHotkeySpec(Mods, VK)
        Settings.Set("VideoHotkey", Spec)
        StopCapture()
        UltraVideo.ApplyHotkey()
        RefreshHotkey()
        If UltraVideo.HotkeyRegistered Then
            Hint($"录制快捷键已改为 {UltraVideo.HotkeyText}", HintType.Green, False)
        Else
            Hint($"已改为 {UltraVideo.HotkeyText}，但被其他软件占用，已改用键盘监听", HintType.Blue, False)
        End If
    End Sub

End Class
