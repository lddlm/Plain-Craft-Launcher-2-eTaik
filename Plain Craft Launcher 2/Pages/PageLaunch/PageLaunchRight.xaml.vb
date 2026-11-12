Imports System.IO
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading

Public Class PageLaunchRight
    Implements IRefreshable, IDispatcherUnhandledException

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        PanLog.Visibility = ModeDebug.ToVisibility
        '快照版提示
        PanHint.Visibility =
            (BuildType <> BuildTypes.Release AndAlso VersionBranchMain = "Official" AndAlso
             CurrentRank < DonationRank.Rank23 AndAlso Not Settings.Get(Of Boolean)("HintSnapshot")).ToVisibility
    End Sub

#Region "主页"

    ''' <summary>
    ''' 刷新主页。
    ''' </summary>
    Private Sub Refresh() Handles Me.Loaded
        RunInThread(
        Sub()
            Try
                SyncLock RefreshLock
                    RefreshReal()
                End SyncLock
            Catch ex As Exception
                Logger.Error(ex, "加载 PCL 主页自定义信息失败", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
            End Try
        End Sub)
    End Sub
    Private Sub RefreshReal()
        Dim Content As String = Nothing, Url As String = Nothing
        Select Case Settings.Get(Of Integer)("UiCustomType")
            Case 1
                '加载本地文件
                Logger.Info("主页自定义数据来源：本地文件")
                Content = FileUtils.TryReadAsString(Paths.Base & "PCL\Custom.xaml")
            Case 2
                '联网下载
                Url = Settings.Get(Of String)("UiCustomNet")
            Case 3
                '预设
                Select Case Settings.Get(Of Integer)("UiCustomPreset")
                    Case 0
                        Logger.Info("主页预设：你知道吗")
                        Content = "
                            <local:MyCard Title=""你知道吗？"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{hint}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 1
                        Logger.Info("主页预设：回声洞")
                        Content = "
                            <local:MyCard Title=""回声洞"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{cave}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 2
                        Logger.Info("主页预设：Minecraft 新闻")
                        Url = "https://mcnews.meloong.com"
                    Case 3
                        Logger.Info("主页预设：简单主页")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/MFn233/Custom.xaml"
                    Case 4
                        Logger.Info("主页预设：每日整合包推荐")
                        Url = "https://pclsub.sodamc.com/"
                    Case 5
                        Logger.Info("主页预设：Minecraft 皮肤推荐")
                        Url = "https://forgepixel.com/pcl_sub_file"
                    Case 6
                        Logger.Info("主页预设：OpenBMCLAPI 仪表盘 Lite")
                        Url = "https://pcl-bmcl.milu.ink/"
                    Case 7
                        Logger.Info("主页预设：主页市场")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/p/Homepages/Homepage.Market/Custom.xaml"
                    Case 8
                        Logger.Info("主页预设：更新日志")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/Joker2184/UpdateHomepage.xaml"
                    Case 9
                        Logger.Info("主页预设：PCL 新功能说明书")
                        Url = "https://raw.gitcode.com/WForst-Breeze/WhatsNewPCL/raw/main/Custom.xaml"
                    Case 10
                        Logger.Info("主页预设：OpenMCIM Dashboard")
                        Url = "https://files.mcimirror.top/PCL"
                    Case 11
                        Logger.Info("主页预设：杂志主页")
                        Url = "https://gh-proxy.com/https://github.com/Neclyon/Magazine-Homepage-PCL/raw/main/output/Custom.xaml"
                    Case 12
                        Logger.Info("主页预设：PCL GitHub 仪表盘")
                        Url = "https://ddf.pcl-community.top/Custom.xaml"
                    Case 13
                        Logger.Info("主页预设：PCL 更新摘要")
                        Url = "https://raw.gitcode.com/ENC_Euphony/PCL-AI-Summary-HomePage/raw/master/Custom.xaml"
                    Case 14
                        Logger.Info("主页预设：今日新闻热点")
                        Url = "https://pcl.wyc-w.top/index.xaml"
                    Case 15
                        Logger.Info("主页预设：Minecraft 芝士站")
                        Url = "https://mkss-services.awaeric.xyz"
                    Case 16
                        Logger.Info("主页预设：整合包推荐引擎")
                        Url = "https://qawsedrftgyhujiko.fun/pcl2/Custom.xaml"
                    Case 17
                        Logger.Info("主页预设：Bangumi 番剧主页")
                        Url = "https://bangumi.p.kaphia.top"
                    Case 18
                        Logger.Info("主页预设：Bilibili 热门")
                        Url = "https://bilibili.p.kaphia.top"
                    Case 19
                        Logger.Info("主页预设：Music 云音乐热门")
                        Url = "https://cloudmusic.p.kaphia.top"
                    Case 20
                        Logger.Info("主页预设：模组推荐")
                        Url = "https://g-fish.dpdns.org/download/ModPage.xaml"
                    Case 21
                        Logger.Info("主页预设：每日游戏推荐")
                        Url = "https://pcl.xn--9iq209asnop94b.top/download/GamePage.xaml"
                    Case 22
                        Logger.Info("主页预设：Wallpaper 每日壁纸")
                        Url = "https://wallpaper.p.kaphia.top"
                    Case 23
                        Logger.Info("主页预设：Weather 天气预报")
                        Url = "https://weather-p.kaphia.top/{variable:PCLDailyWeatherPage:choose}.xaml"
                    Case 24
                        Logger.Info("主页预设：新一代狗屁王大杂烩主页")
                        Url = "https://end.g-fish.dpdns.org/Custom.xaml"
                End Select
        End Select
        '联网下载
        If Not String.IsNullOrWhiteSpace(Url) Then
            '替换自定义变量与设置
            Url = ArgumentReplace(Url, AddressOf WebUtility.HtmlEncode)
            If Url = Settings.Get(Of String)("CacheSavedPageUrl") AndAlso FileUtils.Exists(PathTemp & "Cache\Custom.xaml") Then
                '缓存可用
                Logger.Info("主页自定义数据来源：联网缓存文件")
                Content = FileUtils.TryReadAsString(PathTemp & "Cache\Custom.xaml")
                '后台更新缓存
                OnlineLoader.Start((Url, False))
            Else
                '缓存不可用
                Logger.Info("主页自定义数据来源：联网全新下载")
                Hint("正在加载主页……")
                RunInUiWait(Sub() LoadContent(Nothing)) '在加载结束前清空页面
                Settings.Set("CacheSavedPageVersion", "")
                OnlineLoader.Start((Url, True)) '下载完成后将会再次触发更新
                Return
            End If
        End If
        '同步到 UI
        RunInUi(Sub() LoadContent(Content))
    End Sub
    Private RefreshLock As New Object

    '联网获取主页文件
    Private OnlineLoader As New LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer)("下载主页", AddressOf OnlineLoaderSub) With {.ReloadTimeout = 10 * 60 * 1000}
    Private Sub OnlineLoaderSub(Task As LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer))
        Dim Address As String = Task.Input.Address '#3721 中连续触发两次导致内容变化
        Dim ShouldRefresh As Boolean = Task.Input.ShouldRefresh
        Try
            '获取版本校验地址
            Dim VersionAddress As String
            If Address.Contains(".xaml") Then
                VersionAddress = Address.Replace(".xaml", ".xaml.ini")
            Else
                VersionAddress = Address.BeforeFirst("?")
                If Not VersionAddress.EndsWithF("/") Then VersionAddress += "/"
                VersionAddress += "version"
                If Address.Contains("?") Then VersionAddress += "?" & Address.AfterFirst("?")
            End If
            '校验版本
            Dim Version As String = ""
            Try
                Version = NetRequestByClientRetry(VersionAddress)
                If Version.Length > 1000 Then Throw New Exception($"获取的主页版本过长（{Version.Length} 字符）")
                Dim CurrentVersion As String = Settings.Get(Of String)("CacheSavedPageVersion")
                If Version <> "" AndAlso CurrentVersion <> "" AndAlso Version = CurrentVersion Then
                    Logger.Info($"当前缓存的主页已为最新，当前版本：{Version}，检查源：{VersionAddress}")
                    Return
                End If
                Logger.Info($"需要下载联网主页，当前版本：{Version}，检查源：{VersionAddress}")
            Catch exx As Exception
                Logger.Warn(exx, $"联网获取主页版本失败")
                Logger.Info($"无法检查联网主页版本，将直接下载，检查源：{VersionAddress}")
            End Try
            '实际下载
            Dim FileContent As String = NetRequestByClientRetry(Address)
            Logger.Info($"已联网下载主页，内容长度：{FileContent.Length}，来源：{Address}")
            Settings.Set("CacheSavedPageUrl", Address)
            Settings.Set("CacheSavedPageVersion", Version)
            FileUtils.Write(PathTemp & "Cache\Custom.xaml", FileContent)
            '若内容变更则要求刷新
            If LoadedContentHash <> FileContent.GetStableHashCode() AndAlso ShouldRefresh Then Refresh()
        Catch ex As Exception
            Logger.Error(ex, $"下载主页失败（{Address}）", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
        End Try
    End Sub

    ''' <summary>
    ''' 立即强制刷新主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Public Sub ForceRefresh() Implements IRefreshable.Refresh
        Logger.Info("要求强制刷新主页")
        ClearCache()
        '实际的刷新
        If FrmMain.PageCurrent.Page = FormMain.PageType.Launch Then
            PanBack.ScrollToHome()
            Refresh()
        Else
            FrmMain.PageChange(FormMain.PageType.Launch)
        End If
    End Sub

    ''' <summary>
    ''' 清空主页缓存信息。
    ''' </summary>
    Private Sub ClearCache()
        LoadedContentHash = Nothing
        OnlineLoader.Input = ("", True)
        Settings.Set("CacheSavedPageUrl", "")
        Settings.Set("CacheSavedPageVersion", "")
        Logger.Info("已清空主页缓存")
    End Sub

    ''' <summary>
    ''' 从文本内容中加载主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Private Sub LoadContent(Content As String)
        Try
            SyncLock LoadContentLock
                '如果加载目标内容一致则不加载
                Dim Hash = If(Content, "").GetStableHashCode()
                If Hash = LoadedContentHash Then Return
                LoadedContentHash = Hash
                '实际加载内容
                PanCustom.Children.Clear()
                If String.IsNullOrWhiteSpace(Content) Then
                    Logger.Info($"实例化：清空主页 UI，来源为空")
                    Return
                End If
                Dim LoadStartTime As Date = Date.Now
                '修改时应同时修改 PageOtherHelpDetail.Init
                Content = ArgumentReplace(Content, AddressOf StringUtils.XmlEscape)
                Do While Content.Contains("xmlns")
                    Content = Content.RegexReplace("xmlns[^""']*(""|')[^""']*(""|')", "").Replace("xmlns", "") '禁止声明命名空间
                Loop
                Content = "<StackPanel xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:sys=""clr-namespace:System;assembly=mscorlib"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" xmlns:core=""clr-namespace:MeloongCore;assembly=MeloongCore"" xmlns:corewpf=""clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf"">" & Content & "</StackPanel>"
                Logger.Info($"实例化：加载主页 UI 开始，最终内容长度：{Content.Count}")
                PanCustom.Children.Add(GetObjectFromXML(Content))
                '加载计时
                Dim LoadCostTime = (Date.Now - LoadStartTime).Milliseconds
                Logger.Info($"实例化：加载主页 UI 完成，耗时 {LoadCostTime}ms")
                If LoadCostTime > 3000 Then Hint($"主页加载过于缓慢（花费了 {Math.Round(LoadCostTime / 1000, 1)} 秒），请向主页作者反馈此问题，或暂时停止使用该主页")
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, $"加载失败的主页内容：{vbCrLf}{Content}")
            OnLoadContentFailed(ex)
        End Try
    End Sub
    ''' <summary>
    ''' 加载主页失败时调用。
    ''' </summary>
    Private Sub OnLoadContentFailed(ex As Exception)
        If ModeDebug OrElse Settings.Get(Of Integer)("UiCustomType") = 1 Then
            Logger.Warn(ex, "加载主页失败")
            If MyMsgBox(If(TypeOf ex Is UnauthorizedAccessException, ex.Message, $"主页内容编写有误，请根据下列错误信息进行检查：{vbCrLf}{ex.GetDisplay(False)}"),
                        "加载主页失败", "重试", "取消") = 1 Then ForceRefresh()
        Else
            Logger.Error(ex, "加载主页失败", LogBehavior.Toast)
        End If
    End Sub
    ''' <summary>
    ''' 捕获主页在 Measure 和 Arrange 阶段抛出的异常。
    ''' </summary>
    Private Sub DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Implements IDispatcherUnhandledException.DispatcherUnhandledException
        If TypeOf e.Exception IsNot Markup.XamlParseException Then Return
        e.Handled = True
        LoadContent(Nothing)
        OnLoadContentFailed(e.Exception)
    End Sub

    Private LoadedContentHash As ULong? = Nothing
    Private LoadContentLock As New Object

