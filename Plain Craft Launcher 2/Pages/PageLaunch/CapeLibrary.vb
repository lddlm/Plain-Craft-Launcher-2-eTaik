Imports System.IO
Imports System.Net
Imports System.Text.RegularExpressions
Imports System.Windows.Media.Imaging

''' <summary>披风库中的一条披风。</summary>
Public Class CapeLibraryItem
    ''' <summary>站点内部的披风标识（Laby 为 32 位 hash，NameMC 为 16 位 id）。</summary>
    Public Property Id As String = ""
    Public Property Name As String = ""
    ''' <summary>拥有该披风的玩家数量（NameMC 统计，0 表示未知）。</summary>
    Public Property Owners As Integer
    ''' <summary>网格里显示的渲染图。</summary>
    Public Property PreviewUrl As String = ""
    ''' <summary>该披风在来源网站上的详情页。</summary>
    Public Property PageUrl As String = ""
    ''' <summary>原始 64×32 披风贴图；只有 Laby 提供，NameMC 为空。</summary>
    Public Property TextureUrl As String = ""
    Public Property Source As Integer
    ''' <summary>跨源匹配用的键：就是清洗后的披风名。</summary>
    Public ReadOnly Property Key As String
        Get
            Return If(String.IsNullOrEmpty(Name), Id, Name).Trim()
        End Get
    End Property
End Class

''' <summary>一次披风库查询的结果。</summary>
Public Class CapeLibraryResult
    Public Property Items As New List(Of CapeLibraryItem)
    ''' <summary>是否来自本地缓存（网络失败时的降级）。</summary>
    Public Property FromCache As Boolean
    ''' <summary>是否用了内置清单（两个网站都拦住了 .NET 请求时）。</summary>
    Public Property Builtin As Boolean
End Class

''' <summary>
''' 披风库：从 Laby.net 与 NameMC 的公开披风页抓取官方披风列表。
''' Laby 额外提供 64×32 的原始贴图（/api/v3/texture/{hash}/cape.png），
''' 这是 Quick Skin 唯一能读的格式；NameMC 只有 3D 渲染图，只用于预览，
''' 因此用 NameMC 源时会按名字去 Laby 的列表里补一份贴图地址。
''' </summary>
Public Module CapeLibrary

    Public Const SourceLaby As Integer = 0
    Public Const SourceNameMC As Integer = 1

    Private Const LabyListUrl As String = "https://laby.net/zh/capes"
    Private Const NameMcListUrl As String = "https://zh-cn.namemc.com/capes"
    Private Const LabyTextureBase As String = "https://laby.net/api/v3/texture/"
    Private Const NameMcSite As String = "https://zh-cn.namemc.com"
    ''' <summary>NameMC 渲染 3D 预览时套用的示例玩家皮肤，和它自己列表页里用的是同一个。</summary>
    Private Const NameMcDemoSkin As String = "12b92a9206470fe2"

    ''' <summary>启动器保存披风的目录（也是 Quick Skin 的导入来源）。</summary>
    Public ReadOnly Property CapeFolder As String = Paths.Base & "PCL\Capes\"
    ''' <summary>列表与贴图缓存目录。</summary>
    Public ReadOnly Property CapeCacheFolder As String = CapeFolder & "_library\"

    ''' <summary>按来源拉取全部披风（两个站都是单页，没有分页）。</summary>
    Public Function FetchCapes(Source As Integer) As CapeLibraryResult
        If Source = SourceNameMC Then Return FetchNameMc()
        Return FetchLaby()
    End Function

