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
            Reload()
            AniControlEnabled -= 1
            AddHandler UltraScreenshot.ScreenshotTaken, AddressOf OnScreenshotTaken
        End If
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

    Private Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click
        Try
            UltraScreenshot.EnsureFolder()
            Process.Start("explorer.exe", UltraScreenshot.PhotoFolder.TrimEnd("\"c))
        Catch ex As Exception
            Logger.Error(ex, "打开 photo 文件夹失败", LogBehavior.Toast)
        End Try
    End Sub

End Class