#End Region

#Region "皮肤管理"

    Private Sub SkinUI_Loaded() Handles Me.Loaded
        RefreshSkinUI()
        ApplySavedSkinLibSource()
    End Sub

    Private _SkinPreviewU As Integer = 0
    ''' <summary>
    ''' 预览的放大倍数：让 16×32 单位的预览恰好对齐显示器的物理像素，
    ''' 否则 WPF 会做插值，像素画的边缘就会发糊。
    ''' </summary>
    Private Function SkinPreviewScale() As Integer
        If _SkinPreviewU = 0 Then
            Dim Dpi As Double = 1.0
            Try
                Dim Src = PresentationSource.FromVisual(Me)
                If Src?.CompositionTarget IsNot Nothing Then Dpi = Src.CompositionTarget.TransformToDevice.M11
            Catch ex As Exception
                Dpi = 1.0
            End Try
            If Dpi <= 0 Then Dpi = 1.0
            _SkinPreviewU = Math.Max(3, Math.Min(8, CInt(Math.Round(96 * Dpi / 32))))
        End If
        Return _SkinPreviewU
    End Function

    ''' <summary>刷新皮肤预览与 Quick Skin 状态。</summary>
    Public Sub RefreshSkinUI()
        If CardSkin Is Nothing Then Return
        Dim Instance = SkinHelper.CurrentInstance()
        CheckSkinSync.Checked = Settings.Get(Of Boolean)("SkinSyncToGame")
        RefreshSkinLocalList()
        Dim SkinPath = Settings.Get(Of String)("SkinLastFile")
        If Not String.IsNullOrEmpty(SkinPath) AndAlso File.Exists(SkinPath) Then
            Try
                Dim Bmp = SkinLibrary.LoadTexture(SkinPath)
                ImgSkinPreview.Source = SkinLibrary.BuildPreview(Bmp, SkinPreviewScale(), False)
                LabSkinEmpty.Visibility = Visibility.Collapsed
            Catch ex As Exception
                Logger.Warn(ex, "加载皮肤预览失败")
            End Try
        Else
            ImgSkinPreview.Source = Nothing
            LabSkinEmpty.Visibility = Visibility.Visible
        End If
        If Instance Is Nothing Then
            LabSkinStatus.Text = "先在左侧选择一个版本，才能把皮肤同步进游戏。"
            Return
        End If
        Dim Loader = SkinHelper.LoaderOf(Instance)
        If Loader = "" Then
            LabSkinStatus.Text = "当前版本是原版（没有 Fabric / Forge / NeoForge），无法使用 Quick Skin。"
        ElseIf SkinHelper.IsQuickSkinInstalled(Instance) Then
            LabSkinStatus.Text = $"已检测到 Quick Skin（{Loader}，游戏 {SkinHelper.GameVersionOf(Instance)}）"
        Else
            LabSkinStatus.Text = $"未安装 Quick Skin（{Loader}，游戏 {SkinHelper.GameVersionOf(Instance)}）"
        End If
    End Sub

    ''' <summary>点头像后调用：回到顶部并把皮肤卡片闪一下。</summary>
    ''' <summary>点头像时调用：已展开就收起，未展开就展开。</summary>
    Public Sub ToggleSkinManager()
        If CardSkin Is Nothing Then Return
        If CardSkin.Visibility = Visibility.Visible Then
            HideSkinManager()
        Else
            ShowSkinManager()
        End If
    End Sub

    ''' <summary>展开皮肤区：皮肤管理 + 皮肤库 两张卡按当前动画体系逐个入场。</summary>
    Public Sub ShowSkinManager()
        If CardSkin Is Nothing Then Return
        PanBack.ScrollToHome()
        RefreshSkinUI()
        ShowSkinLibrary()
        AnimateElementsIn(CardSkin, CardSkinLib)
    End Sub

    ''' <summary>收起皮肤区：两张卡按当前动画体系逐个退场，结束后隐藏。</summary>
    Public Sub HideSkinManager()
        If CardSkin Is Nothing Then Return
        If CardSkin.Visibility <> Visibility.Visible Then Return
        SkinLibToken += 1 '让后台还在下载的缩略图停下
        Dim Targets As New List(Of FrameworkElement)
        Targets.Add(CardSkin)
        If CardSkinLib IsNot Nothing Then Targets.Add(CardSkinLib)
        AnimateElementsOut(Targets.ToArray(), Sub() RefreshSkinUI())
    End Sub

    Private Sub BtnSkinClose_Click(sender As Object, e As EventArgs) Handles BtnSkinClose.Click
        '收起整块：皮肤管理卡 + 下面的皮肤库卡
        HideSkinManager()
    End Sub

    Private Sub CardSkin_DragOver(sender As Object, e As DragEventArgs) Handles CardSkin.DragOver
        e.Effects = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
        e.Handled = True
    End Sub

    Private Sub CardSkin_Drop(sender As Object, e As DragEventArgs) Handles CardSkin.Drop
        Try
            Dim Files = CType(e.Data.GetData(DataFormats.FileDrop), String())
            If Files Is Nothing OrElse Files.Length = 0 Then Return
            ImportSkinFlow(Files(0))
        Catch ex As Exception
            Hint("拖入皮肤失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub BtnSkinImport_Click(sender As Object, e As EventArgs) Handles BtnSkinImport.Click
        Dim Dialog As New Microsoft.Win32.OpenFileDialog With {
            .Title = "选择皮肤文件",
            .Filter = "皮肤文件|*.png;*.jpg;*.jpeg"}
        If Dialog.ShowDialog() <> True Then Return
        ImportSkinFlow(Dialog.FileName)
    End Sub

    Private Sub ImportSkinFlow(SourcePath As String)
        Dim Instance = SkinHelper.CurrentInstance()
        Dim Sync As Boolean = CheckSkinSync.Checked
        If Sync AndAlso Instance IsNot Nothing Then
            If SkinHelper.LoaderOf(Instance) = "" Then
                Hint("当前版本不是模组加载器，皮肤只会导入启动器", HintType.Blue)
                Sync = False
            ElseIf Not SkinHelper.IsQuickSkinInstalled(Instance) Then
                If MyMsgBox("同步到游戏需要 Quick Skin 模组，当前版本还没安装。" & vbCrLf &
                            "现在自动安装吗？（支持 Fabric / Forge / NeoForge）", "缺少 Quick Skin", "自动安装", "只导入不安装") = 1 Then
                    InstallSkinModThenImport(SourcePath, Instance)
                    Return
                End If
                Sync = False
            End If
        End If
        FinishSkinImport(SourcePath, Instance, Sync)
    End Sub

    Private Sub InstallSkinModThenImport(SourcePath As String, Instance As McInstance)
        Hint("正在安装 Quick Skin，请稍候…", HintType.Blue, False)
        SkinHelper.InstallQuickSkin(Instance,
        Sub(Ok As Boolean, Message As String)
            If Ok Then
                Hint(Message, HintType.Green, False)
                FinishSkinImport(SourcePath, Instance, True)
            Else
                Hint(Message, HintType.Red)
                RefreshSkinUI()
            End If
        End Sub)
    End Sub

    Private Sub FinishSkinImport(SourcePath As String, Instance As McInstance, Sync As Boolean)
        Try
            Dim Target = SkinHelper.ImportSkin(SourcePath, Instance, Sync)
            '左侧头像也跟着换成这张皮肤的脸
            If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshLocalSkin(Target)
            Hint(If(Sync, "皮肤已导入并同步到游戏", "皮肤已导入"), HintType.Green, False)
        Catch ex As Exception
            Hint("导入皮肤失败：" & ex.Message, HintType.Red)
        End Try
        RefreshSkinUI()
    End Sub

    Private Sub CheckSkinSync_Change(sender As Object, user As Boolean) Handles CheckSkinSync.Change
        If AniControlEnabled <> 0 Then Return
        Settings.Set("SkinSyncToGame", CheckSkinSync.Checked)
        If Not CheckSkinSync.Checked Then Return
        Dim Instance = SkinHelper.CurrentInstance()
        Dim SkinPath = Settings.Get(Of String)("SkinLastFile")
        If Instance Is Nothing OrElse String.IsNullOrEmpty(SkinPath) OrElse Not File.Exists(SkinPath) Then Return
        If SkinHelper.LoaderOf(Instance) = "" Then Return
        If Not SkinHelper.IsQuickSkinInstalled(Instance) Then
            If MyMsgBox("同步到游戏需要 Quick Skin 模组，当前版本还没安装。要现在自动安装吗？", "缺少 Quick Skin", "自动安装", "取消") <> 1 Then
                CheckSkinSync.Checked = False
                Return
            End If
            SkinHelper.InstallQuickSkin(Instance,
            Sub(Ok As Boolean, Message As String)
                Hint(Message, If(Ok, HintType.Green, HintType.Red), Ok)
                If Ok Then SkinHelper.SyncSkinToGame(SkinPath, Instance)
                RefreshSkinUI()
            End Sub)
            Return
        End If
        Try
            SkinHelper.SyncSkinToGame(SkinPath, Instance)
            Hint("皮肤已同步到游戏", HintType.Green, False)
        Catch ex As Exception
            Hint("同步失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub BtnSkinFolder_Click(sender As Object, e As EventArgs) Handles BtnSkinFolder.Click
        Dim Instance = SkinHelper.CurrentInstance()
        Dim Folder = If(Instance Is Nothing, SkinHelper.LauncherSkinFolder, SkinHelper.QuickSkinFolder(Instance))
        Try
            DirectoryUtils.Create(Folder)
            Process.Start("explorer.exe", Folder.TrimEnd("\"c))
        Catch ex As Exception
            Logger.Warn(ex, "打开皮肤文件夹失败")
            Hint("打开文件夹失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region


#Region "皮肤库"

    Private SkinLibSort As String = SkinLibrary.SortNewest
    Private SkinLibPageNo As Integer = 1
    Private SkinLibQuery As String = ""
    Private SkinLibTotal As Integer = 0
    Private SkinLibPages As Integer = 1
    ''' <summary>当前列表是否来自本地缓存（网络失败时的降级）。</summary>
    Private SkinLibFromCache As Boolean = False
    ''' <summary>状态行的完整文案（临时提示之后复位用）。</summary>
    Private SkinLibStatusText As String = ""
    ''' <summary>当前皮肤源：0 photo2skin / 1 LittleSkin。</summary>
    Private SkinLibSource As Integer = SkinLibrary.SourcePhoto2Skin
    ''' <summary>加载令牌：切页 / 排序 / 关闭时自增，用来丢弃过期的后台结果。</summary>
    Private SkinLibToken As Integer = 0

    ''' <summary>展开下方独立的「皮肤库」卡片（首次展开时自动拉第一页）。</summary>
    Public Sub ShowSkinLibrary()
        If CardSkinLib Is Nothing Then Return
        '首次展开才拉第一页；之后保留上次的列表与页码
        If PanSkinLibList.Children.Count = 0 Then
            SkinLibPageNo = 1
            LoadSkinLibraryPage()
        End If
    End Sub

    ''' <summary>收起皮肤库卡片。</summary>
    Public Sub HideSkinLibrary()
        SkinLibToken += 1 '让后台还在下载的缩略图停下
        If CardSkinLib IsNot Nothing Then CardSkinLib.Visibility = Visibility.Collapsed
        RefreshSkinUI()
    End Sub

    ''' <summary>载入时恢复上次选的皮肤源，并同步单选按钮与排序项显隐。</summary>
    Private Sub ApplySavedSkinLibSource()
        Try
            SkinLibSource = Math.Max(0, Math.Min(1, Settings.Get(Of Integer)("SkinLibSource")))
        Catch ex As Exception
            SkinLibSource = SkinLibrary.SourcePhoto2Skin
        End Try
        AniControlEnabled += 1
        If SkinLibSource = SkinLibrary.SourceLittleSkin Then
            RadioSourceLittleSkin.Checked = True
            RadioSkinNewest.Checked = True
        Else
            RadioSourcePhoto2Skin.Checked = True
        End If
        AniControlEnabled -= 1
        ApplySkinLibSourceUI()
    End Sub

    ''' <summary>切换皮肤源：LittleSkin 只有最新 / 点赞两种排序。</summary>
    Private Sub ChangeSkinLibSource(sender As Object, raiseByMouse As Boolean) Handles RadioSourcePhoto2Skin.Check, RadioSourceLittleSkin.Check
        If Not raiseByMouse Then Return
        Dim Btn = TryCast(sender, MyRadioButton)
        If Btn Is Nothing Then Return
        SkinLibSource = If(Btn Is RadioSourceLittleSkin, SkinLibrary.SourceLittleSkin, SkinLibrary.SourcePhoto2Skin)
        Settings.Set("SkinLibSource", SkinLibSource)
        ApplySkinLibSourceUI()
        '切源后回到第一页 + 最新排序
        SkinLibSort = SkinLibrary.SortNewest
        AniControlEnabled += 1
        RadioSkinNewest.Checked = True
        AniControlEnabled -= 1
        SkinLibPageNo = 1
        LoadSkinLibraryPage()
    End Sub

    ''' <summary>按皮肤源决定哪些排序项可用。</summary>
    Private Sub ApplySkinLibSourceUI()
        If RadioSkinHot Is Nothing Then Return
        Dim IsLittle = SkinLibSource = SkinLibrary.SourceLittleSkin
        RadioSkinHot.Visibility = If(IsLittle, Visibility.Collapsed, Visibility.Visible)
        RadioSkinDownloads.Visibility = If(IsLittle, Visibility.Collapsed, Visibility.Visible)
    End Sub

    Private Sub BtnSkinLibSearch_Click(sender As Object, e As EventArgs) Handles BtnSkinLibSearch.Click
        SkinLibQuery = If(SearchSkinLib.Text, "")
        SkinLibPageNo = 1
        LoadSkinLibraryPage()
    End Sub

    Private Sub ChangeSkinLibSort(sender As Object, raiseByMouse As Boolean) Handles RadioSkinNewest.Check, RadioSkinHot.Check, RadioSkinLikes.Check, RadioSkinDownloads.Check
        If Not raiseByMouse Then Return
        Dim Btn = TryCast(sender, MyRadioButton)
        If Btn Is Nothing OrElse Btn.Tag Is Nothing Then Return
        SkinLibSort = Btn.Tag.ToString()
        SkinLibPageNo = 1
        LoadSkinLibraryPage()
    End Sub

    Private Sub BtnSkinLibPrev_Click(sender As Object, e As EventArgs) Handles BtnSkinLibPrev.Click
        If SkinLibPageNo <= 1 Then Return
        SkinLibPageNo -= 1
        LoadSkinLibraryPage()
    End Sub

    Private Sub BtnSkinLibNext_Click(sender As Object, e As EventArgs) Handles BtnSkinLibNext.Click
        If SkinLibPageNo >= SkinLibPages Then Return
        SkinLibPageNo += 1
        LoadSkinLibraryPage()
    End Sub

    ''' <summary>拉取当前排序 / 页码 / 关键词的一页皮肤。</summary>
    Private Sub LoadSkinLibraryPage()
        Dim Token = SkinLibToken + 1
        SkinLibToken = Token
        PanSkinLibList.Children.Clear()
        LabSkinLibStatus.Text = "正在加载…"
        LabSkinLibPage.Text = "…"
        BtnSkinLibPrev.IsEnabled = False
        BtnSkinLibNext.IsEnabled = False
        Dim Sort = SkinLibSort, PageNo = SkinLibPageNo, Query = SkinLibQuery, Source = SkinLibSource
        RunInNewThread(
        Sub()
            Try
                Dim Result = SkinLibrary.Fetch(Source, Sort, PageNo, Query)
                RunInUi(
                Sub()
                    If Token <> SkinLibToken Then Return
                    RenderSkinLibrary(Result, Token)
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    If Token <> SkinLibToken Then Return
                    LabSkinLibStatus.Text = "加载失败：" & ex.Message
                    LabSkinLibPage.Text = "第 0 / 0 页"
                End Sub)
            End Try
        End Sub, "SkinLibrary List")
    End Sub

    ''' <summary>渲染一页：先摆占位格子，再在后台逐张下载并绘制预览。</summary>
    Private Sub RenderSkinLibrary(Result As SkinLibraryResult, Token As Integer)
        PanSkinLibList.Children.Clear()
        SkinLibTotal = Result.Total
        SkinLibFromCache = Result.FromCache
        SkinLibPages = Math.Max(1, Result.Pages)
        'LittleSkin 不返回总数，只显示「来自 xxx」
        Dim SourceName = If(SkinLibSource = SkinLibrary.SourceLittleSkin, "littleskin.cn", "photo2skin.com")
        SkinLibStatusText = If(Result.Total > 0,
                               If(Result.FromCache, $"共 {Result.Total} 个皮肤 · 离线缓存（上次抓取）",
                                                    $"共 {Result.Total} 个皮肤 · 来自 {SourceName}"),
                               If(Result.FromCache, $"离线缓存（上次抓取）· {SourceName}",
                                                    $"来自 {SourceName}"))
        LabSkinLibStatus.Text = SkinLibStatusText
        LabSkinLibPage.Text = If(Result.Total > 0, $"第 {Result.Page} / {SkinLibPages} 页", $"第 {Result.Page} 页")
        BtnSkinLibPrev.IsEnabled = Result.Page > 1
        BtnSkinLibNext.IsEnabled = Result.Page < SkinLibPages
        If Result.Items.Count = 0 Then
            LabSkinLibStatus.Text = "这个关键词没有搜到皮肤"
            Return
        End If
        For Each Item In Result.Items
            PanSkinLibList.Children.Add(BuildSkinLibraryCell(Item))
        Next
        AnimateCellsIn(PanSkinLibList, "PageLaunchRight SkinCells") '格子也走同一套动画体系
        '4 个线程分头下载缩略图（每张约 4KB），拿到一张就点亮一个格子
        For Worker As Integer = 0 To 3
            Dim Start = Worker
            RunInNewThread(
            Sub()
                For i = Start To Result.Items.Count - 1 Step 4
                    If Token <> SkinLibToken Then Exit For
                    Try
                        Dim LocalPath = SkinLibrary.Download(Result.Items(i))
                        '解码可以放在后台，拼图（DrawingVisual）必须回 UI 线程
                        Dim Tex = SkinLibrary.LoadTexture(LocalPath)
                        Dim IsSlimItem = Result.Items(i).IsSlim
                        Dim Index = i
                        RunInUi(
                        Sub()
                            If Token <> SkinLibToken Then Return
                            If Index >= PanSkinLibList.Children.Count Then Return
                            Dim Cell = TryCast(PanSkinLibList.Children(Index), Border)
                            If Cell Is Nothing Then Return
                            Dim Img = TryCast(Cell.Tag, Image)
                            If Img IsNot Nothing Then Img.Source = SkinLibrary.BuildPreview(Tex, SkinPreviewScale(), IsSlimItem)
                        End Sub)
                    Catch ex As Exception
                        '单张失败就留空，不影响其它
                    End Try
                Next
            End Sub, "SkinLibrary Thumb")
        Next
    End Sub

    ''' <summary>
    ''' 皮肤格子按当前动画体系逐个入场：错落节奏、位移距离、缓动强度都由
    ''' 「动画速度」「动画细节」「高级动画」决定；低细节只做一次集体淡入。
    ''' </summary>
    Private Sub AnimateCellsIn(Target As Panel, Group As String)
        If Target Is Nothing OrElse Target.Children.Count = 0 Then Return
        AniStop(Group)
        Dim Advanced As Boolean = AniAdvanced
        Dim Level As Integer = If(Advanced, AniAdvancedLevel, AniDetail)
        Dim IsLow As Boolean = Level = 0
        Dim StepTime As Integer = If(Advanced, AniAdvancedPick(4, 9, 14), If(IsLow, 0, If(Level = 2, 7, 4)))
        Dim FadeTime As Integer = If(Advanced, AniAdvancedPick(160, 220, 280), If(IsLow, 90, 130))
        Dim MoveTime As Integer = If(Advanced, AniAdvancedPick(300, 380, 460), 260)
        Dim Rise As Double = If(Advanced, AniAdvancedPick(4.0, 8.0, 12.0), If(Level = 2, 12.0, 8.0))
        Dim MaxDelay As Integer = If(Advanced, AniAdvancedPick(120, 240, 360), If(Level = 2, 200, 140))
        Dim Ease As AniEase = New AniEaseOutBack(If(Level = 2, AniEasePower.Middle, AniEasePower.Weak))

        Dim AniList As New List(Of AniData)
        Dim Delay As Integer = 0
        For Each ChildEl In Target.Children
            Dim Cell = TryCast(ChildEl, FrameworkElement)
            If Cell Is Nothing Then Continue For
            Cell.Opacity = 0
            If IsLow Then
                '低细节：不位移、不错落，一次淡入
                Cell.RenderTransform = Nothing
                AniList.Add(AaOpacity(Cell, 1, FadeTime, 0, New AniEaseOutFluent(AniEasePower.Weak)))
            Else
                Cell.RenderTransform = New TranslateTransform(0, -Rise)
                AniList.Add(AaOpacity(Cell, 1, FadeTime, Delay, New AniEaseOutFluent(AniEasePower.Weak)))
                AniList.Add(AaTranslateY(Cell, Rise, MoveTime, Delay, Ease))
                Delay = Math.Min(Delay + StepTime, MaxDelay)
            End If
        Next
        AniStart(AniList, Group)
    End Sub

    ''' <summary>生成一个皮肤格子（预览图 + 名称，悬停高亮，点击应用）。</summary>
    Private Function BuildSkinLibraryCell(Item As SkinLibraryItem) As Border
        Dim Img As New Image With {.Height = 94, .Margin = New Thickness(0, 8, 0, 0), .Stretch = Stretch.Uniform}
        RenderOptions.SetBitmapScalingMode(Img, BitmapScalingMode.NearestNeighbor)
        Dim Name As New TextBlock With {
            .Text = Item.Name, .FontSize = 11, .Margin = New Thickness(5, 4, 5, 0),
            .TextAlignment = TextAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = ResBrush("ColorBrushGray3")}
        Dim Stack As New StackPanel
        Stack.Children.Add(Img)
        Stack.Children.Add(Name)
        Dim Cell As New Border With {
            .Width = 88, .Height = 124, .Margin = New Thickness(0, 0, 10, 10),
            .CornerRadius = New CornerRadius(6), .Cursor = Cursors.Hand,
            .BorderThickness = New Thickness(1),
            .BorderBrush = ResBrush("ColorBrushGray5"),
            .Tag = Img,
            .ToolTip = Item.Name & If(String.IsNullOrEmpty(Item.Author), "", " · " & Item.Author)}
        Cell.Child = Stack
        AddHandler Cell.MouseEnter, Sub() Cell.BorderBrush = ResBrush("ColorBrush3")
        AddHandler Cell.MouseLeave, Sub() Cell.BorderBrush = ResBrush("ColorBrushGray5")
        AddHandler Cell.MouseLeftButtonUp, Sub() ApplyLibrarySkin(Item)
        Return Cell
    End Function

    ''' <summary>把皮肤库状态行恢复成「共 N 个皮肤」，用于临时提示之后复位。</summary>
    Private Sub ResetSkinLibStatus()
        If LabSkinLibStatus Is Nothing OrElse String.IsNullOrEmpty(SkinLibStatusText) Then Return
        LabSkinLibStatus.Text = SkinLibStatusText
    End Sub

    ''' <summary>选中皮肤库里的某一款：下载 → 走本地导入流程（含同步与模组安装提示）。</summary>
    Private Sub ApplyLibrarySkin(Item As SkinLibraryItem)
        LabSkinLibStatus.Text = $"正在下载「{Item.Name}」…"
        RunInNewThread(
        Sub()
            Try
                Dim CachePath = SkinLibrary.Download(Item)
                Dim TempFolder = SkinLibrary.CacheFolder & "_import\"
                DirectoryUtils.Create(TempFolder)
                Dim TempPath = TempFolder & SkinLibrary.SafeName(Item) & ".png"
                File.Copy(CachePath, TempPath, True)
                RunInUi(
                Sub()
                    '只做导入，皮肤库卡片继续留在下面，方便接着挑
                    ImportSkinFlow(TempPath)
                    ResetSkinLibStatus()
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    ResetSkinLibStatus()
                    Hint("下载皮肤失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "SkinLibrary Apply")
    End Sub

    ''' <summary>取主题画刷；找不到就返回透明，避免抛异常。</summary>
    Private Function ResBrush(Key As String) As Brush
        Dim B = TryCast(Application.Current.TryFindResource(Key), Brush)
        Return If(B, Brushes.Transparent)
    End Function

