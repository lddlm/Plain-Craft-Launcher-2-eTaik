Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Input
Imports System.Windows.Interop
Imports System.Windows.Media.Imaging
Imports Drawing = System.Drawing

''' <summary>
''' Ultra：区域框选浮层（区域录屏 Alt + 1 / 区域截屏 Alt + 2 共用）。
''' 打开前先把屏幕冻结成一张位图垫在底下，再用四个遮罩块压暗选区之外的部分，
''' 因此不需要 AllowsTransparency（那会让整窗退回软件渲染，全屏尺寸下会明显掉帧）。
''' 框选结果以物理像素返回，直接喂给 UltraVideo.StartRecording / UltraScreenshot.TakeScreenshot。
''' </summary>
Public Class WindowRegionSelect

    Private Shared LastResult As UltraVideo.GameArea?
    Private HasResult As Boolean = False

    Private ScaleX As Double = 1
    Private ScaleY As Double = 1
    Private Dragging As Boolean = False
    Private StartPoint As Point
    Private CurPoint As Point

    Public Sub New()
        InitializeComponent()
        '铺满主屏幕：SystemParameters 给的是 DIP，正好是 WPF 的坐标单位
        Left = 0
        Top = 0
        Width = SystemParameters.PrimaryScreenWidth
        Height = SystemParameters.PrimaryScreenHeight
    End Sub

    ''' <summary>
    ''' 弹出框选浮层，返回用户框出的区域（物理像素）。用户取消或框得太小时返回 Nothing。
    ''' </summary>
    ''' <param name="Purpose">用于提示文字，例如「录制」「截屏」。</param>
    Public Shared Function Pick(Purpose As String) As UltraVideo.GameArea?
        If FrmMain Is Nothing Then Return Nothing
        LastResult = Nothing
        '先冻屏，再开窗：否则浮层自己会被拍进去
        Dim Shot As BitmapSource = CaptureScreen()
        If Shot Is Nothing Then Return Nothing
        Dim Win As New WindowRegionSelect()
        Win.ImgScreen.Source = Shot
        Win.LabHint.Text = $"拖动鼠标框选{Purpose}区域　·　Esc 或右键取消"
        Win.ShowDialog()
        Return Win.Result
    End Function

    ''' <summary>框选结果，没框或取消时为 Nothing。</summary>
    Public ReadOnly Property Result As UltraVideo.GameArea?
        Get
            Return If(HasResult, LastResult, Nothing)
        End Get
    End Property

#Region "屏幕冻结"

    Private Shared Function CaptureScreen() As BitmapSource
        Dim W As Integer = UltraVideo.ScreenWidth
        Dim H As Integer = UltraVideo.ScreenHeight
        If W < 16 OrElse H < 16 Then Return Nothing
        Try
            WindowVideoToast.HideNow()
        Catch
        End Try
        '等置顶提示条真正从桌面合成层上消失
        Thread.Sleep(120)
        Try
            Using Bmp As New Drawing.Bitmap(W, H, Drawing.Imaging.PixelFormat.Format32bppArgb)
                Using G As Drawing.Graphics = Drawing.Graphics.FromImage(Bmp)
                    G.CopyFromScreen(0, 0, 0, 0, New Drawing.Size(W, H), Drawing.CopyPixelOperation.SourceCopy)
                End Using
                Dim HB As IntPtr = Bmp.GetHbitmap()
                Try
                    Dim Src As BitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        HB, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())
                    Src.Freeze()
                    Return Src
                Finally
                    DeleteObject(HB)
                End Try
            End Using
        Catch ex As Exception
            Logger.Error(ex, "区域框选前截取屏幕失败")
            Return Nothing
        End Try
    End Function

#End Region

#Region "窗口与坐标"

    Private Sub WindowRegionSelect_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '用「物理屏幕宽 / 实际 DIP 宽」反算缩放，比直接信 DPI 值稳
        Try
            If ActualWidth > 1 Then ScaleX = UltraVideo.ScreenWidth / ActualWidth
            If ActualHeight > 1 Then ScaleY = UltraVideo.ScreenHeight / ActualHeight
        Catch
        End Try
        If ScaleX <= 0 OrElse Double.IsNaN(ScaleX) Then ScaleX = 1
        If ScaleY <= 0 OrElse Double.IsNaN(ScaleY) Then ScaleY = 1
        ResetSelection()
        Try
            Activate()
            Focus()
        Catch
        End Try
    End Sub

    Private Sub ResetSelection()
        '还没开始拖的时候不压暗，先让人看清屏幕再决定框哪里
        Dim Boxes As FrameworkElement() = {DimTop, DimLeft, DimRight, DimBottom}
        For Each Item As FrameworkElement In Boxes
            Place(Item, 0, 0, 0, 0)
        Next
        SelBorder.Visibility = Visibility.Collapsed
        SizeBadge.Visibility = Visibility.Collapsed
    End Sub

    Private Shared Sub Place(Item As FrameworkElement, X As Double, Y As Double, W As Double, H As Double)
        Canvas.SetLeft(Item, X)
        Canvas.SetTop(Item, Y)
        Item.Width = Math.Max(0, W)
        Item.Height = Math.Max(0, H)
    End Sub

    ''' <summary>把 DIP 坐标裁到窗口范围内。</summary>
    Private Function ClampPoint(P As Point) As Point
        Dim W As Double = PanRoot.ActualWidth
        Dim H As Double = PanRoot.ActualHeight
        Return New Point(Math.Max(0, Math.Min(W, P.X)), Math.Max(0, Math.Min(H, P.Y)))
    End Function

    ''' <summary>当前选区（DIP）：X / Y / 宽 / 高。</summary>
    Private Function SelectionRect() As Rect
        Dim A As Point = ClampPoint(StartPoint)
        Dim B As Point = ClampPoint(CurPoint)
        Dim L As Double = Math.Min(A.X, B.X)
        Dim T As Double = Math.Min(A.Y, B.Y)
        Return New Rect(L, T, Math.Abs(B.X - A.X), Math.Abs(B.Y - A.Y))
    End Function

#End Region

#Region "鼠标与键盘"

    Private Sub PanRoot_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles PanRoot.MouseLeftButtonDown
        Dragging = True
        StartPoint = ClampPoint(e.GetPosition(PanRoot))
        CurPoint = StartPoint
        HintBadge.Visibility = Visibility.Collapsed
        Try
            PanRoot.CaptureMouse()
        Catch
        End Try
        UpdateSelection()
    End Sub

    Private Sub PanRoot_MouseMove(sender As Object, e As MouseEventArgs) Handles PanRoot.MouseMove
        If Not Dragging Then Return
        CurPoint = ClampPoint(e.GetPosition(PanRoot))
        UpdateSelection()
    End Sub

    Private Sub PanRoot_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles PanRoot.MouseLeftButtonUp
        If Not Dragging Then Return
        Dragging = False
        Try
            PanRoot.ReleaseMouseCapture()
        Catch
        End Try
        CurPoint = ClampPoint(e.GetPosition(PanRoot))
        UpdateSelection()
        Confirm()
    End Sub

    ''' <summary>右键随时取消，和截图工具的习惯一致。</summary>
    Private Sub PanRoot_MouseRightButtonDown(sender As Object, e As MouseButtonEventArgs) Handles PanRoot.MouseRightButtonDown
        Cancel()
    End Sub

    Private Sub WindowRegionSelect_PreviewKeyDown(sender As Object, e As KeyEventArgs) Handles Me.PreviewKeyDown
        If e.Key = Key.Escape Then
            e.Handled = True
            Cancel()
        ElseIf e.Key = Key.Enter AndAlso SelBorder.Visibility = Visibility.Visible Then
            e.Handled = True
            Confirm()
        End If
    End Sub

