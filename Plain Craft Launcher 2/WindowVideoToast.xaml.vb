Imports System.Windows.Interop
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Windows.Threading

''' <summary>
''' Ultra：录制开始的提示条。
''' 纯黑底、直角、左侧一条主题色竖条 + 圆环图标 + 一行字（英伟达那种样式）。
''' 贴在屏幕（或游戏窗口）右上角，窗口本身固定贴边不动，内容从右边缘滑出，几秒后滑回。
''' 置顶、不抢焦点、不占任务栏、鼠标可穿透。
''' </summary>
Public Class WindowVideoToast

    Private Shared Instance As WindowVideoToast
    Private ReadOnly HideTimer As New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(3600)}
    Private ShownAt As DateTime

    Public Sub New()
        InitializeComponent()
        AddHandler HideTimer.Tick, AddressOf OnHideTimer
        AddHandler SourceInitialized, AddressOf OnSourceInitialized
    End Sub

    Private Sub OnSourceInitialized(sender As Object, e As EventArgs)
        Try
            UltraVideo.MarkOverlayWindow(New WindowInteropHelper(Me).Handle)
        Catch ex As Exception
            Logger.Warn(ex, "初始化录制提示条失败")
        End Try
    End Sub

    ''' <summary>
    ''' 弹出一条提示（录制开始 / 结束、截屏完成共用同一套样式和动画）。
    ''' </summary>
    ''' <param name="Text">显示的文字，例如「录制已开始」「已截屏」。</param>
    ''' <param name="Target">要贴靠的游戏窗口句柄，IntPtr.Zero 表示贴屏幕右上角。</param>
    ''' <param name="IsScreenshot">True 时换成相机图标。</param>
    Public Shared Sub ShowTip(Text As String, Target As IntPtr, Optional IsScreenshot As Boolean = False)
        If FrmMain Is Nothing Then Return
        Try
            If Instance Is Nothing Then Instance = New WindowVideoToast
            Instance.ShowInternal(Text, Target, IsScreenshot)
        Catch ex As Exception
            Logger.Error(ex, "显示录制提示条失败")
        End Try
    End Sub

    ''' <summary>
    ''' 立刻隐藏提示条，不带动画。
    ''' 截屏前调用，避免把这条置顶浮窗截进图片里。
    ''' </summary>
    Public Shared Sub HideNow()
        Try
            If Instance Is Nothing Then Return
            Instance.HideTimer.Stop()
            Instance.ResetAnimations()
            Instance.PanRoot.Opacity = 0
            Instance.Hide()
        Catch ex As Exception
            Logger.Warn(ex, "隐藏提示条失败")
        End Try
    End Sub

    ''' <summary>
    ''' 关闭提示条（程序退出时使用）。
    ''' </summary>
    Public Shared Sub CloseToast()
        Try
            If Instance IsNot Nothing Then
                Instance.HideTimer.Stop()
                Instance.Hide()
            End If
        Catch
        End Try
    End Sub

    Private Sub ShowInternal(Text As String, Target As IntPtr, Optional IsScreenshot As Boolean = False)
        IcoRecord.Visibility = If(IsScreenshot, Visibility.Collapsed, Visibility.Visible)
        IcoCamera.Visibility = If(IsScreenshot, Visibility.Visible, Visibility.Collapsed)
        LabTitle.Text = If(Text = "", If(IsScreenshot, "已截屏", "录制已开始"), Text)
        If IsVisible AndAlso PanRoot.Opacity > 0.05 Then
            '上一条还在显示（比如刚开录就停了）：只改文字，不重新弹一次
            Dim Elapsed As Double = (Date.Now - ShownAt).TotalMilliseconds
            HideTimer.Stop()
            ResetAnimations() '顺手取消可能正在跑的退场动画
            '至少再留 1.5 秒，免得字刚改完就消失
            HideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1500, 3600 - Elapsed))
            HideTimer.Start()
            Return
        End If
        If Not IsVisible Then Show()
        UpdateLayout()

        '窗口位置一次定死、始终贴着边缘：不再逐帧移动 HWND（那是掉帧和不贴边的原因）
        Dim Area As Point = CalcPosition(Target)
        ResetAnimations()
        Top = Area.Y
        Left = Area.X
        PanRoot.Opacity = 0

        '入场：整条内容从右边缘滑进来，窗口右边缘始终与屏幕边缘重合
        PanSlide.X = Width + 8
        PanSlide.BeginAnimation(TranslateTransform.XProperty,
            New DoubleAnimation(Width + 8, 0, TimeSpan.FromMilliseconds(300)) With {
                .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}})
        PanRoot.BeginAnimation(UIElement.OpacityProperty,
            New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)))
        HideTimer.Stop()
        HideTimer.Interval = TimeSpan.FromMilliseconds(3600)
        HideTimer.Start()
        ShownAt = Date.Now
    End Sub

    ''' <summary>取消所有进行中的动画并复位，避免连续弹提示时叠加。</summary>
    Private Sub ResetAnimations()
        PanRoot.BeginAnimation(UIElement.OpacityProperty, Nothing)
        PanSlide.BeginAnimation(TranslateTransform.XProperty, Nothing)
        PanRoot.Opacity = 1
        PanSlide.X = 0
    End Sub

    ''' <summary>
    ''' 计算贴靠后的左上角坐标：右上角，X 精确等于右边缘减去窗口宽度，不留空隙。
    ''' </summary>
    Private Function CalcPosition(Target As IntPtr) As Point
        Dim RightPixel As Integer = UltraVideo.ScreenWidth
        Dim TopPixel As Integer = 0
        If Target <> IntPtr.Zero Then
            Dim GameArea As UltraVideo.GameArea? = UltraVideo.GetWindowArea(Target)
            If GameArea.HasValue Then
                RightPixel = GameArea.Value.X + GameArea.Value.Width
                TopPixel = GameArea.Value.Y
            End If
        End If
        Dim Scale As Double = 1
        Try
            Scale = VisualTreeHelper.GetDpi(Me).DpiScaleX
        Catch
        End Try
        If Scale <= 0 Then Scale = 1
        '紧贴屏幕（或游戏窗口）右侧边缘，不留空隙
        Dim X As Double = Math.Max(0, RightPixel / Scale - Width)
        Dim Y As Double = Math.Max(16, TopPixel / Scale + 72)
        Return New Point(X, Y)
    End Function

    Private Sub OnHideTimer(sender As Object, e As EventArgs)
        HideTimer.Stop()
        Dim Fade As New DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        AddHandler Fade.Completed, Sub(Sender2 As Object, E2 As EventArgs)
                                      ResetAnimations()
                                      PanRoot.Opacity = 0
                                      Hide()
                                  End Sub
        PanRoot.BeginAnimation(UIElement.OpacityProperty, Fade)
        PanSlide.BeginAnimation(TranslateTransform.XProperty,
            New DoubleAnimation(0, Width + 8, TimeSpan.FromMilliseconds(240)) With {
                .EasingFunction = New QuadraticEase With {.EasingMode = EasingMode.EaseIn}})
    End Sub

End Class
