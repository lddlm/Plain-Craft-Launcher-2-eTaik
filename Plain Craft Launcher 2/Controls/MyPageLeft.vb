Public Class MyPageLeft
    Inherits Grid
    Private Uuid As Integer = GetUuid()

    ''' <summary>左栏淡入/淡出时长（动画时间）。实际时长 = 该值 / AniSpeed。</summary>
    Private Const PageLeftFadeTime As Integer = 100
    ''' <summary>左栏缩放时长（动画时间）。</summary>
    Private Const PageLeftScaleTime As Integer = 400
    ''' <summary>左栏列表项位移时长（动画时间）。</summary>
    Private Const PageLeftSlideTime As Integer = 300
    ''' <summary>左栏退出时淡出的延迟（动画时间）。</summary>
    Private Const PageLeftHideDelay As Integer = 30

    '执行逐个进入动画的控件
    Public Property AnimatedControl As String '需要在 Loaded 之后才能获取到控件，所以不能用 Binding 直接绑定（#6664）
        Get
            Return GetValue(AnimatedControlProperty)
        End Get
        Set(value As String)
            SetValue(AnimatedControlProperty, value)
        End Set
    End Property
    Public Shared ReadOnly AnimatedControlProperty As DependencyProperty = DependencyProperty.Register("AnimatedControl", GetType(String), GetType(MyPageLeft), New PropertyMetadata(Nothing))

    Public Sub TriggerShowAnimation()
        Dim Advanced As Boolean = AniAdvanced
        Dim Level As Integer = If(Advanced, AniAdvancedLevel, AniDetail)
        Dim IsLow As Boolean = Level = 0
        Dim IsHigh As Boolean = Level = 2
        If AnimatedControl Is Nothing Then
            '缩放动画
            RenderTransformOrigin = New Point(0.5, 0.5)
            Opacity = 0
            If Advanced Then
                '高级动画：整栏从略小状态弹回，带明显但克制的回弹
                Dim AdvScale As Double = AniAdvancedPick(0.97, 0.95, 0.93)
                RenderTransform = New ScaleTransform(AdvScale, AdvScale)
                AniStart({
                    AaScaleTransform(Me, 1 - AdvScale, AniAdvancedPick(360, 420, 480),, New AniEaseOutBack(AniEasePower.Middle)),
                    AaOpacity(Me, 1, AniAdvancedPick(260, 340, 400))
                }, "PageLeft PageChange " & Uuid)
            ElseIf IsLow Then
                '低：不做缩放回弹，只淡入（时长与其它档一致，由「动画速度」统一控制）
                RenderTransform = New ScaleTransform(1, 1)
                AniStart(AaOpacity(Me, 1, PageLeftFadeTime, 0, New AniEaseOutFluent), "PageLeft PageChange " & Uuid)
            Else
                '中 / 高：从略小状态弹回原尺寸，细节越高起点越小、回弹越明显
                Dim StartScale As Double = If(IsHigh, 0.93, 0.96)
                RenderTransform = New ScaleTransform(StartScale, StartScale)
                AniStart({
                    AaScaleTransform(Me, 1 - StartScale, PageLeftScaleTime,, If(IsHigh, CType(New AniEaseOutBack(AniEasePower.Middle), AniEase), CType(New AniEaseOutBack(2), AniEase))),
                    AaOpacity(Me, 1, PageLeftFadeTime)
                }, "PageLeft PageChange " & Uuid)
            End If
        Else
            '逐个进入动画
            Dim AniList As New List(Of AniData)
            Dim Id As Integer = 0, Delay As Integer = 0
            For Each Element As FrameworkElement In GetAllAnimControls(True)
                Element = MyVirtualizingElement.TryInit(Element)
                If Element.Visibility = Visibility.Collapsed Then
                    '还原之前的隐藏动画可能导致的改变（#2436）
                    Element.Opacity = 1
                    Element.RenderTransform = New TranslateTransform(0, 0)
                    If TypeOf Element Is MyListItem Then CType(Element, MyListItem).IsMouseOverAnimationEnabled = True
                Else
                    Element.Opacity = 0
                    If IsLow AndAlso Not Advanced Then
                        '低：同时淡入，不位移、不逐个错开
                        Element.RenderTransform = Nothing
                        AniList.Add(AaOpacity(Element, If(TypeOf Element Is TextBlock, 0.6, 1), PageLeftFadeTime, 0, New AniEaseOutFluent(AniEasePower.Weak)))
                    ElseIf Advanced Then
                        '高级动画：单段平滑滑入 + 更长的淡入，逐项错开但不位移过冲
                        Dim SlideIn As Double = AniAdvancedPick(10.0, 16.0, 24.0)
                        Element.RenderTransform = New TranslateTransform(-SlideIn, 0)
                        If TypeOf Element Is MyListItem Then CType(Element, MyListItem).IsMouseOverAnimationEnabled = False
                        AniList.Add(AaOpacity(Element, If(TypeOf Element Is TextBlock, 0.6, 1), AniAdvancedPick(240, 300, 380), Delay, New AniEaseOutFluent(AniEasePower.Weak)))
                        AniList.Add(AaTranslateX(Element, SlideIn, AniAdvancedPick(380, 460, 560), Delay, New AniEaseOutBack(AniAdvancedPick(AniEasePower.Weak, AniEasePower.Middle, AniEasePower.Middle))))
                        If TypeOf Element Is MyListItem Then
                            AniList.Add(AaCode(
                            Sub()
                                CType(Element, MyListItem).IsMouseOverAnimationEnabled = True
                                CType(Element, MyListItem).RefreshColor(Me, New EventArgs)
                            End Sub, Delay + 320))
                        End If
                        Delay += AniAdvancedPick(10, 28, 44)
                        Id += 1
                    Else
                        Element.RenderTransform = New TranslateTransform(If(IsHigh, -32, -25), 0)
                        If TypeOf Element Is MyListItem Then CType(Element, MyListItem).IsMouseOverAnimationEnabled = False
                        AniList.Add(AaOpacity(Element, If(TypeOf Element Is TextBlock, 0.6, 1), PageLeftFadeTime, Delay, New AniEaseOutFluent(AniEasePower.Weak)))
                        AniList.Add(AaTranslateX(Element, If(IsHigh, 7, 5), 200, Delay, New AniEaseOutFluent))
                        AniList.Add(AaTranslateX(Element, If(IsHigh, 25, 20), PageLeftSlideTime, Delay, New AniEaseOutBack(If(IsHigh, AniEasePower.Middle, AniEasePower.Weak))))
                        If TypeOf Element Is MyListItem Then
                            AniList.Add(AaCode(
                            Sub()
                                CType(Element, MyListItem).IsMouseOverAnimationEnabled = True
                                CType(Element, MyListItem).RefreshColor(Me, New EventArgs)
                            End Sub, Delay + 280))
                        End If
                        Delay += Math.Max((If(IsHigh, 22, 15)) - Id, If(IsHigh, 9, 7)) * 2
                        Id += 1
                    End If
                End If
            Next
            AniStart(AniList, "PageLeft PageChange " & Uuid)
        End If
    End Sub
    Public Sub TriggerHideAnimation()
        Dim Advanced As Boolean = AniAdvanced
        Dim Level As Integer = If(Advanced, AniAdvancedLevel, AniDetail)
        Dim IsLow As Boolean = Level = 0
        Dim IsHigh As Boolean = Level = 2
        If AnimatedControl Is Nothing Then
            '缩放动画
            RenderTransformOrigin = New Point(0.5, 0.5)
            If Advanced Then
                '高级动画：只淡出，不缩小
                RenderTransform = New ScaleTransform(1, 1)
                AniStart(AaOpacity(Me, -Opacity, AniAdvancedPick(200, 260, 320)), "PageLeft PageChange " & Uuid)
            ElseIf IsLow Then
                '低：直接淡出，不做缩小
                RenderTransform = New ScaleTransform(1, 1)
                AniStart(AaOpacity(Me, -Opacity, PageLeftFadeTime), "PageLeft PageChange " & Uuid)
            Else
                Dim EndScale As Double = If(IsHigh, 0.92, 0.95)
                If TypeOf RenderTransform IsNot ScaleTransform Then RenderTransform = New ScaleTransform(1, 1)
                AniStart({
                    AaScaleTransform(Me, EndScale - CType(RenderTransform, ScaleTransform).ScaleX, PageLeftScaleTime,, New AniEaseInFluent(AniEasePower.Weak)),
                    AaOpacity(Me, -Opacity, PageLeftFadeTime, PageLeftHideDelay)
                }, "PageLeft PageChange " & Uuid)
            End If
        Else
            '逐个退出动画
            Dim AniList As New List(Of AniData)
            Dim Id As Integer = 0
            Dim Controls = GetAllAnimControls()
            Dim StepTime As Integer = If(Advanced, AniAdvancedPick(0, 8, 12), If(IsLow, 0, If(IsHigh, 16, 70 / Math.Max(1, Controls.Count))))
            Dim OpacityTime As Integer = If(Advanced, AniAdvancedPick(160, 190, 220), PageLeftFadeTime)
            For Each Element As FrameworkElement In Controls
                Dim Delay As Integer = 0
                If Not IsLow OrElse Advanced Then Delay = Math.Min(StepTime * Id, If(Advanced, AniAdvancedPick(0, 70, 110), If(IsHigh, 110, 70)))
                AniList.Add(AaOpacity(Element, -Element.Opacity, OpacityTime, Delay))
                If Not IsLow AndAlso Not Advanced Then AniList.Add(AaTranslateX(Element, If(IsHigh, -8, -6), OpacityTime, Delay))
                Id += 1
            Next
            AniStart(AniList, "PageLeft PageChange " & Uuid)
        End If
    End Sub

    '遍历获取所有需要生成动画的控件
    Private Function GetAllAnimControls(Optional IgnoreInvisibility As Boolean = False) As List(Of FrameworkElement)
        Dim AllControls As New List(Of FrameworkElement)
        GetAllAnimControls(FindName(AnimatedControl), AllControls, IgnoreInvisibility)
        Return AllControls
    End Function
    Private Sub GetAllAnimControls(Element As FrameworkElement, ByRef AllControls As List(Of FrameworkElement), IgnoreInvisibility As Boolean)
        If Not IgnoreInvisibility AndAlso Element.Visibility = Visibility.Collapsed Then Return
        If TypeOf Element Is MyTextButton Then
            AllControls.Add(Element)
        ElseIf TypeOf Element Is MyListItem Then
            AllControls.Add(Element)
        ElseIf TypeOf Element Is ContentControl Then
            GetAllAnimControls(CType(Element, ContentControl).Content, AllControls, IgnoreInvisibility)
        ElseIf TypeOf Element Is Panel Then
            For Each Element2 As FrameworkElement In CType(Element, Panel).Children
                GetAllAnimControls(Element2, AllControls, IgnoreInvisibility)
            Next
        Else
            AllControls.Add(Element)
        End If
    End Sub

End Class