#Region "Laby.net"

    Private Function FetchLaby() As CapeLibraryResult
        Try
            Dim FromCache As Boolean = False
            Dim Html = FetchHtml(LabyListUrl, CapeCacheFolder & "laby.html", FromCache)
            Dim Result As New CapeLibraryResult With {.FromCache = FromCache}
            Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            '页面里每条披风都有 <img src=".../render/cape/{hash}.png?... alt="名字 Minecraft Cape">
            Dim Matches = Regex.Matches(Html,
                "render/cape/([0-9a-f]{32})\.png[^""]*""[^>]*alt=""([^""]*)""",
                RegexOptions.IgnoreCase)
            For Each M As Match In Matches
                Dim Hash = M.Groups(1).Value.ToLowerInvariant()
                If Not Seen.Add(Hash) Then Continue For
                Dim Item As New CapeLibraryItem With {
                    .Id = Hash,
                    .Name = CleanName(M.Groups(2).Value),
                    .Source = SourceLaby,
                    .PreviewUrl = $"https://laby.net/api/v3/render/cape/{Hash}.png?height=256&width=256",
                    .TextureUrl = LabyTextureBase & Hash & "/cape.png",
                    .PageUrl = "https://laby.net/zh/capes/" & Hash}
                If String.IsNullOrEmpty(Item.Name) Then Item.Name = Hash
                Result.Items.Add(Item)
            Next
            If Result.Items.Count = 0 Then Throw New Exception("没有从 Laby.net 解析到披风，页面结构可能已变化")
            Return Result
        Catch ex As Exception
            '网页被 Cloudflare 校验挡住是常态，直接用内置清单，不打扰用户
            Logger.Warn(ex, "Laby.net 披风列表抓取失败，改用内置清单")
            Return BuildFromBuiltin(SourceLaby)
        End Try
    End Function

#End Region