#End Region

#Region "选区绘制与确认"

    Private Sub UpdateSelection()
        Dim R As Rect = SelectionRect()
        Dim TotalW As Double = PanRoot.ActualWidth
        Dim TotalH As Double = PanRoot.ActualHeight
        If R.Width < 2 OrElse R.Height < 2 Then
            '只是点了一下还没拖出面积：保持不压暗
            ResetSelection()
            Return
        End If
        '四个遮罩块围着选区排，中间那块就是「挖」出来的亮区
        Place(DimTop, 0, 0, TotalW, R.Top)
        Place(DimBottom, 0, R.Bottom, TotalW, Math.Max(0, TotalH - R.Bottom))
        Place(DimLeft, 0, R.Top, R.Left, R.Height)
        Place(DimRight, R.Right, R.Top, Math.Max(0, TotalW - R.Right), R.Height)

        SelBorder.Visibility = Visibility.Visible
        Place(SelBorder, R.Left, R.Top, R.Width, R.Height)

        Dim PW As Integer = CInt(Math.Round(R.Width * ScaleX))
        Dim PH As Integer = CInt(Math.Round(R.Height * ScaleY))
        LabSize.Text = $"{PW} × {PH}"
        SizeBadge.Visibility = Visibility.Visible
        '宽度交给内容自己撑开：显式设过 Width 的话 Measure 会返回 0，字就被裁掉了
        SizeBadge.Width = Double.NaN
        SizeBadge.Height = Double.NaN
        SizeBadge.Measure(New Size(Double.PositiveInfinity, Double.PositiveInfinity))
        Dim BW As Double = SizeBadge.DesiredSize.Width
        Dim BH As Double = SizeBadge.DesiredSize.Height
        Dim BX As Double = Math.Min(R.Right - BW, Math.Max(0, TotalW - BW - 4))
        Dim BY As Double = If(R.Bottom + 8 + BH <= TotalH, R.Bottom + 8, Math.Max(0, R.Top - BH - 8))
        Canvas.SetLeft(SizeBadge, BX)
        Canvas.SetTop(SizeBadge, BY)
    End Sub

    Private Sub Confirm()
        Dim R As Rect = SelectionRect()
        Dim X As Integer = CInt(Math.Round(R.Left * ScaleX))
        Dim Y As Integer = CInt(Math.Round(R.Top * ScaleY))
        Dim W As Integer = CInt(Math.Round(R.Width * ScaleX))
        Dim H As Integer = CInt(Math.Round(R.Height * ScaleY))
        '小于 8 像素当作误触
        If W < 8 OrElse H < 8 Then
            Cancel()
            Return
        End If
        '夹到屏幕范围内，避免选到边缘外
        Dim SW As Integer = UltraVideo.ScreenWidth
        Dim SH As Integer = UltraVideo.ScreenHeight
        If X < 0 Then X = 0
        If Y < 0 Then Y = 0
        If X + W > SW Then W = SW - X
        If Y + H > SH Then H = SH - Y
        If W < 8 OrElse H < 8 Then
            Cancel()
            Return
        End If
        LastResult = New UltraVideo.GameArea With {.X = X, .Y = Y, .Width = W, .Height = H}
        HasResult = True
        Close()
    End Sub

    Private Sub Cancel()
        HasResult = False
        LastResult = Nothing
        Close()
    End Sub

#End Region

#Region "Win32"

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function

#End Region

End Class
