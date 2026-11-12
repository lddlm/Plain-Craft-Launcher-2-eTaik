Public Class MyPageRight
    Inherits AdornerDecorator
    Public PageUuid As Integer = GetUuid()

    '“返回顶部” 按钮检测的滚动区域
    Public Property PanScroll As String '需要在 Loaded 之后才能获取到控件，所以不能用 Binding 直接绑定（#6664）
        Get
            Return GetValue(PanScrollProperty)
        End Get
        Set(value As String)
            SetValue(PanScrollProperty, value)
        End Set
    End Property
    Private Shared ReadOnly PanScrollProperty =
        DependencyProperty.Register("PanScroll", GetType(String), GetType(MyPageRight), New PropertyMetadata(Nothing))

    '当前状态
    Public Enum PageStates
        Empty '默认状态，页面全空
        LoaderWait '加载环初始等待
        LoaderEnter '加载环进入动画
        LoaderStayForce '加载环正常显示（强制等待）
        LoaderStay '加载环正常显示
        LoaderExit '加载环退出动画
        ContentEnter '内容进入动画
        ContentStay '内容正常显示
        ContentExit '刷新导致的全部退出动画，或页面内容退出（子页面更改）导致的全部退出动画
        PageExit '切换页面导致的全部退出动画
    End Enum
    Private _PageState As PageStates = PageStates.Empty
    Public Property PageState As PageStates
        Get
            Return _PageState
        End Get
        Set(value As PageStates)
            If _PageState = value Then Return
            _PageState = value
            Logger.Trace($"页面状态切换为 {value}")
        End Set
    End Property