#Region "NameMC"

    Private Function FetchNameMc() As CapeLibraryResult
        Try
            Dim FromCache As Boolean = False
            Dim Html = FetchHtml(NameMcListUrl, CapeCacheFolder & "namemc.html", FromCache)
            Dim Result As New CapeLibraryResult With {.FromCache = FromCache}
            '每张卡是一个 <div class="col-4 col-md-2"> 块，按块切分再分别取字段最稳
            Dim Blocks = Regex.Split(Html, "<div class=""col-4 col-md-2""")
            Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For i = 1 To Blocks.Length - 1
                Dim Block = Blocks(i)
                Dim IdMatch = Regex.Match(Block, "href=""/cape/([0-9a-f]{16})""")
                If Not IdMatch.Success Then Continue For
                Dim Id = IdMatch.Groups(1).Value.ToLowerInvariant()
                If Not Seen.Add(Id) Then Continue For
                Dim Item As New CapeLibraryItem With {.Id = Id, .Source = SourceNameMC}
                Dim NameMatch = Regex.Match(Block, "card-header[^>]*>([^<]+)</div>")
                Item.Name = If(NameMatch.Success, CleanName(WebUtility.HtmlDecode(NameMatch.Groups(1).Value)), Id)
                Dim ImgMatch = Regex.Match(Block, "data-src=""([^""]+)""")
                If ImgMatch.Success Then
                    Item.PreviewUrl = WebUtility.HtmlDecode(ImgMatch.Groups(1).Value).Replace("&amp;", "&")
                    '列表里给的是 256×256 的小图，换成同尺寸的清晰版本
                    Item.PreviewUrl = Regex.Replace(Item.PreviewUrl, "width=\d+", "width=256")
                    Item.PreviewUrl = Regex.Replace(Item.PreviewUrl, "height=\d+", "height=256")
                End If
                Dim OwnerMatch = Regex.Match(Block, "normal-sm"">([\d,]+)<i")
                If OwnerMatch.Success Then
                    Integer.TryParse(OwnerMatch.Groups(1).Value.Replace(",", ""), Item.Owners)
                End If
                Item.PageUrl = NameMcSite & "/cape/" & Id
                Result.Items.Add(Item)
            Next
            If Result.Items.Count = 0 Then Throw New Exception("没有从 NameMC 解析到披风，可能被网站拦截了")
            FillTextureFromLaby(Result.Items, FromCache)
            Return Result
        Catch ex As Exception
            'NameMC 的 Cloudflare 校验对 .NET 请求基本必有，静默走内置清单
            Logger.Warn(ex, "NameMC 披风列表抓取失败，改用内置清单")
            Return BuildFromBuiltin(SourceNameMC)
        End Try
    End Function

    ''' <summary>
    ''' 内置清单兜底：名称|Laby hash|NameMC id。
    ''' 两个网站都给自己的页面套了 Cloudflare 的浏览器校验，.NET 的请求（PCL 用的就是这个）
    ''' 一律被 403 挡下，只有贴图/渲染图的 CDN 和 API 不受影响，所以列表用这份内置数据。
    ''' 网页哪天能抓通会自动改用网页数据，因此这里只需保证官网新增披风时补一行。
    ''' </summary>
    Private ReadOnly BuiltinCapes As String() = {
        "Migrator|5b37a01fde6a3e075f3bc5694c18e667|8a6cc02cc86e43f1",
        "Pan|a60d608f0cac6ad8c50f2258b6c4db92|62db1a06cba0d59a",
        "Common|32f3de244a170452245ee509491f6f1e|6851f20cfe35bd03",
        "15th Anniversary|e7447a28eb5d03b01dee9f6710937eb2|86a126427a9cf1c9",
        "Vanilla|21c9cf811d476ae1c4e58f3cceace025|3c1a1e7e50fce5f0",
        "Cherry Blossom|b4acb3ba0ca9d89e996928e21fad42c5|e22327fef7324090",
        "Purple Heart|bfd10540b531afbcbec285293d17b358|2a92e051c5df531a",
        "Follower's|719f820d513dc22f5f788a46e8ea3aa7|6dfe9599aa000ffc",
        "Menace|b69d6840faf88dadc57da2633aed0835|d89779ee546fe4f0",
        "Aurora|fb422cd7299861506da9e7df7180a9e5|8724e22a0e7fee60",
        "Copper|5e114e2eff454b5e67dc414b42917403|93fa88bcdcdc9a36",
        "Home|7f4224b74be5756a78973d2621f85566|afc3ba389452cbb1",
        "Mojang Office|9af295341c025102c2d9a51b695bd0c4|d40975b63f693972",
        "Yearn|4f4363ee4fc7b1b299dd644dc27d182e|764e20cd7392bc24",
        "Founder's|d5ee5a6faa064d9abec47bcaa672f2c4|71c819d9c37cb61c",
        "Builder|68a68e1ec73dc4d05a04d80c4bfe27ee|e99ac5f5259571a7",
        "Zombie Horse|ae2f8ed92eecc216d391b756218a55db|07973351ab910408",
        "MCC 15th Year|5ca285a4bba6c769dff12453b8084235|b169480d76031eb0",
        "Twisted|d123de443f66372271b9c4d0efc3087c|d84910543ee2842f",
        "Hero|7efbf66fbd8abafcc8bcd015ebc4cfb9|0ac9c5df08969fc1",
        "Minecraft Experience|1025a532853c7a29518ffaedda0621d2|910674a2d3964724",
        "Moonlight Trail|2ee52d6b17eb353d25e2b6be4ddbb2c5|ff3b90ad33a937ef",
        "MineCon 2016|de4a8ad0267f4fc0f41a732ebcf10ec9|1981aad373fa9754",
        "MineCon 2015|4d1709d6e62c99ec7220e0787df0362e|72ee2cfcefbfc081",
        "MineCon 2013|37cd76a8a0879233398d127099326cb7|0e4cc75a5f8a886d",
        "Crafter|f5fb7dc137835d19e15c9f9fac475af3|fd7c1a3d5fa7a9f1",
        "MineCon 2012|2b7ccdbfd1d89520f335822140d83d52|ebc798c3f7eca2a3",
        "MineCon 2011|00f15c80c9ab3540477210d4e58af337|9349fa25c64ae935",
        "Realms Mapmaker|492d8392bf27b659897752dca6de7778|11a3dcc4d826d0a1",
        "Mojang|6390c54ca1e541a75235dadf23555ed2|cb5dd34bee340182",
        "Mojang Studios|8fb7780634ea6c0729fea7a7e6f50c28|c00df589ebea3ad6",
        "Translator|a944bd437bb6d12822f740c55ee1a3c9|129a4675704fa3b8",
        "Mojira Moderator|172e4e9fd6c0afdf086b8bdb58c0bad5|8dd71c1ee6ec0ae4",
        "Mojang Classic|7c07865bdc148efb0d1eb95829eaf11a|298ae017a64261ad",
        "Cobalt|bd64503808bc49c8d80e327754894d4f|696b6cc29946b968",
        "Scrolls|1044b1f5dfc2626b573d8d74a9307a7f|116bacd62b233157",
        "Turtle|22923c6efb6deee96bd7ecd87086bbb8|8c05ef3c54870d04",
        "Translator (Chinese)|a2b113cab4c553b9b306f25e2d599513|d059f1a18b159eb6",
        "Test|c7091e237e7562e73990a97dda5bc554|123b1c8b4352f76d",
        "Valentine|bfa8cc9d7af57fd856890af7c2bf2a4a|3d528060ab734868",
        "Blueprint|9d591eb362d5ad037b71983ff9481b56|f249694c066c160c",
        "Oxeye|6772ba0b69deda78dd070fbd6d0cf5a4|b78918fa2b4d6189",
        "Birthday|04ddcbd316ec68364238697174fb1c61|aab5a23c7495fc70",
        "Translator (Japanese)|008daa52d7b3e61b686f7cafa3680c0f|aa02d4b62762ff22",
        "Spade|f0b8db3ae2d2857c6d262fcbc2aa4737|7a939dc1a7ad4505",
        "Snowman|6206c2b3261ba9b033aae57e32121a86|5e68fa78bd9df310",
        "Millionth Customer|47d30b5dd95e65e696fc9ff186d8ee33|d3c7ac835b24eb29",
        "dB|e71149cdca1d763faf5d4549edc0620b|77421d9cf72e07e9",
        "Prismarine|b32d8c1671c2936e81ec7e711e2af8e4|88f1509813f4e324"
    }

    ''' <summary>用内置清单构建结果（两个来源共用同一批官方披风）。</summary>
    Private Function BuildFromBuiltin(Source As Integer) As CapeLibraryResult
        Dim Result As New CapeLibraryResult With {.FromCache = True, .Builtin = True}
        For Each Row In BuiltinCapes
            Dim Parts = Row.Split("|"c)
            If Parts.Length < 3 Then Continue For
            Dim Name = Parts(0), LabyId = Parts(1), McId = Parts(2)
            Dim Item As New CapeLibraryItem With {.Name = Name, .Source = Source}
            If Source = SourceNameMC Then
                If String.IsNullOrEmpty(McId) Then Continue For
                Item.Id = McId
                Item.PageUrl = NameMcSite & "/cape/" & McId
                Item.PreviewUrl = NameMcPreviewUrl(McId)
                'NameMC 自己不给贴图，用同名披风的 Laby 那份
                If Not String.IsNullOrEmpty(LabyId) Then Item.TextureUrl = LabyTextureBase & LabyId & "/cape.png"
            Else
                If String.IsNullOrEmpty(LabyId) Then Continue For
                Item.Id = LabyId
                Item.PageUrl = "https://laby.net/zh/capes/" & LabyId
                Item.PreviewUrl = $"https://laby.net/api/v3/render/cape/{LabyId}.png?height=256&width=256"
                Item.TextureUrl = LabyTextureBase & LabyId & "/cape.png"
            End If
            Result.Items.Add(Item)
        Next
        Return Result
    End Function

    ''' <summary>NameMC 的 3D 渲染图地址（用一个固定的示例玩家来穿披风）。</summary>
    Public Function NameMcPreviewUrl(CapeId As String) As String
        Return $"https://s.namemc.com/3d/skin/body.png?id={NameMcDemoSkin}&cape={CapeId}&theta=210&width=256&height=256"
    End Function

    ''' <summary>
    ''' NameMC 不提供原始贴图，用名字去 Laby 的列表里配一份。
    ''' 两边都是同一批官方披风，去掉 Laby 的「 Minecraft Cape」后缀就能对上；
    ''' 配不上就留空，点击时降级成只保存预览图。
    ''' </summary>
    Private Sub FillTextureFromLaby(Items As List(Of CapeLibraryItem), ByRef FromCache As Boolean)
        Try
            Dim LabyItems As List(Of CapeLibraryItem) = Nothing
            '优先读缓存的 Laby 列表，没有再联网
            Dim CachePath = CapeCacheFolder & "laby.html"
            If File.Exists(CachePath) Then
                Try
                    Dim Html = File.ReadAllText(CachePath)
                    LabyItems = ParseLabyItems(Html)
                Catch ex As Exception
                    LabyItems = Nothing
                End Try
            End If
            If LabyItems Is Nothing OrElse LabyItems.Count = 0 Then
                Dim Dummy As Boolean = False
                LabyItems = ParseLabyItems(FetchHtml(LabyListUrl, CachePath, Dummy))
            End If
            If LabyItems Is Nothing OrElse LabyItems.Count = 0 Then Return
            Dim Map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each L In LabyItems
                '同名只保留第一个，避免后面覆盖掉前面的
                If Not Map.ContainsKey(L.Key) Then Map(L.Key) = L.TextureUrl
            Next
            For Each Item In Items
                Dim Tex As String = Nothing
                If Map.TryGetValue(Item.Key, Tex) Then Item.TextureUrl = Tex
            Next
        Catch ex As Exception
            '配不上不影响浏览
            Logger.Warn(ex, "从 Laby 补全 NameMC 披风贴图失败")
        End Try
    End Sub

#End Region

#Region "抓取与解析"

    ''' <summary>取列表 HTML：先联网，失败且有缓存就降级；两样都没有才抛异常。</summary>
    Private Function FetchHtml(Url As String, CachePath As String, ByRef FromCache As Boolean) As String
        Dim Raw As String
        Try
            Dim Headers As String(,) = {
                {"User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"},
                {"Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8"},
                {"Sec-Fetch-Dest", "document"},
                {"Sec-Fetch-Mode", "navigate"},
                {"Sec-Fetch-Site", "none"},
                {"Upgrade-Insecure-Requests", "1"}
            }
            Raw = NetRequestByClient(Url, Timeout:=15000,
                                     Accept:="text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
                                     Headers:=Headers, SimulateBrowserHeaders:=True)
            If String.IsNullOrWhiteSpace(Raw) Then Throw New Exception("服务器没有返回内容")
            'Cloudflare 的校验页也长得很像正常网页，得单独认出来
            If Raw.Contains("Just a moment") OrElse Raw.Contains("cf-browser-verification") Then
                Throw New Exception("被网站的访问校验拦截了")
            End If
            Try
                DirectoryUtils.Create(CapeCacheFolder)
                File.WriteAllText(CachePath, Raw)
            Catch ex As Exception
                '写缓存失败不影响本次显示
            End Try
        Catch ex As Exception
            If File.Exists(CachePath) Then
                Raw = File.ReadAllText(CachePath)
                FromCache = True
                Logger.Warn(ex, "披风库拉取失败，改用本地缓存：" & CachePath)
            Else
                Throw
            End If
        End Try
        Return Raw
    End Function

    ''' <summary>把 Laby 的列表 HTML 解析成条目（供跨源补贴图复用）。</summary>
    Private Function ParseLabyItems(Html As String) As List(Of CapeLibraryItem)
        Dim List As New List(Of CapeLibraryItem)
        Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each M As Match In Regex.Matches(Html, "render/cape/([0-9a-f]{32})\.png[^""]*""[^>]*alt=""([^""]*)""", RegexOptions.IgnoreCase)
            Dim Hash = M.Groups(1).Value.ToLowerInvariant()
            If Not Seen.Add(Hash) Then Continue For
            List.Add(New CapeLibraryItem With {
                .Id = Hash,
                .Name = CleanName(M.Groups(2).Value),
                .Source = SourceLaby,
                .TextureUrl = LabyTextureBase & Hash & "/cape.png"})
        Next
        Return List
    End Function

    ''' <summary>去掉 Laby 名字后面统一带的「 Minecraft Cape」，并做 HTML 反转义。</summary>
    Private Function CleanName(Raw As String) As String
        If String.IsNullOrEmpty(Raw) Then Return ""
        Const Suffix As String = " Minecraft Cape"
        Dim Name = WebUtility.HtmlDecode(Raw).Trim()
        If Name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) Then
            Name = Name.Substring(0, Name.Length - Suffix.Length).Trim()
        End If
        Return Name
    End Function

