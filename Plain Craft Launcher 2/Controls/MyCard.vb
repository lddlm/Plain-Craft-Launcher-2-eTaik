Public Class MyCard

    '控件
    Inherits Grid
    Private ReadOnly MainGrid As Grid
    Public ReadOnly Property MainChrome As MyDropShadow
    Private ReadOnly MainBorder As Border
    Public Property BorderChild As UIElement
        Get
            Return MainBorder.Child
        End Get
        Set(value As UIElement)
            MainBorder.Child = value
        End Set
    End Property
    Private _MainTextBlock As TextBlock
    Public Property MainTextBlock As TextBlock
        Get
            Init() '当父级触发 Loaded 时，本卡片可能尚未触发 Loaded（该事件从父级向子级调用），因此这会是 null。手动触发以确保控件已加载。
            Return _MainTextBlock
        End Get
        Set(value As TextBlock)
            _MainTextBlock = value
        End Set
    End Property
    Private _MainSwap As Shapes.Path
    Public Property MainSwap As Shapes.Path
        Get
            Init()
            Return _MainSwap
        End Get
        Set(value As Shapes.Path)
            _MainSwap = value
        End Set
    End Property

    '属性
    Public Uuid As Integer = GetUuid()
    Public ReadOnly Property Inlines As InlineCollection
        Get
            Return MainTextBlock.Inlines
        End Get
    End Property
    Public Property CornerRadius As CornerRadius
        Get
            Return MainChrome.CornerRadius
        End Get
        Set(value As CornerRadius)
            MainChrome.CornerRadius = value
            MainBorder.CornerRadius = value
        End Set
    End Property
    'Ultra：允许卡片显示描边（Grid 本身无边框，转发到内部 MainBorder）
    '必须是依赖属性，否则 XAML 里无法用 {DynamicResource} 绑定，换主题色时描边不会跟着变
    Public Property CardBorderBrush As Brush
        Get
            Return GetValue(CardBorderBrushProperty)
        End Get
        Set(value As Brush)
            SetValue(CardBorderBrushProperty, value)
        End Set
    End Property
    Public Shared ReadOnly CardBorderBrushProperty As DependencyProperty =
        DependencyProperty.Register("CardBorderBrush", GetType(Brush), GetType(MyCard),
                                    New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsRender,
                                       Sub(dpo As DependencyObject, e As DependencyPropertyChangedEventArgs)
                                           Dim Card = TryCast(dpo, MyCard)
                                           If Card IsNot Nothing AndAlso Card.MainBorder IsNot Nothing Then
                                               Card.MainBorder.BorderBrush = TryCast(e.NewValue, Brush)
                                           End If
                                       End Sub))
    Public Property CardBorderThickness As Thickness
        Get
            Return MainBorder.BorderThickness
        End Get
        Set(value As Thickness)
            MainBorder.BorderThickness = value
        End Set
    End Property
    Public Property Title As String
        Get
            Return GetValue(TitleProperty)
        End Get
        Set(value As String)
            SetValue(TitleProperty, value)
            If _MainTextBlock IsNot Nothing Then MainTextBlock.Text = value
        End Set
    End Property
    Public Shared ReadOnly TitleProperty As DependencyProperty = DependencyProperty.Register("Title", GetType(String), GetType(MyCard), New PropertyMetadata(""))

    'UI 建立
    Public Sub New()
        MainChrome = New MyDropShadow With {
            .Margin = New Thickness(-3, -3, -3, -3 - GetWPFSize(1)), .ShadowRadius = 3, .Opacity = DropShadowIdleOpacity, .CornerRadius = New CornerRadius(5)}
        MainChrome.SetResourceReference(MyDropShadow.ColorProperty, "ColorObject1")
        Children.Insert(0, MainChrome)
        MainBorder = New Border With {.CornerRadius = New CornerRadius(5), .IsHitTestVisible = False}
        MainBorder.SetResourceReference(Border.BackgroundProperty, "UltraCardBackground") 'Ultra：可通过该资源全局换卡片底色
        Children.Insert(1, MainBorder)
        MainGrid = New Grid
        Children.Add(MainGrid)
    End Sub
    Private IsLoad As Boolean = False
    Private Sub Init() Handles Me.Loaded
        If IsLoad Then Return
        IsLoad = True
        '初次加载限定
        If MainTextBlock Is Nothing Then
            MainTextBlock = New TextBlock With {.HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Top, .Margin = New Thickness(15, 12, 0, 0), .FontWeight = FontWeights.Bold, .FontSize = 13, .IsHitTestVisible = False}
            MainTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            MainTextBlock.SetBinding(TextBlock.TextProperty, New Binding("Title") With {.Source = Me, .Mode = BindingMode.OneWay})
            MainGrid.Children.Add(MainTextBlock)
        End If
        If CanSwap OrElse SwapControl IsNot Nothing Then
            If SwapControl Is Nothing AndAlso Children.Count > 3 Then SwapControl = Children(3)
            MainSwap = New Shapes.Path With {.HorizontalAlignment = HorizontalAlignment.Right, .Stretch = Stretch.Uniform, .Height = 6, .Width = 10, .VerticalAlignment = VerticalAlignment.Top, .Margin = New Thickness(0, 17, 16, 0), .Data = New GeometryConverter().ConvertFromString("M2,4 l-2,2 10,10 10,-10 -2,-2 -8,8 -8,-8 z"), .RenderTransform = New RotateTransform(180), .RenderTransformOrigin = New Point(0.5, 0.5)}
            MainSwap.SetResourceReference(Shapes.Path.FillProperty, "ColorBrush1")
            MainGrid.Children.Add(MainSwap)
        End If
        '改变默认的折叠
        If IsSwapped AndAlso SwapControl IsNot Nothing Then
            MainSwap.RenderTransform = New RotateTransform(If(SwapLogoRight, 270, 0))
            SwapControl.Visibility = Visibility.Collapsed
            '取消由于高度变化被迫触发的高度动画
            Dim RawUseAnimation As Boolean = UseAnimation
            UseAnimation = False
            Height = SwapedHeight
            AniStop("MyCard Height " & Uuid)
            IsHeightAnimating = False
            RunInUi(Sub() UseAnimation = RawUseAnimation, True)
        End If
    End Sub

    '填充
    Private ContentBuilder As Action(Of Panel)
    Public Sub SetContentBuilder(Builder As Action(Of Panel))
        ContentBuilder = Builder
    End Sub
    Public Sub SetContentBuilder(Of T)(Items As IEnumerable(Of T), ItemBuilder As Func(Of T, UIElement))
        SetContentBuilder(
        Sub(Stack)
            For Each Item In Items
                Stack.Children.Add(ItemBuilder(Item))
            Next
            Stack.Children.Add(New FrameworkElement With {.Height = 18})
        End Sub)
    End Sub
    ''' <summary>
    ''' 使用 ContentBuilder 生成卡片内容。
    ''' </summary>
    Public Sub BuildContent()
        If ContentBuilder Is Nothing Then Return
        Dim Stack = TryCast(SwapControl, Panel)
        If Stack Is Nothing Then Return
        Try
            ContentBuilder(Stack)
            ContentBuilder = Nothing
        Catch
            Stack.Children.Clear()
            Throw
        End Try
        TriggerForceResize()
    End Sub

    '动画
    Private Const DropShadowIdleOpacity As Double = 0.07
    Private Const DropShadowHoverOpacity As Double = 0.4
    Public Property HasMouseAnimation As Boolean = True
    ''' <summary>悬停时卡片浮起的高度（像素）。</summary>
    Private Const HoverLift As Double = -1.5
    ''' <summary>悬停变色时长（动画时间）。实际时长 = 该值 / AniSpeed。</summary>
    Private Const HoverFadeTime As Integer = 90
    ''' <summary>悬停浮起/落回时长（动画时间）。</summary>
    Private Const HoverLiftTime As Integer = 140
    Private Sub MyCard_MouseEnter(sender As Object, e As MouseEventArgs) Handles Me.MouseEnter
        If Not HasMouseAnimation Then Return
        Dim Advanced As Boolean = AniAdvanced
        Dim IsLow As Boolean = If(Advanced, False, AniDetailIsLow)
        Dim IsHigh As Boolean = If(Advanced, False, AniDetailIsHigh)
        Dim Time As Integer = If(Advanced, AniAdvancedPick(140, 180, 220), HoverFadeTime)
        Dim Lift As Double = If(Advanced, -AniAdvancedPick(1.2, 2.0, 3.0), If(IsHigh, -2.6, HoverLift))
        Dim AniList As New List(Of AniData)
        If Not IsNothing(MainTextBlock) Then AniList.Add(AaColor(MainTextBlock, TextBlock.ForegroundProperty, "ColorBrush2", Time))
        If Not IsNothing(MainSwap) Then AniList.Add(AaColor(MainSwap, Shapes.Path.FillProperty, "ColorBrush2", Time))
        AniList.AddRange({
            AaColor(MainChrome, MyDropShadow.ColorProperty, "ColorObject4", Time),
            AaOpacity(MainChrome, DropShadowHoverOpacity - MainChrome.Opacity, Time)
        })
        '轻微浮起：只动内部阴影容器，不占用卡片自身的 RenderTransform（留给切页动画用）
        '低细节下不浮起，只做颜色反馈；高细节浮得更高、回弹更明显
        If Not IsNothing(MainChrome) AndAlso (Not IsLow OrElse Advanced) AndAlso Lift <> 0 Then
            If TypeOf MainChrome.RenderTransform IsNot TranslateTransform Then
                MainChrome.RenderTransform = New TranslateTransform(0, 0)
            End If
            AniList.Add(AaTranslateY(MainChrome, Lift - CType(MainChrome.RenderTransform, TranslateTransform).Y, HoverLiftTime, 0,
                                     If(IsHigh OrElse Advanced, CType(New AniEaseOutBack(AniEasePower.Weak), AniEase), CType(New AniEaseOutFluent(AniEasePower.Middle), AniEase))))
        End If
        AniStart(AniList, "MyCard Mouse " & Uuid)
    End Sub
    Private Sub MyCard_MouseLeave(sender As Object, e As MouseEventArgs) Handles Me.MouseLeave
        If Not HasMouseAnimation Then Return
        Dim Advanced As Boolean = AniAdvanced
        Dim IsHigh As Boolean = If(Advanced, False, AniDetailIsHigh)
        Dim Time As Integer = If(Advanced, AniAdvancedPick(140, 180, 220), HoverFadeTime)
        Dim AniList As New List(Of AniData)
        If Not IsNothing(MainTextBlock) Then AniList.Add(AaColor(MainTextBlock, TextBlock.ForegroundProperty, "ColorBrush1", Time))
        If Not IsNothing(MainSwap) Then AniList.Add(AaColor(MainSwap, Shapes.Path.FillProperty, "ColorBrush1", Time))
        AniList.AddRange({
            AaColor(MainChrome, MyDropShadow.ColorProperty, "ColorObject1", Time),
            AaOpacity(MainChrome, DropShadowIdleOpacity - MainChrome.Opacity, Time)
        })
        '落回原位
        If Not IsNothing(MainChrome) Then
            If TypeOf MainChrome.RenderTransform IsNot TranslateTransform Then
                MainChrome.RenderTransform = New TranslateTransform(0, 0)
            End If
            If Math.Abs(CType(MainChrome.RenderTransform, TranslateTransform).Y) > 0.01 Then
                AniList.Add(AaTranslateY(MainChrome, 0 - CType(MainChrome.RenderTransform, TranslateTransform).Y, HoverLiftTime + 40, 0, New AniEaseOutFluent(AniEasePower.Middle)))
            End If
        End If
        AniStart(AniList, "MyCard Mouse " & Uuid)
    End Sub

