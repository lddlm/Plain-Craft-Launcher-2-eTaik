Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Net.Sockets
Imports PCL

Public Class PageLinkMain
    Private Const INVITE_CODE_VERSION As Integer = 2

    '===============================
    '  状态机与前端页面
    '===============================

#Region "状态管理"

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared LinkState As LinkStates = LinkStates.Waiting

    ''' <summary>
    ''' 切换到指定的状态。
    ''' </summary>
    Private Sub ChangeState(NewState As LinkStates)
        If LinkState = NewState Then Return
        Dim OldState = LinkState
        LinkState = NewState
        Logger.Info($"主状态由 {OldState} 变更为 {NewState}")
        '触发状态切换
        RunInUi(
        Sub()
            Select Case NewState
                Case LinkStates.Waiting
                    SwitchToWaiting(OldState)
                Case LinkStates.Loading
                    SwitchToLoading(OldState)
                Case LinkStates.Failed
                    SwitchToFailed(OldState)
                Case LinkStates.Finished
                    SwitchToFinished(OldState)
            End Select
        End Sub)
    End Sub

    '让加载器的 成功/失败 事件触发状态切换
    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, PanAlways, LinkLoader, AutoRun:=False)
        AddHandler LinkLoader.OnStateChangedUi,
        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
            Select Case NewState
                Case LoadState.Finished
                    ChangeState(LinkStates.Finished)
                Case LoadState.Failed
                    Telemetry("联机失败", "Exception", FilterUserName(Loader.Error.GetDisplay(True), "*"))
                    ChangeState(LinkStates.Failed)
            End Select
        End Sub
    End Sub

    '页面动画兼容（Ultra：功能包未就绪时先显示导入页）
    Private Sub UpdatePanelVisibility() Handles Me.PageEnter, Me.Loaded
        Dim Ready As Boolean = IsPackReady()
        Dim NeedImport As Boolean = (Not Ready) AndAlso LinkState = LinkStates.Waiting
        Me.PanNeedDownload.Visibility = If(NeedImport, Visibility.Visible, Visibility.Collapsed)
        Me.PanSelect.Visibility = If(Ready AndAlso LinkState = LinkStates.Waiting, Visibility.Visible, Visibility.Collapsed)
        Me.PanFinish.Visibility = If(LinkState = LinkStates.Finished, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "功能包导入（Ultra）"

    ''' <summary>
    ''' 联机模块是否已导入并解压完毕。
    ''' </summary>
    Private Function IsPackReady() As Boolean
        Return FileUtils.Exists(PathEasyTier & "联机模块.exe") AndAlso
               FileUtils.Exists(PathEasyTier & "联机模块 CLI.exe")
    End Function

    '导入时：蓝色描边变成以底部中心为起点、沿卡片绕一圈的环形进度条
    Private PackRingLength As Double = 0
    Private PackShownProgress As Double = 0

    Private Sub StartPackProgress()
        Dim W As Double = CardNeedDownload.ActualWidth
        Dim H As Double = CardNeedDownload.ActualHeight
        If W < 10 OrElse H < 10 Then Return
        Dim Inset As Double = 2 '描边中心线，避免被卡片边缘裁掉
        Dim P1 As New Point(W / 2, H - Inset)   '底边中心：起点
        Dim P2 As New Point(W - Inset, H - Inset)
        Dim P3 As New Point(W - Inset, Inset)
        Dim P4 As New Point(Inset, Inset)
        Dim P5 As New Point(Inset, H - Inset)
        Dim Fig As New PathFigure With {.StartPoint = P1, .IsClosed = True}
        Fig.Segments.Add(New PolyLineSegment({P2, P3, P4, P5, P1}, True))
        Dim Geo As New PathGeometry()
        Geo.Figures.Add(Fig)
        PackProgressTrack.Data = Geo
        PackProgressFill.Data = Geo
        PackProgressTrack.Width = W
        PackProgressTrack.Height = H
        PackProgressFill.Width = W
        PackProgressFill.Height = H
        PackRingLength = (P2.X - P1.X) + (P2.Y - P3.Y) + (P3.X - P4.X) + (P5.Y - P4.Y) + (P1.X - P5.X)
        '隐藏静态蓝框，改由轨道 + 进度路径绘制
        CardNeedDownload.CardBorderThickness = New Thickness(0)
        PackProgressTrack.Visibility = Visibility.Visible
        PackProgressFill.Visibility = Visibility.Visible
        PackShownProgress = 0
        UpdatePackProgress(0)
    End Sub

    Private Sub UpdatePackProgress(Value As Double)
        Value = Math.Max(0, Math.Min(1, Value))
        Value = Math.Max(PackShownProgress, Value) '网络重试时进度不倒退
        PackShownProgress = Value
        If PackRingLength > 0 Then
            'StrokeDashArray 的单位是 StrokeThickness 的倍数
            Dim Dash As Double = Value * PackRingLength / 3
            PackProgressFill.StrokeDashArray = New DoubleCollection From {Dash, PackRingLength / 3 + 1}
        End If
        LabPackDesc.Text = $"正在导入联机功能包…… {Value * 100:0}%"
    End Sub

    Private Sub EndPackProgress()
        PackProgressTrack.Visibility = Visibility.Collapsed
        PackProgressFill.Visibility = Visibility.Collapsed
        CardNeedDownload.CardBorderThickness = New Thickness(1.5)
        CardNeedDownload.CardBorderBrush = New SolidColorBrush(Color.FromRgb(19, 112, 243))
    End Sub

    Private IsPackInstalling As Boolean = False
    ''' <summary>
    ''' 从本地导入功能包（分体式：exe 不内嵌、不联网，包由用户放置或选择）。
    ''' 若 EasyTier.zip 就在程序同目录，直接拿来用；否则弹文件选择框。
    ''' </summary>
    Private Sub BtnImportPack_Click() Handles BtnImportPack.Click
        If IsPackInstalling Then Return
        Dim FilePath As String = Paths.Base & "EasyTier.zip"
        If Not FileUtils.Exists(FilePath) Then
            FilePath = Dialogs.SelectFile("选择联机功能包（EasyTier.zip）", False,
                filter:={({"zip"}, "联机功能包")}).FirstOrDefault()
        End If
        If String.IsNullOrWhiteSpace(FilePath) Then Return
        '校验：必须是包含 easytier-core.exe 的 zip
        Try
            Using Zip As IO.Compression.ZipArchive = IO.Compression.ZipFile.OpenRead(FilePath)
                If Not Zip.Entries.Any(Function(E) E.FullName.ToLowerInvariant().EndsWith("easytier-core.exe")) Then
                    Hint("这不是联机功能包（压缩包里没有 easytier-core.exe）。", HintType.Red)
                    Return
                End If
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取导入的功能包失败")
            Hint("无法读取该压缩包，请重新选择。", HintType.Red)
            Return
        End Try
        IsPackInstalling = True
        BtnImportPack.IsEnabled = False
        StartPackProgress()
        RunInThread(
        Sub()
            Try
                IO.Directory.CreateDirectory(PathEasyTier)
                IO.File.Copy(FilePath, PathEasyTier & "EasyTier.zip", True)
                RunInUi(Sub() LabPackDesc.Text = "正在解压联机功能包……")
                InstallPack()
                RunInUi(
                Sub()
                    EndPackProgress()
                    LabPackDesc.Text = "导入完成，现在可以使用联机了！"
                    UpdatePanelVisibility()
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, "导入联机功能包失败", LogBehavior.Toast)
                RunInUi(
                Sub()
                    EndPackProgress()
                    LabPackDesc.Text = "导入失败：" & ex.Message
                End Sub)
            Finally
                RunInUi(Sub() BtnImportPack.IsEnabled = True)
                IsPackInstalling = False
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 解压已导入的 EasyTier.zip。只认本地文件，绝不联网下载。
    ''' </summary>
    Private Sub InstallPack()
        Directory.CreateDirectory(PathEasyTier)
        Dim ZipPath As String = PathEasyTier & "EasyTier.zip"
        If Not FileUtils.Exists(ZipPath) Then Throw New Exception("未找到联机功能包，请先导入 EasyTier.zip")
        '本地解压很快，让环形进度条完整绕一圈
        For i As Integer = 1 To 5
            Dim StepProgress As Double = i / 5
            RunInUi(Sub() UpdatePackProgress(StepProgress))
            Thread.Sleep(220)
        Next
        '解压：不依赖 zip 内是否有顶层目录，直接把文件平铺到模块目录
        Dim ExtractPath As String = PathTemp & "EasyTierPack\"
        If Directory.Exists(ExtractPath) Then Directory.Delete(ExtractPath, True)
        FileUtils.ExtractToDirectory(ZipPath, ExtractPath)
        For Each FilePath As String In Directory.GetFiles(ExtractPath, "*", SearchOption.AllDirectories)
            Dim Name As String = Path.GetFileName(FilePath)
            Dim Dest As String = PathEasyTier & Name
            Select Case Name.ToLowerInvariant()
                Case "easytier-core.exe" : Dest = PathEasyTier & "联机模块.exe"
                Case "easytier-cli.exe" : Dest = PathEasyTier & "联机模块 CLI.exe"
            End Select
            IO.File.Copy(FilePath, Dest, True)
        Next
        RunInUi(Sub() UpdatePackProgress(1))
        FileUtils.Delete(ZipPath)
        Settings.Set("LinkEasyTierVersion", CInt(ServerConfig("Link")("EasyTierVersion")))
        If Not IsPackReady() Then Throw New Exception("功能包解压后未找到联机模块，请重试")
    End Sub

#End Region

#Region "初始页面 (Waiting)"

    '由任意状态切换到 Waiting
    Private Sub SwitchToWaiting(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        SyncLock LinkLoader.LockState
            LinkLoader.State = LoadState.Waiting
        End SyncLock
        ProcessStop()
        '清掉上一次房间的模式信息，避免下一次建房沿用
        StopMemberService()
        DirectMode = False
        DirectPeers.Clear()
        DirectHosts.Clear()
        ShortCode = ""
        PublicIpv4 = ""
        UpnpMapped = False
        Ipv6ListenersPort = 0
        ListenersPort = 0
        Peers = Nothing
        NATType = NATTypes.Pending
        FailCount = 0
        '页面切换
        If OldState <> LinkStates.Finished Then
            '从完成页面退出时，PageOnContentExit 结束会触发 PageEnter 事件，这会调用 UpdatePanelVisibility
            '如果在现在提前调用了，会丢失完成页面的退出动画
            UpdatePanelVisibility()
        End If
        PageOnContentExit()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '创建
    Private Sub Create_MouseLeftButtonUp() Handles PanSelectCreate.MouseLeftButtonUp
        '输入端口号
        Dim Port As String = MyMsgBoxInput("输入端口", $"在单人游戏的暂停菜单选择 {vbLQ}对局域网开放{vbRQ}，然后输入端口数字。{vbCrLf}甚至可以输入其他游戏的端口……嗯……",
            ValidateRules:=New Collection(Of Validate) From {New ValidateInteger(1024, 65535)},
            HintText:="端口号")
        If Port Is Nothing Then Return
        Dim RoomNameInput As String = AskRoomName()
        If RoomNameInput Is Nothing Then Return
        RoomName = RoomNameInput.Trim()
        MyGameName = GetGameName()
        If MyGameName = "" Then
            Hint("请先在启动页填写「游戏用户名」，才能创建房间！", HintType.Red)
            Return
        End If
        '基础信息
        IsServerSide = True
        DirectMode = True 'P2P 直连：不连任何公共节点，朋友直接连房主地址
        DirectPeers.Clear()
        ShortCode = GenerateShortCode()
        ServerPort = Port
        Dim GenerateRandomCode =
        Function() As String
            Dim Result As String = ""
            For i = 1 To 5
                Result &= RandomInteger(0, 35).ConvertRadix(36)
            Next
            Return Result.Replace("O", "0").Replace("I", "1")
        End Function
        NetworkName = $"P{ServerPort.ConvertRadix(16).PadLeft(4, "0"c)}-{GenerateRandomCode()}"
        NetworkSecret = GenerateRandomCode()
        DiscoverNodeID = -1
        Logger.Info($"尝试创建房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}")
        '启动
        ChangeState(LinkStates.Loading)
    End Sub

    '加入
    Private Shared LastCode As String = Nothing
    ''' <summary>
    ''' 输入邀请码，切换到联机页并立即加入房间。
    ''' </summary>
    Public Shared Sub Join() Handles PanSelectJoin.MouseLeftButtonUp
        Dim Code As String = MyMsgBoxInput("输入连接码", $"输入房主发给你的连接码。{vbCrLf}如果你们在同一个 WiFi，直接输入房主的 6 位短码也行。",
            HintText:=If(String.IsNullOrEmpty(LastCode), "", "使用上一次的连接码"))
        If Not String.IsNullOrEmpty(LastCode) AndAlso Code IsNot Nothing AndAlso Code = "" Then Code = LastCode
        If Code Is Nothing Then Return
        Code = Code.Trim()
        If IsShortCode(Code) Then
            '短码：在局域网广播里找房主的完整连接码，解析放到后台线程，避免卡住 UI
            Hint("正在同一网络里查找房间……", HintType.Blue)
            Dim Target As String = Code.ToUpper()
            RunInNewThread(
            Sub()
                Dim Full As String = ResolveShortCode(Target)
                RunInUi(
                Sub()
                    If Full Is Nothing Then
                        Hint("没找到这个短码的房间。请确认你们在同一个 WiFi，或让房主把完整连接码发给你。", HintType.Red)
                    Else
                        Logger.Info($"短码 {Target} 已在局域网中找到房间")
                        FrmLinkMain.JoinInternal(Full)
                    End If
                End Sub)
            End Sub, "Link ShortCode")
            Return
        End If
        Join(Code)
    End Sub
    Public Sub JoinInternal(Code As String)
        If Code Is Nothing Then Return
        MyGameName = GetGameName()
        If MyGameName = "" Then
            Hint("请先在启动页填写「游戏用户名」，才能加入房间！", HintType.Red)
            Return
        End If
        '基础格式校验
        Dim ValidateResult = ValidateCodeFormat(Code)
        If ValidateResult IsNot Nothing Then
            Hint(ValidateResult, HintType.Red)
            Return
        End If
        Code = FixCodeFormat(Code)
        Logger.Info($"实际使用的邀请码：{Code}")
        '新格式邀请码尾部可能带着房主的直连地址：<主码>~<监听端口>~<地址>
        DirectPeers.Clear()
        LanPeerHint = ""
        MemberPort = 0
        LocalMemberPort = 0
        Dim Tilde As Integer = Code.IndexOf("~"c)
        If Tilde > 0 Then
            Dim Parts As String() = Code.Substring(Tilde + 1).Split(CChar("~"))
            If Parts.Length >= 2 Then
                Dim Port As Integer
                If Integer.TryParse(Parts(0).Trim(), Port) AndAlso Port > 0 AndAlso Port <= 65535 Then
                    For Each Host In Parts(1).Split(CChar(";"))
                        Dim Clean As String = Host.Trim()
                        If Clean <> "" Then DirectPeers.Add(ParseDirectPeer(Clean, Port))
                    Next
                End If
            End If
            If Parts.Length >= 3 Then
                Dim Mp As Integer
                If Integer.TryParse(Parts(2).Trim(), Mp) AndAlso Mp > 0 AndAlso Mp <= 65535 Then MemberPort = Mp
            End If
            Logger.Info($"邀请码里带有直连地址：{DirectPeer}，成员端口：{MemberPort}")
            Code = Code.Substring(0, Tilde) '后面按原来的格式解析
        End If
        '基础信息
        IsServerSide = False
        ServerPort = Code.Substring(1, 4).Upper.ConvertRadix(16, 10)
        NetworkName = Code.Substring(0, 11)
        NetworkSecret = Code.Substring(12, 5)
        If Code.Substring(20, 3) = "000" Then
            DiscoverNodeID = -2
        Else
            DiscoverNodeID = Code.Substring(20, 3).Upper.ConvertRadix(16, 10)
        End If
        Logger.Info($"尝试加入房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}，发现节点 {DiscoverNodeID}")
        '启动
        LastCode = Code
        ChangeState(LinkStates.Loading)
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        If Code Is Nothing Then Return "邀请码为空！"
        Code = FixCodeFormat(Code)
        '判断类型
        If Not (Code.Length >= 14 AndAlso Code(0) = "P"c AndAlso Code(5) = "-"c AndAlso Code(11) = "-"c) Then
            If Code.StartsWithF("U/") Then 'HMCL
                Return "请让房主使用 PCL 创建房间！"
            ElseIf Code.Length = 10 Then 'PCL CE
                Return "请让房主使用非社区版的 PCL 创建房间！"
            Else
                Return "邀请码有误，请让房主使用 PCL 创建房间！"
            End If
        End If
        '校验版本
        If Code.Length >= 23 AndAlso Code(17) = "-"c AndAlso
            Val(Code.Substring(18, 2)) > INVITE_CODE_VERSION Then Return "你的 PCL 版本太老了，请在更新 PCL 之后再联机！"
        Return Nothing
    End Function
    Private Shared Function FixCodeFormat(Code As String) As String
        Code = Code.Between("【", "】").Between("[", "]") '从完整消息中提取
        Code = Code.Upper.Replace("O", "0").Replace("I", "1") '输入修正
        '版本 1 兼容
        If Code.Length >= 17 AndAlso (Code.Length < 23 OrElse (Code.Length >= 18 AndAlso Code(17) <> "-"c)) Then
            Code = Code.Substring(0, 17) & "-0105E"
        End If
        Return Code
    End Function

    '自动加入

    ''' <summary>
    ''' 切换到联机页并立即加入指定房间。
    ''' </summary>
    Public Shared Sub Join(Code As String)
        If LinkState <> LinkStates.Waiting Then
            Hint("你已经在联机房间中了！", HintType.Red)
        ElseIf FrmMain.PageCurrent = FormMain.PageType.Link Then
            FrmLinkMain.JoinInternal(Code)
        Else
            AutoJoinCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub
    '在进入页面时自动尝试加入房间
    Private Shared AutoJoinCode As String = Nothing
    Private Sub PanSelectJoin_Loaded() Handles PanSelectJoin.Loaded
        If AutoJoinCode Is Nothing Then Return
        If LinkState = LinkStates.Waiting Then JoinInternal(AutoJoinCode)
        AutoJoinCode = Nothing
    End Sub

#End Region

#Region "加载页面 (Loading | Failed)"

    '由 Waiting 或 Failed 状态切换到 Loading
    Private Sub SwitchToLoading(OldState As LinkStates)
        '清理变量
        Peers = Nothing
        FailCount = 0
        '加载器与进程状态
        LinkLoader.Start(IsForceRestart:=True)
        '页面切换会由 Loader 调用 MyPageRight 来触发
        'UI 更新
        UpdateProgressBar(0)
        LabLoadTitle.Text = If(IsServerSide, "创建房间中", "加入房间中")
        UpdateLoadingPage("正在初始化……", "准备初始化")
    End Sub

    '由 Loading 状态切换到 Failed
    Private Sub SwitchToFailed(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        ProcessStop()
        'UI 更新
        UpdateProgressBar(1)
        '显示错误信息
        LabLoadTitle.Text = FailReason
        Dim Brief As String = LinkLoader.Error.GetDisplay(False)
        Dim InnerEx As Exception = LinkLoader.Error
        If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Do Until InnerEx.InnerException Is Nothing
            InnerEx = InnerEx.InnerException
            If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Loop
        LabLoadDesc.Text = If(Brief.StartsWithF("$"), Brief.TrimStart("$"), LinkLoader.Error.GetDisplay(True))
        Logger.Warn(LinkLoader.Error, LabLoadTitle.Text)
    End Sub

    '===============================
    '  页面 UI
    '===============================

    Private Sub UpdateLoadingPage(Title As String, FailBrief As String)
        If FailReason = FailBrief & "失败" Then Return
        FailReason = FailBrief & "失败"
        Logger.Info($"开始步骤：{Title}")
        RunInUiWait(
        Sub()
            If FrmLinkMain Is Nothing OrElse Not FrmLinkMain.LabLoadDesc.IsLoaded Then Return
            FrmLinkMain.LabLoadDesc.Text = Title
        End Sub)
    End Sub
    Private FailReason As String = "准备初始化"

    '取消加载
    Private Sub BtnLoadCancel_Click() Handles BtnLoadCancel.Click
        ChangeState(LinkStates.Waiting)
    End Sub

    '点击重试
    Private Sub CardLoad_MouseLeftButtonUp() Handles CardLoad.MouseLeftButtonUp
        ChangeState(LinkStates.Loading)
    End Sub

    '进度条
    Private Sub UpdateProgressBar(Optional Value As Double = -1)
        If Value = -1 Then Value = LinkLoader.Progress
        Dim DisplayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(Value - DisplayingProgress, 3) = 0 Then Return
        If DisplayingProgress <= Value Then
            Dim NewProgress As Double = If(Value = 1, 1, DisplayingProgress +
                (Value - DisplayingProgress) ^ 2 * 0.5)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, NewProgress - ColumnProgressA.Width.Value, 200),
                AaGridLengthWidth(ColumnProgressB, (1 - NewProgress) - ColumnProgressB.Width.Value, 200)
            }, "Link Progress")
        Else
            ColumnProgressA.Width = New GridLength(Value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - Value, GridUnitType.Star)
            AniStop("Link Progress")
        End If
    End Sub
    Private Sub CardLoad_SizeChanged() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "完成页面 (Finished)"

    '由 Loading 状态切换到 Finished
    Private Sub SwitchToFinished(OldState As LinkStates)
        '由于只可能从加载器的完成事件触发，不需要管加载器
        UpdatePanelVisibility() '页面的实际切换会由 Loader 调用 MyPageRight 来触发

        'UI 更新
        If IsServerSide Then
            LabFinishTitle.Text = "已创建房间"
            If DirectMode Then
                Dim Reach As String = LanAddress() & If(PublicIpv4 <> "", "、" & PublicIpv4, "")
                Dim CrossNetHint As String
                If UpnpMapped Then
                    CrossNetHint = "路由器已自动放行端口，跨网络也能直连。"
                ElseIf Ipv6ListenersPort > 0 Then
                    CrossNetHint = "已启用公网 IPv6 直连，跨网络也能连。"
                Else
                    CrossNetHint = "本机没有公网入站能力：跨网络联机需房主有公网 IP 并在路由器放行 " & ListenersPort &
                                   " 端口，否则请用同一个 WiFi 的短码联机。"
                End If
                LabFinishDesc.Text =
                    $"房间名：{RoomName}{vbCrLf}" &
                    $"P2P 直连已就绪，数据不经过任何服务器。{vbCrLf}" &
                    $"同一个 WiFi：让朋友输入 6 位短码 {ShortCode}{vbCrLf}" &
                    $"不同网络：完整连接码已自动复制，点「复制连接码」可重发{vbCrLf}" &
                    $"本机地址：{Reach}（端口 {ListenersPort}）{vbCrLf}{CrossNetHint}"
            Else
                LabFinishDesc.Text = $"把邀请码发给朋友，让大家加入房间吧！{vbCrLf}邀请码：{GetInviteCode()}"
            End If
            BtnFinishExit.Text = "关闭"
            BtnFinishPing.ToolTip = "网络延迟"
            BtnFinishCopy.Visibility = Visibility.Visible
            Copy() '立即复制邀请码
            '下边栏
            BtnFinishPort.Visibility = Visibility.Visible
            LabFinishPort.Visibility = Visibility.Visible
            BtnFinishIp.Visibility = Visibility.Collapsed
            LabFinishIp.Visibility = Visibility.Collapsed
            CardFinishPort.Visibility = Visibility.Visible
            CardFinishIp.Visibility = Visibility.Collapsed
            LabFinishPort.Text = ServerPort
        Else
            LabFinishTitle.Text = "已加入房间"
            If DirectPeer <> "" Then
                LabFinishDesc.Text = BuildGuestDesc()
            Else
                LabFinishDesc.Text = $"在多人游戏页面的最下方就能找到联机房间！{vbCrLf}注意：使用离线登录时不要手动输入 IP！"
            End If
            BtnFinishExit.Text = "离开"
            BtnFinishPing.ToolTip = "与房主的延迟"
            BtnFinishCopy.Visibility = Visibility.Collapsed
            '下边栏
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            LabFinishIp.Text = ClientAddress
            BtnFinishIp.Visibility = Visibility.Visible
            LabFinishIp.Visibility = Visibility.Visible
            CardFinishPort.Visibility = Visibility.Collapsed
            CardFinishIp.Visibility = Visibility.Visible
        End If
        If IsServerSide Then StartLanBroadcast()
        Update() '立即刷新
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '退出
    Private Sub BtnFinishExit_Click() Handles BtnFinishExit.Click
        TryExit(False, False)
    End Sub
    ''' <summary>
    ''' 退出联机。
    ''' 返回是否弹出了警告窗口并且玩家选择了取消。
    ''' </summary>
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If LinkState = LinkStates.Waiting OrElse LinkState = LinkStates.Failed Then Return False
        If Not Silent Then
            If IsServerSide AndAlso PeopleCount > 1 Then
                If MyMsgBox("你确定要关闭联机房间吗？" & vbCrLf & "所有玩家都需要重新输入邀请码才可加入游戏！", "退出联机", "确定", "取消", IsWarn:=True) = 2 Then Return True
            ElseIf Closing Then
                If MyMsgBox(If(IsServerSide, "你确定要关闭联机房间吗？", "你确定要离开联机房间吗？"), "退出联机", "确定", "取消") = 2 Then Return True
            End If
        End If
        ChangeState(LinkStates.Waiting)
        Return False
    End Function

    '复制邀请码
    Private Sub Copy() Handles BtnFinishCopy.Click
        Dim Extra As String = If(IsServerSide AndAlso ShortCode <> "", $"（同一个 WiFi 也可以直接输入短码 {ShortCode}）", "")
        Dim CodeText As String = $"在 PCL 启动器中输入连接码【{GetInviteCode()}】即可加入联机房间！{Extra}"
        Logger.Info($"当前连接码：{GetInviteCode()}")
        ClipboardSet(CodeText, False)
        Settings.Set("LinkLastAutoJoinInviteCode", CodeText)
        Hint("已复制连接码！", HintType.Green)
    End Sub
    Private Function GetInviteCode() As String
        Dim Base As String = $"{NetworkName}-{NetworkSecret}-{INVITE_CODE_VERSION.ToString.PadLeft(2, "0"c)}{ _
            If(DiscoverNodeID = -1, 0, DiscoverNodeID).ConvertRadix(16).PadLeft(3, "0"c)}"
        '直连模式：把监听端口与本机地址一起编进邀请码，朋友粘贴就能连，不用碰设置
        If DirectMode AndAlso IsServerSide AndAlso ListenersPort > 0 Then
            Dim Hosts As List(Of String) = If(DirectHosts.Count > 0, DirectHosts, New List(Of String)(LanAddresses()))
            Return $"{Base}~{ListenersPort}~{String.Join(";", Hosts)}~{MemberPort}"
        End If
        Return Base
    End Function

    '复制 IP
    Private Sub BtnFinishIp_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles BtnFinishIp.MouseLeftButtonUp
        ClipboardSet(ClientAddress, False)
        Hint("已复制服务器地址！", HintType.Green)
    End Sub

#End Region

    '===============================
    '  后端
    '===============================

    ''' <summary>
    ''' EasyTier 所在的文件夹路径，以 \ 结尾。
    ''' </summary>
    Private PathEasyTier As String = Paths.AppDataThenName & "EasyTier\"
    ''' <summary>
    ''' 当前是服务端还是客户端。
    ''' </summary>
    Private IsServerSide As Boolean
    ''' <summary>
    ''' 服务端映射前的端口号。
    ''' </summary>
    Private ServerPort As Integer
    ''' <summary>
    ''' 客户端映射后的端口号。
    ''' </summary>
    Private ClientPort As Integer
    ''' <summary>
    ''' RPC 端口号。
    ''' </summary>
    Private RPCPort As Integer
    ''' <summary>
    ''' 在客户端映射后的地址。
    ''' 除非已作为客户端启动 EasyTier，否则一直为 Nothing。
    ''' </summary>
    Private ClientAddress As String = Nothing
    ''' <summary>
    ''' 发现节点的 ID。
    ''' 若必须设定自定义节点则为 -2；若等待选择则为 -1；选择回退节点则为 0；否则为对应节点的 ID。
    ''' </summary>
    Private DiscoverNodeID As Integer = -1
    ''' <summary>
    ''' 网络信息。
    ''' </summary>
    Private NetworkName As String, NetworkSecret As String
    ''' <summary>
    ''' 直连模式：不依赖任何公共节点，朋友直接用房主的地址连进来。
    ''' 公共节点全部不可用时，房主会自动切换到这个模式。
    ''' </summary>
    Private DirectMode As Boolean
    ''' <summary>
    ''' 加入房间时从邀请码里解析出的房主直连地址（tcp://host:port）。
    ''' </summary>
    Private DirectPeers As New List(Of String)
    ''' <summary>给界面显示的直连地址（多个用逗号连起来）。</summary>
    Private ReadOnly Property DirectPeer As String
        Get
            Return String.Join("，", DirectPeers)
        End Get
    End Property
    ''' <summary>
    ''' EasyTier 实际监听的端口（直连模式下朋友连的就是它）。
    ''' </summary>
    Private ListenersPort As Integer
    ''' <summary>IPv6 独立监听端口（Windows 上 IPv4/IPv6 不能共用同一端口）。</summary>
    Private Ipv6ListenersPort As Integer = 0
    ''' <summary>房主探测出的可达地址列表，元素形如 192.168.1.2 / [2408::1] / 1.2.3.4:12345。</summary>
    Private DirectHosts As New List(Of String)
    ''' <summary>公网 IPv4（从多个国内可达的 echo 服务探测），没有则为空。</summary>
    Private PublicIpv4 As String = ""
    ''' <summary>是否通过 UPnP 成功在路由器上映射了端口。</summary>
    Private UpnpMapped As Boolean = False
    ''' <summary>同一局域网内使用的 6 位短码（MineLink 风格，无服务器）。</summary>
    Private ShortCode As String = ""
    ''' <summary>局域网房间发现用的 UDP 端口。</summary>
    Private Const LanDiscoveryPort As Integer = 24575

#Region "加载"

    Private WithEvents LinkLoader As New LoaderCombo(Of Integer)("联机", {
        New LoaderTask(Of Integer, Integer)("获取配置", AddressOf InitConfig) With {.ProgressWeight = 8},
        New LoaderTask(Of Integer, Integer)("启动联机模块", AddressOf InitLaunch) With {.ProgressWeight = 50}
    })

    '1. 获取服务器配置
    Private Sub InitConfig(Task As LoaderTask(Of Integer, Integer))
        UpdateLoadingPage("正在联网获取配置……", "联网获取配置")
        'Ultra 本地自用版：开源分支的 ServerLoader 不会联网获取配置，
        'ModSecret 中已内置等价的公共 EasyTier 配置，因此这里不再拦截。
        If VersionBranchMain <> "Official" Then Logger.Info("使用内置的本地联机配置")
        ServerLoader.WaitForExit(LoaderToSyncProgress:=Task)
        If ServerConfig Is Nothing Then Throw New Exception("无法从服务器获取配置")
        '检查是否已禁用联机功能
        Dim DisableReason = ServerConfig("Link")?("DisableReason2")?.ToString
        If Not String.IsNullOrEmpty(DisableReason) Then Throw New Exception("$" & DisableReason) 'TODO: 可能不会显示错误原因？
        If CType(ServerConfig("Link"), JObject).ContainsKey("MinVersionCode") AndAlso
           VersionCode < ServerConfig("Link")("MinVersionCode").ToObject(Of Integer) Then
            Throw New Exception("$你的 PCL 版本太老了，请在更新 PCL 之后再联机！")
        End If
    End Sub

    '2. 启动联机模块
    Private Sub InitLaunch(Task As LoaderTask(Of Integer, Integer))
        Telemetry("联机开始")
        Task.Progress = 0.07
        '获取节点列表
        UpdateLoadingPage("正在获取节点列表……", "获取节点列表")
        Dim RawPeers As List(Of String)
        Dim CustomPeers As String = Settings.Get(Of String)("LinkCustomPeer")
        If DirectMode AndAlso IsServerSide Then
            'P2P 直连房主：只监听，不连任何公共节点，也不用联网取节点列表
            RawPeers = New List(Of String)()
            Logger.Info("P2P 直连模式：房主只监听，不依赖公共节点")
        ElseIf DirectPeers.Count > 0 Then
            '邀请码里带了房主的直连地址：直接连，既不用节点列表也不用改设置
            RawPeers = New List(Of String)(DirectPeers)
            Logger.Info($"使用邀请码中的直连地址：{DirectPeer}")
        ElseIf String.IsNullOrWhiteSpace(CustomPeers) Then
            If DiscoverNodeID = -2 AndAlso Not IsServerSide Then
                Panic("未填写自定义节点设置", $"$你必须在 {vbLQ}自定义节点{vbRQ} 设置中填写与房主相同的内容，{vbCrLf}才能进入该房间！")
                Return
            End If
            RawPeers = GetTargetPeers()
        Else
            If DiscoverNodeID <> -2 AndAlso Not IsServerSide Then Hint("房主可能没有使用自定义节点设置，请确认你们的自定义节点设置是否一致！")
            RawPeers = CustomPeers.Split("，,".ToCharArray).Select(Function(p) p.Trim).Where(Function(p) Not String.IsNullOrEmpty(p)).ToList()
            Logger.Info($"使用自定义节点：{CustomPeers}")
        End If
        Task.Progress = 0.13
        '先确认中继节点真的连得上，否则起来也只能干等 30 秒
        If DirectPeers.Count = 0 AndAlso String.IsNullOrWhiteSpace(CustomPeers) AndAlso RawPeers IsNot Nothing AndAlso RawPeers.Count > 0 Then
            Dim Unreachable As String = CheckPeersReachable(RawPeers)
            If Unreachable IsNot Nothing Then
                If IsServerSide Then
                    '房主自己不需要连别人，没人可连也能建房：切到直连模式
                    DirectMode = True
                    RawPeers = New List(Of String)
                    Logger.Info("公共联机节点不可用，房主改用直连模式（不需要任何公共节点）")
                Else
                    Panic("联机节点不可用",
                          "$内置的公共联机节点现在都连不上，也没在邀请码里找到房主的直连地址。" & vbCrLf & vbCrLf &
                          "可以这样做：" & vbCrLf &
                          "1. 请房主把完整的邀请码重新发给你（新版邀请码会自动带上直连地址）；" & vbCrLf &
                          "2. 或者在「设置 → 联机 → 自定义节点」里填写房主的地址，形如 tcp://房主IP:端口。" & vbCrLf & vbCrLf & Unreachable)
                    Return
                End If
            End If
        End If
        '获取空闲端口
        UpdateLoadingPage("正在启动联机模块……", "启动联机模块")
        Dim FreePorts = FindFreePorts(5, ServerPort)
        ClientPort = FreePorts(0)
        RPCPort = FreePorts(1)
        ListenersPort = FreePorts(2) '它会占用【当前、当前 +1、当前 +2】共三个端口
        '有全球 IPv6 时额外开一个 IPv6 监听（Windows 上不能与 IPv4 共用端口）
        Ipv6ListenersPort = 0
        If IsServerSide AndAlso GlobalIpv6Addresses().Count > 0 Then Ipv6ListenersPort = ListenersPort + 3
        PrepareMemberPort()
        LocalMemberPort = If(IsServerSide, 0, PickFreePort())
        '获取启动参数
        Dim Arguments As String = ServerConfig("Link")("Argument")
        Arguments += $" --network-name={NetworkName} --network-secret={NetworkSecret} --listeners {ListenersPort} --rpc-portal {RPCPort} --private-mode true"
        If Ipv6ListenersPort > 0 Then Arguments += $" --listeners tcp://[::]:{Ipv6ListenersPort}"
        Dim HostName = If(IsServerSide, "Server-", "Client-") & Identify.GetStableHashCode().ConvertRadix(36).TrimStart("-"c).EnsureLength("0", 6)
        If IsServerSide Then
            Dim TcpWhite As String = If(MemberPort > 0, $"{ServerPort} {MemberPort}", ServerPort.ToString())
            Arguments += $" -i 10.114.114.114 --hostname={HostName} --tcp-whitelist {TcpWhite} --udp-whitelist={ServerPort}"
        Else
            Arguments += $" -d --hostname={HostName} --tcp-whitelist=0 --udp-whitelist=0"
            '端口转发
            ClientAddress = $"localhost:{ClientPort}"
            Arguments += $" --port-forward tcp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}"
            Arguments += $" --port-forward udp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}"
            Arguments += $" --port-forward tcp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}"
            Arguments += $" --port-forward udp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}"
            If MemberPort > 0 AndAlso LocalMemberPort > 0 Then
                Arguments += $" --port-forward tcp://{IPAddress.Loopback}:{LocalMemberPort}/10.114.114.114:{MemberPort}"
            End If
        End If
        If DirectPeers.Count > 0 Then
            Dim LanAddress As String = If(LanPeerHint <> "", LanPeerHint, ProbeLanHost(NetworkName))
            If LanAddress <> "" AndAlso Not RawPeers.Contains(LanAddress) Then
                RawPeers.Insert(0, LanAddress)
                Logger.Info($"局域网发现的房主真实地址：{LanAddress}")
            End If
        End If
        For Each Peer As String In RawPeers
            Arguments += $" -p=""{Peer}"""
        Next
        If Settings.Get(Of Integer)("LinkLatencyMode") = 1 Then Arguments += " --latency-first"
        '启动进程
        ProcessStart(Arguments)
        '直连房主用的是随机端口，不放行会被系统防火墙拦掉；枚举规则比较慢，丢到后台做
        RunInNewThread(Sub() EnsureFirewallRule(), "Link Firewall")
        Task.Progress = 0.15
        '直连模式的房主只负责监听，不需要连上任何节点
        If DirectMode AndAlso IsServerSide Then
            UpdateLoadingPage("正在探测可达地址……", "探测网络")
            PrepareDirectAddresses()
            UpdateLoadingPage("正在等待朋友加入……", "等待加入")
            For i = 1 To 12
                Thread.Sleep(300)
                CheckCrash()
                If Task.IsCanceled Then Throw New OperationCanceledException
            Next
            Logger.Info($"P2P 直连已就绪：{String.Join("，", DirectHosts)}")
            StartMemberService()
            Return
        End If
        '等待连接
        Dim TimeoutCounter As Integer = 0
        Do
            Thread.Sleep(500) '为避免还没启动好先跑 CLI 了，先等半秒
            '刷新信息
            CheckCrash()
            RefreshPeerLoader.WaitForExit(IsForceRestart:=True)
            '查找目标节点
            Dim Ping = GetPeerPing()
            If Ping > 0 Then
                Logger.Info($"已与目标建立连接，当前 Ping 为 {Ping:0.0}ms")
                Telemetry("联机成功", "Server", IsServerSide, "NAT", NATType)
                Exit Do '退出循环
            End If
            '更新进度
            Dim LastProgress = Task.Progress
            Dim PeerCount As Integer = If(Peers Is Nothing, -1, Peers.Where(Function(p) p.Ping > 0).Count)
            Select Case PeerCount
                Case -1 'CLI 无返回
                    Task.Progress = (Task.Progress + 0.02).Clamp(0.15, 0.25)
                Case 0 'CLI 有返回，但未连接到任何节点
                    UpdateLoadingPage("正在连接到节点……", "连接节点")
                    Task.Progress = (Task.Progress + 0.02).Clamp(If(IsServerSide, 0.5, 0.3), If(IsServerSide, 0.95, 0.5))
                Case Else '已连接到节点，但未连接到房主
                    UpdateLoadingPage("正在连接到房主……", "连接房主")
                    Task.Progress = (Task.Progress + 0.02).Clamp(Math.Min(0.45 + PeerCount * 0.05, 0.65), 0.95)
            End Select
            '超时判定
            If LastProgress <> Task.Progress Then
                TimeoutCounter = 0
            ElseIf TimeoutCounter < 30 * 2 Then
                TimeoutCounter += 1
            Else '进度停滞超过 30s
                Select Case PeerCount
                    Case -1 'CLI 无返回
                        Panic("无法启动联机模块", "近期日志：" & vbCrLf & LogHistory.Join(vbCrLf))
                    Case 0 'CLI 有返回，但未连接到任何节点
                        If DirectPeers.Count > 0 Then
                            Panic("连不上房主", $"$已经尝试连接房主 {DirectPeer}，但一直没连上。{vbCrLf}{vbCrLf}" & DiagnosePeers(DirectPeers))
                        Else
                            Panic("无法连接到节点", $"请检查你的网络环境是否良好。")
                        End If
                    Case Else '已连接到节点，但未连接到房主
                        Panic("无法连接到房主", $"可能的原因：{vbCrLf}- 你或者房主的网络环境不佳{vbCrLf}- 房主已关闭房间{vbCrLf}- 邀请码输错了")
                End Select
            End If
        Loop Until Task.IsCanceled
        If Task.IsCanceled Then Throw New OperationCanceledException
        '等待连接稳定，最多 5s
        If IsServerSide Then Return
        StartMemberClient()
        UpdateLoadingPage("连接优化中……", "优化连接")
        Task.Progress = 0.999
        For i = 1 To 50
            Dim Server = GetTargetPeer()
            If Server IsNot Nothing AndAlso Not Server.Relay AndAlso Server.Ping < 100 Then Return '结束
            If Task.IsCanceled Then Throw New OperationCanceledException
            Thread.Sleep(100)
        Next
    End Sub
    ''' <summary>
    ''' 从在线配置和 API 获取需要连接的节点列表，并更新发现节点 ID。
    ''' 根据发现节点 ID，会有以下行为变化：
    '''   -1：作为房主，根据负载均衡选择一个发现节点。
    '''   0：作为加入者，但邀请码未提供发现节点 ID，使用回退发现节点。
    '''   >0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点。
    ''' </summary>
    Private Function GetTargetPeers() As List(Of String)
        Dim FinalPeers As New List(Of String), FinalDiscoverID As Integer = -1
        Dim FallbackDiscoverID = ServerConfig("Link")("DiscoverPeerId").ToObject(Of Integer)
        Dim FallbackDiscoverAddress As String = ServerConfig("Link")("DiscoverPeer").ToString()
        Try
            '从 API 获取节点列表
            Dim RawNodes As JObject
            Dim IsFallbackRawList As Boolean = False
            Try
                RawNodes = NetRequestByClient("https://uptime.easytier.cn/api/nodes?page=1&per_page=1000", RequireJson:=True, Timeout:=6000).DeserializeJson()
            Catch exx As Exception
                Logger.Warn(exx, "从源站获取节点列表失败，将使用 CDN 缓存")
                RawNodes = NetRequestByClient("https://easytier.meloong.com/?page=1&per_page=1000", RequireJson:=True, Timeout:=6000).DeserializeJson()
                IsFallbackRawList = True
            End Try
            '分析节点列表
            Dim Nodes As New List(Of JObject) '负载会添加在 load 字段上
            Dim BlackList As List(Of String) = ServerConfig("Link")("PeersBlackList").Select(Function(p) p.ToString).ToList()
            For Each Node As JObject In RawNodes("data")("items")
                '状态检查
                Dim ID = Node("id").ToObject(Of Integer)
                If Not Node("is_active").ToObject(Of Boolean) OrElse Not Node("is_approved").ToObject(Of Boolean) Then Continue For
                If ID = DiscoverNodeID Then GoTo ForcedPass '若为指定的发现节点，忽略后续检查
                If ID = FallbackDiscoverID Then Continue For '不主动选取回退发现节点
                If BlackList.Contains(Node("address").ToString) Then Continue For 'ServerConfig 黑名单
                If Node("usage_percentage").ToObject(Of Double) = 0 AndAlso RandomInteger(1, 100) <> 1 Then Continue For '或许节点有问题才导致是 0 负载，让它只有 1% 概率被选中
                '标签检查
                Dim Tags = Node("tags").Select(Function(t) t.ToString).ToList
                If Not Tags.Contains("国内") OrElse Not Tags.Contains("MC中继") Then Continue For
                '计算负载并加入列表
ForcedPass:
                Dim Load As Double = Node("usage_percentage").ToObject(Of Double) '负载
                Load *= 110 - Node("health_percentage_24h").ToObject(Of Double) '可用率
                Node("load") = Load
                Nodes.Add(Node)
            Next
            '排序
            If Not IsFallbackRawList Then
                Nodes = Nodes.OrderBy(Function(n) n("load").ToObject(Of Double)).ToList() '按负载从低到高排序
            Else
                Nodes = Nodes.Shuffle().ToList() '回退到 CDN 缓存时，由于负载数据可能过期，直接随机选择
            End If
            '选取发现节点
            Dim SelectedDiscoverNode As JObject = Nothing
            If DiscoverNodeID = -1 Then '-1：作为房主，根据负载均衡选择一个发现节点
                SelectedDiscoverNode = Nodes.FirstOrDefault(Function(n) Not n("allow_relay").ToObject(Of Boolean))
            ElseIf DiscoverNodeID > 0 Then '>0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点
                SelectedDiscoverNode = Nodes.FirstOrDefault(Function(n) n("id").ToObject(Of Integer) = DiscoverNodeID)
                If SelectedDiscoverNode Is Nothing Then
                    Logger.Warn($"未找到 ID {DiscoverNodeID} 的发现节点")
                    Panic("房间已过期", "请让房主重新创建房间！")
                    Throw New OperationCanceledException
                End If
            End If
            If SelectedDiscoverNode Is Nothing Then '使用回退发现节点
                SelectedDiscoverNode = New JObject From {{"address", FallbackDiscoverAddress}, {"id", FallbackDiscoverID}}
                Logger.Warn("将使用回退发现节点")
            End If
            FinalPeers.Add(SelectedDiscoverNode("address").ToString())
            FinalDiscoverID = SelectedDiscoverNode("id").ToObject(Of Integer)
            Logger.Info($"发现节点：{SelectedDiscoverNode("address")} (ID: {FinalDiscoverID})")
            '选取中继节点
            If ModeDebug OrElse Not IsServerSide Then '房主只连接发现节点，不连接中继节点
                Dim RelayCount As Integer = ServerConfig("Link")("RandomPeer").ToObject(Of Integer)
                Dim RelayNodes = Nodes.Where(Function(n) n("allow_relay").ToObject(Of Boolean)).ToList()
                If RelayNodes.Count < RelayCount Then Throw New Exception($"可用的中继节点数量不足，需要 {RelayCount} 个，实际 {RelayNodes.Count} 个")
                FinalPeers.AddRange(RelayNodes.Take(RelayCount).Select(Function(n) n("address").ToString()))
            End If
        Catch ex As Exception
            ex.ThrowIfCanceled()
            '拿不到节点列表不影响先用内置节点，具体连不上时会给出明确提示，这里不再弹红色错误
            Logger.Warn(ex, "获取节点列表失败，将使用内置联机节点")
            FinalPeers.AddRange(ServerConfig("Link")("Peers").Select(Function(p) p.ToString))
            If FinalDiscoverID <= 0 Then
                FinalPeers.Add(FallbackDiscoverAddress)
                FinalDiscoverID = FallbackDiscoverID
            End If
        End Try
        '版本 1 兼容
        If CType(ServerConfig("Link"), JObject).ContainsKey("EnableForcedPeer") Then
            FinalPeers.Add("tcp://mc1.easytier.cn:55558")
            If FinalDiscoverID <= 0 Then FinalDiscoverID = 94
        End If
        '强制添加的节点
        FinalPeers.AddRange(ServerConfig("Link")("MandatoryPeers").Select(Function(p) p.ToString))
        '结束
        DiscoverNodeID = FinalDiscoverID
        Return FinalPeers.Distinct.ToList
    End Function

#End Region

#Region "进程与日志"

    ''' <summary>
    ''' EasyTier Core 进程。
    ''' </summary>
    Private Shared ProcessCore As Process
    Private Shared ProcessOutputHandle As AutoResetEvent, ProcessErrorHandle As AutoResetEvent

    ''' <summary>
    ''' 若程序正在运行，则结束程序进程。
    ''' 即使失败也不会抛出异常。
    ''' </summary>
    Public Shared Sub ProcessStop()
        Try
            '关闭未捕获的进程
            For Each ProcessObject In Process.GetProcesses
                If ProcessObject.Id = ProcessCore?.Id Then Continue For
                If ProcessObject.ProcessName <> "联机模块" AndAlso ProcessObject.ProcessName <> "联机模块 CLI" Then Continue For
                Try
                    Logger.Info($"停止残留的联机模块，PID：{ProcessObject.Id}，进程名：{ProcessObject.ProcessName}")
                    ProcessObject.Kill()
                    ProcessObject.Close()
                Catch exx As Exception
                    Logger.Warn(exx, $"结束进程失败（{ProcessObject.ProcessName}，PID {ProcessObject.Id}）")
                End Try
            Next
            '关闭由自身启动的进程
            If ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited Then
                Logger.Info($"停止所启动的联机模块，PID：{ProcessCore.Id}")
                ProcessCore.Kill()
                ProcessCore.Close()
                ProcessCore = Nothing
            End If
            '释放资源
            ProcessOutputHandle?.Dispose()
            ProcessErrorHandle?.Dispose()
        Catch ex As Exception
            Logger.Warn(ex, "停止联机模块失败")
        End Try
    End Sub
    ''' <summary>
    ''' 出现意外错误，给出错误信息并结束联机。
    ''' </summary>
    Private Sub Panic(Brief As String, Detail As String)
        '常见原因分析
        If Detail.Contains("failed to listen on ") Then
            Detail = $"监听端口失败。{vbCrLf}请点击重试，如果还是出现此错误，可以重启电脑解决。{vbCrLf}{vbCrLf}{Detail}"
        End If
        '显示信息
        If LinkState = LinkStates.Loading Then
            FailReason = Brief
            LinkLoader.Failed(New Exception(Detail))
        Else
            MyMsgBox(Detail, "联机出错：" & Brief, IsWarn:=True)
            ChangeState(LinkStates.Waiting)
            Telemetry("联机失败", "Exception", FilterUserName(Brief & "：" & Detail, "*"))
        End If
    End Sub

    ''' <summary>
    ''' 运行 EasyTier CLI 并取回输出（用于查询已连接的节点）。
    ''' </summary>
    Private Function RunCli(Arguments As String, TimeoutMs As Integer) As String
        Dim Info As New ProcessStartInfo With {
            .FileName = PathEasyTier & "联机模块 CLI.exe",
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Using Proc As Process = Process.Start(Info)
            If Proc Is Nothing Then Throw New Exception("CLI 调用失败：无法启动进程")
            '先异步读干输出，再等退出，避免管道写满后死锁
            Dim OutTask = Proc.StandardOutput.ReadToEndAsync()
            Dim ErrTask = Proc.StandardError.ReadToEndAsync()
            If Not Proc.WaitForExit(TimeoutMs) Then
                Try
                    Proc.Kill()
                Catch
                End Try
                Throw New Exception($"CLI 调用失败：超过 {TimeoutMs}ms 无响应")
            End If
            Dim Out As String = OutTask.Result
            If Proc.ExitCode <> 0 AndAlso String.IsNullOrWhiteSpace(Out) Then
                Throw New Exception("CLI 调用失败：" & vbCrLf & ErrTask.Result.Trim())
            End If
            Return Out
        End Using
    End Function

    ''' <summary>
    ''' 检查中继节点是否至少有一个能连上；全都不通时返回说明文字，否则返回 Nothing。
    ''' </summary>
    Private Function CheckPeersReachable(Peers As IEnumerable(Of String)) As String
        Dim Details As New List(Of String)
        For Each Raw In Peers.Take(4)
            Dim Peer As String = If(Raw, "").Trim()
            If Peer = "" Then Continue For
            If PeerProbe(Peer) Then Return Nothing
            Details.Add("· " & Peer & " 连不上")
        Next
        If Details.Count = 0 Then Return "没有配置任何联机节点"
        Return "检测结果：" & vbCrLf & String.Join(vbCrLf, Details)
    End Function

    ''' <summary>尝试连接一个 tcp://host:port 节点；UDP 节点只能验证域名能否解析。</summary>
    Private Function PeerProbe(Peer As String) As Boolean
        Try
            Dim Url As String = If(Peer.Contains("://"), Peer, "tcp://" & Peer)
            Dim Split As Integer = Url.IndexOf("://")
            If Split <= 0 Then Return False
            Dim Scheme As String = Url.Substring(0, Split).ToLowerInvariant()
            Dim HostPort As String = Url.Substring(Split + 3).TrimEnd("/"c)
            Dim Host As String = HostPort
            Dim Port As Integer = 11010
            Dim Colon As Integer = HostPort.LastIndexOf(":"c)
            If Colon > 0 Then
                Host = HostPort.Substring(0, Colon)
                If Not Integer.TryParse(HostPort.Substring(Colon + 1), Port) Then Port = 11010
            End If
            Host = Host.Trim("[", "]")
            If Host = "" Then Return False
            Dim Addresses = Net.Dns.GetHostAddresses(Host)
            If Addresses.Length = 0 Then Return False
            If Scheme = "udp" Then Return True 'UDP 探不了连通性，绑定域名能解析就认为可用
            For Each Address In Addresses.Take(2)
                Try
                    Using Client As New Net.Sockets.TcpClient(Address.AddressFamily)
                        Dim Async = Client.BeginConnect(Address, Port, Nothing, Nothing)
                        If Not Async.AsyncWaitHandle.WaitOne(1500) Then Continue For
                        Client.EndConnect(Async)
                        Logger.Info($"联机节点可达：{Peer}")
                        Return True
                    End Using
                Catch
                End Try
            Next
            Return False
        Catch
            Return False
        End Try
    End Function

    ''' <summary>本机所有局域网 IPv4 地址；第一个是默认出口网卡，排在最前。</summary>
    Private Function LanAddresses() As List(Of String)
        Dim Result As New List(Of String)
        Try
            '连一个公网地址只为让系统挑出口网卡，不会真的发包
            Using S As New Net.Sockets.Socket(Net.Sockets.AddressFamily.InterNetwork, Net.Sockets.SocketType.Dgram, Net.Sockets.ProtocolType.Udp)
                S.Connect("223.5.5.5", 80)
                Dim Ip = CType(S.LocalEndPoint, Net.IPEndPoint).Address
                If Not Net.IPAddress.IsLoopback(Ip) Then Result.Add(Ip.ToString())
            End Using
        Catch
        End Try
        Try
            For Each Address In Net.Dns.GetHostAddresses(Net.Dns.GetHostName())
                If Address.AddressFamily <> Net.Sockets.AddressFamily.InterNetwork Then Continue For
                Dim Text As String = Address.ToString()
                If Text.StartsWithF("127.") OrElse Text.StartsWithF("169.254.") Then Continue For
                If Not Result.Contains(Text) Then Result.Add(Text)
            Next
        Catch
        End Try
        If Result.Count = 0 Then Result.Add("127.0.0.1")
        Return Result.Take(3).ToList() '最多带三个，免得邀请码太长
    End Function

    ''' <summary>本机主局域网地址。</summary>
    Private Function LanAddress() As String
        Return LanAddresses().First()
    End Function
    ''' <summary>
    ''' 本机全球单播 IPv6 地址（排除 link-local / 私有 / 回环），可用于跨网直连。
    ''' </summary>
    Private Function GlobalIpv6Addresses() As List(Of String)
        Dim Result As New List(Of String)
        Try
            For Each Nic In Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                If Nic.OperationalStatus <> Net.NetworkInformation.OperationalStatus.Up Then Continue For
                For Each Ua In Nic.GetIPProperties().UnicastAddresses
                    If Ua.Address.AddressFamily <> Net.Sockets.AddressFamily.InterNetworkV6 Then Continue For
                    Dim Addr = Ua.Address
                    If Addr.IsIPv6LinkLocal OrElse Addr.IsIPv6SiteLocal OrElse Addr.IsIPv6Multicast OrElse Net.IPAddress.IsLoopback(Addr) Then Continue For
                    Dim Text As String = Addr.ToString().Split("%"c)(0)
                    If Text.StartsWithF("fc") OrElse Text.StartsWithF("fd") Then Continue For
                    If Not Result.Contains(Text) Then Result.Add(Text)
                Next
            Next
        Catch ex As Exception
            Logger.Warn(ex, "获取 IPv6 地址失败")
        End Try
        Return Result.Take(2).ToList()
    End Function

    ''' <summary>用国内可达的 echo 服务查询本机公网 IPv4，全部失败返回空串。</summary>
    Private Function FetchPublicIpv4() As String
        For Each Url As String In {"https://myip.ipip.net", "https://ip.3322.net", "https://ifconfig.me/ip"}
            Try
                Dim Req = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
                Req.Timeout = 2200
                Req.ReadWriteTimeout = 2200
                Req.UserAgent = "PCL Ultra"
                Using Resp = CType(Req.GetResponse(), Net.HttpWebResponse)
                    Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                        Dim M = System.Text.RegularExpressions.Regex.Match(Reader.ReadToEnd(), "\b(?:\d{1,3}\.){3}\d{1,3}\b")
                        If M.Success Then Return M.Value
                    End Using
                End Using
            Catch
            End Try
        Next
        Return ""
    End Function

    ''' <summary>发起一个短超时的 HTTP GET。</summary>
    Private Function HttpGet(Url As String, TimeoutMs As Integer) As String
        Try
            Dim Req = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Timeout = TimeoutMs
            Req.ReadWriteTimeout = TimeoutMs
            Req.UserAgent = "PCL Ultra"
            Using Resp = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>向 UPnP 设备发一个 SOAP 请求，失败返回空串（不抛异常）。</summary>
    Private Function SoapPost(Url As String, Action As String, Body As String, TimeoutMs As Integer) As String
        Try
            Dim Data As Byte() = Encoding.UTF8.GetBytes(Body)
            Dim Req = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "POST"
            Req.Timeout = TimeoutMs
            Req.ContentType = "text/xml; charset=""utf-8"""
            Req.Headers.Add("SOAPACTION", """" & Action & """")
            Req.ContentLength = Data.Length
            Using St = Req.GetRequestStream()
                St.Write(Data, 0, Data.Length)
            End Using
            Using Resp = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 尝试通过 UPnP (IGD) 在路由器上映射 TCP 端口，成功返回 "公网IP:外部端口"。
    ''' 路由器没开 UPnP / 多层 NAT / 无权限时静默失败，不影响建房。
    ''' </summary>
    Private Function TryUpnpMap(InternalPort As Integer, InternalIp As String) As String
        Try
            '1. SSDP 发现
            Dim Probe As Byte() = Encoding.UTF8.GetBytes(
                "M-SEARCH * HTTP/1.1" & vbCrLf &
                "HOST: 239.255.255.250:1900" & vbCrLf &
                "MAN: ""ssdp:discover""" & vbCrLf & "MX: 1" & vbCrLf &
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1" & vbCrLf & vbCrLf)
            Dim Location As String = ""
            Using U As New Net.Sockets.UdpClient(0)
                U.Client.ReceiveTimeout = 1400
                U.EnableBroadcast = True
                U.Send(Probe, Probe.Length, "239.255.255.250", 1900)
                Try
                    Dim Ep As New Net.IPEndPoint(Net.IPAddress.Any, 0)
                    Dim Reply As String = Encoding.UTF8.GetString(U.Receive(Ep))
                    Dim M = System.Text.RegularExpressions.Regex.Match(Reply, "LOCATION:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    If M.Success Then Location = M.Groups(1).Value
                Catch
                End Try
            End Using
            If Location = "" Then Return ""
            '2. 设备描述里找 WANIPConnection 的 controlURL
            Dim Xml As String = HttpGet(Location, 2200)
            If Xml = "" Then Return ""
            Dim Svc = System.Text.RegularExpressions.Regex.Match(Xml,
                "(?is)<service>(?:(?!</service>).)*WANIPConnection:1(?:(?!</service>).)*</service>")
            If Not Svc.Success Then Return ""
            Dim Control As String = System.Text.RegularExpressions.Regex.Match(Svc.Value,
                "(?is)<controlURL>\s*([^<]+?)\s*</controlURL>").Groups(1).Value
            If Control = "" Then Return ""
            If Not Control.StartsWithF("http") Then
                Dim Slash As Integer = Location.IndexOf("/"c, Location.IndexOf("//") + 2)
                Dim Base As String = If(Slash > 0, Location.Substring(0, Slash), Location)
                Control = Base & If(Control.StartsWithF("/"), "", "/") & Control
            End If
            '3. 注册端口映射
            Dim SvcType As String = "urn:schemas-upnp-org:service:WANIPConnection:1"
            Dim Body As String =
                "<?xml version=""1.0""?>" &
                "<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/""><s:Body>" &
                "<u:AddPortMapping xmlns:u=""" & SvcType & """>" &
                "<NewRemoteHost></NewRemoteHost><NewExternalPort>" & InternalPort & "</NewExternalPort>" &
                "<NewProtocol>TCP</NewProtocol><NewInternalPort>" & InternalPort & "</NewInternalPort>" &
                "<NewInternalClient>" & InternalIp & "</NewInternalClient><NewEnabled>1</NewEnabled>" &
                "<NewPortMappingDescription>PCL Ultra P2P</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration>" &
                "</u:AddPortMapping></s:Body></s:Envelope>"
            Dim AddResult As String = SoapPost(Control, SvcType & "#AddPortMapping", Body, 2200)
            If AddResult = "" OrElse AddResult.ContainsIgnoreCase("UPnPError") Then Return ""
            '4. 读取公网 IP
            Dim IpBody As String =
                "<?xml version=""1.0""?>" &
                "<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/""><s:Body>" &
                "<u:GetExternalIPAddress xmlns:u=""" & SvcType & """ />" &
                "</s:Body></s:Envelope>"
            Dim IpResult As String = SoapPost(Control, SvcType & "#GetExternalIPAddress", IpBody, 2200)
            Dim IpMatch = System.Text.RegularExpressions.Regex.Match(IpResult,
                "(?is)<NewExternalIPAddress>\s*([^<]+?)\s*</NewExternalIPAddress>")
            If Not IpMatch.Success Then Return ""
            Return IpMatch.Groups(1).Value & ":" & InternalPort
        Catch ex As Exception
            Logger.Warn(ex, "UPnP 端口映射失败（不影响联机）")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 房主在建房时探测所有可能被朋友连上的地址，按「内网 → IPv6 → 公网」排序。
    ''' 全部失败也没关系，至少还有内网地址可用。
    ''' </summary>
    Private Sub PrepareDirectAddresses()
        DirectHosts.Clear()
        UpnpMapped = False
        PublicIpv4 = ""
        '1. 内网 IPv4（同一 WiFi 下最快）
        For Each Ip As String In LanAddresses()
            DirectHosts.Add(Ip)
        Next
        '2. 全球单播 IPv6（有公网 IPv6 时可以直接跨网，不需要端口映射）
        Dim V6 As List(Of String) = GlobalIpv6Addresses()
        If V6.Count > 0 Then
            For Each Addr As String In V6
                DirectHosts.Add($"[{Addr}]" & If(Ipv6ListenersPort > 0, $":{Ipv6ListenersPort}", ""))
            Next
            Logger.Info($"已启用 IPv6 直连监听：{Ipv6ListenersPort}")
        End If
        '3. 公网 IPv4 + UPnP 自动映射
        Dim Pub As String = FetchPublicIpv4()
        If Pub <> "" Then
            PublicIpv4 = Pub
            Dim Mapped As String = TryUpnpMap(ListenersPort, LanAddress())
            If Mapped <> "" Then
                DirectHosts.Add(Mapped)
                UpnpMapped = True
                Logger.Info($"UPnP 已映射公网地址：{Mapped}")
            Else
                '没有 UPnP 也写进候选：若房主本来就有公网 IP / 已手动映射，照样能连
                DirectHosts.Add($"{Pub}:{ListenersPort}")
                Logger.Info($"公网 IPv4：{Pub}（未发现 UPnP，需路由器已放行 {ListenersPort} 端口）")
            End If
        End If
        Logger.Info($"P2P 候选地址：{String.Join("，", DirectHosts)}")
    End Sub

    ''' <summary>
    ''' 把邀请码里的地址片段还原成 EasyTier 的 tcp:// 地址。
    ''' 支持 192.168.1.2、[2408::1]、1.2.3.4:12345、[2408::1]:12345 四种写法。
    ''' </summary>
    Private Function ParseDirectPeer(Raw As String, DefaultPort As Integer) As String
        Dim Clean As String = Raw.Trim()
        Try
            If Clean.StartsWithF("[") Then
                Dim Close As Integer = Clean.IndexOf("]"c)
                If Close > 0 Then
                    Dim Host As String = Clean.Substring(0, Close + 1)
                    Dim Rest As String = Clean.Substring(Close + 1).TrimStart(":"c)
                    Return $"tcp://{Host}:{If(Rest.Length > 0, Rest, DefaultPort.ToString())}"
                End If
            Else
                Dim Colon As Integer = Clean.LastIndexOf(":"c)
                If Colon > 0 AndAlso Integer.TryParse(Clean.Substring(Colon + 1), Nothing) Then Return $"tcp://{Clean}"
                Return $"tcp://{Clean}:{DefaultPort}"
            End If
        Catch
        End Try
        Return $"tcp://{Clean}:{DefaultPort}"
    End Function

    ''' <summary>生成 6 位局域网短码（去掉容易看错的 0/O/1/I）。</summary>
    Private Function GenerateShortCode() As String
        Dim Chars As String = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
        Dim Result As String = ""
        For i = 1 To 6
            Result &= Chars(RandomInteger(0, Chars.Length - 1))
        Next
        Return Result
    End Function

    ''' <summary>判断输入是否是 6 位短码。</summary>
    Private Shared Function IsShortCode(Code As String) As Boolean
        If Code Is Nothing OrElse Code.Length <> 6 Then Return False
        Return Code.ToUpper.All(Function(c) "23456789ABCDEFGHJKLMNPQRSTUVWXYZ".Contains(c))
    End Function

    ''' <summary>
    ''' 房主在局域网里周期性广播自己的完整连接码；加入者只要输入 6 位短码就能找到房间。
    ''' 全程没有服务器，数据也不经过这里（这里只发一条几十字节的 UDP 文本）。
    ''' </summary>
    Private Sub StartLanBroadcast()
        If Not IsServerSide OrElse ShortCode = "" Then Return
        RunInNewThread(
        Sub()
            Try
                Using U As New Net.Sockets.UdpClient()
                    U.EnableBroadcast = True
                    Do While LinkState = LinkStates.Finished AndAlso IsServerSide AndAlso ShortCode <> ""
                        Try
                            Dim Payload As String = "PCLP2P1|" & ShortCode & "|" & GetInviteCode()
                            Dim Data As Byte() = Encoding.UTF8.GetBytes(Payload)
                            '两个目标分开捕获：广播失败不能连带跳过回环（同机双开要靠它）
                            Try
                                U.Send(Data, Data.Length, "255.255.255.255", LanDiscoveryPort)
                            Catch
                            End Try
                            Try
                                U.Send(Data, Data.Length, "127.0.0.1", LanDiscoveryPort)
                            Catch
                            End Try
                        Catch
                        End Try
                        Thread.Sleep(1200)
                    Loop
                End Using
            Catch ex As Exception
                Logger.Warn(ex, "局域网房间广播失败")
            End Try
        End Sub, "Link LanBroadcast")
    End Sub

    ''' <summary>加入者监听局域网广播，按短码找到房主的完整连接码。</summary>
    Private Shared Function ResolveShortCode(Code As String) As String
        Try
            Using U As New Net.Sockets.UdpClient()
                U.Client.SetSocketOption(Net.Sockets.SocketOptionLevel.Socket, Net.Sockets.SocketOptionName.ReuseAddress, True)
                U.Client.Bind(New Net.IPEndPoint(Net.IPAddress.Any, LanDiscoveryPort))
                U.Client.ReceiveTimeout = 1200
                Dim Deadline = Date.Now.AddMilliseconds(6500)
                Do While Date.Now < Deadline
                    Try
                        Dim Ep As New Net.IPEndPoint(Net.IPAddress.Any, 0)
                        Dim Text As String = Encoding.UTF8.GetString(U.Receive(Ep))
                        Dim Parts As String() = Text.Split("|"c)
                        If Parts.Length >= 3 AndAlso Parts(0) = "PCLP2P1" AndAlso Parts(1) = Code.ToUpper() Then
                            Dim Full As String = String.Join("|", Parts.Skip(2))
                            Dim Seg As String() = Full.Split("~"c)
                            Dim P As Integer
                            If Seg.Length >= 2 AndAlso Integer.TryParse(Seg(1), P) AndAlso P > 0 Then
                                LanPeerHint = $"tcp://{Ep.Address}:{P}"
                            End If
                            Return Full
                        End If
                    Catch
                    End Try
                Loop
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "查找局域网房间失败")
        End Try
        Return Nothing
    End Function


    ''' <summary>为联机模块添加 Windows 防火墙规则时使用的名字。</summary>
    Private Const FirewallRuleName As String = "PCL Ultra 联机模块"

    ''' <summary>
    ''' 给联机模块加一条入站放行规则（按程序而不是按端口，避免每次建房都弹窗）。
    ''' 需要管理员权限；没有权限就只记日志，不影响创建房间。
    ''' </summary>
    Private Sub EnsureFirewallRule()
        Try
            Dim ExePath As String = PathEasyTier & "联机模块.exe"
            If Not FileUtils.Exists(ExePath) Then Return
            Dim Policy As Object = Interaction.CreateObject("HNetCfg.FwPolicy2")
            '已经加过就不再重复
            Dim Count As Integer = CInt(Policy.Rules.Count)
            For i As Integer = 1 To Count
                Try
                    If CStr(Policy.Rules.Item(i).Name).StartsWithF(FirewallRuleName) Then Return
                Catch
                End Try
            Next
            For Each Protocol As Integer In {6, 17} '6 = TCP，17 = UDP
                Dim Rule As Object = Interaction.CreateObject("HNetCfg.FWRule")
                Rule.Name = FirewallRuleName & If(Protocol = 6, " TCP", " UDP")
                Rule.Description = "允许 PCL Ultra 的联机模块接收朋友的连接"
                Rule.ApplicationName = ExePath
                Rule.Protocol = Protocol
                Rule.Direction = 1 '入站
                Rule.Action = 1 '允许
                Rule.Enabled = True
                Rule.Profiles = 7 '域、专用、公用
                Policy.Rules.Add(Rule)
            Next
            Logger.Info("已添加联机模块的防火墙入站放行规则")
        Catch ex As Exception
            Logger.Warn(ex, "添加防火墙规则失败（可能不是管理员），联机可能被系统防火墙拦截")
        End Try
    End Sub

    ''' <summary>
    ''' 启动进程。
    ''' </summary>
    Private Sub ProcessStart(Arguments As String)
        Dim Info = New ProcessStartInfo With {
            .FileName = PathEasyTier & "联机模块.exe",
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardError = True,
            .RedirectStandardOutput = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Logger.Info($"正在启动 EasyTier：{Arguments}")
        ProcessCore = New Process With {.StartInfo = Info}
        Dim LogLineHandler =
        Sub(sender As Object, e As DataReceivedEventArgs, Handle As AutoResetEvent)
            Try
                If e.Data Is Nothing Then
                    Handle.[Set]()
                    Return
                End If
                ProcessLogLine(e.Data)
            Catch unused As ObjectDisposedException
            Catch ex As Exception
                Logger.Warn(ex, "读取 EasyTier 信息失败")
            End Try
        End Sub
        ProcessOutputHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.OutputDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessOutputHandle)
        ProcessErrorHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.ErrorDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessErrorHandle)
        ProcessCore.Start()
        ProcessCore.BeginOutputReadLine()
        ProcessCore.BeginErrorReadLine()
    End Sub
    Private Sub ProcessLogLine(Line As String)
        '记录日志
        Logger.Info(Line)
        LogHistory.Enqueue(Line)
        If LogHistory.Count >= 10 Then LogHistory.Dequeue()
        '检查日志内容
        If Line.ContainsIgnoreCase("new peer connection added") Then
            Logger.Warn($"已建立连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        ElseIf Line.ContainsIgnoreCase("peer connection removed") Then
            Logger.Warn($"已断开连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        End If
    End Sub
    Private LogHistory As New Queue(Of String)(10)

#End Region

#Region "CLI 节点信息获取"

    Private Class Peer
        ''' <summary>
        ''' 该节点的类型。
        ''' </summary>
        Public ReadOnly Type As Types
        Public Enum Types
            ''' <summary>
            ''' 客户端。
            ''' </summary>
            Client
            ''' <summary>
            ''' 服务器。
            ''' </summary>
            Server
            ''' <summary>
            ''' 公共节点。
            ''' </summary>
            Misc
            ''' <summary>
            ''' 自己，或者自己的重复（它可能返回多个自己）。
            ''' </summary>
            Self
        End Enum
        ''' <summary>
        ''' 与该节点的延迟 (ms)。
        ''' 若是自己则为 0。
        ''' </summary>
        Public ReadOnly Ping As Double
        ''' <summary>
        ''' 名称。
        ''' </summary>
        Public ReadOnly Name As String
        ''' <summary>
        ''' 是否通过中继连接。
        ''' </summary>
        Public ReadOnly Relay As Boolean
        ''' <summary>
        ''' NAT 类型。
        ''' </summary>
        Public ReadOnly NATType As NATTypes

        ''' <summary>
        ''' 从 CLI 给出的信息分析对应的数据。
        ''' </summary>
        Public Sub New(Info As JObject)
            '类别
            Dim PeerName = Info("hostname").ToString
            If Info("cost").ToString = "Local" Then
                Type = Types.Self
            ElseIf PeerName.StartsWithF("Client") Then
                Type = Types.Client
            ElseIf Info("ipv4") = "10.114.114.114" Then '只允许这个 IP 作为房主，避免网络被多个房主搞炸的情况
                Type = Types.Server
            Else
                Type = Types.Misc
            End If
            '基础信息
            Double.TryParse(Info("lat_ms"), NumberStyles.Any, CultureInfo.InvariantCulture, Ping)
            Name = PeerName
            Relay = Info("cost").ToString.ContainsIgnoreCase("relay")
            NATType = EnumUtils.FromString(Of NATTypes)(Info("nat_type"))
        End Sub
        Public Overrides Function ToString() As String
            Return $"{Type} - {Name} - Ping {Ping:0.0}ms [中继? {Relay}] - NAT {NATType}"
        End Function
    End Class
    Private Enum NATTypes
        'https://github.com/EasyTier/EasyTier/blob/6bb2fd9a15ab2499bdeabdcc3a925e9bd9aebf50/easytier/src/proto/common.proto#L129
        Unknown = 0
        OpenInternet = 1
        NoPAT = 2
        FullCone = 3
        Restricted = 4
        PortRestricted = 5
        Symmetric = 6
        SymUdpFirewall = 7
        SymmetricEasyInc = 8
        SymmetricEasyDec = 9
        ''' <summary>
        ''' 尚未获取。
        ''' </summary>
        Pending = 10
    End Enum

    ''' <summary>
    ''' 当前的节点列表，使用 RefreshPeerLoader 来刷新。
    ''' 若尚未成功获取过则为 Nothing，但保证在加载完成后至少是一个列表。
    ''' </summary>
    Private Peers As List(Of Peer) = Nothing
    ''' <summary>
    ''' 自己的 NAT 类型。
    ''' </summary>
    Private NATType As NATTypes = NATTypes.Pending

    ''' <summary>
    ''' 调用 EasyTier CLI 获取已连接节点信息。
    ''' 这会过滤掉尚未完成连接的其他节点以及自己。
    ''' 若在加载完成后连续获取失败 3 次，则会强制断开连接。
    ''' </summary>
    Private RefreshPeerLoader As New LoaderTask(Of Integer, List(Of Peer))("EasyTier CLI", AddressOf RefreshPeer)
    Private Sub RefreshPeer()
        Try
            '调用 CLI 获取节点列表：EasyTier 自己不会给出已连接节点，只能问 CLI
            Dim CliResult As String = RunCli($"-o json -p 127.0.0.1:{RPCPort} peer", 4000)
            '解析
            If Not CliResult.Contains("lat_ms") Then Throw New Exception("CLI 调用失败：" & vbCrLf & CliResult)
            If GetUuid() Mod If(ModeDebug, 23, 103) = 0 Then Logger.Info($"CLI 输出抽样：{vbCrLf}{CliResult}")
            Dim NewPeers As New List(Of Peer)
            For Each Line As JObject In CType(CliResult.DeserializeJson(), JArray)
                Try
                    Dim Peer = New Peer(Line)
                    If Peer.Type = Peer.Types.Self Then
                        NATType = Peer.NATType '记录自己的 NAT
                    Else
                        NewPeers.Add(Peer)
                    End If
                Catch exx As Exception
                    Logger.Warn(exx, $"错误的信息（{Line}）")
                End Try
            Next
            '完成
            Peers = NewPeers
            FailCount = 0
        Catch ex As Exception
            Logger.Warn(ex, "获取节点信息失败")
            If LinkState = LinkStates.Finished Then
                FailCount += 1
                If FailCount >= 4 Then Panic("获取节点信息失败", ex.Message)
            End If
        End Try
    End Sub
    ''' <summary>
    ''' CLI 调用连续失败的次数。
    ''' </summary>
    Private FailCount As Integer = 0

    ''' <summary>
    ''' 根据当前的节点列表获取 Ping 值。
    ''' 服务端会返回所有节点中最低的 Ping，客户端会返回服务端的 Ping。
    ''' 0 代表尚不可用。
    ''' </summary>
    Private Function GetPeerPing() As Double
        Dim Peer = GetTargetPeer()
        If Peer Is Nothing Then Return 0
        Return Peer.Ping
    End Function
    ''' <summary>
    ''' 服务端会返回所有节点中 Ping 大于 0 最低的那一个，客户端会返回服务端。
    ''' 若没有则为 Nothing。
    ''' </summary>
    Private Function GetTargetPeer() As Peer
        If Peers Is Nothing Then Return Nothing
        Dim Targets As IEnumerable(Of Peer)
        If IsServerSide Then
            Targets = Peers.Where(Function(p) p.Ping > 0)
        Else
            Targets = Peers.Where(Function(p) p.Type = Peer.Types.Server AndAlso p.Ping > 0)
        End If
        If Not Targets.Any Then Return Nothing
        '返回 Ping 且最低的那个
        Dim MinPing = Targets.Min(Function(p) p.Ping)
        Return Targets.First(Function(p) p.Ping = MinPing)
    End Function

    ''' <summary>
    ''' 获取当前房间中的人数，包括自己。
    ''' 0 代表不可用。
    ''' </summary>
    Private ReadOnly Property PeopleCount As Integer
        Get
            If Peers Is Nothing Then Return 0
            Return Peers.Where(Function(p) p.Type <> Peer.Types.Misc).Count() + 1 '加上自己
        End Get
    End Property

#End Region

#Region "定时任务"

    '启动
    Private Sub StartTimerThread() Handles Me.Loaded
        Static IsTimerStarted As Boolean = False
        If IsTimerStarted Then Return
        RunInNewThread(
        Sub()
            Dim Counter As Integer = 0
            Do While True
                Try
                    Thread.Sleep(200)
                    Counter += 200
                    '每秒执行一次
                    If Counter >= 1000 Then
                        Counter = 0
                        Update()
                    End If
                    '每 200ms 更新进度条
                    If LinkState = LinkStates.Loading Then RunInUi(AddressOf UpdateProgressBar)
                Catch ex As Exception
                    Logger.Error(ex, "联机模块主时钟出错")
                    Thread.Sleep(10000)
                End Try
            Loop
        End Sub, "Link Timer")
    End Sub

    '每秒或进入页面时触发
    Private Sub Update() Handles Me.Loaded
        If LinkState <> LinkStates.Finished Then Return
        '重新获取信息
        SyncLock RefreshPeerLoader.LockState
            If RefreshPeerLoader.State <> LoadState.Loading Then RefreshPeerLoader.Start(IsForceRestart:=True)
        End SyncLock
        '更新 UI
        If FrmMain.PageCurrent = FormMain.PageType.Link Then
            RunInUi(
            Sub()
                'Logo 旋转动画
                AniStart(AaRotateTransform(ImgFinishLogo, 500, 5000), "Link Logo Rotation")
                'Ping
                Dim Ping As Double = GetPeerPing()
                Dim Connecting As Boolean = Ping Mod 500 = 0 OrElse $"{Ping:0.0}" = "1.0" OrElse FailCount > 0
                If DirectMode AndAlso IsServerSide AndAlso (Peers Is Nothing OrElse Peers.Count = 0) Then
                    LabFinishPing.Text = "等待加入" '直连房主还没人等，别显示成"连接优化中"
                ElseIf Connecting Then
                    LabFinishPing.Text = "连接优化中"
                Else
                    LabFinishPing.Text = If(Ping >= 10, $"{Ping:0} ms", $"{Ping:0.0} ms")
                End If
                '人数显示
                LabFinishPlayer.Text = If(Members.Count > 0, Members.Count, PeopleCount) & "  人"
                '------------------------
                '  提示条
                '------------------------
                HintFinish.Visibility = Visibility.Collapsed
                If IsServerSide Then Return '服务端没有需要显示的提示
                If Connecting Then Return '连接稳定过程中不显示提示
                '中继提示
                Dim Server As Peer = GetTargetPeer()
                If Server Is Nothing Then Return
                If Server.Relay Then
                    If NATType >= NATTypes.Symmetric AndAlso Server.NATType >= NATTypes.Symmetric Then
                        HintFinish.Text = "你和房主的网络环境都不太好，"
                    ElseIf NATType >= NATTypes.Symmetric Then
                        HintFinish.Text = "你的网络环境不太好，"
                    ElseIf Server.NATType >= NATTypes.Symmetric Then
                        HintFinish.Text = "房主的网络环境不太好，"
                    Else
                        HintFinish.Text = "你或者房主的网络环境不太好，"
                    End If
                    If String.IsNullOrWhiteSpace(Settings.Get(Of String)("LinkCustomPeer")) Then
                        HintFinish.Text &= "正使用社区节点进行中继。"
                    Else
                        HintFinish.Text &= "正通过自定义节点进行中继。"
                    End If
                    HintFinish.Visibility = Visibility.Visible
                    HintFinish.Theme = MyHint.Themes.Yellow
                    Return
                End If
                '环境比房主更好的提示
                If NATType >= NATTypes.PortRestricted Then Return '自身的 NAT 为 2 或更好
                If Server.NATType < NATTypes.PortRestricted Then Return '房主的 NAT 为 3 或更差
                Dim OtherPlayers = Peers.Where(Function(p) p.Type = Peer.Types.Client).ToList()
                If Not OtherPlayers.Any() Then Return '房间里还有其他玩家
                If OtherPlayers.Any(Function(p) p.Relay) Then Return '自己可以与所有玩家打洞
                If OtherPlayers.All(Function(p) p.NATType < NATTypes.PortRestricted) Then Return '任意其他玩家的 NAT 为 3 或更差
                HintFinish.Visibility = Visibility.Visible
                HintFinish.Theme = MyHint.Themes.Blue
                HintFinish.Text = "你的网络环境比房主更好！如果你来当房主，其他玩家或许能更加流畅！"
            End Sub)
        End If
        '检查核心状态
        CheckCrash()
        '检查节点状态
        If Peers Is Nothing Then Return '第一次 CLI 结果还没回来
        If Not Peers.Any Then
            '直连模式的房主本来就没有可连的节点，安静等朋友连进来即可
            If DirectMode AndAlso IsServerSide Then Return
            Panic("网络连接已断开", "请检查你的网络环境是否正常。")
            Return
        End If
        If Not IsServerSide AndAlso Not Peers.Any(Function(p) p.Type = Peer.Types.Server) Then
            MyMsgBox("房主已离开房间！", "联机结束")
            ChangeState(LinkStates.Waiting)
            Return
        End If
        '广播联机房间端口
        If Not IsServerSide Then
            Try
                Static BroadcastSocket As New Socket(SocketType.Dgram, ProtocolType.Udp)
                BroadcastSocket.SendTo(
                    Encoding.UTF8.GetBytes($"[MOTD]PCL 联机房间[/MOTD][AD]{ClientPort}[/AD]"),
                    SocketFlags.None,
                    New IPEndPoint(IPAddress.Loopback, 4445))
            Catch ex As Exception
                Logger.Warn(ex, "广播联机房间端口失败")
            End Try
        End If
    End Sub
    ''' <summary>
    ''' 检查联机模块是否崩溃。
    ''' </summary>
    Private Sub CheckCrash()
        If ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited Then Return
        Panic("联机模块已崩溃", "近期日志：" & vbCrLf & LogHistory.Join(vbCrLf))
    End Sub

#End Region

    ''' <summary>房间里的一个成员。</summary>
    Private Class RoomMember
        Public Uuid As String
        Public Name As String
        Public DisplayName As String
        Public IsHost As Boolean
        Public Client As Net.Sockets.TcpClient
        Public Writer As IO.StreamWriter
    End Class

    ''' <summary>自己在本局的游戏名（建房/加入时都必须填）。</summary>
    Private MyGameName As String = ""
    Private MyUuid As String = Guid.NewGuid().ToString("N").Substring(0, 8)
    ''' <summary>房主成员服务的端口（写进连接码）；加入者本地映射端口。</summary>
    Private MemberPort As Integer = 0
    Private LocalMemberPort As Integer = 0
    Private Members As New List(Of RoomMember)
    Private MemberListener As Net.Sockets.TcpListener = Nothing
    Private MemberStop As Boolean = False
    Private WasKicked As Boolean = False
    Private Const MemberSep As Char = ChrW(2)

    ''' <summary>房间名（房主创建时填写，会同步给所有成员）。</summary>
    Private RoomName As String = ""

    ''' <summary>直接读取启动页的「游戏用户名」，不再另外弹窗。</summary>
    Private Function GetGameName() As String
        Try
            '启动页输入框里的内容只在「启动游戏」时才写入设置，所以必须优先读控件本身
            Dim Raw As String = ""
            Dim FromBox As Boolean = False
            If FrmLoginLegacy IsNot Nothing AndAlso FrmLoginLegacy.ComboName IsNot Nothing Then
                Raw = FrmLoginLegacy.ComboName.Text
                FromBox = Not String.IsNullOrWhiteSpace(Raw)
            End If
            If Not FromBox Then Raw = McLoginName()
            If String.IsNullOrWhiteSpace(Raw) Then Return ""
            Dim N As String = Raw.Trim()
            For Each C As Char In "|~;," & vbCr & vbLf
                N = N.Replace(C, "")
            Next
            If N.Length > 16 Then N = N.Substring(0, 16)
            N = N.Trim()
            If N <> "" AndAlso FromBox Then
                Try
                    Settings.Set("LoginLegacyName", N) '顺便持久化，下次启动能直接显示
                Catch
                End Try
            End If
            Return N
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>创建房间时输入房间名；取消返回 Nothing。</summary>
    Private Function AskRoomName() As String
        Dim Rules As New Collection(Of Validate) From {
            New ValidateNullOrWhiteSpace(),
            New ValidateLength(1, 20),
            New ValidateFunc(Function(t) If(t.IndexOfAny("|~;," & vbCr & vbLf) >= 0,
                                            "房间名不能包含 | ~ ; , 或换行！", ""))
        }
        Return MyMsgBoxInput("输入房间名", "给这个房间起个名字，朋友加入后能看到。",
            DefaultInput:=If(RoomName = "", "", RoomName),
            ValidateRules:=Rules, HintText:="1~20 个字符")
    End Function

    '=====================================================================
    '  房主：成员服务
    '=====================================================================

    ''' <summary>先占用一个本地端口给成员服务（必须在拼 EasyTier 参数之前调用）。</summary>
    Private Sub PrepareMemberPort()
        '客户端的 MemberPort 来自邀请码，不能清零
        If Not (DirectMode AndAlso IsServerSide) Then Return
        MemberPort = 0
        Try
            MemberListener = New Net.Sockets.TcpListener(Net.IPAddress.Loopback, 0)
            MemberListener.Start()
            MemberPort = CType(MemberListener.LocalEndpoint, Net.IPEndPoint).Port
            Logger.Info($"房间成员服务端口：{MemberPort}")
        Catch ex As Exception
            Logger.Warn(ex, "房间成员服务端口分配失败")
            MemberPort = 0
        End Try
    End Sub

    ''' <summary>让系统挑一个空闲的本地端口（给加入者的成员通道用）。</summary>
    Private Function PickFreePort() As Integer
        Try
            Dim L As New Net.Sockets.TcpListener(Net.IPAddress.Loopback, 0)
            L.Start()
            Dim P As Integer = CType(L.LocalEndpoint, Net.IPEndPoint).Port
            L.Stop()
            Return P
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>启动房主的成员服务，房主自己永远是列表第一项。</summary>
    Private Sub StartMemberService()
        If MemberPort = 0 OrElse MemberListener Is Nothing Then Return
        MemberStop = False
        WasKicked = False
        SyncLock Members
            Members.Clear()
            Members.Add(New RoomMember With {.Uuid = "HOST", .Name = MyGameName,
                                             .DisplayName = MyGameName, .IsHost = True})
        End SyncLock
        RecalcMemberNames()
        RunInNewThread(
        Sub()
            Try
                Do While Not MemberStop
                    Try
                        Dim C As Net.Sockets.TcpClient = MemberListener.AcceptTcpClient()
                        RunInNewThread(Sub() HandleMember(C), "Link MemberConn")
                    Catch
                        If MemberStop Then Exit Do
                        Thread.Sleep(100)
                    End Try
                Loop
            Catch ex As Exception
                If Not MemberStop Then Logger.Warn(ex, "房间成员服务结束")
            End Try
        End Sub, "Link MemberService")
        RunInUi(Sub() RefreshMemberUi())
    End Sub

    ''' <summary>处理一个成员连接：登记 → 持续读 → 断开时移除。</summary>
    Private Sub HandleMember(C As Net.Sockets.TcpClient)
        Dim M As RoomMember = Nothing
        Try
            Dim Reader As New IO.StreamReader(C.GetStream(), Encoding.UTF8)
            Dim Writer As New IO.StreamWriter(C.GetStream(), New Text.UTF8Encoding(False)) With {.AutoFlush = True, .NewLine = vbLf}
            Dim Hello As String = Reader.ReadLine()
            If Hello Is Nothing OrElse Not Hello.StartsWithF("HELLO|") Then Return
            Dim Fields As String() = Hello.Split("|"c)
            If Fields.Length < 3 OrElse Fields(1).Trim() = "" Then Return
            M = New RoomMember With {
                .Uuid = Fields(2), .Name = Fields(1).Trim(), .DisplayName = Fields(1).Trim(),
                .IsHost = False, .Client = C, .Writer = Writer}
            SyncLock Members
                Members.Add(M)
            End SyncLock
            Logger.Info($"成员加入：{M.Name}（{M.Uuid}）")
            Try
                Writer.WriteLine("ROOM|" & RoomName)
            Catch
            End Try
            RecalcMemberNames()
            BroadcastMembers()
            '保持连接，直到对方断开
            Do While Not MemberStop
                If Reader.ReadLine() Is Nothing Then Exit Do
            Loop
        Catch ex As Exception
            Logger.Warn(ex, "成员连接处理结束")
        Finally
            If M IsNot Nothing Then
                SyncLock Members
                    Members.Remove(M)
                End SyncLock
                RecalcMemberNames()
                BroadcastMembers()
            End If
            Try
                C.Close()
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>同名成员按顺序编号：第 2 个显示 (2)，第 3 个显示 (3)……</summary>
    Private Sub RecalcMemberNames()
        SyncLock Members
            Dim Counts As New Dictionary(Of String, Integer)
            For Each M In Members
                If Not Counts.ContainsKey(M.Name) Then Counts(M.Name) = 0
                Counts(M.Name) += 1
                M.DisplayName = If(Counts(M.Name) = 1, M.Name, $"{M.Name}({Counts(M.Name)})")
            Next
        End SyncLock
    End Sub

    ''' <summary>把最新成员列表推给所有在线成员，同时刷新房主自己的界面。</summary>
    Private Sub BroadcastMembers()
        Dim Payload As String
        SyncLock Members
            Payload = "LIST|" & String.Join(MemberSep,
                Members.Select(Function(m) m.Uuid & "|" & m.DisplayName & "|" & If(m.IsHost, "1", "0")))
            For Each M In Members
                If M.Writer IsNot Nothing Then
                    Try
                        M.Writer.WriteLine(Payload)
                    Catch
                    End Try
                End If
            Next
        End SyncLock
        RunInUi(Sub() RefreshMemberUi())
    End Sub

    ''' <summary>房主踢人：先通知对方，再断开并从名单移除。</summary>
    Private Sub KickMember(Uuid As String)
        Dim Target As RoomMember = Nothing
        SyncLock Members
            Target = Members.FirstOrDefault(Function(m) m.Uuid = Uuid)
        End SyncLock
        If Target Is Nothing OrElse Target.IsHost Then Return
        Logger.Info($"房主踢出成员：{Target.DisplayName}")
        Try
            Target.Writer.WriteLine("KICKED")
        Catch
        End Try
        Try
            Target.Client.Close()
        Catch
        End Try
        SyncLock Members
            Members.Remove(Target)
        End SyncLock
        RecalcMemberNames()
        BroadcastMembers()
    End Sub

    '=====================================================================
    '  加入者：成员客户端
    '=====================================================================

    ''' <summary>连接房主的成员服务，登记游戏名并接收名单 / 踢出通知。</summary>
    Private Sub StartMemberClient()
        If MemberPort = 0 OrElse LocalMemberPort = 0 Then Return
        MemberStop = False
        WasKicked = False
        Dim Port As Integer = LocalMemberPort
        Dim Uuid As String = MyUuid
        Dim Name As String = MyGameName
        Logger.Info($"成员客户端启动：本地 {Port}，房主成员服务 {MemberPort}")
        RunInNewThread(
        Sub()
            For attempt = 1 To 12
                If MemberStop OrElse WasKicked Then Return
                Try
                    Using C As New Net.Sockets.TcpClient()
                        C.Connect("127.0.0.1", Port)
                        Logger.Info("成员客户端已连上房主成员服务")
                        Dim Reader As New IO.StreamReader(C.GetStream(), Encoding.UTF8)
                        Dim Writer As New IO.StreamWriter(C.GetStream(), New Text.UTF8Encoding(False)) With {.AutoFlush = True, .NewLine = vbLf}
                        Writer.WriteLine("HELLO|" & Name & "|" & Uuid)
                        Do While Not MemberStop
                            Dim Line As String = Reader.ReadLine()
                            If Line Is Nothing Then Exit Do
                            If Line = "KICKED" Then
                                WasKicked = True
                                RunInUi(Sub() OnKicked())
                                Return
                            ElseIf Line.StartsWithF("ROOM|") Then
                                ApplyRoomName(Line.Substring(5))
                            ElseIf Line.StartsWithF("LIST|") Then
                                ApplyMemberList(Line.Substring(5))
                            End If
                        Loop
                    End Using
                Catch ex As Exception
                    If attempt <= 2 Then Logger.Warn(ex, $"成员客户端连接失败（第 {attempt} 次）")
                End Try
                If MemberStop OrElse WasKicked Then Return
                Thread.Sleep(1000)
            Next
            Logger.Warn("成员客户端重试结束，未能连上房主成员服务")
        End Sub, "Link MemberClient")
    End Sub

    ''' <summary>解析房主推来的成员名单。</summary>
    Private Sub ApplyMemberList(Payload As String)
        Dim NewList As New List(Of RoomMember)
        For Each Chunk In Payload.Split(MemberSep)
            Dim F As String() = Chunk.Split("|"c)
            If F.Length < 3 OrElse F(0) = "" Then Continue For
            NewList.Add(New RoomMember With {.Uuid = F(0), .DisplayName = F(1), .Name = F(1), .IsHost = F(2) = "1"})
        Next
        SyncLock Members
            Members.Clear()
            Members.AddRange(NewList)
        End SyncLock
        RunInUi(Sub() RefreshMemberUi())
    End Sub

    ''' <summary>收到房主发来的房间名。</summary>
    Private Sub ApplyRoomName(Name As String)
        If String.IsNullOrWhiteSpace(Name) Then Return
        RoomName = Name.Trim()
        Logger.Info($"已收到房间名：{RoomName}")
        RunInUi(
        Sub()
            If Not IsServerSide Then LabFinishDesc.Text = BuildGuestDesc()
            RefreshMemberUi()
        End Sub)
    End Sub

    ''' <summary>加入者完成页正文（房间名可能晚到，所以单独生成）。</summary>
    Private Function BuildGuestDesc() As String
        Dim Head As String = If(RoomName <> "", $"房间名：{RoomName}{vbCrLf}", "")
        If DirectPeer <> "" Then
            Return Head &
                $"已通过 P2P 直连连上房主，数据不经过任何服务器。{vbCrLf}" &
                $"在多人游戏页面最下方就能找到联机房间，也可以直接连接：{vbCrLf}{ClientAddress}{vbCrLf}" &
                "注意：使用离线登录时不要手动输入 IP！"
        Else
            Return Head & $"在多人游戏页面的最下方就能找到联机房间！{vbCrLf}注意：使用离线登录时不要手动输入 IP！"
        End If
    End Function

    ''' <summary>被房主踢出：明确告知并退回等待页。</summary>
    Private Sub OnKicked()
        Hint("房主已把你踢出房间", HintType.Red)
        MyMsgBox("房主已把你踢出房间", "联机结束")
        MemberStop = True
        ChangeState(LinkStates.Waiting)
    End Sub

    ''' <summary>退出房间时关掉成员通道。</summary>
    Private Sub StopMemberService()
        MemberStop = True
        Try
            If MemberListener IsNot Nothing Then MemberListener.Stop()
        Catch
        End Try
        MemberListener = Nothing
        SyncLock Members
            For Each M In Members
                Try
                    If M.Client IsNot Nothing Then M.Client.Close()
                Catch
                End Try
            Next
            Members.Clear()
        End SyncLock
        MemberPort = 0
        LocalMemberPort = 0
        MyGameName = ""
        WasKicked = False
        Try
            PanMembers.Visibility = Visibility.Collapsed
        Catch
        End Try
    End Sub

    '=====================================================================
    '  成员管理界面
    '=====================================================================

    ''' <summary>点击中间的人数卡片打开成员管理。</summary>
    Private Sub BtnFinishPlayer_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles BtnFinishPlayer.MouseLeftButtonUp
        If LinkState <> LinkStates.Finished Then Return
        RefreshMemberUi()
        PanMembers.Visibility = Visibility.Visible
    End Sub
    Private Sub BtnMemberClose_Click() Handles BtnMemberClose.Click
        PanMembers.Visibility = Visibility.Collapsed
    End Sub

    ''' <summary>把当前成员名单渲染到管理面板（房主可见「踢出」按钮）。</summary>
    Private Sub RefreshMemberUi()
        If PanMemberList Is Nothing Then Return
        PanMemberList.Children.Clear()
        Dim Snapshot As New List(Of RoomMember)
        SyncLock Members
            Snapshot.AddRange(Members)
        End SyncLock
        LabMemberCount.Text = Snapshot.Count & " 人"
        LabMemberTitle.Text = If(RoomName <> "", "房间成员 · " & RoomName, "房间成员")
        LabFinishPlayer.Text = Snapshot.Count & "  人"
        For Each M In Snapshot
            Dim Row As New Grid With {.Height = 40, .Margin = New Thickness(0, 1, 0, 1)}
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Dim Label As New TextBlock With {
                .Text = M.DisplayName & If(M.IsHost, "　【房主】", If(M.Uuid = MyUuid, "　（我）", "")),
                .FontSize = 14, .VerticalAlignment = VerticalAlignment.Center}
            Label.SetResourceReference(TextBlock.ForegroundProperty, If(M.IsHost, "ColorBrush3", "ColorBrushGray1"))
            Grid.SetColumn(Label, 0)
            Row.Children.Add(Label)
            If IsServerSide AndAlso DirectMode AndAlso Not M.IsHost Then
                Dim Uid As String = M.Uuid
                Dim Btn As New MyButton With {.Text = "踢出", .Height = 27, .Width = 62,
                                              .Margin = New Thickness(6, 0, 0, 0), .Padding = New Thickness(6, 3, 6, 3),
                                              .ColorType = MyButton.ColorState.Red}
                AddHandler Btn.Click, Sub(s, ev) KickMember(Uid)
                Grid.SetColumn(Btn, 1)
                Row.Children.Add(Btn)
            End If
            PanMemberList.Children.Add(Row)
        Next
        If Snapshot.Count = 0 Then
            PanMemberList.Children.Add(New TextBlock With {.Text = "正在获取房间成员……", .FontSize = 13,
                                                          .Margin = New Thickness(0, 12, 0, 12),
                                                          .Foreground = Application.Current.Resources("ColorBrushGray1")})
        End If
    End Sub


    ''' <summary>短码发现时记下的房主真实地址（UDP 包源 IP，比邀请码里的地址可靠）。</summary>
    Private Shared LanPeerHint As String = ""

    ''' <summary>
    ''' 监听房主的局域网广播，用数据包的源 IP 拼出直连地址。
    ''' 房主有多个网卡时，邀请码里写的 IP 未必是加入者能摸到的那个，源 IP 一定可达。
    ''' </summary>
    Private Function ProbeLanHost(ExpectedNetwork As String) As String
        Try
            Using U As New Net.Sockets.UdpClient()
                U.Client.SetSocketOption(Net.Sockets.SocketOptionLevel.Socket, Net.Sockets.SocketOptionName.ReuseAddress, True)
                U.Client.Bind(New Net.IPEndPoint(Net.IPAddress.Any, LanDiscoveryPort))
                U.Client.ReceiveTimeout = 600
                Dim Deadline = Date.Now.AddMilliseconds(2000)
                Do While Date.Now < Deadline
                    Try
                        Dim Ep As New Net.IPEndPoint(Net.IPAddress.Any, 0)
                        Dim Text As String = Encoding.UTF8.GetString(U.Receive(Ep))
                        Dim Parts As String() = Text.Split("|"c)
                        If Parts.Length >= 3 AndAlso Parts(0) = "PCLP2P1" Then
                            Dim Full As String = String.Join("|", Parts.Skip(2))
                            If Full.StartsWithF(ExpectedNetwork) Then
                                Dim Seg As String() = Full.Split("~"c)
                                Dim P As Integer
                                If Seg.Length >= 2 AndAlso Integer.TryParse(Seg(1), P) AndAlso P > 0 Then
                                    Return $"tcp://{Ep.Address}:{P}"
                                End If
                            End If
                        End If
                    Catch
                    End Try
                Loop
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "局域网房主发现失败")
        End Try
        Return ""
    End Function

    ''' <summary>连不上时逐个实测候选地址，返回可操作的诊断文本。</summary>
    Private Function DiagnosePeers(PeerList As IEnumerable(Of String)) As String
        Dim Sb As New Text.StringBuilder()
        Sb.AppendLine("逐个实测结果：")
        Dim AnyOk As Boolean = False
        For Each P In PeerList
            Dim Host As String = P.Replace("tcp://", "")
            Dim Colon As Integer = Host.LastIndexOf(":"c)
            Dim Port As Integer
            If Colon < 0 OrElse Not Integer.TryParse(Host.Substring(Colon + 1), Port) Then Continue For
            Dim Ip As String = Host.Substring(0, Colon).Trim("["c, "]"c)
            Dim Reachable As Boolean = False
            Try
                Using C As New Net.Sockets.TcpClient()
                    Dim Ar As IAsyncResult = C.BeginConnect(Ip, Port, Nothing, Nothing)
                    If Ar.AsyncWaitHandle.WaitOne(1200) Then
                        C.EndConnect(Ar)
                        Reachable = True
                    End If
                    C.Close()
                End Using
            Catch
            End Try
            If Reachable Then AnyOk = True
            Sb.AppendLine($"  {If(Reachable, "√ 能连上", "× 连不上")}  {Ip}:{Port}")
        Next
        Sb.AppendLine()
        If AnyOk Then
            Sb.Append("有地址能连上，多半是联机模块启动慢了，请重试一次。若仍失败，让房主关闭房间重新创建，把【最新】连接码发给你。")
        Else
            Sb.Append("所有地址都连不上，说明这两台设备之间网络不通。请按顺序检查：" & vbCrLf &
                      "1. 两台设备是否连同一个 WiFi/路由器（访客网络、AP 隔离、光猫不同频段都算不同网）；" & vbCrLf &
                      "2. 让房主关闭房间重新创建，重新复制【最新】连接码 —— 每次建房端口都会变，旧码必然连不上；" & vbCrLf &
                      "3. 同一 WiFi 下优先用房主完成页的 6 位短码；" & vbCrLf &
                      "4. 跨网络联机需要房主有公网 IP 且在路由器放行该端口，运营商大内网（CGNAT）下无法直连。")
        End If
        Return Sb.ToString()
    End Function


End Class
