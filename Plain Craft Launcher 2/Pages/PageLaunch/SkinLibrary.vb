Imports System.IO
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports Newtonsoft.Json.Linq

''' <summary>皮肤库中的一条皮肤。</summary>
Public Class SkinLibraryItem
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property Author As String = ""
    Public Property Model As String = "classic"
    Public Property SkinUrl As String = ""
    Public Property PageUrl As String = ""
    Public Property Views As Integer
    Public Property Likes As Integer
    ''' <summary>是否为 3 像素宽的细手臂模型。</summary>
    Public ReadOnly Property IsSlim As Boolean
        Get
            Return String.Equals(Model, "slim", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property
End Class

''' <summary>一页皮肤库结果。</summary>
Public Class SkinLibraryResult
    Public Property Items As New List(Of SkinLibraryItem)
    Public Property Page As Integer = 1
    Public Property Pages As Integer = 1
    Public Property Total As Integer
    ''' <summary>本次数据是否来自本地缓存（网络失败时的降级）。</summary>
    Public Property FromCache As Boolean
End Class

''' <summary>
''' 皮肤库：从 photo2skin.com（app.photo2skin.com）的公开接口拉取皮肤列表与原始 PNG。
''' 接口：GET /api/library/skins?sort=newest|views|likes|downloads&amp;page=N&amp;q=关键词
''' 单条皮肤的原始贴图：https://app.photo2skin.com/api/library/skins/{id}/skin.png
''' </summary>
Public Module SkinLibrary

    Public Const SiteBase As String = "https://app.photo2skin.com"
    Private Const ApiList As String = SiteBase & "/api/library/skins"

    ''' <summary>皮肤源：photo2skin（0）/ LittleSkin（1）。</summary>
    Public Const SourcePhoto2Skin As Integer = 0
    Public Const SourceLittleSkin As Integer = 1
    Public Const LittleSkinBase As String = "https://littleskin.cn"

    Public Const SortNewest As String = "newest"
    Public Const SortViews As String = "views"
    Public Const SortLikes As String = "likes"
    Public Const SortDownloads As String = "downloads"

    ''' <summary>缩略图缓存目录（按皮肤 ID 存原始 PNG，避免重复下载）。</summary>
    Public ReadOnly Property CacheFolder As String = Paths.Base & "PCL\Skins\_library\"

    ''' <summary>按皮肤源拉取一页皮肤列表。</summary>
    Public Function Fetch(Source As Integer, Sort As String, Page As Integer, Optional Query As String = "") As SkinLibraryResult
        If Source = SourceLittleSkin Then Return FetchLittleSkin(Sort, Page, Query)
        Return FetchPhoto2Skin(Sort, Page, Query)
    End Function

    ''' <summary>photo2skin：拉取一页皮肤列表。</summary>
    Private Function FetchPhoto2Skin(Sort As String, Page As Integer, Optional Query As String = "") As SkinLibraryResult
        Dim RealSort = If(String.IsNullOrWhiteSpace(Sort), SortNewest, Sort)
        Dim Url = $"{ApiList}?sort={RealSort}&page={Math.Max(1, Page)}"
        If Not String.IsNullOrWhiteSpace(Query) Then Url &= "&q=" & Uri.EscapeDataString(Query.Trim())
        Dim Raw As String
        Dim CachePath = CacheFile(SourcePhoto2Skin, RealSort, Page, Query)
        Dim FromCache As Boolean = False
        Try
            '先快试一次：网络正常时 1~2 秒就能拿到
            Raw = NetRequestByClient(Url, Timeout:=12000, RequireJson:=True, SimulateBrowserHeaders:=True)
            If String.IsNullOrWhiteSpace(Raw) Then Throw New Exception("服务器没有返回内容")
            Try
                DirectoryUtils.Create(CacheFolder)
                File.WriteAllText(CachePath, Raw)
            Catch ex As Exception
                '写缓存失败不影响本次显示
            End Try
        Catch ex As Exception
            If File.Exists(CachePath) Then
                '有缓存就立刻降级，不让用户干等重试
                Raw = File.ReadAllText(CachePath)
                FromCache = True
                Logger.Warn(ex, "皮肤库拉取失败，改用本地缓存：" & CachePath)
            Else
                '没缓存才慢重试一次（10s + 30s），还失败就报错
                Raw = NetRequestByClientRetry(Url, RequireJson:=True, SimulateBrowserHeaders:=True)
                Try
                    DirectoryUtils.Create(CacheFolder)
                    File.WriteAllText(CachePath, Raw)
                Catch ex2 As Exception
                    '忽略
                End Try
            End If
        End Try
        Dim Json = JObject.Parse(Raw)
        Dim Result As New SkinLibraryResult With {.FromCache = FromCache}
        Result.Page = If(Json("page") Is Nothing, Page, CInt(Json("page")))
        Result.Pages = If(Json("pages") Is Nothing, 1, Math.Max(1, CInt(Json("pages"))))
        Result.Total = If(Json("total") Is Nothing, 0, CInt(Json("total")))
        Dim Nodes = TryCast(Json("skins"), JArray)
        If Nodes IsNot Nothing Then
            For Each Node As JToken In Nodes
                Dim Item As New SkinLibraryItem
                Item.Id = Node("id")?.ToString()
                Item.Name = Node("name")?.ToString()
                Item.Model = Node("model")?.ToString()
                Item.SkinUrl = Node("skin_png_url")?.ToString()
                Item.PageUrl = Node("page_url")?.ToString()
                Item.Views = If(Node("views") Is Nothing, 0, CInt(Node("views")))
                Item.Likes = If(Node("likes") Is Nothing, 0, CInt(Node("likes")))
                'author 有时是对象、有时是纯字符串，别直接索引
                Item.Author = AuthorNameOf(Node("author"))
                If String.IsNullOrEmpty(Item.SkinUrl) Then
                    Item.SkinUrl = $"{SiteBase}/api/library/skins/{Item.Id}/skin.png"
                End If
                If Not String.IsNullOrEmpty(Item.Id) AndAlso Not String.IsNullOrEmpty(Item.Name) Then Result.Items.Add(Item)
            Next
        End If
        Return Result
    End Function

    ''' <summary>
    ''' LittleSkin（Blessing Skin）：列表用 /skinlib/list，原始贴图用 /raw/{tid}（PNG）。
    ''' 只有「最新 time」「点赞 likes」两种排序，且不返回总数，靠 next_page_url 判断还有没有下一页。
    ''' </summary>
    Private Function FetchLittleSkin(Sort As String, Page As Integer, Optional Query As String = "") As SkinLibraryResult
        Dim RealSort = If(String.Equals(Sort, SortLikes, StringComparison.OrdinalIgnoreCase), "likes", "time")
        Dim Url = $"{LittleSkinBase}/skinlib/list?filter=skin&sort={RealSort}&page={Math.Max(1, Page)}"
        If Not String.IsNullOrWhiteSpace(Query) Then Url &= "&keyword=" & Uri.EscapeDataString(Query.Trim())
        Dim CachePath = CacheFile(SourceLittleSkin, RealSort, Page, Query)
        Dim Raw As String
        Dim FromCache As Boolean = False
        Try
            Raw = NetRequestByClient(Url, Timeout:=12000, RequireJson:=True, SimulateBrowserHeaders:=True)
            If String.IsNullOrWhiteSpace(Raw) Then Throw New Exception("服务器没有返回内容")
            Try
                DirectoryUtils.Create(CacheFolder)
                File.WriteAllText(CachePath, Raw)
            Catch ex As Exception
                '写缓存失败不影响显示
            End Try
        Catch ex As Exception
            If File.Exists(CachePath) Then
                Raw = File.ReadAllText(CachePath)
                FromCache = True
                Logger.Warn(ex, "LittleSkin 拉取失败，改用本地缓存：" & CachePath)
            Else
                Throw
            End If
        End Try
        Dim Json = JObject.Parse(Raw)
        Dim Result As New SkinLibraryResult With {.FromCache = FromCache, .Page = Page}
        Dim Nodes = TryCast(Json("data"), JArray)
        If Nodes IsNot Nothing Then
            For Each Node As JToken In Nodes
                Dim Tid = Node("tid")?.ToString()
                If String.IsNullOrEmpty(Tid) Then Continue For
                Dim Item As New SkinLibraryItem
                Item.Id = "ls" & Tid
                Item.Name = Node("name")?.ToString()
                Item.Model = If(String.Equals(Node("type")?.ToString(), "alex", StringComparison.OrdinalIgnoreCase), "slim", "classic")
                Item.SkinUrl = $"{LittleSkinBase}/raw/{Tid}"
                Item.PageUrl = $"{LittleSkinBase}/skinlib/show/{Tid}"
                Item.Author = Node("nickname")?.ToString()
                Item.Likes = If(Node("likes") Is Nothing, 0, CInt(Node("likes")))
                Result.Items.Add(Item)
            Next
        End If
        '返回里有 next_page_url 就说明还有下一页
        Dim NextUrl = Json("next_page_url")
        Result.Pages = If(NextUrl Is Nothing OrElse NextUrl.Type = JTokenType.Null, Page, Page + 1)
        Result.Total = 0
        Return Result
    End Function

    ''' <summary>兼容 author 为对象或字符串两种形态。</summary>
    Private Function AuthorNameOf(Node As JToken) As String
        If Node Is Nothing Then Return ""
        If Node.Type = JTokenType.Object Then
            Dim Display = Node("display_name")
            If Display IsNot Nothing AndAlso Display.Type <> JTokenType.Null Then Return Display.ToString()
            Dim UserName = Node("username")
            If UserName IsNot Nothing AndAlso UserName.Type <> JTokenType.Null Then Return UserName.ToString()
            Return ""
        End If
        If Node.Type = JTokenType.Null Then Return ""
        Return Node.ToString()
    End Function

    ''' <summary>列表缓存文件名（按排序 / 页码 / 关键词区分）。</summary>
    Private Function CacheFile(Source As Integer, Sort As String, Page As Integer, Query As String) As String
        Dim Key = If(Source = SourceLittleSkin, "ls_", "p2s_") & Sort & "_" & Page & "_" & If(String.IsNullOrWhiteSpace(Query), "all", Query.Trim())
        For Each C As Char In Path.GetInvalidFileNameChars()
            Key = Key.Replace(C, "_"c)
        Next
        Return CacheFolder & "list_" & Key & ".json"
    End Function

    ''' <summary>下载某条皮肤的原始 PNG 到缓存目录，返回本地路径。</summary>
    Public Function Download(Item As SkinLibraryItem) As String
        If Item Is Nothing OrElse String.IsNullOrEmpty(Item.SkinUrl) Then Throw New Exception("皮肤信息不完整")
        DirectoryUtils.Create(CacheFolder)
        Dim Target = CacheFolder & Item.Id & ".png"
        If File.Exists(Target) AndAlso New FileInfo(Target).Length > 200 Then Return Target
        NetDownloadByClient(Item.SkinUrl, Target)
        If Not File.Exists(Target) Then Throw New Exception("下载失败，未得到文件")
        Return Target
    End Function

    ''' <summary>把皮肤文件读成可跨线程使用的位图。</summary>
    Public Function LoadTexture(Path As String) As BitmapSource
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

    ''' <summary>取脸层（8×8 像素块）做小缩略图，放大交给 Image 的最近邻缩放。</summary>
    Public Function LoadFace(Path As String) As ImageSource
        Dim Texture = LoadTexture(Path)
        If Texture Is Nothing Then Return Nothing
        Dim Scale = Math.Max(1, Texture.PixelWidth \ 64)
        Dim Size = 8 * Scale
        If Texture.PixelWidth < 8 * Scale OrElse Texture.PixelHeight < 8 * Scale Then Return Texture
        Dim Face As New CroppedBitmap(Texture, New Int32Rect(8 * Scale, 8 * Scale, Size, Size))
        Face.Freeze()
        Return Face
    End Function

    ''' <summary>用于文件名的安全名称（去掉非法字符并截断）。</summary>
    Public Function SafeName(Item As SkinLibraryItem) As String
        Dim Name = If(Item.Name, Item.Id)
        For Each C As Char In Path.GetInvalidFileNameChars()
            Name = Name.Replace(C, "_"c)
        Next
        Name = Name.Trim().TrimEnd("."c)
        If Name.Length > 40 Then Name = Name.Substring(0, 40)
        Return If(String.IsNullOrEmpty(Name), Item.Id, Name) & "-" & Item.Id
    End Function

    ''' <summary>
    ''' 用 64×64 的原始贴图拼出一张 16×32 单位的正视图（头 + 身体 + 四肢，含外层）。
    ''' 注意：内部使用 DrawingVisual / RenderTargetBitmap，**必须在 UI 线程调用**。
    ''' </summary>
    Public Function BuildPreview(Texture As BitmapSource, Scale As Integer, IsSlim As Boolean) As ImageSource
        If Texture Is Nothing Then Return Nothing
        Dim U = Math.Max(1, Scale)
        Dim W = 16 * U, H = 32 * U
        Dim ArmW = If(IsSlim, 3, 4)
        '贴图分辨率：64×64 为 1，128×128（高清皮肤）为 2，坐标要一起缩放
        Dim S = Math.Max(1, Texture.PixelWidth \ 64)
        Dim Legacy = Texture.PixelHeight < 64 * S '64×32 的老格式：左右肢体共用一张贴图

        Dim Visual As New DrawingVisual()
        RenderOptions.SetBitmapScalingMode(Visual, BitmapScalingMode.NearestNeighbor)
        Using dc = Visual.RenderOpen()
            '底层：腿 → 身体 → 手臂 → 头
            DrawPart(dc, Texture, 4 * S, 20 * S, 4 * S, 12 * S, 4 * U, 20 * U, U)
            DrawPart(dc, Texture, If(Legacy, 4, 20) * S, If(Legacy, 20, 52) * S, 4 * S, 12 * S, 8 * U, 20 * U, U, Legacy)
            DrawPart(dc, Texture, 20 * S, 20 * S, 8 * S, 12 * S, 4 * U, 8 * U, U)
            DrawPart(dc, Texture, 44 * S, 20 * S, ArmW * S, 12 * S, (4 - ArmW) * U, 8 * U, U)
            DrawPart(dc, Texture, If(Legacy, 44, 36) * S, If(Legacy, 20, 52) * S, ArmW * S, 12 * S, 12 * U, 8 * U, U, Legacy)
            DrawPart(dc, Texture, 8 * S, 8 * S, 8 * S, 8 * S, 4 * U, 0, U)
            If Not Legacy Then
                '外层：裤腿 → 衣服 → 袖子 → 帽子
                DrawPart(dc, Texture, 4 * S, 36 * S, 4 * S, 12 * S, 4 * U, 20 * U, U)
                DrawPart(dc, Texture, 4 * S, 52 * S, 4 * S, 12 * S, 8 * U, 20 * U, U)
                DrawPart(dc, Texture, 20 * S, 36 * S, 8 * S, 12 * S, 4 * U, 8 * U, U)
                DrawPart(dc, Texture, 44 * S, 36 * S, ArmW * S, 12 * S, (4 - ArmW) * U, 8 * U, U)
                DrawPart(dc, Texture, 52 * S, 52 * S, ArmW * S, 12 * S, 12 * U, 8 * U, U)
                DrawPart(dc, Texture, 40 * S, 8 * S, 8 * S, 8 * S, 4 * U, 0, U)
            End If
        End Using

        Dim Rtb As New RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32)
        Rtb.Render(Visual)
        Rtb.Freeze()
        Return Rtb
    End Function

    ''' <summary>把贴图上的一个矩形放大后画到目标位置（越界自动跳过）。</summary>
    Private Sub DrawPart(dc As DrawingContext, Texture As BitmapSource,
                         Sx As Integer, Sy As Integer, Sw As Integer, Sh As Integer,
                         Dx As Integer, Dy As Integer, U As Integer,
                         Optional FlipX As Boolean = False)
        If Sx < 0 OrElse Sy < 0 OrElse Sx + Sw > Texture.PixelWidth OrElse Sy + Sh > Texture.PixelHeight Then Return
        Dim Crop As New CroppedBitmap(Texture, New Int32Rect(Sx, Sy, Sw, Sh))
        If FlipX Then
            '老格式没有左肢贴图，用右肢水平翻转
            dc.PushTransform(New ScaleTransform(-1, 1, Dx + Sw * U / 2, 0))
            dc.DrawImage(Crop, New Rect(Dx, Dy, Sw * U, Sh * U))
            dc.Pop()
        Else
            dc.DrawImage(Crop, New Rect(Dx, Dy, Sw * U, Sh * U))
        End If
    End Sub

End Module