#Region "加载器"

    Private PageLoader As LoaderBase
    Private PageLoaderInputInvoke
    Private PageLoaderUi As MyLoading
    Private PanLoader As FrameworkElement
    Private PanContent As FrameworkElement
    Private PanAlways As FrameworkElement
    Private PageLoaderAutoRun As Boolean

    '初始化
    ''' <summary>
    ''' 表明页面存在需要在后台执行的加载器。
    ''' </summary>
    ''' <param name="LoaderUi">MyLoading 控件。</param>
    ''' <param name="PanLoader">MyLoading 控件对应的卡片。</param>
    ''' <param name="PanContent">加载结束后出现的内容容器。</param>
    ''' <param name="PanAlways">无论是否在加载总是要显示的容器。可以为 Nothing。</param>
    ''' <param name="RealLoader">在工作线程执行的加载器。</param>
    ''' <param name="FinishedInvoke">当加载器执行完成，在 UI 线程触发的 UI 初始化事件。</param>
    Public Sub PageLoaderInit(Of TIn, TOut)(LoaderUi As MyLoading, PanLoader As FrameworkElement, PanContent As FrameworkElement, PanAlways As FrameworkElement,
                              RealLoader As LoaderTask(Of TIn, TOut),
                              Optional FinishedInvoke As Action(Of LoaderTask(Of TIn, TOut)) = Nothing, Optional InputInvoke As Func(Of TIn) = Nothing,
                              Optional AutoRun As Boolean = True)
        '初始化参数
        Me.PanLoader = PanLoader
        Me.PanContent = PanContent
        Me.PanAlways = PanAlways
        Me.PageLoader = RealLoader
        Me.PageLoaderUi = LoaderUi
        Me.PageLoaderInputInvoke = InputInvoke
        Me.PageLoaderAutoRun = AutoRun
        '添加结束 Invoke
        If FinishedInvoke IsNot Nothing Then
            AddHandler RealLoader.PreviewFinish,
            Sub()
                Do While PageState = MyPageRight.PageStates.PageExit OrElse PageState = MyPageRight.PageStates.ContentExit
                    Thread.Sleep(10) '不在退出动画时执行 UI 线程操作，避免退出动画被重置
                Loop
                RunInUiWait(Sub() FinishedInvoke(RealLoader))
                Thread.Sleep(20) '由于大量初始化控件会导致掉帧，延迟触发 State 改变事件
            End Sub
        End If
        AddHandler RealLoader.OnStateChangedUi, Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState) RunInUi(Sub() PageLoaderState(Loader, NewState, OldState))
        '隐藏 UI
        PanLoader.Visibility = Visibility.Collapsed
        PanContent.Visibility = Visibility.Collapsed
        If PanAlways IsNot Nothing Then PanAlways.Visibility = Visibility.Collapsed
        '初次运行加载器
        If PageLoaderAutoRun Then
            '上下两个重载只有这里不一样
            PageLoader.Start(RealLoader.StartGetInput(Nothing, PageLoaderInputInvoke))
        End If
        If PageLoader.State = LoadState.Finished AndAlso FinishedInvoke IsNot Nothing Then
            RunInUiWait(Sub() FinishedInvoke(RealLoader)) '加载器已提前完成，直接触发事件
        End If
        '设置加载环
        PageLoaderUi.State = RealLoader
        AddHandler PageLoaderUi.Click, Sub() If RealLoader.State = LoadState.Failed Then PageLoaderRestart() '点击重试事件
    End Sub
    ''' <summary>
    ''' 表明页面存在需要在后台执行的加载器。
    ''' </summary>
    ''' <param name="LoaderUi">MyLoading 控件。</param>
    ''' <param name="PanLoader">MyLoading 控件对应的卡片。</param>
    ''' <param name="PanContent">加载结束后出现的内容容器。</param>
    ''' <param name="PanAlways">无论是否在加载总是要显示的容器。可以为 Nothing。</param>
    ''' <param name="RealLoader">在工作线程执行的加载器。</param>
    ''' <param name="FinishedInvoke">当加载器执行完成，在 UI 线程触发的 UI 初始化事件。</param>
    Public Sub PageLoaderInit(Of TIn)(LoaderUi As MyLoading, PanLoader As FrameworkElement, PanContent As FrameworkElement, PanAlways As FrameworkElement,
                              RealLoader As LoaderCombo(Of TIn),
                              Optional FinishedInvoke As Action(Of LoaderCombo(Of TIn)) = Nothing, Optional InputInvoke As Func(Of TIn) = Nothing,
                              Optional AutoRun As Boolean = True)
        '初始化参数
        Me.PanLoader = PanLoader
        Me.PanContent = PanContent
        Me.PanAlways = PanAlways
        Me.PageLoader = RealLoader
        Me.PageLoaderUi = LoaderUi
        Me.PageLoaderInputInvoke = InputInvoke
        Me.PageLoaderAutoRun = AutoRun
        '添加结束 Invoke
        If FinishedInvoke IsNot Nothing Then
            AddHandler RealLoader.PreviewFinish,
            Sub()
                Do While PageState = MyPageRight.PageStates.PageExit OrElse PageState = MyPageRight.PageStates.ContentExit
                    Thread.Sleep(10) '不在退出动画时执行 UI 线程操作，避免退出动画被重置
                Loop
                RunInUiWait(Sub() FinishedInvoke(RealLoader))
                Thread.Sleep(20) '由于大量初始化控件会导致掉帧，延迟触发 State 改变事件
            End Sub
        End If
        AddHandler RealLoader.OnStateChangedUi, Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState) RunInUi(Sub() PageLoaderState(Loader, NewState, OldState))
        '隐藏 UI
        PanLoader.Visibility = Visibility.Collapsed
        PanContent.Visibility = Visibility.Collapsed
        If PanAlways IsNot Nothing Then PanAlways.Visibility = Visibility.Collapsed
        '初次运行加载器
        If PageLoaderAutoRun Then
            '上下两个重载只有这里不一样
            Dim Input = Nothing
            If PageLoaderInputInvoke IsNot Nothing Then Input = PageLoaderInputInvoke()
            PageLoader.Start(Input)
        End If
        If PageLoader.State = LoadState.Finished AndAlso FinishedInvoke IsNot Nothing Then
            RunInUiWait(Sub() FinishedInvoke(RealLoader)) '加载器已提前完成，直接触发事件
        End If
        '设置加载环
        PageLoaderUi.State = RealLoader
        AddHandler PageLoaderUi.Click, Sub() If RealLoader.State = LoadState.Failed Then PageLoaderRestart() '点击重试事件
    End Sub
    '重试
    Public Sub PageLoaderRestart(Optional Input As Object = Nothing, Optional IsForceRestart As Boolean = True) '由外部调用的重试
        If Not PageLoaderAutoRun Then Return
        If GeneralUtils.IsGenericInstanceOf(PageLoader, GetType(LoaderTask(Of ,))) OrElse TypeOf PageLoader Is LoaderWorker Then
            PageLoader.Start(CType(PageLoader, Object).StartGetInput(Input, PageLoaderInputInvoke), IsForceRestart:=IsForceRestart)
        Else
            If Input Is Nothing AndAlso PageLoaderInputInvoke IsNot Nothing Then Input = PageLoaderInputInvoke()
            PageLoader.Start(Input, IsForceRestart:=IsForceRestart)
        End If
    End Sub

#End Region

