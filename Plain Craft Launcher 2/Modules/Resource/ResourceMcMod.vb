Imports System.Net
Imports System.Text.RegularExpressions

''' <summary>
''' MC 百科（mcmod.cn）的词条检索。
''' 使用 search.mcmod.cn 的服务端渲染搜索页，分别检索模组（filter=1）与整合包（filter=2）。
''' 结果只提供词条信息，点击后用浏览器打开对应的 MC 百科页面。
''' </summary>
Public Class ResourceMcMod

    ''' <summary>
    ''' 搜索页地址。{0} 为关键词，{1} 为分类编号。
    ''' </summary>
    Private Const ApiSearch As String = "https://search.mcmod.cn/s?key={0}&filter={1}&mold="
    Private Const BrowserUserAgent As String =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
    ''' <summary>
    ''' 每个分类最多取多少条结果。
    ''' </summary>
    Private Const MaxPerCategory As Integer = 20
    ''' <summary>
    ''' 一次检索返回的最大结果数。
    ''' </summary>
    Private Const MaxResult As Integer = 40

#Region "检索"

    ''' <summary>
    ''' 检索 MC 百科中的模组与整合包词条。
    ''' </summary>
    ''' <param name="SearchText">搜索关键词。</param>
    ''' <param name="Type">页面类型。Mod 页同时返回模组与整合包。</param>
    Public Shared Function Search(SearchText As String, Type As ResourceTypes) As List(Of ResourceProject)
        Dim Result As New List(Of ResourceProject)
        If String.IsNullOrWhiteSpace(SearchText) Then Return Result
        '分类：0 = 全部，1 = 模组，2 = 整合包
        Dim Categories As New List(Of Integer)
        Select Case Type
            Case ResourceTypes.ModPack
                Categories.Add(2)
            Case ResourceTypes.Mod
                Categories.Add(1)
                Categories.Add(2)
            Case Else
                Categories.Add(0)
        End Select
        Dim Key As String = SearchText.Trim.Lower
        Dim Buckets As New List(Of List(Of ResourceProject))
        For Each Category In Categories
            If Category > Categories.First Then Thread.Sleep(300) '避免连续请求被限流
            Try
                Dim Html = GetPage(SearchText, Category)
                If Html Is Nothing Then Continue For
                Dim PageItems = ParsePage(Html, Category).
                    OrderByDescending(Function(p) If(p.RawName.Lower.Contains(Key), 1, 0)).ToList()
                Buckets.Add(PageItems)
                Logger.Info($"MC 百科分类 {Category} 得到 {PageItems.Count} 个词条")
            Catch ex As Exception
                Logger.Warn(ex, $"MC 百科检索失败（category={Category}）")
            End Try
        Next
        '各分类交错合并，让模组与整合包都能出现在第一屏
        Dim MaxCount As Integer = 0
        For Each Bucket In Buckets
            MaxCount = Math.Max(MaxCount, Bucket.Count)
        Next
        For i = 0 To MaxCount - 1
            For Each Bucket In Buckets
                If i < Bucket.Count Then Result.Add(Bucket(i))
            Next
        Next
        If Result.Count > MaxResult Then Result = Result.Take(MaxResult).ToList()
        Return Result
    End Function

    ''' <summary>
    ''' 获取某个分类的搜索结果页 HTML。
    ''' </summary>
    Private Shared Function GetPage(SearchText As String, Category As Integer) As String
        Dim Url As String = String.Format(ApiSearch, StringUtils.UrlEscape(SearchText), Category)
        Dim Headers As String(,) = {
            {"User-Agent", BrowserUserAgent},
            {"Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"},
            {"Accept-Language", "zh-CN,zh;q=0.9"},
            {"Referer", "https://www.mcmod.cn/"}
        }
        Logger.Info($"开始 MC 百科搜索：{Url}")
        '连续请求时 MC 百科偶发返回空列表，重试两次
        For Each Attempt In {1, 2, 3}
            Dim Html = NetRequestByClient(Url, HttpMethod.Get, Nothing, Nothing,
                                         "text/html,application/xhtml+xml,*/*;q=0.8",
                                         20000, Headers, False, Encoding.UTF8, True)
            If Not String.IsNullOrEmpty(Html) AndAlso Html.Contains("class=""result-item""") Then Return Html
            Logger.Warn($"MC 百科第 {Attempt} 次未取得结果列表（长度 {If(Html, "").Length}）")
            If Attempt < 3 Then Thread.Sleep(500)
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' 解析搜索结果页中的词条列表。
    ''' </summary>
    Private Shared Function ParsePage(Html As String, Category As Integer) As List(Of ResourceProject)
        Dim Result As New List(Of ResourceProject)
        Dim Blocks = Regex.Matches(Html, "<div class=""result-item"">(.*?)<div class=""foot"">", RegexOptions.Singleline)
        Logger.Info($"MC 百科解析到 {Blocks.Count} 个结果块（分类 {Category}）")
        For Each BlockMatch As Match In Blocks
            If Result.Count >= MaxPerCategory Then Exit For
            Dim Block As String = BlockMatch.Groups(1).Value
            '标题与词条地址
            Dim Link = Regex.Match(Block,
                "<a[^>]+href=""(https?://www\.mcmod\.cn/(?:class|modpack)/\d+\.html)""[^>]*>(.*?)</a>",
                RegexOptions.Singleline)
            If Not Link.Success Then Continue For
            Dim Website As String = Link.Groups(1).Value
            Dim Title As String = CleanText(Link.Groups(2).Value, False)
            If String.IsNullOrWhiteSpace(Title) Then Continue For
            '简介
            Dim Body = Regex.Match(Block, "<div class=""body"">(.*?)</div>", RegexOptions.Singleline)
            Dim Description As String = If(Body.Success, CleanText(Body.Groups(1).Value, True), "")
            If Description.Length > 200 Then Description = Description.Substring(0, 200) & "…"
            '类型与 ID
            Dim IsPack As Boolean = Website.Contains("/modpack/")
            Dim IdMatch = Regex.Match(Website, "/(?:class|modpack)/(\d+)\.html")
            Dim RealId As String = If(IdMatch.Success, IdMatch.Groups(1).Value, Title)
            Dim RealType As ResourceTypes = If(IsPack, ResourceTypes.ModPack, ResourceTypes.Mod)
            Dim Tags As New List(Of String) From {If(IsPack, "整合包", "模组"), "MC 百科"}
            '组装成 ResourceProject（结构与 ToJson 一致，走缓存解析分支）
            Dim Json As New JObject
            Json("Platform") = CInt(ResourcePlatforms.McMod)
            Json("Types") = CInt(RealType)
            Json("Slug") = Title
            Json("Id") = "mcmod-" & If(IsPack, "pack-", "class-") & RealId
            Json("RawName") = Title
            Json("Description") = Description
            Json("Website") = Website
            Json("LastUpdate") = Date.Now
            Json("DownloadCount") = 0
            Json("Tags") = New JArray(Tags)
            Json("Drops") = New JArray()
            Json("LogoUrl") = PathImage & If(IsPack, "Icons/McModPack.png", "Icons/McModMod.png")
            Result.Add(New ResourceProject(Json))
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 去掉 HTML 标签、MC 百科正文标记与实体，压缩空白。
    ''' </summary>
    Private Shared Function CleanText(Raw As String, IsBody As Boolean) As String
        Dim Text As String = Raw
        If IsBody Then
            '正文里的 [h1=简介]、[ban:title_menu] 等排版标记
            Text = Regex.Replace(Text, "\[[^\]\[]{1,200}\]", "")
        End If
        Text = Regex.Replace(Text, "<[^>]+>", "")
        Text = WebUtility.HtmlDecode(Text)
        Text = Text.Replace(ChrW(160), " ").Replace(vbCr, " ").Replace(vbLf, " ").Replace(vbTab, " ")
        Text = Regex.Replace(Text, "\s{2,}", " ").Trim
        Return Text
    End Function

#End Region

End Class
