Public Class PageSetupMore

    Private IsInit As Boolean = False
    Private CurrentRight As MyPageRight = Nothing

    Private Sub PageSetupMore_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If IsInit Then Return
        IsInit = True
        PageSetupUI.HiddenRefresh()
        '只把第一页放好；入场动画统一由 PageEnter 触发，避免连续播两次
        ShowRight(GetRightPage(0), False)
    End Sub

    ''' <summary>
    ''' 每次进入本子页时，按 PCL 原生体系重播入场动画：
    ''' 左栏走 MyPageLeft.TriggerShowAnimation，右侧走页面自己的 PageOnEnter。
    ''' </summary>
    Private Sub PageSetupMore_PageEnter() Handles Me.PageEnter
        LeftNav.TriggerShowAnimation()
        PlayCurrentEnter()
    End Sub

    ''' <summary>取回原「更多」页的右页面实例，与全局缓存共用同一份。</summary>
    Private Function GetRightPage(ID As Integer) As MyPageRight
        Select Case ID
            Case 0
                If FrmOtherHelp Is Nothing Then FrmOtherHelp = New PageOtherHelp
                Return FrmOtherHelp
            Case 1
                If FrmOtherAbout Is Nothing Then FrmOtherAbout = New PageOtherAbout
                Return FrmOtherAbout
            Case 2
                If FrmOtherTest Is Nothing Then FrmOtherTest = New PageOtherTest
                Return FrmOtherTest
            Case Else
                Throw New Exception("未知的更多子页面种类：" & ID)
        End Select
    End Function

    ''' <summary>切换右侧内容。</summary>
    Private Sub ShowRight(Target As MyPageRight, Optional PlayAnim As Boolean = True)
        If CurrentRight Is Target Then
            If PlayAnim Then PlayCurrentEnter()
            Return
        End If
        If CurrentRight IsNot Nothing AndAlso CurrentRight.PageState <> MyPageRight.PageStates.Empty Then
            CurrentRight.PageOnForceExit()
        End If
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        PanRightHost.Child = Target
        CurrentRight = Target
        Target.Opacity = 1
        If Target.PageState <> MyPageRight.PageStates.Empty Then Target.PageOnForceExit()
        If PlayAnim Then PlayCurrentEnter()
    End Sub

    ''' <summary>
    ''' 让当前右页面重新走一遍 PageOnEnter，从而触发它自己的 TriggerEnterAnimation。
    ''' 必须等 Loaded 跑完（页面有加载器），所以用 Background 优先级延后。
    ''' </summary>
    Private Sub PlayCurrentEnter()
        Dim T As MyPageRight = CurrentRight
        If T Is Nothing Then Return
        If T.PageState <> MyPageRight.PageStates.Empty Then
            AniStop("PageRight PageChange " & T.PageUuid)
            T.PageOnForceExit()
        End If
        Dim Weak As MyPageRight = T
        T.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            New Action(
            Sub()
                If CurrentRight IsNot Weak Then Return
                If Weak.PageState = MyPageRight.PageStates.Empty Then Weak.PageOnEnter()
            End Sub))
    End Sub

    ''' <summary>
    ''' 离开本子页前，把自己与内部页面的动画组收干净。
    ''' 否则快速切页时，动画收尾回调会在状态被重置为 Empty 之后才执行，
    ''' MyPageRight 会抛「在状态为 Empty 时触发了 PageOnExitAnimationFinished」。
    ''' </summary>
    Public Sub PrepareExit()
        AniStop("PageRight PageChange " & PageUuid)
        PageState = MyPageRight.PageStates.Empty
        For Each P As MyPageRight In New MyPageRight() {FrmOtherHelp, FrmOtherAbout, FrmOtherTest}
            If P IsNot Nothing Then
                AniStop("PageRight PageChange " & P.PageUuid)
                P.PageState = MyPageRight.PageStates.Empty
            End If
        Next
    End Sub

    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemAbout.Check, ItemHelp.Check, ItemTest.Check
        '尚未初始化控件属性时，sender.Tag 为 Nothing
        If sender.Tag Is Nothing Then Return
        ShowRight(GetRightPage(Val(sender.Tag)), True)
    End Sub

    '刷新帮助
    Public Sub Refresh(sender As Object, e As EventArgs)
        If Val(sender.Tag) = 0 Then
            PageOtherLeft.RefreshHelp()
            ItemHelp.Checked = True
        End If
        Hint("正在刷新……", Log:=False)
    End Sub

    '反馈
    Private Sub TryFeedback(sender As Object, e As RouteEventArgs) Handles ItemFeedback.Changed
        If Not ItemFeedback.Checked Then Return
        PageOtherLeft.TryFeedback()
        e.Handled = True
    End Sub

    '新功能投票
    Private Sub TryVote(sender As Object, e As RouteEventArgs) Handles ItemVote.Changed
        If Not ItemVote.Checked Then Return
        PageOtherLeft.TryVote()
        e.Handled = True
    End Sub

End Class
