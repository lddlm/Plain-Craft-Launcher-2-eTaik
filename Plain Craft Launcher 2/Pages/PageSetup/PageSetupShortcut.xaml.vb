Imports System.Windows.Input

''' <summary>
''' 设置 → 快捷键：录制开关 / 区域录制 / 截屏 / 区域截屏 四个全局快捷键。
''' </summary>
Public Class PageSetupShortcut

    ''' <summary>一条快捷键的界面引用与读写方式。</summary>
    Private Class HotkeyRow
        Public Id As String
        Public Title As String
        Public Key As String
        Public DefaultSpec As String
        Public Lab As TextBlock
        Public ChangeBtn As MyButton
        Public ResetBtn As MyButton
        ''' <summary>当前生效的显示文本，例如「Alt + Q」。</summary>
        Public ReadText As Func(Of String)
        ''' <summary>按设置重新注册，返回是否抢到了系统热键。</summary>
        Public Apply As Func(Of Boolean)
    End Class

    Private ReadOnly Rows As New List(Of HotkeyRow)
    Private Capturing As HotkeyRow = Nothing

    Private Sub PageSetupShortcut_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        BuildRows()
        RefreshHotkey()
    End Sub

    Private Sub PageSetupShortcut_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        StopCapture()
    End Sub

    Private Sub BuildRows()
        Rows.Clear()
        AddRow("video", "录制开关", "VideoHotkey", "Alt+Q",
               LabHotkey, BtnChange, BtnReset,
               Function() UltraVideo.HotkeyText,
               Function() UltraVideo.ApplyHotkey())
        AddRow("videoregion", "区域录制", "VideoRegionHotkey", "Alt+1",
               LabRegionHotkey, BtnRegionChange, BtnRegionReset,
               Function() UltraVideo.RegionHotkeyText,
               Function() UltraVideo.ApplyRegionHotkey())
        AddRow("shot", "截屏", "ScreenshotHotkey", "Alt+W",
               LabShotHotkey, BtnShotChange, BtnShotReset,
               Function() UltraScreenshot.HotkeyText,
               Function() UltraScreenshot.ApplyHotkey())
        AddRow("shotregion", "区域截屏", "ScreenshotRegionHotkey", "Alt+2",
               LabShotRegionHotkey, BtnShotRegionChange, BtnShotRegionReset,
               Function() UltraScreenshot.RegionHotkeyText,
               Function() UltraScreenshot.ApplyRegionHotkey())
    End Sub

    Private Sub AddRow(Id As String, Title As String, Key As String, DefaultSpec As String,
                       Lab As TextBlock, ChangeBtn As MyButton, ResetBtn As MyButton,
                       ReadText As Func(Of String), Apply As Func(Of Boolean))
        Dim R As New HotkeyRow With {
            .Id = Id, .Title = Title, .Key = Key, .DefaultSpec = DefaultSpec,
            .Lab = Lab, .ChangeBtn = ChangeBtn, .ResetBtn = ResetBtn,
            .ReadText = ReadText, .Apply = Apply}
        Rows.Add(R)
    End Sub

    Private Function RowById(Id As String) As HotkeyRow
        For Each R As HotkeyRow In Rows
            If R.Id = Id Then Return R
        Next
        Return Nothing
    End Function

    Private Sub RefreshHotkey()
        For Each R As HotkeyRow In Rows
            If R Is Capturing Then Continue For
            R.Lab.Text = R.ReadText()
            R.ChangeBtn.Text = "修改"
        Next
    End Sub

    '==== 修改 ====

    Private Sub BtnChange_Click() Handles BtnChange.Click
        ToggleCapture("video")
    End Sub

    Private Sub BtnRegionChange_Click() Handles BtnRegionChange.Click
        ToggleCapture("videoregion")
    End Sub

    Private Sub BtnShotChange_Click() Handles BtnShotChange.Click
        ToggleCapture("shot")
    End Sub

    Private Sub BtnShotRegionChange_Click() Handles BtnShotRegionChange.Click
        ToggleCapture("shotregion")
    End Sub

    Private Sub ToggleCapture(Id As String)
        Dim R As HotkeyRow = RowById(Id)
        If R Is Nothing Then Return
        If Capturing Is R Then
            StopCapture()
        Else
            StartCapture(R)
        End If
    End Sub

    Private Sub StartCapture(R As HotkeyRow)
        StopCapture()
        Capturing = R
        R.ChangeBtn.Text = "按下组合键…"
        R.Lab.Text = "…"
        AddHandler FrmMain.PreviewKeyDown, AddressOf OnCaptureKey
        Try
            FrmMain.Focus()
        Catch
        End Try
    End Sub

    Private Sub StopCapture()
        If Capturing Is Nothing Then Return
        Capturing = Nothing
        Try
            RemoveHandler FrmMain.PreviewKeyDown, AddressOf OnCaptureKey
        Catch
        End Try
        RefreshHotkey()
    End Sub

    Private Sub OnCaptureKey(sender As Object, e As KeyEventArgs)
        Dim R As HotkeyRow = Capturing
        If R Is Nothing Then Return
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
        Settings.Set(R.Key, Spec)
        StopCapture()
        ApplyRow(R, $"{R.Title}快捷键已改为")
    End Sub

    '==== 恢复默认 ====

    Private Sub BtnReset_Click() Handles BtnReset.Click
        ResetRow("video")
    End Sub

    Private Sub BtnRegionReset_Click() Handles BtnRegionReset.Click
        ResetRow("videoregion")
    End Sub

    Private Sub BtnShotReset_Click() Handles BtnShotReset.Click
        ResetRow("shot")
    End Sub

    Private Sub BtnShotRegionReset_Click() Handles BtnShotRegionReset.Click
        ResetRow("shotregion")
    End Sub

    Private Sub ResetRow(Id As String)
        Dim R As HotkeyRow = RowById(Id)
        If R Is Nothing Then Return
        Settings.Set(R.Key, R.DefaultSpec)
        ApplyRow(R, $"{R.Title}快捷键已恢复为", GreenAlways:=True)
    End Sub

    ''' <summary>按设置重新注册，刷新显示，并提示结果。</summary>
    Private Sub ApplyRow(R As HotkeyRow, Prefix As String, Optional GreenAlways As Boolean = False)
        Dim Ok As Boolean = False
        Try
            Ok = R.Apply()
        Catch ex As Exception
            Logger.Error(ex, $"重新注册{R.Title}快捷键失败")
        End Try
        RefreshHotkey()
        Dim Text As String = R.ReadText()
        If Not GreenAlways AndAlso Not Ok Then
            Hint($"{Prefix} {Text}，但被其他软件占用，已改用键盘监听", HintType.Blue, False)
        Else
            Hint($"{Prefix} {Text}", HintType.Green, False)
        End If
        '撞车提醒：后注册的那个会失效
        For Each O As HotkeyRow In Rows
            If O Is R Then Continue For
            If O.ReadText() = Text Then
                Hint($"{R.Title}和「{O.Title}」用了同一个组合键，「{O.Title}」会失效", HintType.Blue, False)
                Exit For
            End If
        Next
    End Sub

End Class