#End Region


#Region "已有皮肤"

    ''' <summary>把启动器里已导入的皮肤横向排开，点一下切换。</summary>
    Private Sub RefreshSkinLocalList()
        If PanSkinLocal Is Nothing OrElse PanSkinLocalBox Is Nothing Then Return
        PanSkinLocal.Children.Clear()
        Dim Folder = SkinHelper.LauncherSkinFolder
        If Not Directory.Exists(Folder) Then
            PanSkinLocalBox.Visibility = Visibility.Collapsed
            Return
        End If
        Dim Current = Settings.Get(Of String)("SkinLastFile")
        Dim AllFiles = Directory.GetFiles(Folder, "*.png").ToList()
        '按用户拖拽保存的顺序排；没记录过的排在后面，新的在前
        Dim OrderList As New List(Of String)
        Dim OrderRaw = Settings.Get(Of String)("SkinOrder")
        If Not String.IsNullOrEmpty(OrderRaw) Then OrderList.AddRange(OrderRaw.Split("|"c))
        Dim Files As New List(Of String)
        For Each OrderName In OrderList
            Dim Full = Path.Combine(Folder, OrderName)
            If AllFiles.Contains(Full) AndAlso Not Files.Contains(Full) Then Files.Add(Full)
        Next
        For Each F In AllFiles.OrderByDescending(Function(OneFile) File.GetLastWriteTime(OneFile))
            If Not Files.Contains(F) Then Files.Add(F)
        Next
        If Files.Count > 40 Then Files = Files.Take(40).ToList()
        If Files.Count = 0 Then
            PanSkinLocalBox.Visibility = Visibility.Collapsed
            Return
        End If
        For Each FilePath In Files
            Dim IsCurrent As Boolean = String.Equals(FilePath, Current, StringComparison.OrdinalIgnoreCase)
            PanSkinLocal.Children.Add(BuildLocalSkinCell(FilePath, IsCurrent))
        Next
        PanSkinLocalBox.Visibility = Visibility.Visible
        AnimateCellsIn(PanSkinLocal, "PageLaunchRight SkinLocalCells")
    End Sub

    '衣橱管理模式：点格子 = 删除
    Private SkinManageMode As Boolean = False

    Private Sub BtnSkinManage_Click(sender As Object, e As EventArgs) Handles BtnSkinManage.Click
        SkinManageMode = Not SkinManageMode
        BtnSkinManage.Text = If(SkinManageMode, "完成", "管理")
        LabSkinLocalHint.Text = If(SkinManageMode, "管理皮肤：点一下删除（会再确认一次）", "已有皮肤（点一下切换）")
        RefreshSkinLocalList()
    End Sub

    '拖拽排序状态
    Private _SkinDragCell As Border
    Private _SkinDragStartX As Double
    Private _SkinDragActive As Boolean
    Private _SkinDragMoved As Boolean

    Private Sub LocalSkinCell_MouseDown(sender As Object, e As MouseButtonEventArgs)
        Dim Cell = TryCast(sender, Border)
        If Cell Is Nothing OrElse e.ChangedButton <> MouseButton.Left Then Return
        If SkinManageMode Then Return
        _SkinDragCell = Cell
        _SkinDragStartX = e.GetPosition(PanSkinLocal).X
        _SkinDragActive = True
        _SkinDragMoved = False
        Cell.CaptureMouse()
    End Sub

    Private Sub LocalSkinCell_MouseMove(sender As Object, e As MouseEventArgs)
        If SkinManageMode Then Return
        If Not _SkinDragActive OrElse _SkinDragCell Is Nothing Then Return
        If e.LeftButton <> MouseButtonState.Pressed Then Return
        Dim X = e.GetPosition(PanSkinLocal).X
        If Not _SkinDragMoved AndAlso Math.Abs(X - _SkinDragStartX) < 6 Then Return
        _SkinDragMoved = True
        _SkinDragCell.Opacity = 0.75
        '按光标位置实时换位：数出光标左边有几个格子，就是它的新下标
        Dim Target As Integer = 0
        For Each ChildEl In PanSkinLocal.Children
            Dim El = TryCast(ChildEl, FrameworkElement)
            If El Is Nothing OrElse El Is _SkinDragCell Then Continue For
            Dim Left = El.TranslatePoint(New Point(0, 0), PanSkinLocal).X
            If X > Left + El.ActualWidth / 2 Then Target += 1
        Next
        Dim Current = PanSkinLocal.Children.IndexOf(_SkinDragCell)
        If Current < 0 OrElse Target = Current Then Return
        PanSkinLocal.Children.Remove(_SkinDragCell)
        PanSkinLocal.Children.Insert(Math.Min(Target, PanSkinLocal.Children.Count), _SkinDragCell)
    End Sub

    Private Sub LocalSkinCell_MouseUp(sender As Object, e As MouseButtonEventArgs)
        If _SkinDragCell Is Nothing Then Return
        If SkinManageMode Then
            _SkinDragActive = False
            _SkinDragCell = Nothing
            Return
        End If
        Dim Cell = _SkinDragCell
        Dim Moved = _SkinDragMoved
        _SkinDragActive = False
        _SkinDragCell = Nothing
        _SkinDragMoved = False
        Try
            Cell.ReleaseMouseCapture()
        Catch ex As Exception
            '忽略
        End Try
        Cell.Opacity = 1
        If Moved Then
            SaveSkinOrder()
        Else
            ApplyLocalSkin(CStr(Cell.Tag))
        End If
    End Sub

    ''' <summary>把当前横排顺序写进设置，下次打开还按这个顺序排。</summary>
    Private Sub SaveSkinOrder()
        If PanSkinLocal Is Nothing Then Return
        Dim Names As New List(Of String)
        For Each ChildEl In PanSkinLocal.Children
            Dim Cell = TryCast(ChildEl, Border)
            If Cell Is Nothing OrElse Cell.Tag Is Nothing Then Continue For
            Names.Add(Path.GetFileName(CStr(Cell.Tag)))
        Next
        Settings.Set("SkinOrder", String.Join("|", Names))
    End Sub

    ''' <summary>删除某个已有皮肤（右键菜单）；删的是当前皮肤时顺便清空选择。</summary>
    Private Sub DeleteLocalSkin(FilePath As String)
        If String.IsNullOrEmpty(FilePath) OrElse Not File.Exists(FilePath) Then Return
        If MyMsgBox("要删除皮肤「" & SkinDisplayName(FilePath) & "」吗？" & vbCrLf & "文件会被永久删除，无法恢复。",
                    "删除皮肤", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            File.Delete(FilePath)
        Catch ex As Exception
            Hint("删除失败：" & ex.Message, HintType.Red)
            Return
        End Try
        If String.Equals(Settings.Get(Of String)("SkinLastFile"), FilePath, StringComparison.OrdinalIgnoreCase) Then
            Settings.Set("SkinLastFile", "")
            If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.ClearLocalSkin()
        End If
        SaveSkinOrder()
        RefreshSkinUI()
        Hint("已删除皮肤", HintType.Green, False)
    End Sub


    ''' <summary>文件名转成好看的名字：去掉皮肤库导入时追加的 -{id} 后缀。</summary>
    Private Function SkinDisplayName(FilePath As String) As String
        Dim Name = Path.GetFileNameWithoutExtension(FilePath)
        Dim Dash = Name.LastIndexOf("-"c)
        If Dash > 1 AndAlso Name.Length - Dash - 1 >= 8 Then
            Dim AllOk As Boolean = True
            For i = Dash + 1 To Name.Length - 1
                Dim C = Name(i)
                If Not ((C >= "0"c AndAlso C <= "9"c) OrElse (C >= "a"c AndAlso C <= "z"c)) Then
                    AllOk = False
                    Exit For
                End If
            Next
            If AllOk Then Name = Name.Substring(0, Dash)
        End If
        Return Name
    End Function

    ''' <summary>已有皮肤的一个小格子（脸部缩略图 + 名称，选中项高亮）。</summary>
    Private Function BuildLocalSkinCell(FilePath As String, IsCurrent As Boolean) As Border
        Dim Manage As Boolean = SkinManageMode
        Dim Face As New Image With {
            .Width = 44, .Height = 44, .Margin = New Thickness(6, 6, 6, 0),
            .Stretch = Stretch.Uniform}
        RenderOptions.SetBitmapScalingMode(Face, BitmapScalingMode.NearestNeighbor)
        Try
            Face.Source = SkinLibrary.LoadFace(FilePath)
        Catch ex As Exception
            Logger.Warn(ex, "加载皮肤缩略图失败：" & FilePath)
        End Try
        Dim Name As New TextBlock With {
            .Text = If(Manage, "删除", SkinDisplayName(FilePath)),
            .FontSize = 10, .Margin = New Thickness(3, 3, 3, 0), .MaxWidth = 56,
            .TextAlignment = TextAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = If(Manage, ResBrush("ColorBrushRedBack"), If(IsCurrent, ResBrush("ColorBrush3"), ResBrush("ColorBrushGray3")))}
        Dim Stack As New StackPanel
        Stack.Children.Add(Face)
        Stack.Children.Add(Name)
        Dim Cell As New Border With {
            .Width = 64, .Height = 76, .Margin = New Thickness(0, 0, 8, 0),
            .CornerRadius = New CornerRadius(6), .Cursor = Cursors.Hand,
            .Background = Brushes.Transparent,
            .BorderThickness = New Thickness(If(Manage OrElse IsCurrent, 2, 1)),
            .BorderBrush = If(Manage, ResBrush("ColorBrushRedBack"), If(IsCurrent, ResBrush("ColorBrush3"), ResBrush("ColorBrushGray5"))),
            .Tag = FilePath,
            .ToolTip = If(Manage, "点一下删除这个皮肤",
                          SkinDisplayName(FilePath) & vbCrLf & "左键切换 · 按住拖动排序")}
        Cell.Child = Stack
        If Manage Then
            '管理模式：整格就是个删除按钮，不参与拖动
            AddHandler Cell.MouseEnter, Sub() Cell.BorderBrush = ResBrush("ColorBrushRedBack")
            AddHandler Cell.MouseLeftButtonUp, Sub() DeleteLocalSkin(FilePath)
        Else
            AddHandler Cell.MouseEnter, Sub() If Not IsCurrent Then Cell.BorderBrush = ResBrush("ColorBrush3")
            AddHandler Cell.MouseLeave, Sub() If Not IsCurrent Then Cell.BorderBrush = ResBrush("ColorBrushGray5")
            AddHandler Cell.MouseLeftButtonDown, AddressOf LocalSkinCell_MouseDown
            AddHandler Cell.MouseMove, AddressOf LocalSkinCell_MouseMove
            AddHandler Cell.MouseLeftButtonUp, AddressOf LocalSkinCell_MouseUp
        End If
        Return Cell
    End Function

    ''' <summary>切换到某个已有皮肤：记住它、按需同步到游戏、刷新预览与左侧头像。</summary>
    Private Sub ApplyLocalSkin(FilePath As String)
        If String.IsNullOrEmpty(FilePath) OrElse Not File.Exists(FilePath) Then Return
        Settings.Set("SkinLastFile", FilePath)
        Dim Instance = SkinHelper.CurrentInstance()
        If CheckSkinSync.Checked AndAlso Instance IsNot Nothing AndAlso SkinHelper.LoaderOf(Instance) <> "" Then
            Try
                SkinHelper.SyncSkinToGame(FilePath, Instance)
            Catch ex As Exception
                Logger.Warn(ex, "切换皮肤时同步失败")
            End Try
        End If
        If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshLocalSkin(FilePath)
        RefreshSkinUI()
    End Sub

#End Region

End Class