#Region "高度改变动画"

    ''' <summary>
    ''' 是否启用高度改变动画。
    ''' </summary>
    Public Property UseAnimation As Boolean = True
    Private IsHeightAnimating As Boolean = False
    Private ActualUsedHeight As Double '回滚实际高度（例如 NaN）
    Private Sub MySizeChanged(sender As Object, e As SizeChangedEventArgs) Handles Me.SizeChanged
        If Not UseAnimation Then Return
        Dim DeltaHeight As Double = If(IsSwapped, SwapedHeight, e.NewSize.Height) - e.PreviousSize.Height
        '卡片的进入时动画已被页面通用切换动画替代
        If e.PreviousSize.Height = 0 OrElse IsHeightAnimating OrElse Math.Abs(DeltaHeight) < 1 OrElse ActualHeight = 0 Then Return
        StartHeightAnimation(DeltaHeight, e.PreviousSize.Height, False)
    End Sub
    Private Sub StartHeightAnimation(Delta As Double, PreviousHeight As Double, IsLoadAnimation As Boolean)
        If IsHeightAnimating OrElse FrmMain Is Nothing Then Return '避免 XAML 设计器出错

        Dim AnimList As New List(Of AniData)
        Dim AbsDelta = Math.Abs(Delta)

        If AbsDelta <= 800 Then
            '短距离，直接使用 150ms 的缓动动画
            AnimList.Add(AaHeight(Me, Delta, 150,, New AniEaseOutFluent(AniEasePower.ExtraStrong)))
        Else
            Dim EaseLength As Integer, EaseTime As Integer
            Dim InitSpeed As Integer '到达缓动区前的初速度
            If Delta < 0 AndAlso AbsDelta - EaseLength > 5000 * 0.1 Then
                '收回距离过长 (>0.1s)，强制以 100ms 完成匀速段，然后让减速段更长
                EaseLength = 200
                EaseTime = 150
                InitSpeed = (AbsDelta - EaseLength) / 0.1
            ElseIf Delta > 0 AndAlso AbsDelta - EaseLength > 5000 * 0.6 Then
                '展开距离过长 (>0.6s)，以 5000 速度展示 300ms 匀速段，剩下的距离全部归入减速段
                InitSpeed = 5000
                EaseLength = AbsDelta - InitSpeed * 0.3
                EaseTime = 400
            Else
                '中程，匀速地快速展开（或收回）
                EaseLength = 150
                EaseTime = 200
                InitSpeed = 4000
            End If
            '匀速段
            AnimList.Add(AaHeight(Me, (AbsDelta - EaseLength) * Math.Sign(Delta),
                (AbsDelta - EaseLength) / InitSpeed * 1000))
            '减速段
            AnimList.Add(AaHeight(Me, EaseLength * Math.Sign(Delta),
                EaseTime,, New AniEaseOutFluentWithInitial(InitSpeed, EaseTime / 1000, EaseLength), True))
        End If

        AnimList.Add(AaCode(
        Sub()
            IsHeightAnimating = False
            Height = ActualUsedHeight
            If IsSwapped AndAlso SwapControl IsNot Nothing Then SwapControl.Visibility = Visibility.Collapsed
        End Sub,, True))
        AniStart(AnimList, "MyCard Height " & Uuid)
        IsHeightAnimating = True
        ActualUsedHeight = If(IsSwapped, SwapedHeight, Height)
        Height = PreviousHeight
    End Sub
    ''' <summary>
    ''' 通知 MyCard，控件内容已改变，需要中断动画并瞬间更新高度。
    ''' </summary>
    Public Sub TriggerForceResize()
        Height = If(IsSwapped, SwapedHeight, Double.NaN)
        AniStop("MyCard Height " & Uuid)
        IsHeightAnimating = False
    End Sub

