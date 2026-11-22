Imports System.Windows.Markup

<ContentProperty("Inlines")>
Public Class MyButton

    '声明
    Public Event Click(sender As Object, e As MouseButtonEventArgs) '自定义事件

    '自定义属性
    Public Uuid As Integer = GetUuid()
    Public ReadOnly Property Inlines As InlineCollection
        Get
            Return LabText.Inlines
        End Get
    End Property
    Public Property Text As String
        Get
            Return GetValue(TextProperty)
        End Get
        Set(value As String)
            SetValue(TextProperty, value)
        End Set
    End Property '显示文本
    Public Shared ReadOnly TextProperty As DependencyProperty = DependencyProperty.Register("Text", GetType(String), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
    Sub(sender As DependencyObject, e As DependencyPropertyChangedEventArgs)
        If sender IsNot Nothing Then CType(sender, MyButton).LabText.Text = e.NewValue
    End Sub)))
    Public Property TextPadding As Thickness
        Get
            Return LabText.Padding
        End Get
        Set(value As Thickness)
            LabText.Padding = value
        End Set
    End Property
    Private _ColorType As ColorState = ColorState.Normal  '配色方案
    Public Property ColorType As ColorState
        Get
            Return _ColorType
        End Get
        Set(value As ColorState)
            _ColorType = value
            RefreshColor()
        End Set
    End Property
    Public Enum ColorState
        Normal = 0
        Highlight = 1
        Red = 2
    End Enum
    '属性穿透
    Public Shared Shadows ReadOnly PaddingProperty As DependencyProperty = DependencyProperty.Register("Padding", GetType(Thickness), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
        Sub(sender As MyButton, e As DependencyPropertyChangedEventArgs) If sender IsNot Nothing Then sender.PanFore.Padding = e.NewValue)))
    Public Overloads Property Padding As Thickness
        Get
            Return PanFore.Padding
        End Get
        Set(value As Thickness)
            PanFore.Padding = value
        End Set
    End Property
    Public Property RealRenderTransform As Transform
        Get
            Return PanFore.RenderTransform
        End Get
        Set(value As Transform)
            PanFore.RenderTransform = value
        End Set
    End Property

    '全局按钮尺寸：跟随设置→个性化里的按钮高度/宽度滑块（0 = 不干预）。
    '高度用 MinHeight 下限即可；宽度只对"独占一行"的按钮生效，
    '并排按钮（Grid 同行 / 水平 StackPanel）强制撑宽会互相重叠，故跳过。
    Private Sub ApplyGlobalSize()
        Try
            If UltraCorner.ButtonHeight > 0 Then Me.MinHeight = UltraCorner.ButtonHeight
            If UltraCorner.ButtonWidth > 0 AndAlso Not IsSharingRow() Then Me.MinWidth = UltraCorner.ButtonWidth
        Catch ex As Exception
            Logger.Warn(ex, "应用全局按钮尺寸失败")
        End Try
    End Sub

    '判断本按钮是否与其它 MyButton 并排共享一行。
    '并排时若强行加宽，固定宽度容器（如启动页左栏 300px）里两个按钮会溢出重叠。
    Private Function IsSharingRow() As Boolean
        Try
            Dim P = Me.Parent
            If P Is Nothing Then Return False
            '水平 StackPanel / WrapPanel 里的多个子元素视为并排
            If TypeOf P Is StackPanel AndAlso DirectCast(P, StackPanel).Orientation = Orientation.Horizontal Then
                Return DirectCast(P, Panel).Children.Count > 1
            End If
            If TypeOf P Is WrapPanel Then Return DirectCast(P, Panel).Children.Count > 1
            'Grid 里同一 Row 有其它 MyButton 视为并排
            Dim G = TryCast(P, Grid)
            If G IsNot Nothing Then
                Dim MyRow = Grid.GetRow(Me)
                For Each Child As UIElement In G.Children
                    If Child IsNot Me AndAlso TypeOf Child Is MyButton AndAlso Grid.GetRow(Child) = MyRow Then Return True
                Next
            End If
            Return False
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub MyButton_LoadedSize(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ApplyGlobalSize()
        AddHandler UltraCorner.ButtonSizeChanged, AddressOf Me.OnButtonSizeChanged
    End Sub
    Private Sub MyButton_UnloadedSize(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        RemoveHandler UltraCorner.ButtonSizeChanged, AddressOf Me.OnButtonSizeChanged
    End Sub
    Private Sub OnButtonSizeChanged()
        If Not IsLoaded Then Return
        Dispatcher.BeginInvoke(Sub() ApplyGlobalSize())
    End Sub

    '自定义事件
    Private Const AnimationColorIn As Integer = 100
    Private Const AnimationColorOut As Integer = 200
    Private Sub RefreshColor(Optional obj = Nothing, Optional e = Nothing) Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded, Me.IsEnabledChanged
        Try
            If IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画

                If IsEnabled Then
                    Select Case ColorType
                        Case ColorState.Normal
                            If IsMouseOver Then
                                '指向（Main 3）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush3", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '普通（Main 1）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush1", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                        Case ColorState.Highlight
                            If IsMouseOver Then
                                '指向（Main 3）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush3", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '高亮（Main 2）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush2", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                        Case ColorState.Red
                            If IsMouseOver Then
                                '红色指向
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrushRedLight", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '红色
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrushRedDark", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                    End Select
                Else
                    '不可用（Gray 4）
                    AniStart({AaColor(PanFore, Border.BorderBrushProperty, ColorGray4 - PanFore.BorderBrush, AnimationColorOut)}, "MyButton Color " & Uuid)
                End If
            Else

                AniStop("MyButton Color " & Uuid)
                If IsEnabled Then
                    Select Case ColorType
                        Case ColorState.Normal
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush3")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush1")
                            End If
                        Case ColorState.Highlight
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush3")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush2")
                            End If
                        Case ColorState.Red
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedLight")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedDark")
                            End If
                    End Select
                Else
                    PanFore.BorderBrush = ColorGray4
                End If

            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新按钮颜色出错")
        End Try
    End Sub

    '鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
    Private IsMouseDown As Boolean = False
    Private Sub Button_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        Logger.Info($"按下按钮：{Text}")
        RaiseEvent Click(sender, e)
        RaiseCustomEvent() '自定义事件
    End Sub
    Private Sub Button_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        IsMouseDown = True
        Focus()
        AniStart({
                 AaScaleTransform(PanFore, 0.955 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 80,, New AniEaseOutFluent(AniEasePower.ExtraStrong)),
                 AaScaleTransform(PanFore, -0.01, 700,, New AniEaseOutFluent(AniEasePower.Middle))
                 }, "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseEnter() Handles Me.MouseEnter
        AniStart(AaColor(PanFore, BackgroundProperty, If(_ColorType = ColorState.Red, "ColorBrushRedBack", "ColorBrush7"), AnimationColorIn), "MyButton Background " & Uuid)
    End Sub
    Private Sub Button_MouseUp() Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart({
               AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 300, 10, New AniEaseOutFluent(AniEasePower.Middle))
           }, "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseLeave() Handles Me.MouseLeave
        AniStart(AaColor(PanFore, BackgroundProperty, "ColorBrushHalfWhite", AnimationColorOut), "MyButton Background " & Uuid)
        If Not IsMouseDown Then Return
        IsMouseDown = False
        AniStart(AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 800,, New AniEaseOutFluent(AniEasePower.Strong)), "MyButton Scale " & Uuid)
    End Sub

End Class
