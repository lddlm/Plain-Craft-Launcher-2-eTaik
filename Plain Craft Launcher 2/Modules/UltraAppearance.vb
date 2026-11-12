Imports System.IO
Imports System.Windows.Media

''' <summary>
''' Ultra 个性化：主题色、默认白色背景、静态图片与动态视频。
''' 界面始终使用不透明的白色背景层，不会透视桌面，也没有任何模糊 / 半透明玻璃逻辑。
''' </summary>
Public Module UltraAppearance

    Private VideoPlayer As MediaPlayer = Nothing

    ''' <summary>
    ''' 按当前设置重新生成整套主题色画刷。
    ''' 颜色浓度会调整饱和度与明度：浓度低 → 颜色更灰更淡，浓度高 → 更鲜艳更深。
    ''' 默认 55 为中性点，不改变预设色 / 自定义色本身。
    ''' 所有对主题色的刷新都应走这里，避免直接用 ThemeRefresh 让浓度失效。
    ''' </summary>
    Public Sub RefreshThemeColors()
        Dim Intensity As Integer = Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("UltraColorIntensity")))
        Dim BaseSat As Integer = Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("UltraThemeSat")))
        Dim BaseLight As Integer = Math.Max(-50, Math.Min(50, Settings.Get(Of Integer)("UltraThemeLight")))
        Dim Delta As Double = (Intensity - 55) / 55.0 '-1（最淡）~ 0.82（最浓）
        ColorHue = Settings.Get(Of Integer)("UltraThemeHue")
        ColorSat = CInt(Math.Round(Math.Max(0, Math.Min(100, BaseSat + Delta * 32))))
        ColorLightAdjust = CInt(Math.Round(Math.Max(-50, Math.Min(50, BaseLight - Delta * 9))))
        ThemeRefresh()
    End Sub

    ''' <summary>
    ''' 拖动「颜色浓度」时的轻量刷新：只更新主题色，不重建背景图片 / 视频，
    ''' 避免拖动时视频反复重载造成卡顿。
    ''' </summary>
    Public Sub ApplyIntensity(Win As FormMain, Value As Integer)
        If Win Is Nothing OrElse Not Win.IsLoaded Then Return
        RefreshThemeColors()
    End Sub

    ''' <summary>
    ''' 从设置应用整套外观：主题色 + 背景（纯色 / 图片 / 视频）。
    ''' </summary>
    Public Sub Apply(Win As FormMain)
        If Win Is Nothing OrElse Not Win.IsLoaded Then Return
        Dim BgMode As Integer = Settings.Get(Of Integer)("UltraBackgroundMode")

        '主题颜色：复用原版的 HSL 主题机制
        RefreshThemeColors()

        Dim Res As ResourceDictionary = Application.Current.Resources
        RestoreLegacyBrushes(Res)
        ApplyBackground(Win, BgMode)
        ApplySurfaceOpacity(Res, BgMode)
    End Sub

    ''' <summary>
    ''' 界面不透明度：有自定义背景时，让卡片与侧栏的半透明白色把背景透出来。
    ''' </summary>
    Private Sub ApplySurfaceOpacity(Res As ResourceDictionary, BgMode As Integer)
        Dim Value As Integer = Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("UltraBackgroundOpacity")))
        If BgMode = 0 Then
            '没有自定义背景，保持不透明
            ModSecret.SetThemeBrushColor("UltraCardBackground", Color.FromArgb(245, 255, 255, 255))
            ModSecret.SetThemeBrushColor("ColorBrushBackgroundTransparentSidebar", Color.FromArgb(241, 255, 255, 255))
        Else
            ModSecret.SetThemeBrushColor("UltraCardBackground", Color.FromArgb(CByte(Math.Round(245 * Value / 100)), 255, 255, 255))
            ModSecret.SetThemeBrushColor("ColorBrushBackgroundTransparentSidebar", Color.FromArgb(CByte(Math.Round(241 * Value / 100)), 255, 255, 255))
        End If
    End Sub

    ''' <summary>
    ''' 所有画刷恢复为完全不透明的原版值。
    ''' </summary>
    Private Sub RestoreLegacyBrushes(Res As ResourceDictionary)
        ModSecret.SetThemeBrushColor("UltraCardBackground", Color.FromArgb(245, 255, 255, 255))
        ModSecret.SetThemeBrushColor("UltraGlassBackground", Color.FromArgb(128, 255, 255, 255))
        ModSecret.SetThemeBrushColor("ColorBrushSemiWhite", Color.FromArgb(187, 255, 255, 255))
        ModSecret.SetThemeBrushColor("ColorBrushHalfWhite", Color.FromArgb(85, 255, 255, 255))
        ModSecret.SetThemeBrushColor("ColorBrushBackgroundTransparentSidebar", Color.FromArgb(241, 255, 255, 255))
        'ColorBrush7 / 8 / Bg0 / Bg1 已由 ThemeRefresh 按当前色调重建为不透明画刷
    End Sub

    ''' <summary>
    ''' 背景层：0 = 默认白色，1 = 图片，2 = 视频。
    ''' </summary>
    Private Sub ApplyBackground(Win As FormMain, bgMode As Integer)
        Dim Host As Grid = Win.PanUltraBack
        If Host Is Nothing Then Return
        ClearVideo()
        Host.Children.Clear()
        Host.Background = Nothing

        Dim DefaultBrush As New SolidColorBrush(Color.FromRgb(245, 245, 245)) '完全不透明
        DefaultBrush.Freeze()
        Host.Background = DefaultBrush '先铺底，即使图片 / 视频加载失败也不会透视

        Select Case bgMode
            Case 1 '静态图片
                Dim Path As String = Settings.Get(Of String)("UltraBackgroundImage")
                If File.Exists(Path) Then
                    Try
                        Dim Bmp As New BitmapImage()
                        Bmp.BeginInit()
                        Bmp.CacheOption = BitmapCacheOption.OnLoad
                        Bmp.UriSource = New Uri(Path, UriKind.Absolute)
                        Bmp.EndInit()
                        Bmp.Freeze()
                        Host.Background = New ImageBrush(Bmp) With {
                            .Stretch = Stretch.UniformToFill,
                            .AlignmentX = AlignmentX.Center,
                            .AlignmentY = AlignmentY.Center}
                    Catch ex As Exception
                        Logger.Warn(ex, "加载静态背景图片失败")
                    End Try
                End If
            Case 2 '动态视频
                Dim Path As String = Settings.Get(Of String)("UltraBackgroundVideo")
                If File.Exists(Path) Then ShowVideo(Host, Path)
        End Select
    End Sub

    ''' <summary>
    ''' 动态背景：用 MediaPlayer + VideoDrawing 把视频画进背景层。
    ''' 不用 MediaElement，因为它在 AllowsTransparency 的分层窗口中不会渲染画面。
    ''' </summary>
    Private Sub ShowVideo(Host As Grid, path As String)
        Try
            Dim Player As New MediaPlayer() With {
                .Volume = 0,
                .IsMuted = True,
                .ScrubbingEnabled = True}
            AddHandler Player.MediaOpened,
                Sub()
                    Try
                        Player.Play()
                        Logger.Info("动态背景视频开始播放：" & path)
                    Catch ex As Exception
                        Logger.Warn(ex, "播放动态背景失败")
                    End Try
                End Sub
            AddHandler Player.MediaEnded,
                Sub()
                    Try
                        Player.Position = TimeSpan.Zero
                        Player.Play()
                    Catch ex As Exception
                        Logger.Warn(ex, "循环动态背景失败")
                    End Try
                End Sub
            AddHandler Player.MediaFailed,
                Sub(sender As Object, e As ExceptionEventArgs)
                    Logger.Warn(e.ErrorException, "动态背景视频无法播放：" & path)
                End Sub
            Player.Open(New Uri(path, UriKind.Absolute))

            Dim Drawing As New VideoDrawing With {
                .Rect = New Rect(0, 0, 1, 1),
                .Player = Player}
            Dim Brush As New DrawingBrush(Drawing) With {
                .Stretch = Stretch.UniformToFill,
                .TileMode = TileMode.None,
                .AlignmentX = AlignmentX.Center,
                .AlignmentY = AlignmentY.Center}
            Host.Background = Brush
            VideoPlayer = Player
            Logger.Info("动态背景已加载：" & path)
        Catch ex As Exception
            Logger.Warn(ex, "初始化动态背景失败")
        End Try
    End Sub

    Private Sub ClearVideo()
        Try
            If VideoPlayer IsNot Nothing Then
                VideoPlayer.Stop()
                VideoPlayer.Close()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "停止动态背景失败")
        Finally
            VideoPlayer = Nothing
        End Try
    End Sub

End Module
