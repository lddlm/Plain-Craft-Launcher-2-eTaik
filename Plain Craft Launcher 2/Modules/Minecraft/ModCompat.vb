Imports System.Text.RegularExpressions

''' <summary>
''' Ultra：Mod 兼容性检测。
''' 只扫描并记录结果，不会自动禁用、删除或改动任何 Mod 文件。
''' </summary>
Public Module ModCompat

#Region "数据结构"

    ''' <summary>单个 Mod 的元数据。</summary>
    Public Class ModMeta
        Public FileName As String = ""
        Public FullPath As String = ""
        Public IsEnabled As Boolean = True
        Public Loader As String = ""
        Public Id As String = ""
        Public Name As String = ""
        Public Version As String = ""
        Public Depends As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public Breaks As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public Provides As New List(Of String)
        ''' <summary>provides / 嵌套 Mod 的 ID 对应版本号（用于版本范围判断）。</summary>
        Public ReadOnly ProvideVersions As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        Public ReadOnly Property Title As String
            Get
                If Not String.IsNullOrWhiteSpace(Name) Then Return Name
                If Not String.IsNullOrWhiteSpace(Id) Then Return Id
                Return FileName
            End Get
        End Property
    End Class

    ''' <summary>一次兼容性检测的结果。</summary>
    Public Class ScanResult
        ''' <summary>参与检测的 Mod 数量（只统计已启用的）。</summary>
        Public ModCount As Integer = 0
        Public GameVersion As String = ""
        ''' <summary>问题列表：Key 为 Mod 的 EnabledName（如 xxx.jar），Value 为原因。</summary>
        Public ReadOnly Issues As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public ReadOnly Mods As New List(Of ModMeta)
        ''' <summary>本次检测中确认没有安装的前置 ID（去重，按发现顺序）。</summary>
        Public ReadOnly MissingDependencies As New List(Of String)
        ''' <summary>前置 ID → 需要它的 Mod 文件名列表。</summary>
        Public ReadOnly MissingRequiredBy As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
    End Class

    Private Class CrashRecord
        Public Label As String = ""
        Public Tokens As New List(Of String)
    End Class

    Private Class InstanceState
        Public GameVersion As String = ""
        Public ReadOnly Mods As New List(Of ModMeta)
        Public ReadOnly Index As New Dictionary(Of String, ModMeta)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>手动兼容性检测的结果。</summary>
        Public ReadOnly ScanIssues As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>崩溃分析得到的结果。</summary>
        Public ReadOnly CrashIssues As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public ReadOnly CrashRecords As New List(Of CrashRecord)
        ''' <summary>最近一次扫描确认缺失的前置 ID。</summary>
        Public ReadOnly MissingDependencies As New List(Of String)
    End Class

    Private ReadOnly States As New Dictionary(Of String, InstanceState)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly StateLock As New Object

#End Region

