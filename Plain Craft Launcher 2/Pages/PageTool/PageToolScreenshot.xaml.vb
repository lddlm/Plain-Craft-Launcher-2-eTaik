Imports System.Diagnostics
Imports System.IO

''' <summary>
''' 工具箱 → 截屏页面。
''' Ultra：GDI 原生抓屏，输出到 PCL 目录下的 photo 文件夹，Alt + W 全局快捷键。
''' </summary>
Public Class PageToolScreenshot

    Private Sub PageToolScreenshot_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        Static Reloaded As Boolean = False
        If Not Reloaded Then
            Reloaded = True
            AniControlEnabled += 1
            Try
                Reload()
            Finally
                AniControlEnabled -= 1
            End Try
            AddHandler UltraScreenshot.ScreenshotTaken, AddressOf OnScreenshotTaken
        Else
            '每次回到这个页面都以设置文件为准，免得界面显示的和实际用的不一致
            AniControlEnabled += 1
            Try
                SettingService.RefreshSettings(Me)
            Finally
                AniControlEnabled -= 1
            End Try
        End If
        LabHotkeyTip.Text = $"游戏内按 {UltraScreenshot.HotkeyText} 可以随时截屏（按当前截屏范围），按 {UltraScreenshot.RegionHotkeyText} 可以直接框选一块区域截屏。图片保存在 PCL 目录下的 photo 文件夹。"
        RefreshResolutionRow()
        RefreshRange()
        RefreshStatus()
    End Sub

    ''' <summary>重新载入设置与状态。</summary>
    Public Sub Reload()
        SettingService.RefreshSettings(Me)
        LabFolder.Text = UltraScreenshot.PhotoFolder
        RefreshStatus()
    End Sub

    ''' <summary>刷新「最近截屏」一行。</summary>
    Private Sub RefreshStatus()
        Try
            If UltraScreenshot.LastFile = "" Then
                LabLast.Text = "还没有截过屏"
            Else
                LabLast.Text = Path.GetFileName(UltraScreenshot.LastFile) &
                    If(UltraScreenshot.LastInfo = "", "", "　·　" & UltraScreenshot.LastInfo)
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新截屏状态失败")
        End Try
    End Sub

    Private Sub OnScreenshotTaken(sender As Object, e As EventArgs)
        If Not Dispatcher.CheckAccess() Then
            Dispatcher.BeginInvoke(New Action(AddressOf RefreshStatus))
            Return
        End If
        RefreshStatus()
    End Sub

    Private Sub BtnShot_Click() Handles BtnShot.Click
        UltraScreenshot.TakeScreenshot()
    End Sub

    '==== 截屏范围 = 区域 ====

    Private Sub RadioRegion_Changed(sender As Object, e As RouteEventArgs) Handles RadioRegion.Changed
        RefreshResolutionRow()
        RefreshRange()
    End Sub

    Private Sub RadioFullScreen_Changed(sender As Object, e As RouteEventArgs) Handles RadioFullScreen.Changed
        RefreshRange()
    End Sub

    Private Sub RadioGameWindow_Changed(sender As Object, e As RouteEventArgs) Handles RadioGameWindow.Changed
        RefreshRange()
    End Sub

    ''' <summary>
    ''' 把「这次会截什么范围」直接写在界面上。
    ''' 读的是设置值而不是单选框状态——界面可能因为保存失败和实际不一致，这样一眼能看出来。
    ''' </summary>
    Private Sub RefreshRange()
        Dim Text As String
        Select Case Settings.Get(Of Integer)("ScreenshotMode")
            Case UltraScreenshot.ScreenshotModeGameWindow
                Text = "截屏范围：锁定游戏窗口"
            Case UltraScreenshot.ScreenshotModeRegion
                Text = "截屏范围：区域（每次截屏时框选一次）"
            Case Else
                Text = "截屏范围：全屏"
        End Select
        If LabRange.Text <> Text Then
            LabRange.Text = Text
            Logger.Info("[ScreenshotMode] " & Text)
        End If
    End Sub

    ''' <summary>区域截屏按框选原尺寸输出，分辨率那一行直接收起来，免得看起来像能调。</summary>
    Private Sub RefreshResolutionRow()
        Dim IsRegion As Boolean = RadioRegion.Checked
        RowHeight.Height = New GridLength(If(IsRegion, 0, 28))
        RowHeightGap.Height = New GridLength(If(IsRegion, 0, 9))
        LabHeightLabel.Visibility = If(IsRegion, Visibility.Collapsed, Visibility.Visible)
        ComboHeight.Visibility = LabHeightLabel.Visibility
    End Sub

    Private Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click
        Try
            UltraScreenshot.EnsureFolder()
            Process.Start("explorer.exe", UltraScreenshot.PhotoFolder.TrimEnd("\"c))
        Catch ex As Exception
            Logger.Error(ex, "打开 photo 文件夹失败", LogBehavior.Toast)
        End Try
    End Sub

End Class