#End Region

#Region "下载与保存"

    ''' <summary>下载某一款披风的原始 64×32 贴图，返回本地路径。</summary>
    Public Function DownloadCapeTexture(Item As CapeLibraryItem) As String
        If Item Is Nothing Then Throw New Exception("披风信息不完整")
        If String.IsNullOrEmpty(Item.TextureUrl) Then Throw New Exception("这个来源没有提供原始贴图")
        DirectoryUtils.Create(CapeCacheFolder)
        Dim Target = CapeCacheFolder & "tex_" & Item.Id & ".png"
        If File.Exists(Target) AndAlso New FileInfo(Target).Length > 100 AndAlso IsCapeTexture(Target) Then Return Target
        NetDownloadByClient(Item.TextureUrl, Target, SimulateBrowserHeaders:=True)
        If Not File.Exists(Target) Then Throw New Exception("下载失败，未得到文件")
        If Not IsCapeTexture(Target) Then
            File.Delete(Target)
            Throw New Exception("下载到的不是披风贴图")
        End If
        Return Target
    End Function

    ''' <summary>下载渲染图（NameMC 来源只能拿到这个），返回本地路径。</summary>
    Public Function DownloadCapePreview(Item As CapeLibraryItem) As String
        If Item Is Nothing OrElse String.IsNullOrEmpty(Item.PreviewUrl) Then Throw New Exception("披风信息不完整")
        DirectoryUtils.Create(CapeCacheFolder)
        Dim Target = CapeCacheFolder & "prev_" & Item.Source & "_" & Item.Id & ".png"
        If File.Exists(Target) AndAlso New FileInfo(Target).Length > 200 Then Return Target
        NetDownloadByClient(Item.PreviewUrl, Target, SimulateBrowserHeaders:=True)
        If Not File.Exists(Target) Then Throw New Exception("下载失败，未得到文件")
        Return Target
    End Function

    ''' <summary>把贴图复制到启动器披风目录（同名覆盖），返回目标路径。</summary>
    Public Function SaveCapeFile(SourcePath As String, Item As CapeLibraryItem) As String
        DirectoryUtils.Create(CapeFolder)
        Dim Target = CapeFolder & CapeSafeName(Item) & Path.GetExtension(SourcePath)
        File.Copy(SourcePath, Target, True)
        Return Target
    End Function

    ''' <summary>Quick Skin 读取披风的目录，与皮肤目录平级。</summary>
    Public Function QuickSkinCapeFolder(Instance As McInstance) As String
        If Instance Is Nothing Then Return ""
        Return Instance.PathIndie & "quickskin\capes\"
    End Function

    ''' <summary>把披风贴图复制进 Quick Skin，进游戏后在披风菜单里就能选到。</summary>
    Public Sub SyncCapeToQuickSkin(CapePath As String, Item As CapeLibraryItem, Instance As McInstance)
        If Item Is Nothing Then Return
        SyncCapeFileToGame(CapePath, Item.Name, Instance)
    End Sub

    ''' <summary>按给定的披风名把贴图复制进 Quick Skin（文件名就是游戏里显示的名字）。</summary>
    Public Sub SyncCapeFileToGame(CapePath As String, Name As String, Instance As McInstance)
        If String.IsNullOrEmpty(CapePath) OrElse Not File.Exists(CapePath) OrElse Instance Is Nothing Then Return
        Dim Safe = SanitizeName(If(String.IsNullOrEmpty(Name), Path.GetFileNameWithoutExtension(CapePath), Name))
        Dim Dir = QuickSkinCapeFolder(Instance)
        DirectoryUtils.Create(Dir)
        File.Copy(CapePath, Dir & Safe & ".png", True)
        Logger.Info($"披风已复制到 Quick Skin 目录：{Dir & Safe}.png")
    End Sub

    ''' <summary>把 64×32 贴图里的披风正面（1,0 起 10×16）放大成预览图，纯像素最近邻。</summary>
    Public Function BuildCapePreview(Texture As BitmapSource, Scale As Integer) As ImageSource
        If Texture Is Nothing Then Return Nothing
        Dim S = Math.Max(1, Texture.PixelWidth \ 64)
        Dim U = Math.Max(1, Scale)
        If Texture.PixelWidth < 11 * S OrElse Texture.PixelHeight < 16 * S Then Return Texture
        Dim Crop As New CroppedBitmap(Texture, New Int32Rect(1 * S, 0, 10 * S, 16 * S))
        Dim Visual As New DrawingVisual()
        RenderOptions.SetBitmapScalingMode(Visual, BitmapScalingMode.NearestNeighbor)
        Using dc = Visual.RenderOpen()
            dc.DrawImage(Crop, New Rect(0, 0, 10 * U, 16 * U))
        End Using
        Dim Rtb As New RenderTargetBitmap(10 * U, 16 * U, 96, 96, PixelFormats.Pbgra32)
        Rtb.Render(Visual)
        Rtb.Freeze()
        Return Rtb
    End Function

    ''' <summary>校验是不是原版披风贴图尺寸：64×32（1.9+）或 22×17（更早）。</summary>
    Public Function IsCapeTexture(Path As String) As Boolean
        Try
            Using Stream = File.OpenRead(Path)
                Dim Bmp As New BitmapImage()
                Bmp.BeginInit()
                Bmp.CacheOption = BitmapCacheOption.OnLoad
                Bmp.StreamSource = Stream
                Bmp.EndInit()
                Return (Bmp.PixelWidth = 64 AndAlso Bmp.PixelHeight = 32) OrElse
                       (Bmp.PixelWidth = 22 AndAlso Bmp.PixelHeight = 17)
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>把图片读成可跨线程使用的位图。</summary>
    Public Function LoadCapeImage(Path As String) As BitmapSource
        Using Stream = File.OpenRead(Path)
            Dim Bmp As New BitmapImage()
            Bmp.BeginInit()
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.StreamSource = Stream
            Bmp.EndInit()
            Bmp.Freeze()
            Return Bmp
        End Using
    End Function

    ''' <summary>用于文件名的安全名称。</summary>
    Public Function CapeSafeName(Item As CapeLibraryItem) As String
        Dim Safe = SanitizeName(If(String.IsNullOrEmpty(Item.Name), Item.Id, Item.Name))
        Return If(String.IsNullOrEmpty(Safe), Item.Id, Safe)
    End Function

    Private Function SanitizeName(Raw As String) As String
        Dim Name = If(Raw, "").Trim()
        For Each C As Char In Path.GetInvalidFileNameChars()
            Name = Name.Replace(C, "_"c)
        Next
        Name = Name.Trim().TrimEnd("."c)
        If Name.Length > 48 Then Name = Name.Substring(0, 48)
        Return Name
    End Function

#End Region

End Module
