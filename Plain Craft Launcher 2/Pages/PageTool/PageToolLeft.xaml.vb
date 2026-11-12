''' <summary>
''' 工具箱左侧栏。
''' Ultra：录制 / 截屏两项；以后加工具时在这里加 MyListItem，并在 PageGet / PageChange 里补分支。
''' </summary>
Public Class PageToolLeft

    ''' <summary>
    ''' 当前页面的编号。
    ''' </summary>
    Public PageID As FormMain.PageSubType

    Public Sub New()
        InitializeComponent()
        PageID = FormMain.PageSubType.ToolRecord
    End Sub

    ''' <summary>
    ''' 勾选事件改变页面。
    ''' </summary>
    Private Sub PageCheck(sender As MyListItem, e As EventArgs) Handles ItemRecord.Check, ItemScreenshot.Check
        '尚未初始化控件属性时，sender.Tag 为 Nothing，跳过切换
        If sender.Tag IsNot Nothing Then PageChange(Val(sender.Tag))
    End Sub

    ''' <summary>
    ''' 获取当前导航指定的右页面。
    ''' </summary>
    Public Function PageGet(Optional ID As FormMain.PageSubType = -1) As MyPageRight
        If ID = -1 Then ID = PageID
        Select Case ID
            Case FormMain.PageSubType.ToolRecord
                If FrmToolRecord Is Nothing Then FrmToolRecord = New PageToolRecord
                Return FrmToolRecord
            Case FormMain.PageSubType.ToolScreenshot
                If FrmToolScreenshot Is Nothing Then FrmToolScreenshot = New PageToolScreenshot
                Return FrmToolScreenshot
            Case Else
                Throw New Exception("未知的工具箱子页面种类：" & ID)
        End Select
    End Function

    ''' <summary>
    ''' 切换现有页面。
    ''' </summary>
    Public Sub PageChange(ID As FormMain.PageSubType)
        If PageID = ID Then Return
        AniControlEnabled += 1
        Try
            Select Case ID
                Case FormMain.PageSubType.ToolRecord
                    If IsNothing(FrmToolRecord) Then FrmToolRecord = New PageToolRecord
                    PageChangeRun(FrmToolRecord)
                Case FormMain.PageSubType.ToolScreenshot
                    If IsNothing(FrmToolScreenshot) Then FrmToolScreenshot = New PageToolScreenshot
                    PageChangeRun(FrmToolScreenshot)
                Case Else
                    Throw New Exception("未知的工具箱子页面种类：" & ID)
            End Select
            PageID = ID
        Catch ex As Exception
            Logger.Error(ex, $"切换分页面失败（ID {ID}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Shared Sub PageChangeRun(Target As MyPageRight)
        AniStop("FrmMain PageChangeRight") '停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        FrmMain.PageRight = Target
        CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
        AniStart({
            AaCode(
            Sub()
                CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                FrmMain.PanMainRight.Child = FrmMain.PageRight
                FrmMain.PageRight.Opacity = 0
            End Sub, 130),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                FrmMain.PageRight.Opacity = 1
                FrmMain.PageRight.PageOnEnter()
            End Sub, 30, True)
        }, "PageLeft PageChange")
    End Sub

End Class