#End Region

#Region "折叠"

    '若设置了 CanSwap，或 SwapControl 不为空，则判定为会进行折叠
    '这是因为不能直接在 XAML 中设置 SwapControl
    Public SwapControl As FrameworkElement
    Public Property CanSwap As Boolean = False
    ''' <summary>
    ''' 是否已被折叠。
    ''' </summary>
    Public Property IsSwapped As Boolean
        Get
            Return _IsSwapped
        End Get
        Set(value As Boolean)
            If _IsSwapped = value Then Return
            _IsSwapped = value
            If SwapControl Is Nothing Then Return
            '展开
            If Not IsSwapped Then BuildContent()
            '若尚未加载，会在 Loaded 事件中触发无动画的折叠，不需要在这里进行
            If Not IsLoaded Then Return
            '更新样式
            SwapControl.Visibility = Visibility.Visible
            TriggerForceResize()
            UpdateCursorStyle()
            '改变箭头
            AniStart(AaRotateTransform(MainSwap, If(_IsSwapped, If(SwapLogoRight, 270, 0), 180) - CType(MainSwap.RenderTransform, RotateTransform).Angle, 250,, New AniEaseOutFluent(AniEasePower.ExtraStrong)), "MyCard Swap " & Uuid, True)
        End Set
    End Property
    Private _IsSwapped As Boolean = False
    <Obsolete("IsSwaped 存在拼写错误，请换用 IsSwapped 属性。")>
    Public Property IsSwaped As Boolean
        Get
            Return GetValue(IsSwapedProperty)
        End Get
        Set(value As Boolean)
            SetValue(IsSwapedProperty, value)
        End Set
    End Property
    Public Shared ReadOnly IsSwapedProperty As DependencyProperty = DependencyProperty.Register("IsSwaped", GetType(Boolean), GetType(MyCard), New PropertyMetadata(False,
    Sub(sender, e) If sender IsNot Nothing AndAlso TypeOf sender Is MyCard Then CType(sender, MyCard).IsSwapped = CType(e.NewValue, Boolean)))

    Public Property SwapLogoRight As Boolean = False
    Private IsSwapMouseDown As Boolean = False '用于触发卡片展开/折叠的 MouseDown
    Private IsCustomMouseDown As Boolean = False '用于触发自定义事件的 MouseDown
    Public Event PreviewSwap(sender As Object, e As RouteEventArgs)
    Public Event Swap(sender As Object, e As RouteEventArgs)
    Public Const SwapedHeight As Integer = 40
    Private Sub MyCard_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        Dim Pos As Double = Mouse.GetPosition(Me).Y
        If Not IsSwapped AndAlso
            (Pos > If(IsSwapped, SwapedHeight, SwapedHeight - 6) OrElse (Pos = 0 AndAlso Not IsMouseDirectlyOver)) Then Return
        IsCustomMouseDown = True
        If Not IsSwapped AndAlso
            (SwapControl Is Nothing OrElse Pos > If(IsSwapped, SwapedHeight, SwapedHeight - 6) OrElse (Pos = 0 AndAlso Not IsMouseDirectlyOver)) Then Return '检测点击位置；或已经不在可视树上的误判
        IsSwapMouseDown = True
    End Sub
    Private Sub MyCard_MouseLeftButtonUp() Handles Me.MouseLeftButtonUp
        If Not IsCustomMouseDown Then Return
        IsCustomMouseDown = False
        RaiseCustomEvent() '触发自定义事件

        If Not IsSwapMouseDown Then Return
        IsSwapMouseDown = False

        Dim Pos As Double = Mouse.GetPosition(Me).Y
        If Not IsSwapped AndAlso
            (SwapControl Is Nothing OrElse Pos > If(IsSwapped, SwapedHeight, SwapedHeight - 6) OrElse (Pos = 0 AndAlso Not IsMouseDirectlyOver)) Then Return '检测点击位置；或已经不在可视树上的误判

        Dim e = New RouteEventArgs(True)
        RaiseEvent PreviewSwap(Me, e)
        If e.Handled Then
            IsSwapMouseDown = False
            Return
        End If

        IsSwapped = Not IsSwapped
        Logger.Info($"{If(IsSwapped, "折叠卡片", "展开卡片")}{If(Title Is Nothing, "", "：" & Title)}")
        RaiseEvent Swap(Me, e)
    End Sub
    Private Sub MyCard_MouseLeave_Swap(sender As Object, e As MouseEventArgs) Handles Me.MouseLeave
        IsSwapMouseDown = False
    End Sub
    Private Sub UpdateCursorStyle() Handles Me.MouseMove
        If _MainSwap Is Nothing Then Cursor = Nothing : Return
        Dim Pos As Double = Mouse.GetPosition(Me).Y
        Cursor = If(MainSwap.Visibility = Visibility.Visible AndAlso (IsSwapped OrElse (Pos <= SwapedHeight - 6 AndAlso Not (Pos = 0 AndAlso Not IsMouseDirectlyOver))),
            Cursors.Hand, Nothing)
    End Sub

#End Region

End Class
Partial Public Module ModAnimation
    Public Sub AniDispose(Control As MyCard, RemoveFromChildren As Boolean, Optional CallBack As ParameterizedThreadStart = Nothing)
        If Control.IsHitTestVisible Then
            Control.IsHitTestVisible = False
            AniStart({
                AaScaleTransform(Control, -0.08, 200,, New AniEaseInFluent),
                AaOpacity(Control, -1, 200,, New AniEaseOutFluent),
                AaHeight(Control, -Control.ActualHeight, 150, 100, New AniEaseOutFluent),
                AaCode(
                Sub()
                    If RemoveFromChildren Then
                        If Control.Parent Is Nothing Then Return
                        CType(Control.Parent, Object).Children.Remove(Control)
                    Else
                        Control.Visibility = Visibility.Collapsed
                    End If
                    If CallBack IsNot Nothing Then CallBack(Control)
                End Sub,, True)
            }, "MyCard Dispose " & Control.Uuid)
        Else
            If RemoveFromChildren Then
                If Control.Parent Is Nothing Then Return
                CType(Control.Parent, Object).Children.Remove(Control)
            Else
                Control.Visibility = Visibility.Collapsed
            End If
            If CallBack IsNot Nothing Then CallBack(Control)
        End If
    End Sub
End Module