#Region "事件"

    '外部触发的事件
    ''' <summary>
    ''' 需要切换到当前页面，并且原本的 Loaded 事件已执行完成。
    ''' 需要根据加载器状态，从 Empty 切换到 ContentEnter、LoaderWait、LoaderEnter。
    ''' </summary>
    Public Sub PageOnEnter()
        Logger.Trace("已触发 PageOnEnter")
        RaiseEvent PageEnter()
        Select Case PageState
            Case PageStates.Empty
                If PageLoader Is Nothing OrElse PageLoader.State = LoadState.Finished OrElse PageLoader.State = LoadState.Waiting OrElse PageLoader.State = LoadState.Canceled Then
                    '如果加载器在进入页面时不启动（例如联机），那么在此时就会有 State = Waiting
                    PageState = PageStates.ContentEnter
                    TriggerEnterAnimation(PanAlways, If(PanContent, Child))
                ElseIf PageLoader.State = LoadState.Loading Then
                    PageState = PageStates.LoaderWait
                    AniStart(AaCode(AddressOf PageOnLoaderWaitFinished, 400), "PageRight PageChange " & PageUuid)
                Else 'PageLoader.State = LoadState.Failed
                    PageState = PageStates.LoaderEnter
                    TriggerEnterAnimation(PanAlways, PanLoader)
                End If
            Case PageStates.ContentExit
                '和上面的一样，但是不管 PanAlways
                If PageLoader Is Nothing OrElse PageLoader.State = LoadState.Finished OrElse PageLoader.State = LoadState.Waiting OrElse PageLoader.State = LoadState.Canceled Then
                    PageState = PageStates.ContentEnter
                    TriggerEnterAnimation(If(PanContent, Child))
                ElseIf PageLoader.State = LoadState.Loading Then
                    PageState = PageStates.LoaderWait
                    AniStart(AaCode(AddressOf PageOnLoaderWaitFinished, 400), "PageRight PageChange " & PageUuid)
                Else 'PageLoader.State = LoadState.Failed
                    PageState = PageStates.LoaderEnter
                    TriggerEnterAnimation(PanLoader)
                End If
            Case PageStates.ContentEnter
                '重复调用 PageOnEnter，直接忽略
            Case Else
                Throw New Exception("在状态为 " & PageState.ToString() & " 时触发了 PageOnEnter 事件。")
        End Select
    End Sub
    Public Event PageEnter()
    ''' <summary>
    ''' 需要切换到其他页面。
    ''' 需要立即切换至 PageExit 或 Empty。
    ''' </summary>
    Public Sub PageOnExit()
        Logger.Trace("已触发 PageOnExit")
        RaiseEvent PageExit()
        Select Case PageState
            Case PageStates.ContentEnter, PageStates.ContentStay
                PageState = PageStates.PageExit
                TriggerExitAnimation(PanAlways, If(PanContent, Child))
            Case PageStates.LoaderEnter, PageStates.LoaderStayForce, PageStates.LoaderStay
                PageState = PageStates.PageExit
                TriggerExitAnimation(PanAlways, PanLoader)
            Case PageStates.LoaderWait
                PageState = PageStates.PageExit
                TriggerExitAnimation(PanAlways)
            Case PageStates.LoaderExit, PageStates.ContentExit
                PageState = PageStates.PageExit
                If PanAlways IsNot Nothing Then TriggerExitAnimation(PanAlways, If(PanContent, Child))
            Case PageStates.PageExit, PageStates.Empty
        End Select
    End Sub
    Public Event PageExit()
    ''' <summary>
    ''' 即将切换到其他页面，需要强制完成页面状态清理。
    ''' 需要立即切换至 Empty。
    ''' </summary>
    Public Sub PageOnForceExit()
        If PageState = PageStates.Empty Then Return
        Logger.Trace("已触发 PageOnForceExit")
        PageState = PageStates.Empty
        AniStop("PageRight PageChange " & PageUuid)
        '由于动画会被强制中止，所以需要手动进行隐藏
        If PageLoader Is Nothing Then
            Child.Visibility = Visibility.Collapsed
        Else
            PanContent.Visibility = Visibility.Collapsed
            PanLoader.Visibility = Visibility.Collapsed
            If PanAlways IsNot Nothing Then PanAlways.Visibility = Visibility.Collapsed
        End If
    End Sub
    ''' <summary>
    ''' PanContent 中的子页面改变，需要让当前内容退出，再显示新的内容。
    ''' 需要在 PageEnter 事件确认要显示的子页面有哪些。
    ''' </summary>
    Public Sub PageOnContentExit()
        Logger.Trace("已触发 PageOnContentExit")
        If PageLoader IsNot Nothing AndAlso PageLoader.State = LoadState.Loading Then
            'Loading 的加载器可能触发进一步变化，难以预测会触发子页面的动画还是加载器完成的动画
            Throw New Exception("在调用 PageOnContentExit 时，加载器不能为 Loading 状态")
        End If
        Select Case PageState
            Case PageStates.ContentEnter, PageStates.ContentStay
                PageState = PageStates.ContentExit
                TriggerExitAnimation(PanContent)
            Case PageStates.LoaderExit
                PageState = PageStates.ContentExit
            Case PageStates.LoaderEnter, PageStates.LoaderStayForce, PageStates.LoaderStay
                PageState = PageStates.ContentExit
                TriggerExitAnimation(PanLoader)
            Case PageStates.LoaderWait
                PageOnEnter()
            Case PageStates.Empty
                '可能是在离开页面后，其他后台线程调用了本函数，此时不应触发 PageOnEnter()
                '例如：在加入联机房间后，切换到其他页面，然后联机房间被解散
                If IsLoaded Then PageOnEnter()
        End Select
    End Sub

    '内部触发的事件
    ''' <summary>
    ''' 逐个进入动画已执行完成。
    ''' 需要根据目前状态，从 ContentEnter 切换到 ContentStay，或从 LoaderEnter 切换到 LoaderStayForce。
    ''' </summary>
    Private Sub PageOnEnterAnimationFinished()
        Logger.Trace("已触发 PageOnEnterAnimationFinished")
        Select Case PageState
            Case PageStates.ContentEnter
                PageState = PageStates.ContentStay
            Case PageStates.LoaderEnter
                PageState = PageStates.LoaderStayForce
                AniStart(AaCode(AddressOf PageOnLoaderStayFinished, 400), "PageRight PageChange " & PageUuid)
            Case Else
                Throw New Exception("在状态为 " & PageState.ToString() & " 时触发了 PageOnEnterAnimationFinished 事件。")
        End Select
    End Sub
    ''' <summary>
    ''' 逐个退出动画已执行完成。
    ''' 需要根据目前状态，从 AllExit 切换到 Empty，或从 LoaderExit 切换到 ContentEnter，或从 ContentExit 重新触发 PageOnEnter。
    ''' </summary>
    Private Sub PageOnExitAnimationFinished()
        Logger.Trace("已触发 PageOnExitAnimationFinished")
        Select Case PageState
            Case PageStates.PageExit
                PageState = PageStates.Empty
            Case PageStates.ContentExit
                PageOnEnter()
            Case PageStates.LoaderExit
                PageState = PageStates.ContentEnter
                TriggerEnterAnimation(PanContent)
            Case Else
                Throw New Exception("在状态为 " & PageState.ToString() & " 时触发了 PageOnExitAnimationFinished 事件。")
        End Select
    End Sub
    ''' <summary>
    ''' 加载环进入等待已结束。
    ''' 需要从 LoaderWait 切换到 LoaderEnter。
    ''' </summary>
    Private Sub PageOnLoaderWaitFinished()
        Logger.Trace("已触发 PageOnLoaderWaitFinished")
        Select Case PageState
            Case PageStates.LoaderWait
                PageState = PageStates.LoaderEnter
                If PanAlways IsNot Nothing AndAlso PanAlways.Visibility = Visibility.Collapsed Then
                    TriggerEnterAnimation(PanAlways, PanLoader)
                Else
                    TriggerEnterAnimation(PanLoader)
                End If
            Case Else
                Throw New Exception("在状态为 " & PageState.ToString() & " 时触发了 PageOnLoaderWaitFinished 事件。")
        End Select
    End Sub
    ''' <summary>
    ''' 加载环展示等待已结束。
    ''' 需要从 LoaderStayForce 切换到 LoaderStay 或 LoaderExit。
    ''' </summary>
    Private Sub PageOnLoaderStayFinished()
        Logger.Trace("已触发 PageOnLoaderStayFinished")
        Select Case PageState
            Case PageStates.LoaderStayForce
                If PageLoader.State = LoadState.Finished Then
                    PageState = PageStates.LoaderExit
                    TriggerExitAnimation(PanLoader)
                Else
                    PageState = PageStates.LoaderStay
                End If
            Case Else
                Throw New Exception("在状态为 " & PageState.ToString() & " 时触发了 PageOnLoaderWaitFinished 事件。")
        End Select
    End Sub

    ''' <summary>
    ''' 全局加载状态已改变。
    ''' </summary>
    Private Sub PageLoaderState(sender As Object, NewState As LoadState, OldState As LoadState)
        Select Case NewState
            Case LoadState.Failed, LoadState.Loading
                If OldState = LoadState.Failed OrElse OldState = LoadState.Loading Then Return
                Logger.Trace("已触发 PageLoaderState (Start/Refresh)")
                '（重新）开始运行
                '需要从部分状态切换到 ReloadExit
                Select Case PageState
                    Case PageStates.ContentEnter, PageStates.ContentStay
                        PageState = PageStates.ContentExit
                        TriggerExitAnimation(PanContent)
                    Case PageStates.LoaderExit
                        PageState = PageStates.ContentExit
                End Select
            Case LoadState.Finished, LoadState.Canceled, LoadState.Waiting
                If Not (OldState = LoadState.Failed OrElse OldState = LoadState.Loading) Then Return
                Logger.Trace("已触发 PageLoaderState (Stop/Canceled)")
                '运行结束
                '需要从 LoaderWait 切换到 ContentEnter，或从 LoaderStay 切换到 LoaderExit
                Select Case PageState
                    Case PageStates.LoaderWait
                        PageState = PageStates.ContentEnter
                        If PanAlways IsNot Nothing AndAlso PanAlways.Visibility = Visibility.Collapsed Then
                            TriggerEnterAnimation(PanAlways, PanContent)
                        Else
                            TriggerEnterAnimation(PanContent)
                        End If
                    Case PageStates.LoaderStay
                        PageState = PageStates.LoaderExit
                        TriggerExitAnimation(PanLoader)
                End Select
        End Select
    End Sub

#End Region

#Region "动画"

    ''' <summary>
    ''' 单页最多参与「逐个入场」动画的元素数量。超出的元素直接呈现终态，不生成动画。
    ''' </summary>
    Private Const MaxEnterAnimCount As Integer = 32
    ''' <summary>
    ''' 「高」动画细节下允许参与逐个入场的元素数量上限。
    ''' </summary>
    Private Const MaxEnterAnimCountHigh As Integer = 44
    ''' <summary>
    ''' 逐个入场动画的延迟累加上限（毫秒）。防止元素很多时最后一个迟迟不出现。
    ''' </summary>
    Private Const MaxEnterAnimDelay As Integer = 420
    ''' <summary>
    ''' 「高」动画细节下逐个入场动画的延迟累加上限（毫秒）。
    ''' </summary>
    Private Const MaxEnterAnimDelayHigh As Integer = 560
    ''' <summary>
    ''' 逐个入场动画的延迟步长（毫秒）。
    ''' </summary>
    Private Const EnterAnimStep As Integer = 18
    ''' <summary>
    ''' 「高」动画细节下逐个入场动画的延迟步长（毫秒）。
    ''' </summary>
    Private Const EnterAnimStepHigh As Integer = 30
    ''' <summary>入场淡入时长（动画时间）。实际时长 = 该值 / AniSpeed。</summary>
    Private Const EnterOpacityTime As Integer = 100
    ''' <summary>入场位移与缩放时长（动画时间）。</summary>
    Private Const EnterMoveTime As Integer = 350
    ''' <summary>退场淡出与位移时长（动画时间）。</summary>
    Private Const ExitOpacityTime As Integer = 70
    ''' <summary>退场滚动条时长（动画时间）。</summary>
    Private Const ExitScrollTime As Integer = 90

    ''' <summary>
    ''' 让一批元素按当前动画体系逐个入场：尊重「动画速度」「动画细节」「高级动画」。
    ''' 与页面级入场用同一套参数，但**不会改动页面状态机**，可用于按需展开的卡片。
    ''' </summary>
    Public Sub AnimateElementsIn(ParamArray Elements As FrameworkElement())
        Dim RealElements = Elements.Where(Function(e) e IsNot Nothing).ToArray()
        If RealElements.Length = 0 Then Return
        AniStop("PageRight Card " & PageUuid)

        Dim Advanced As Boolean = AniAdvanced
        Dim Level As Integer = If(Advanced, AniAdvancedLevel, AniDetail)
        Dim IsLow As Boolean = Level = 0
        Dim IsHigh As Boolean = Level = 2
        Dim MaxCount As Integer
        Dim MaxDelay As Integer
        Dim StepTime As Integer
        Dim FadeTime As Integer
        Dim MoveTime As Integer
        Dim Rise As Double
        Dim MoveEase As AniEase
        If Advanced Then
            MaxCount = AniAdvancedPick(20, 32, 44)
            MaxDelay = AniAdvancedPick(200, 360, 500)
            StepTime = AniAdvancedPick(8, 20, 30)
            FadeTime = AniAdvancedPick(200, 260, 320)
            MoveTime = AniAdvancedPick(340, 420, 500)
            Rise = AniAdvancedPick(4.0, 8.0, 12.0)
            MoveEase = New AniEaseOutBack(AniAdvancedPick(AniEasePower.Weak, AniEasePower.Middle, AniEasePower.Middle))
        Else
            MaxCount = If(IsHigh, MaxEnterAnimCountHigh, MaxEnterAnimCount)
            MaxDelay = If(IsHigh, MaxEnterAnimDelayHigh, MaxEnterAnimDelay)
            StepTime = If(IsLow, 0, If(IsHigh, EnterAnimStepHigh, EnterAnimStep))
            FadeTime = EnterOpacityTime
            MoveTime = EnterMoveTime
            Rise = If(IsHigh, 16.0, 12.0)
            MoveEase = New AniEaseOutBack(If(IsHigh, AniEasePower.Middle, AniEasePower.Weak))
        End If

        For Each Element In RealElements
            Element.Visibility = Visibility.Visible
        Next
        Dim AniList As New List(Of AniData)
        Dim Delay As Integer = 0
        Dim Animated As Integer = 0
        For Each Element In RealElements
            For Each Control As FrameworkElement In GetAllAnimControls(Element)
                If Animated >= MaxCount Then
                    '元素太多时后面的直接呈现终态，避免上千条动画同时推进
                    Control.Opacity = 1
                    Control.RenderTransform = Nothing
                ElseIf IsLow Then
                    Animated += 1
                    Control.Opacity = 0
                    Control.RenderTransform = Nothing
                    AniList.Add(AaOpacity(Control, 1, FadeTime, 0, New AniEaseOutFluent(AniEasePower.Weak)))
                Else
                    Animated += 1
                    Control.Opacity = 0
                    Control.RenderTransform = New TranslateTransform(0, -Rise)
                    AniList.Add(AaOpacity(Control, 1, FadeTime, Delay, New AniEaseOutFluent(AniEasePower.Weak)))
                    AniList.Add(AaTranslateY(Control, Rise, MoveTime, Delay, MoveEase))
                    Delay = Math.Min(Delay + StepTime, MaxDelay)
                End If
            Next
        Next
        AniStart(AniList, "PageRight Card " & PageUuid)
    End Sub

    ''' <summary>
    ''' 让一批元素按当前动画体系逐个退场，结束后隐藏它们并执行 OnDone（在 UI 线程）。
    ''' </summary>
    Public Sub AnimateElementsOut(Elements As FrameworkElement(), OnDone As Action)
        Dim RealElements = Elements.Where(Function(e) e IsNot Nothing).ToArray()
        If RealElements.Length = 0 Then Return
        AniStop("PageRight Card " & PageUuid)

        Dim Advanced As Boolean = AniAdvanced
        Dim Level As Integer = If(Advanced, AniAdvancedLevel, AniDetail)
        Dim IsLow As Boolean = Level = 0
        Dim FadeTime As Integer = If(Advanced, AniAdvancedPick(140, 190, 240), ExitOpacityTime)
        Dim StepTime As Integer = If(Advanced, AniAdvancedPick(0, 8, 12), If(IsLow, 0, If(Level = 2, 12, 8)))
        Dim MaxDelay As Integer = If(Advanced, AniAdvancedPick(0, 90, 150), If(IsLow, 0, If(Level = 2, 200, 150)))
        Dim Move As Double = If(Advanced, AniAdvancedPick(0.0, 6.0, 10.0), If(Level = 2, 10.0, 6.0))

        Dim AniList As New List(Of AniData)
        Dim Delay As Integer = 0
        For Each Element In RealElements
            For Each Control As FrameworkElement In GetAllAnimControls(Element)
                AniList.Add(AaOpacity(Control, -Control.Opacity, FadeTime, Delay))
                If Not IsLow AndAlso Move > 0 Then AniList.Add(AaTranslateY(Control, -Move, FadeTime, Delay))
                Delay = Math.Min(Delay + StepTime, MaxDelay)
            Next
        Next
        AniList.Add(AaCode(
        Sub()
            For Each Element In RealElements
                Element.Visibility = Visibility.Collapsed
                Element.Opacity = 1
            Next
            If OnDone IsNot Nothing Then OnDone()
        End Sub,, True))
        AniStart(AniList, "PageRight Card " & PageUuid)
    End Sub

    '逐个进入动画
    Public Sub TriggerEnterAnimation(ParamArray Elements As FrameworkElement())
        Dim RealElements = Elements.Where(Function(e) e IsNot Nothing)
        For Each Element In RealElements
            Element.Visibility = Visibility.Visible '页面均处于默认的隐藏状态
        Next
        '高级动画开启时用自己的一套参数，与「动画速度」「动画细节」完全无关
        Dim Advanced As Boolean = AniAdvanced
        Dim Level As Integer = If(Advanced, AniAdvancedLevel, AniDetail)
        Dim IsLow As Boolean = Level = 0
        Dim IsHigh As Boolean = Level = 2
        Dim MaxCount As Integer
        Dim MaxDelay As Integer
        Dim StepTime As Integer
        Dim FadeTime As Integer
        Dim MoveTime As Integer
        Dim Rise As Double
        Dim CardRise As Double
        Dim CardFromScale As Double
        Dim MoveEase As AniEase
        If Advanced Then
            MaxCount = AniAdvancedPick(24, 32, 40) '少而大：同时推进的元素越少，帧越稳、越丝滑
            MaxDelay = AniAdvancedPick(240, 520, 900)
            StepTime = AniAdvancedPick(8, 26, 48)
            FadeTime = AniAdvancedPick(220, 300, 400)
            MoveTime = AniAdvancedPick(380, 480, 600)
            Rise = AniAdvancedPick(5.0, 12.0, 20.0)
            CardRise = Rise
            CardFromScale = AniAdvancedPick(0.988, 0.978, 0.965)
            MoveEase = New AniEaseOutBack(AniAdvancedPick(AniEasePower.Weak, AniEasePower.Middle, AniEasePower.Middle))
        Else
            MaxCount = If(IsHigh, MaxEnterAnimCountHigh, MaxEnterAnimCount)
            MaxDelay = If(IsHigh, MaxEnterAnimDelayHigh, MaxEnterAnimDelay)
            StepTime = If(IsLow, 0, If(IsHigh, EnterAnimStepHigh, EnterAnimStep))
            FadeTime = EnterOpacityTime
            MoveTime = EnterMoveTime
            Rise = If(IsHigh, 20.0, 16.0)
            CardRise = 24
            CardFromScale = 0.972
            MoveEase = New AniEaseOutBack(AniEasePower.Middle)
        End If
        Dim AniList As New List(Of AniData)
        Dim Delay As Integer = 0
        Dim Animated As Integer = 0 '已经生成入场动画的元素数
        '基础动画
        For Each Element In RealElements
            For Each Control As FrameworkElement In GetAllAnimControls(Element, True)
                '还原被隐藏的卡片的消失动画
                Control.IsHitTestVisible = True
                If TypeOf Control.RenderTransform Is TranslateTransform OrElse TypeOf Control.RenderTransform Is TransformGroup Then Control.RenderTransform = Nothing
            Next
            For Each Control As FrameworkElement In GetAllAnimControls(Element)
                If TypeOf Control Is MyExtraTextButton Then
                    CType(Control, MyExtraTextButton).Show = True
                ElseIf Animated >= MaxCount Then
                    '元素太多：只给前 MaxCount 个做逐个入场，其余直接呈现终态。
                    '否则会有上千条动画同时挂在队列里，每帧遍历它们必然掉帧，
                    '而且延迟是累加的，最后一个元素要等几十秒才出现。
                    Control.Opacity = 1
                    Control.RenderTransform = Nothing
                Else
                    Animated += 1
                    Control.Opacity = 0
                    If IsLow Then
                        '低：全部元素同时淡入，不做位移、缩放与错落
                        AniList.Add(AaOpacity(Control, 1, FadeTime, 0, New AniEaseOutFluent(AniEasePower.Weak)))
                    ElseIf IsHigh AndAlso TypeOf Control Is MyCard Then
                        '高：卡片从略小一点浮起并放大到原尺寸
                        Dim Trans As New TranslateTransform(0, -CardRise)
                        Dim Scale As New ScaleTransform(CardFromScale, CardFromScale)
                        Dim ScaleGain As Double = 1 - CardFromScale
                        Dim Group As New TransformGroup()
                        Group.Children.Add(Trans)
                        Group.Children.Add(Scale)
                        Control.RenderTransform = Group
                        Control.RenderTransformOrigin = New Point(0.5, 0.5)
                        AniList.Add(AaOpacity(Control, 1, FadeTime, Delay, New AniEaseOutFluent(AniEasePower.Weak)))
                        AniList.Add(AaDouble(
                        Sub(i)
                            'i 是本帧的插值增量，累加即可同时推进位移与缩放
                            Trans.Y += CardRise * i
                            Scale.ScaleX += ScaleGain * i
                            Scale.ScaleY += ScaleGain * i
                        End Sub, 1, MoveTime, Delay, MoveEase))
                        Delay = Math.Min(Delay + StepTime, MaxDelay)
                    Else
                        Control.RenderTransform = New TranslateTransform(0, -Rise)
                        AniList.Add(AaOpacity(Control, 1, FadeTime, Delay, New AniEaseOutFluent(AniEasePower.Weak)))
                        If Advanced Then
                            '高级：单段平滑位移，没有回弹过冲
                            AniList.Add(AaTranslateY(Control, Rise, MoveTime, Delay, MoveEase))
                        Else
                            '一段位移即可（原来分两段，动画数量多 1/3，设置页这种重页面会掉帧）
                            AniList.Add(AaTranslateY(Control, Rise, MoveTime, Delay, New AniEaseOutBack(If(IsHigh, AniEasePower.Middle, AniEasePower.Weak))))
                        End If
                        '延迟逐个累加，但封顶，避免最后几个元素迟迟不出现
                        Delay = Math.Min(Delay + StepTime, MaxDelay)
                    End If
                End If
            Next
        Next
        '滚动条动画
        Dim Scroll As MyScrollBar = GetFirstScrollViewer(RealElements)
        If Scroll IsNot Nothing Then
            If TypeOf Scroll.RenderTransform IsNot TranslateTransform Then Scroll.RenderTransform = New TranslateTransform(If(IsHigh, 14, 10), 0)
            If Not IsLow AndAlso Not Advanced Then AniList.Add(AaTranslateX(Scroll, -CType(Scroll.RenderTransform, TranslateTransform).X, EnterMoveTime, 0, New AniEaseOutFluent))
        End If
        '结束
        AniList.Add(AaCode(Sub() PageOnEnterAnimationFinished(),, True))
        AniStart(AniList, "PageRight PageChange " & PageUuid, True)
    End Sub

    '逐个退出动画
    Public Sub TriggerExitAnimation(ParamArray Elements As FrameworkElement())
        Dim RealElements = Elements.Where(Function(e) e IsNot Nothing)
        Dim Advanced As Boolean = AniAdvanced
        Dim Level As Integer = If(Advanced, AniAdvancedLevel, AniDetail)
        Dim IsLow As Boolean = Level = 0
        Dim IsHigh As Boolean = Level = 2
        Dim MaxCount As Integer = If(Advanced, AniAdvancedPick(40, 56, 72), If(IsHigh, MaxEnterAnimCountHigh, MaxEnterAnimCount))
        Dim OpacityTime As Integer = If(Advanced, AniAdvancedPick(140, 200, 260), ExitOpacityTime)
        Dim StepTime As Integer = If(Advanced, AniAdvancedPick(0, 10, 14), If(IsLow, 0, If(IsHigh, 14, 10)))
        Dim MaxDelay As Integer = If(Advanced, AniAdvancedPick(0, 110, 180), If(IsLow, 0, If(IsHigh, 260, 200)))
        Dim TransDistance As Double = If(IsHigh, -10, -6)
        Dim AniList As New List(Of AniData)
        Dim Delay As Integer = 0
        Dim Animated As Integer = 0
        For Each Element In RealElements
            For Each Control As FrameworkElement In GetAllAnimControls(Element)
                If TypeOf Control Is MyExtraTextButton Then
                    CType(Control, MyExtraTextButton).Show = False
                ElseIf Animated >= MaxCount Then
                    '同样是限制数量：超出的直接隐藏，不生成动画
                    Control.IsHitTestVisible = False
                    Control.Opacity = 0
                Else
                    Animated += 1
                    Control.IsHitTestVisible = False
                    AniList.Add(AaOpacity(Control, -1, OpacityTime, Delay))
                    If Not IsLow AndAlso Not Advanced Then AniList.Add(AaTranslateY(Control, TransDistance, OpacityTime, Delay))
                    Delay = Math.Min(Delay + StepTime, MaxDelay)
                End If
            Next
        Next
        '滚动条动画
        Dim Scroll As MyScrollBar = GetFirstScrollViewer(RealElements)
        If Scroll IsNot Nothing AndAlso Not IsLow AndAlso Not Advanced Then
            If TypeOf Scroll.RenderTransform IsNot TranslateTransform Then Scroll.RenderTransform = New TranslateTransform
            AniList.Add(AaTranslateX(Scroll, If(IsHigh, 12, 10) - CType(Scroll.RenderTransform, TranslateTransform).X, ExitScrollTime, 0, New AniEaseInFluent))
        End If
        '结束
        AniList.Add(AaCode(
        Sub()
            For Each Element In RealElements
                Element.Visibility = Visibility.Collapsed
            Next
            PageOnExitAnimationFinished()
        End Sub,, True))
        AniStart(AniList, "PageRight PageChange " & PageUuid)
    End Sub

    ''' <summary>
    ''' 禁用页面切换动画的控件列表。
    ''' </summary>
    Public DisabledPageAnimControls As New List(Of FrameworkElement)
    ''' <summary>
    ''' 遍历获取所有需要生成动画的控件。
    ''' </summary>
    Friend Function GetAllAnimControls(Element As FrameworkElement, Optional IgnoreInvisibility As Boolean = False) As IEnumerable(Of FrameworkElement)
        Dim AllControls As New List(Of FrameworkElement)
        _GetAllAnimControls(Element, AllControls, IgnoreInvisibility)
        Return AllControls.Except(DisabledPageAnimControls)
    End Function
    Private Sub _GetAllAnimControls(Element As FrameworkElement, ByRef AllControls As List(Of FrameworkElement), IgnoreInvisibility As Boolean)
        If Not IgnoreInvisibility AndAlso Element.Visibility = Visibility.Collapsed Then Return
        If TypeOf Element Is MyCard OrElse TypeOf Element Is MyHint OrElse TypeOf Element Is MyExtraTextButton OrElse TypeOf Element Is TextBlock OrElse TypeOf Element Is MyTextButton Then
            AllControls.Add(Element)
        ElseIf TypeOf Element Is ContentControl Then
            Dim Content = CType(Element, ContentControl).Content
            If Content IsNot Nothing AndAlso TypeOf Content Is FrameworkElement Then _GetAllAnimControls(Content, AllControls, IgnoreInvisibility)
        ElseIf TypeOf Element Is Panel Then
            For Each Element2 In CType(Element, Panel).Children
                If TypeOf Element2 Is FrameworkElement Then _GetAllAnimControls(Element2, AllControls, IgnoreInvisibility)
            Next
        End If
    End Sub

    '查找列表中的第一个滚动条
    Private Function GetFirstScrollViewer(Elements As IEnumerable(Of FrameworkElement)) As MyScrollBar
        Dim Viewer As MyScrollViewer = Nothing
        For Each Element In Elements
            If TypeOf Element Is MyScrollViewer Then
                Viewer = Element
                GoTo FindViewer
            End If
            For Each Control In LogicalTreeHelper.GetChildren(Element)
                If TypeOf Control Is MyScrollViewer Then
                    Viewer = Control
                    GoTo FindViewer
                End If
            Next
        Next
        Return Nothing
FindViewer:
        If Viewer.ComputedVerticalScrollBarVisibility <> Visibility.Visible Then Return Nothing
        Return Viewer.ScrollBar
    End Function

#End Region

End Class