#Region "对外接口"

    ''' <summary>
    ''' 扫描指定版本隔离目录下的全部 Mod，检查 Mod 之间、Mod 与游戏版本之间的兼容性。
    ''' 结果会缓存，供 Mod 列表的「不兼容」筛选使用。
    ''' </summary>
    Public Function Scan(PathIndie As String, GameVersion As String) As ScanResult
        Dim Result As New ScanResult With {.GameVersion = If(GameVersion, "")}
        Dim Mods As New List(Of ModMeta)
        ParseFolder(PathIndie, Mods) '解析不需要加锁，避免阻塞 UI
        Dim Index As New Dictionary(Of String, ModMeta)(StringComparer.OrdinalIgnoreCase)
        Evaluate(Mods, Index, Result, Result.GameVersion)
        Result.Mods.AddRange(Mods)

        SyncLock StateLock
            Dim State = GetState(PathIndie, Result.GameVersion)
            State.Mods.Clear()
            State.Mods.AddRange(Mods)
            State.Index.Clear()
            For Each Pair In Index
                State.Index(Pair.Key) = Pair.Value
            Next
            State.ScanIssues.Clear()
            For Each Pair In Result.Issues
                State.ScanIssues(Pair.Key) = Pair.Value
            Next
            State.MissingDependencies.Clear()
            State.MissingDependencies.AddRange(Result.MissingDependencies)
            ApplyCrashRecords(State) '重新用崩溃记录匹配一次，避免文件增删后结果失效
        End SyncLock

        Logger.Info($"Mod 兼容性检测完成：{Result.ModCount} 个 Mod，{Result.Issues.Count} 个问题")
        Return Result
    End Function

    ''' <summary>
    ''' 记录崩溃分析中发现的疑似 Mod。返回匹配到的 Mod 文件数量。
    ''' </summary>
    Public Function RegisterCrashSuspects(PathIndie As String, GameVersion As String, Label As String, Tokens As IEnumerable(Of String)) As Integer
        Dim Clean As New List(Of String)
        If Tokens IsNot Nothing Then
            For Each Token In Tokens
                If Not String.IsNullOrWhiteSpace(Token) Then Clean.Add(Token)
            Next
        End If
        If Not Clean.Any() Then Return 0

        '若还没有解析过这个版本隔离目录，先解析一次（不加锁）
        Dim NeedParse As Boolean
        SyncLock StateLock
            NeedParse = GetState(PathIndie, GameVersion).Mods.Count = 0
        End SyncLock
        Dim Parsed As List(Of ModMeta) = Nothing
        If NeedParse Then
            Parsed = New List(Of ModMeta)
            ParseFolder(PathIndie, Parsed)
        End If

        Dim Count As Integer = 0
        SyncLock StateLock
            Dim State = GetState(PathIndie, GameVersion)
            If Parsed IsNot Nothing Then
                State.Mods.Clear()
                State.Mods.AddRange(Parsed)
                RebuildIndex(State)
            End If
            State.CrashRecords.Add(New CrashRecord With {.Label = Label, .Tokens = Clean})
            ApplyCrashRecords(State)
            Count = State.CrashIssues.Count
        End SyncLock
        Return Count
    End Function

    ''' <summary>获取某个 Mod 的不兼容原因；没有问题时返回 Nothing。</summary>
    Public Function GetIssue(PathIndie As String, Entry As LocalResourceFile) As String
        If Entry Is Nothing Then Return Nothing
        SyncLock StateLock
            Dim State As InstanceState = Nothing
            If Not States.TryGetValue(NormalizeKey(PathIndie), State) Then Return Nothing
            Dim Reason As String = Nothing
            If State.ScanIssues.TryGetValue(Entry.EnabledName, Reason) Then Return Reason
            If State.CrashIssues.TryGetValue(Entry.EnabledName, Reason) Then Return Reason
        End SyncLock
        Return Nothing
    End Function

    ''' <summary>统计当前列表中存在兼容性问题的 Mod 数量。</summary>
    Public Function CountIssues(PathIndie As String, Entries As IEnumerable(Of LocalResourceFile)) As Integer
        Dim Count As Integer = 0
        For Each Entry In Entries
            If GetIssue(PathIndie, Entry) IsNot Nothing Then Count += 1
        Next
        Return Count
    End Function

    ''' <summary>取最近一次检测中确认缺失的前置 ID 列表（没有检测过时返回空列表）。</summary>
    Public Function GetMissingDependencies(PathIndie As String) As List(Of String)
        Dim Result As New List(Of String)
        SyncLock StateLock
            Dim State As InstanceState = Nothing
            If States.TryGetValue(NormalizeKey(PathIndie), State) Then Result.AddRange(State.MissingDependencies)
        End SyncLock
        Return Result
    End Function

    ''' <summary>清除某个版本隔离目录的检测结果。</summary>
    Public Sub Clear(PathIndie As String)
        SyncLock StateLock
            States.Remove(NormalizeKey(PathIndie))
        End SyncLock
    End Sub

#End Region

#Region "状态与匹配"

    Private Function NormalizeKey(PathIndie As String) As String
        If String.IsNullOrWhiteSpace(PathIndie) Then Return ""
        Return PathIndie.TrimEnd("\"c).ToLowerInvariant()
    End Function

    Private Function GetState(PathIndie As String, GameVersion As String) As InstanceState
        '调用方需持有 StateLock
        Dim Key = NormalizeKey(PathIndie)
        Dim State As InstanceState = Nothing
        If Not States.TryGetValue(Key, State) Then
            State = New InstanceState
            States(Key) = State
        End If
        If Not String.IsNullOrWhiteSpace(GameVersion) Then State.GameVersion = GameVersion
        Return State
    End Function

    Private Sub RebuildIndex(State As InstanceState)
        State.Index.Clear()
        For Each Meta In State.Mods
            If Not Meta.IsEnabled OrElse String.IsNullOrWhiteSpace(Meta.Id) Then Continue For
            If Not State.Index.ContainsKey(Meta.Id) Then State.Index(Meta.Id) = Meta
            For Each Provide In Meta.Provides
                If Not State.Index.ContainsKey(Provide) Then State.Index(Provide) = Meta
            Next
        Next
    End Sub

    ''' <summary>用已记录的崩溃信息重新匹配 Mod 文件。</summary>
    Private Sub ApplyCrashRecords(State As InstanceState)
        State.CrashIssues.Clear()
        For Each Record In State.CrashRecords
            For Each Meta In State.Mods
                If Not Meta.IsEnabled Then Continue For
                If Not MatchesTokens(Meta, Record.Tokens) Then Continue For
                AddReason(State.CrashIssues, Meta.FileName, Record.Label)
            Next
        Next
    End Sub

    Private Function MatchesTokens(Meta As ModMeta, Tokens As IEnumerable(Of String)) As Boolean
        Dim FileName = Meta.FileName.ToLowerInvariant()
        Dim Id = Meta.Id.ToLowerInvariant()
        Dim Title = Meta.Name.ToLowerInvariant()
        For Each Token In Tokens
            Dim Text = Token.ToLowerInvariant()
            '文件名直接匹配最可靠
            If Text.Contains(FileName) Then Return True
            '把括号、引号当作分隔符后按整词匹配 Mod ID / 名称，避免 "api" 之类的子串误判
            Dim Padded = " " & Text.Replace("("c, " "c).Replace(")"c, " "c).Replace("'"c, " "c).Replace(""""c, " "c).Replace(vbTab, " "c) & " "
            If Id.Length >= 3 AndAlso Padded.Contains(" " & Id & " ") Then Return True
            If Title.Length >= 3 AndAlso Padded.Contains(" " & Title & " ") Then Return True
        Next
        Return False
    End Function

    Private Sub AddReason(Target As Dictionary(Of String, String), FileName As String, Reason As String)
        If String.IsNullOrWhiteSpace(FileName) OrElse String.IsNullOrWhiteSpace(Reason) Then Return
        Dim Old As String = Nothing
        If Target.TryGetValue(FileName, Old) AndAlso Not String.IsNullOrWhiteSpace(Old) Then
            If Old.Contains(Reason) Then Return
            Target(FileName) = Old & "；" & Reason
        Else
            Target(FileName) = Reason
        End If
    End Sub

#End Region

#Region "解析 Metadata"

    Private Sub ParseFolder(PathIndie As String, Mods As List(Of ModMeta))
        Dim ModsPath As String = If(PathIndie, "").TrimEnd("\"c) & "\mods\"
        If Not Directory.Exists(ModsPath) Then Return
        For Each FilePath As String In Directory.GetFiles(ModsPath)
            Dim FileName As String = Path.GetFileName(FilePath)
            Dim Extension As String = Path.GetExtension(FileName).ToLowerInvariant()
            '只检测已启用的 Mod：被禁用的文件不可能导致游戏崩溃
            If Not {".jar", ".zip", ".litemod"}.Contains(Extension) Then Continue For
            Dim Meta As ModMeta = ReadMeta(FilePath)
            If Meta Is Nothing Then
                Meta = New ModMeta()
            End If
            Meta.FileName = FileName
            Meta.FullPath = FilePath
            Meta.IsEnabled = True
            If String.IsNullOrWhiteSpace(Meta.Id) AndAlso String.IsNullOrWhiteSpace(Meta.Name) Then
                Meta.Name = Path.GetFileNameWithoutExtension(FileName)
            End If
            Mods.Add(Meta)
        Next
    End Sub

    Private Function ReadMeta(FilePath As String) As ModMeta
        Try
            Using Zip As ZipArchive = ZipFile.OpenRead(FilePath)
                Dim Meta As New ModMeta With {.FileName = Path.GetFileName(FilePath), .FullPath = FilePath}
                'Fabric
                Dim Entry As ZipArchiveEntry = Zip.GetEntry("fabric.mod.json")
                If Entry IsNot Nothing Then
                    Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                        ParseFabric(Reader.ReadToEnd(), Meta, Zip)
                    End Using
                    Return Meta
                End If
                'Quilt
                Entry = Zip.GetEntry("quilt.mod.json")
                If Entry IsNot Nothing Then
                    Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                        ParseQuilt(Reader.ReadToEnd(), Meta)
                    End Using
                    CollectNestedMods(Zip, Meta)
                    AdoptNestedId(Meta)
                    Return Meta
                End If
                'Forge / NeoForge
                Entry = Zip.GetEntry("META-INF/neoforge.mods.toml")
                If Entry Is Nothing Then Entry = Zip.GetEntry("META-INF/mods.toml")
                If Entry IsNot Nothing Then
                    Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                        ParseForgeToml(Reader.ReadToEnd(), Meta)
                    End Using
                    CollectNestedMods(Zip, Meta)
                    AdoptNestedId(Meta)
                    Return Meta
                End If
                '完全没有清单的壳 jar（只靠 Jar-in-Jar 提供 Mod）
                CollectNestedMods(Zip, Meta)
                AdoptNestedId(Meta)
                Return Meta
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"读取 Mod 元数据失败：{FilePath}")
            Return Nothing
        End Try
    End Function

    Private Function JsonValue(Token As JToken) As String
        If Token Is Nothing Then Return ""
        Select Case Token.Type
            Case JTokenType.String, JTokenType.Integer, JTokenType.Float
                Return Token.ToString()
            Case Else
                Return ""
        End Select
    End Function

    Private Sub ParseFabric(Text As String, Meta As ModMeta, Optional Container As ZipArchive = Nothing)
        Dim Json As JObject
        Try
            Json = JObject.Parse(Text)
        Catch ex As Exception
            Logger.Warn(ex, "解析 fabric.mod.json 失败")
            Return
        End Try
        Meta.Loader = "fabric"
        Meta.Id = JsonValue(Json("id"))
        Meta.Name = JsonValue(Json("name"))
        Meta.Version = JsonValue(Json("version"))
        AddProvides(Json, Meta)
        ParseDependencyObject(TryCast(Json("depends"), JObject), Meta.Depends)
        ParseDependencyObject(TryCast(Json("breaks"), JObject), Meta.Breaks)
        ParseDependencyObject(TryCast(Json("conflicts"), JObject), Meta.Breaks)
        'Jar-in-Jar：把内嵌的 Mod ID 也收集进来（Fabric 的 META-INF/jars、Forge 的 META-INF/jarjar）
        If Container IsNot Nothing Then
            CollectNestedMods(Container, Meta)
            AdoptNestedId(Meta)
        End If
    End Sub

    Private Sub AddProvides(Json As JObject, Meta As ModMeta)
        Dim Provides = TryCast(Json("provides"), JArray)
        If Provides Is Nothing Then Return
        For Each Token In Provides
            Dim Value = JsonValue(Token)
            If Not String.IsNullOrWhiteSpace(Value) AndAlso Not Meta.Provides.Contains(Value) Then Meta.Provides.Add(Value)
        Next
    End Sub

    Private Sub ParseDependencyObject(Obj As JObject, Target As Dictionary(Of String, String))
        If Obj Is Nothing Then Return
        For Each Prop As JProperty In Obj.Properties()
            Target(Prop.Name) = JsonValue(Prop.Value)
        Next
    End Sub

    Private Sub ParseQuilt(Text As String, Meta As ModMeta)
        Try
            Dim Json = JObject.Parse(Text)
            Meta.Loader = "quilt"
            Dim Loader = TryCast(Json("quilt_loader"), JObject)
            If Loader Is Nothing Then Return
            Meta.Id = JsonValue(Loader("id"))
            Meta.Version = JsonValue(Loader("version"))
            Dim Metadata = TryCast(Loader("metadata"), JObject)
            If Metadata IsNot Nothing Then Meta.Name = JsonValue(Metadata("name"))
            Dim Depends = TryCast(Loader("depends"), JArray)
            If Depends Is Nothing Then Return
            For Each Token In Depends
                Dim Obj = TryCast(Token, JObject)
                If Obj Is Nothing Then
                    Dim Id = JsonValue(Token)
                    If Not String.IsNullOrWhiteSpace(Id) Then Meta.Depends(Id) = ""
                Else
                    Dim Id = JsonValue(Obj("id"))
                    If Not String.IsNullOrWhiteSpace(Id) Then Meta.Depends(Id) = JsonValue(Obj("versions"))
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析 quilt.mod.json 失败")
        End Try
    End Sub

    ''' <summary>
    ''' 轻量解析 mods.toml / neoforge.mods.toml：
    ''' 收集所有 [[mods]] 的 modId / provides，以及 required 且 mandatory 的依赖。
    ''' </summary>
    Private Sub ParseForgeToml(Text As String, Meta As ModMeta)
        Meta.Loader = "forge"
        Dim ModIndex As Integer = 0
        For Each SectionText In SplitTomlSections(Text)
            Dim Lines = SectionText.Split(CChar(vbLf))
            Dim Header = Lines(0).Trim()
            If Header.StartsWithF("mods") Then
                Dim Fields = ParseTomlFields(Lines)
                Dim Id = TomlString(GetTomlField(Fields, "modId"))
                Dim DisplayName = TomlString(GetTomlField(Fields, "displayName"))
                Dim Version = TomlString(GetTomlField(Fields, "version"))
                If ModIndex = 0 Then
                    Meta.Id = Id
                    Meta.Name = DisplayName
                    If Not Version.Contains("$") Then Meta.Version = Version '形如 ${file.jarVersion} 的无法解析
                ElseIf Not String.IsNullOrWhiteSpace(Id) Then
                    '同一个 jar 里的多个 mod 块：额外 ID 视为该 jar 提供
                    If Not Meta.Provides.Contains(Id) Then Meta.Provides.Add(Id)
                    If Not Version.Contains("$") AndAlso Not String.IsNullOrWhiteSpace(Version) Then Meta.ProvideVersions(Id) = Version
                End If
                For Each Item In TomlArray(GetTomlField(Fields, "provides"))
                    If Not Meta.Provides.Contains(Item) Then Meta.Provides.Add(Item)
                Next
                ModIndex += 1
            ElseIf Header.StartsWithF("dependencies") Then
                Dim Fields = ParseTomlFields(Lines)
                CommitForgeDependency(Meta,
                                      TomlString(GetTomlField(Fields, "modId")),
                                      TomlString(GetTomlField(Fields, "versionRange")),
                                      Not GetTomlField(Fields, "mandatory").Equals("false", StringComparison.OrdinalIgnoreCase),
                                      TomlString(GetTomlField(Fields, "type")))
            End If
        Next
    End Sub

    ''' <summary>按 [[表头]] 切分 TOML，第一段是文件头。</summary>
    Private Function SplitTomlSections(Text As String) As List(Of String)
        Return Text.Replace(vbCrLf, vbLf).Split(New String() {"[["}, StringSplitOptions.None).ToList
    End Function

    ''' <summary>解析一个表内的 key = value（只取首个）。</summary>
    Private Function ParseTomlFields(Lines As String()) As Dictionary(Of String, String)
        Dim Fields As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Index = 1 To Lines.Length - 1
            Dim Line = Lines(Index).Trim()
            If Line = "" OrElse Line.StartsWithF("#") Then Continue For
            If Line.StartsWithF("[") Then Exit For '进入下一个表
            Dim Split = Line.IndexOf("="c)
            If Split <= 0 Then Continue For
            Dim Key = Line.Substring(0, Split).Trim()
            If Not Fields.ContainsKey(Key) Then Fields(Key) = StripTomlComment(Line.Substring(Split + 1))
        Next
        Return Fields
    End Function

    ''' <summary>去掉 TOML 行内注释（引号内的 # 不算）。</summary>
    Private Function StripTomlComment(Value As String) As String
        Dim InQuote As Boolean = False
        Dim QuoteChar As Char = " "c
        For Index = 0 To Value.Length - 1
            Dim Ch = Value(Index)
            If InQuote Then
                If Ch = QuoteChar Then InQuote = False
            ElseIf Ch = """"c OrElse Ch = "'"c Then
                InQuote = True
                QuoteChar = Ch
            ElseIf Ch = "#"c Then
                Return Value.Substring(0, Index).Trim()
            End If
        Next
        Return Value.Trim()
    End Function

    Private Function GetTomlField(Fields As Dictionary(Of String, String), Key As String) As String
        Dim Value As String = Nothing
        If Fields.TryGetValue(Key, Value) Then Return Value
        Return ""
    End Function

    Private Function TomlString(Value As String) As String
        If String.IsNullOrWhiteSpace(Value) Then Return ""
        Dim Text = Value.Trim()
        If Text.Length >= 2 Then
            Dim First = Text(0)
            Dim Last = Text(Text.Length - 1)
            If (First = """"c AndAlso Last = """"c) OrElse (First = "'"c AndAlso Last = "'"c) Then
                Return Text.Substring(1, Text.Length - 2)
            End If
        End If
        Return Text
    End Function

    Private Function TomlArray(Value As String) As List(Of String)
        Dim Result As New List(Of String)
        If String.IsNullOrWhiteSpace(Value) Then Return Result
        For Each Match As Match In Regex.Matches(Value, """([^""]*)""")
            Dim Item = Match.Groups(1).Value
            If Not String.IsNullOrWhiteSpace(Item) Then Result.Add(Item)
        Next
        Return Result
    End Function

    Private Sub CommitForgeDependency(Meta As ModMeta, DepId As String, DepRange As String, Mandatory As Boolean, Type As String)
        If String.IsNullOrWhiteSpace(DepId) Then Return
        If Not Mandatory Then Return
        If DepId.Equals(Meta.Id, StringComparison.OrdinalIgnoreCase) Then Return
        If Type.Equals("incompatible", StringComparison.OrdinalIgnoreCase) Then
            Meta.Breaks(DepId) = DepRange '不兼容声明，不是缺少前置
            Return
        End If
        'NeoForge 的 discouraged 是"不推荐一起装"，optional 是可选，都不算缺少前置
        If Type <> "" AndAlso Not Type.Equals("required", StringComparison.OrdinalIgnoreCase) Then Return
        Meta.Depends(DepId) = DepRange
    End Sub

    ''' <summary>
    ''' 收集 Jar-in-Jar 内嵌 Mod 的 ID：Fabric 的 META-INF/jars 与 Forge 的 META-INF/jarjar。
    ''' 内嵌依赖（如 Kotlin for Forge、Extensible Enums、Forgified Fabric API 的子模块）由游戏自行加载。
    ''' </summary>
    Private Sub CollectNestedMods(Container As ZipArchive, Meta As ModMeta)
        For Each NestedEntry As ZipArchiveEntry In Container.Entries
            Dim EntryName = NestedEntry.FullName
            If Not EntryName.EndsWithF(".jar") Then Continue For
            If Not (EntryName.StartsWithF("META-INF/jars/") OrElse EntryName.StartsWithF("META-INF/jarjar/")) Then Continue For
            Try
                Using NestedStream = NestedEntry.Open()
                    Using NestedZip As New ZipArchive(NestedStream, ZipArchiveMode.Read, True)
                        Dim Manifest = NestedZip.GetEntry("fabric.mod.json")
                        If Manifest IsNot Nothing Then
                            Using Reader As New StreamReader(Manifest.Open(), Encoding.UTF8)
                                Dim NestedJson = JObject.Parse(Reader.ReadToEnd())
                                AddNestedMod(Meta, JsonValue(NestedJson("id")), JsonValue(NestedJson("version")))
                                AddProvides(NestedJson, Meta)
                            End Using
                        End If
                        For Each TomlName In {"META-INF/neoforge.mods.toml", "META-INF/mods.toml"}
                            Dim TomlEntry = NestedZip.GetEntry(TomlName)
                            If TomlEntry Is Nothing Then Continue For
                            Using Reader As New StreamReader(TomlEntry.Open(), Encoding.UTF8)
                                CollectForgeModIds(Reader.ReadToEnd(), Meta)
                            End Using
                            Exit For
                        Next
                    End Using
                End Using
            Catch ex As Exception
                Logger.Warn(ex, $"解析内嵌 Mod 失败：{EntryName}")
            End Try
        Next
    End Sub

    Private Sub CollectForgeModIds(Toml As String, Meta As ModMeta)
        For Each SectionText In SplitTomlSections(Toml)
            Dim Lines = SectionText.Split(CChar(vbLf))
            If Not Lines(0).Trim().StartsWithF("mods") Then Continue For
            Dim Fields = ParseTomlFields(Lines)
            AddNestedMod(Meta, TomlString(GetTomlField(Fields, "modId")), TomlString(GetTomlField(Fields, "version")))
            For Each Item In TomlArray(GetTomlField(Fields, "provides"))
                If Not Meta.Provides.Contains(Item) Then Meta.Provides.Add(Item)
            Next
        Next
    End Sub

    ''' <summary>壳 jar（如 Sinytra Connector、Kotlin for Forge）自身没有 mod 声明，用第一个内嵌 ID 代替。</summary>
    Private Sub AdoptNestedId(Meta As ModMeta)
        If Not String.IsNullOrWhiteSpace(Meta.Id) Then Return
        If Not Meta.Provides.Any() Then Return
        Meta.Id = Meta.Provides(0)
        If String.IsNullOrWhiteSpace(Meta.Name) Then Meta.Name = Meta.Id
    End Sub

    Private Sub AddNestedMod(Meta As ModMeta, Id As String, Version As String)
        If String.IsNullOrWhiteSpace(Id) Then Return
        If Id.Equals(Meta.Id, StringComparison.OrdinalIgnoreCase) Then Return
        If Not Meta.Provides.Contains(Id) Then Meta.Provides.Add(Id)
        If Not String.IsNullOrWhiteSpace(Version) AndAlso Not Version.Contains("$") Then Meta.ProvideVersions(Id) = Version
    End Sub

#End Region

#Region "兼容性判定"

    Private Sub Evaluate(Mods As List(Of ModMeta), Index As Dictionary(Of String, ModMeta), Result As ScanResult, GameVersion As String)
        Dim EnabledMods = Mods.Where(Function(m) m.IsEnabled).ToList
        Result.ModCount = EnabledMods.Count

        '建立 Mod ID 索引（含 provides）：同一个 ID 被多个 jar 提供时，取版本最高的那个（与 JarJar 行为一致）
        For Each Meta In EnabledMods
            If String.IsNullOrWhiteSpace(Meta.Id) Then Continue For
            AddToIndex(Index, Meta.Id, Meta)
            For Each Provide In Meta.Provides
                AddToIndex(Index, Provide, Meta)
            Next
        Next

        '1. 同一个 Mod ID 被安装了多次
        Dim Duplicated = EnabledMods.
            Where(Function(m) Not String.IsNullOrWhiteSpace(m.Id)).
            GroupBy(Function(m) m.Id.ToLowerInvariant()).
            Where(Function(g) g.Count() > 1)
        For Each Group In Duplicated
            For Each Meta In Group
                Dim Others = Group.Where(Function(m) Not m Is Meta).Select(Function(m) m.FileName)
                AddReason(Result.Issues, Meta.FileName, $"重复安装（{Meta.Id}）：「{String.Join("」「", Others)}」")
            Next
        Next

        'Sinytra Connector 环境下，Fabric API 及其子模块由 Connector + Forgified Fabric API 在运行时提供，
        '这些模块的 ID 不会出现在任何 jar 的 metadata 里，所以不再逐个校验
        Dim HasConnector As Boolean = Index.ContainsKey("connector")
        '只要装了 Fabric API（原版或 Forgified 版），它的子模块（fabric-resource-loader-v0 之类）就已经在运行期存在
        Dim HasFabricApi As Boolean = HasConnector OrElse
            Index.ContainsKey("fabric-api") OrElse Index.ContainsKey("fabric_api") OrElse Index.ContainsKey("fabricapi")

        '2. 前置缺失 / 版本不符 / MC 版本不符
        For Each Meta In EnabledMods
            If String.IsNullOrWhiteSpace(Meta.Id) Then Continue For
            For Each Dependency In Meta.Depends
                If Dependency.Key.Equals("minecraft", StringComparison.OrdinalIgnoreCase) Then
                    If Not String.IsNullOrWhiteSpace(GameVersion) AndAlso Not MinecraftVersionSatisfies(GameVersion, Dependency.Value) Then
                        AddReason(Result.Issues, Meta.FileName, $"仅适用于 Minecraft {Dependency.Value}，当前版本为 {GameVersion}")
                    End If
                    Continue For
                End If
                If IsBuiltinDependency(Dependency.Key) Then Continue For
                If Dependency.Key.Equals(Meta.Id, StringComparison.OrdinalIgnoreCase) Then Continue For '自己依赖自己
                If HasFabricApi AndAlso IsFabricApiModule(Dependency.Key) Then Continue For
                Dim Target As ModMeta = Nothing
                If Not Index.TryGetValue(Dependency.Key, Target) Then
                    AddReason(Result.Issues, Meta.FileName, $"缺少前置 {Dependency.Key}")
                    AddMissing(Result, Meta, Dependency.Key)
                Else
                    Dim InstalledVersion = ResolvedVersion(Target, Dependency.Key)
                    If Not VersionSatisfies(InstalledVersion, Dependency.Value) Then
                        AddReason(Result.Issues, Meta.FileName, $"前置 {Target.Title} 版本不符（需要 {Dependency.Value}，已安装 {SafeVersion(InstalledVersion)}）")
                    End If
                End If
            Next
        Next

        '3. Mod 之间声明的冲突
        For Each Meta In EnabledMods
            If String.IsNullOrWhiteSpace(Meta.Id) Then Continue For
            For Each Broken In Meta.Breaks
                Dim Target As ModMeta = Nothing
                If Not Index.TryGetValue(Broken.Key, Target) Then Continue For
                If Target Is Meta Then Continue For
                If Not BreaksApplies(Broken.Value, ResolvedVersion(Target, Broken.Key)) Then Continue For '声明的冲突版本不包含已安装版本
                AddReason(Result.Issues, Meta.FileName, $"与 {Target.FileName} 不兼容")
                AddReason(Result.Issues, Target.FileName, $"与 {Meta.FileName} 不兼容")
            Next
        Next
    End Sub

    ''' <summary>登记 ID → Mod，若已存在则保留版本更高的一个。</summary>
    Private Sub AddToIndex(Index As Dictionary(Of String, ModMeta), Id As String, Meta As ModMeta)
        If String.IsNullOrWhiteSpace(Id) Then Return
        Dim Existing As ModMeta = Nothing
        If Index.TryGetValue(Id, Existing) Then
            Dim OldVersion = ResolvedVersion(Existing, Id)
            Dim NewVersion = ResolvedVersion(Meta, Id)
            If CompareVersion(ParseVersion(NewVersion), ParseVersion(OldVersion)) <= 0 Then Return
        End If
        Index(Id) = Meta
    End Sub

    ''' <summary>取某个 Mod 提供的某个 ID 对应的版本（provides / 嵌套模块用它自己的版本）。</summary>
    Private Function ResolvedVersion(Target As ModMeta, Id As String) As String
        Dim Version As String = Nothing
        If Target.ProvideVersions.TryGetValue(Id, Version) AndAlso Not String.IsNullOrWhiteSpace(Version) Then Return Version
        Return Target.Version
    End Function

    ''' <summary>判断声明的冲突范围是否命中已安装版本。</summary>
    Private Function BreaksApplies(Range As String, TargetVersion As String) As Boolean
        If String.IsNullOrWhiteSpace(Range) OrElse Range.Trim() = "*" Then Return True
        If String.IsNullOrWhiteSpace(TargetVersion) Then Return False '版本未知时不误报
        Return VersionSatisfies(TargetVersion, Range)
    End Function

    Private Function SafeVersion(Version As String) As String
        If String.IsNullOrWhiteSpace(Version) Then Return "未知版本"
        Return Version
    End Function

    ''' <summary>Fabric API 本体与它的子模块（fabric-api-base、fabric-resource-loader-v0 之类）。</summary>
    Private Function IsFabricApiModule(Id As String) As Boolean
        Dim Lower = Id.ToLowerInvariant()
        If Lower = "fabric-api" OrElse Lower = "fabric_api" OrElse Lower = "fabricapi" Then Return True
        '真正的 Fabric API 子模块都以 -v数字 结尾；fabric-permissions-api、fabric-language-kotlin 这类独立库不会被误跳过
        Return Regex.IsMatch(Lower, "^fabric-[a-z0-9_.-]+-v\d+$")
    End Function

    ''' <summary>记录一个缺失前置，供「一键安装缺少前置」使用。</summary>
    Private Sub AddMissing(Result As ScanResult, Meta As ModMeta, DepId As String)
        If String.IsNullOrWhiteSpace(DepId) Then Return
        Dim Key = DepId.Trim()
        If Not Result.MissingDependencies.Contains(Key) Then Result.MissingDependencies.Add(Key)
        Dim List As List(Of String) = Nothing
        If Not Result.MissingRequiredBy.TryGetValue(Key, List) Then
            List = New List(Of String)
            Result.MissingRequiredBy(Key) = List
        End If
        If Not List.Contains(Meta.FileName) Then List.Add(Meta.FileName)
    End Sub

    Private Function IsBuiltinDependency(Id As String) As Boolean
        Select Case Id.ToLowerInvariant()
            Case "minecraft", "java", "fabric", "fabricloader", "fabric-loader", "forge", "neoforge", "quilt_loader", "quilt-loader",
                 "mixinextras", "mixinextras-fabric", "sponge-mixin", "asm", "fabric_loader"
                Return True
        End Select
        Return False
    End Function

    ''' <summary>判断已安装版本是否满足要求的版本范围。无法解析时按“满足”处理，避免误报。</summary>
    Private Function VersionSatisfies(Installed As String, Range As String) As Boolean
        If String.IsNullOrWhiteSpace(Range) Then Return True
        Dim Constraint As String = Range.Trim()
        If Constraint = "*" OrElse Constraint.Equals("any", StringComparison.OrdinalIgnoreCase) Then Return True
        If String.IsNullOrWhiteSpace(Installed) Then Return True '版本未知时不误报
        Dim InstalledNumbers = ParseVersion(Installed)
        If InstalledNumbers.Length = 0 Then Return True

        '区间写法：[1.0,2.0) / (1.0,] 等
        If (Constraint.StartsWithF("[") OrElse Constraint.StartsWithF("(")) AndAlso
           (Constraint.EndsWithF("]") OrElse Constraint.EndsWithF(")")) Then
            Dim Inner = Constraint.Substring(1, Constraint.Length - 2)
            Dim Parts = Inner.Split(CChar(","))
            If Parts.Length = 2 Then
                Dim Low = Parts(0).Trim()
                Dim High = Parts(1).Trim()
                If Low <> "" Then
                    Dim CmpResult = CompareVersion(InstalledNumbers, ParseVersion(Low))
                    If If(Constraint.StartsWithF("["), CmpResult < 0, CmpResult <= 0) Then Return False
                End If
                If High <> "" Then
                    Dim CmpResult = CompareVersion(InstalledNumbers, ParseVersion(High))
                    If If(Constraint.EndsWithF("]"), CmpResult > 0, CmpResult >= 0) Then Return False
                End If
                Return True
            End If
        End If

        '空格分隔的多个条件
        For Each Part In Constraint.Split(New Char() {" "c, ";"c}, StringSplitOptions.RemoveEmptyEntries)
            Dim Item = Part.Trim()
            If Item = "" OrElse Item = "*" Then Continue For
            Dim Oper As String = ""
            Dim Rest As String = Item
            For Each Candidate In {">=", "<=", ">", "<", "=", "~", "^"}
                If Rest.StartsWithF(Candidate) Then
                    Oper = Candidate
                    Rest = Rest.Substring(Candidate.Length).Trim()
                    Exit For
                End If
            Next
            If Rest = "" OrElse Rest = "*" Then Continue For
            Dim Target = ParseVersion(Rest)
            If Target.Length = 0 Then Continue For '无法解析的条件忽略
            Dim CmpResult = CompareVersion(InstalledNumbers, Target)
            Select Case Oper
                Case ">="
                    If CmpResult < 0 Then Return False
                Case "<="
                    If CmpResult > 0 Then Return False
                Case ">"
                    If CmpResult <= 0 Then Return False
                Case "<"
                    If CmpResult >= 0 Then Return False
                Case "="
                    If CmpResult <> 0 Then Return False
                Case "~", "^"
                    If CmpResult < 0 Then Return False '宽松处理：只要求不低于下界
                Case Else
                    If Not VersionPrefixMatch(InstalledNumbers, Target) Then Return False
            End Select
        Next
        Return True
    End Function

    ''' <summary>
    ''' Minecraft 版本判断：只比到主版本.次版本，并把上界当作包含（1.21 == 1.21.1），
    ''' 避免 [1.21,1.21.1) 这类常见写法把 1.21.1 判成不兼容。
    ''' </summary>
    Private Function MinecraftVersionSatisfies(Installed As String, Range As String) As Boolean
        If String.IsNullOrWhiteSpace(Range) Then Return True
        Dim Normalized = Range.Trim()
        If Normalized = "*" Then Return True
        Normalized = Regex.Replace(Normalized, "(\d+)\.(\d+)\.\d+", "$1.$2")
        Normalized = Normalized.Replace(")", "]").Replace("(", "[")
        Return VersionSatisfies(MinorVersion(Installed), Normalized)
    End Function

    Private Function MinorVersion(Version As String) As String
        Dim Numbers = ParseVersion(Version)
        If Numbers.Length >= 2 Then Return $"{Numbers(0)}.{Numbers(1)}"
        Return Version
    End Function

    Private Function ParseVersion(Version As String) As Integer()
        If String.IsNullOrWhiteSpace(Version) Then Return New Integer() {}
        Dim Clean = Version.Trim()
        Dim Cut = Clean.IndexOfAny(New Char() {"+"c, "-"c, " "c})
        If Cut > 0 Then Clean = Clean.Substring(0, Cut)
        Dim Numbers As New List(Of Integer)
        For Each Match As Match In Regex.Matches(Clean, "\d+")
            Dim Value As Integer
            If Integer.TryParse(Match.Value, Value) Then Numbers.Add(Value)
            If Numbers.Count >= 4 Then Exit For
        Next
        Return Numbers.ToArray()
    End Function

    Private Function CompareVersion(A As Integer(), B As Integer()) As Integer
        Dim Length As Integer = Math.Max(A.Length, B.Length)
        For Index = 0 To Length - 1
            Dim Left As Integer = If(Index < A.Length, A(Index), 0)
            Dim Right As Integer = If(Index < B.Length, B(Index), 0)
            If Left < Right Then Return -1
            If Left > Right Then Return 1
        Next
        Return 0
    End Function

    Private Function VersionPrefixMatch(Installed As Integer(), Prefix As Integer()) As Boolean
        If Prefix.Length = 0 Then Return True
        For Index = 0 To Prefix.Length - 1
            Dim Value As Integer = If(Index < Installed.Length, Installed(Index), 0)
            If Value <> Prefix(Index) Then Return False
        Next
        Return True
    End Function

#End Region

End Module
