Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Windows.Media.Imaging

Namespace World

    ' 单个存档的信息。
    Friend Class SaveEntry
        Public Property Folder As DirectoryInfo
        Public Property WorldName As String = ""
        Public Property Seed As String = ""
        Public Property SeedValue As Long = 0
        Public Property SpawnX As Integer = 0
        Public Property SpawnZ As Integer = 0
        Public Property GameVersion As String = ""
        Public Property LastPlayed As DateTime = DateTime.MinValue
        Public Property GameMode As String = ""
        Public Property Difficulty As String = ""
        Public Property PlayTime As Long = 0
        Public Property EntityCount As Integer = -1
        Public Property ItemCount As Integer = -1
        Public Property TileEntityCount As Integer = -1
        Public Property ChunkTotal As Integer = 0
        Public Property ChunksScanned As Integer = 0
        Public Property StatsEstimated As Boolean = False
        Public Property Corrupted As Boolean = False
        Public Property CustomCover As String = ""

        Public ReadOnly Property Title As String
            Get
                Return If(String.IsNullOrWhiteSpace(WorldName), Folder.Name, WorldName)
            End Get
        End Property

        Public ReadOnly Property HasStats As Boolean
            Get
                Return EntityCount >= 0
            End Get
        End Property
    End Class

    ' 统计结果的会话缓存，避免刷新列表时重复扫描大存档。
    Friend Class SaveStatsSnapshot
        Public Stamp As Long
        Public Entity As Integer
        Public Item As Integer
        Public Tile As Integer
        Public ChunkTotal As Integer
        Public Scanned As Integer
        Public Estimated As Boolean
    End Class

    ' 存档读取与管理：level.dat 元数据、实体统计。
    Friend NotInheritable Class SaveManager

        ' 单次统计最多扫描的区块数。几 GB 的世界全量扫描要几十秒，抽样够用且不卡界面。
        Private Const MaxScanChunks As Integer = 600

        Private Shared ReadOnly StatsCache As New Dictionary(Of String, SaveStatsSnapshot)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly StatsLock As New Object

        ' 读取版本隔离目录下的全部存档，按最后游玩时间倒序。
        Public Shared Function LoadAll(InstanceRoot As String) As List(Of SaveEntry)
            Dim Result As New List(Of SaveEntry)
            If String.IsNullOrEmpty(InstanceRoot) Then Return Result
            Dim SaveFolder = DirectoryUtils.GetInfo(InstanceRoot & "saves")
            If Not SaveFolder.Exists Then Return Result

            For Each SubFolder In SaveFolder.EnumerateDirectories()
                Try
                    If Not SubFolder.EnumerateFileSystemInfos().Any() Then Continue For
                    Result.Add(LoadOne(SubFolder))
                Catch ex As Exception
                    Logger.Warn(ex, $"读取存档失败：{SubFolder.Name}")
                    Result.Add(New SaveEntry With {.Folder = SubFolder, .Corrupted = True})
                End Try
            Next
            Return Result.OrderByDescending(Function(s) s.LastPlayed).ToList
        End Function

        ' 读取单个存档的 level.dat。
        Public Shared Function LoadOne(SaveFolder As DirectoryInfo) As SaveEntry
            Dim Entry As New SaveEntry With {.Folder = SaveFolder}
            Dim LevelPath = Path.Combine(SaveFolder.FullName, "level.dat")
            If Not File.Exists(LevelPath) Then
                Entry.Corrupted = True
                Return Entry
            End If
            Try
                Using Fs = New FileStream(LevelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using Reader = NbtReader.Open(Fs)
                    Dim Root = Reader.ReadRoot()
                    Dim R = If(Root.Item("Data"), Root)
                    Entry.WorldName = R.Str("LevelName", SaveFolder.Name)
                    Entry.SeedValue = R.AsLong("RandomSeed", R.AsLong("seed", 0))
                    Entry.Seed = Entry.SeedValue.ToString()
                    Entry.SpawnX = R.AsInt("SpawnX", 0)
                    Entry.SpawnZ = R.AsInt("SpawnZ", 0)
                    Dim UnixEpoch = New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    Entry.LastPlayed = UnixEpoch.AddMilliseconds(R.AsLong("LastPlayed", 0)).ToLocalTime()
                    If Entry.LastPlayed.Year < 2000 Then Entry.LastPlayed = SaveFolder.LastWriteTime

                    Dim VersionName = R.Str("Version/Id", R.Str("Version/Name", ""))
                    If String.IsNullOrEmpty(VersionName) Then
                        Dim DataVersion = R.AsInt("DataVersion", 0)
                        If DataVersion > 0 Then VersionName = "数据版本 " & DataVersion.ToString()
                    End If
                    Entry.GameVersion = VersionName

                    Select Case R.AsInt("GameType", 0)
                        Case 0 : Entry.GameMode = "生存"
                        Case 1 : Entry.GameMode = "创造"
                        Case 2 : Entry.GameMode = "冒险"
                        Case 3 : Entry.GameMode = "旁观"
                        Case Else : Entry.GameMode = "生存"
                    End Select
                    Select Case R.AsInt("Difficulty", 2)
                        Case 0 : Entry.Difficulty = "和平"
                        Case 1 : Entry.Difficulty = "简单"
                        Case 2 : Entry.Difficulty = "普通"
                        Case 3 : Entry.Difficulty = "困难"
                    End Select
                    Entry.PlayTime = R.AsLong("Time", 0)
                End Using
                End Using
            Catch ex As Exception
                Logger.Warn(ex, $"解析 level.dat 失败：{SaveFolder.Name}")
                Entry.Corrupted = True
            End Try
            ApplyCachedStats(Entry)
            Return Entry
        End Function

        ' 给存档改名：只替换 level.dat（NBT）里 Data/LevelName 这个 TAG_String 的值，
        ' 其余字节原样保留，避免重建整棵 NBT 树带来的兼容风险。
        ' 压缩格式（gzip / zlib / 裸 NBT）保持与原来一致。
        Public Shared Sub RenameWorld(Entry As SaveEntry, NewName As String)
            If Entry Is Nothing OrElse Entry.Folder Is Nothing Then Throw New ArgumentException("存档无效")
            If String.IsNullOrWhiteSpace(NewName) Then Throw New ArgumentException("名称不能为空")
            If NewName.Length > 100 Then Throw New ArgumentException("名称过长（最多 100 字符）")
            Dim LevelPath = Path.Combine(Entry.Folder.FullName, "level.dat")
            If Not File.Exists(LevelPath) Then Throw New FileNotFoundException("缺少 level.dat", LevelPath)

            ' 1) 读出原始字节并解压（保持原压缩格式）
            Dim Raw = File.ReadAllBytes(LevelPath)
            Dim CompressedAsGzip = Raw.Length >= 2 AndAlso Raw(0) = &H1F AndAlso Raw(1) = &H8B
            Dim CompressedAsZlib = Raw.Length >= 2 AndAlso Raw(0) = &H78 AndAlso (Raw(1) And &HF) = 8
            Dim Plain As Byte()
            If CompressedAsGzip Then
                Using Ms = New MemoryStream(Raw), Gz = New GZipStream(Ms, CompressionMode.Decompress), Out = New MemoryStream()
                    Gz.CopyTo(Out) : Plain = Out.ToArray()
                End Using
            ElseIf CompressedAsZlib Then
                Using Ms = New MemoryStream(Raw), Z = New DeflateStream(Ms, CompressionMode.Decompress), Out = New MemoryStream()
                    Z.CopyTo(Out) : Plain = Out.ToArray()
                End Using
            Else
                Plain = Raw
            End If

            ' 2) 定位 TAG_String：type=8, nameLen=9, "LevelName"
            Dim Needle As New List(Of Byte) From {8, 0, 9}
            Needle.AddRange(Encoding.UTF8.GetBytes("LevelName"))
            Dim Pos = FindBytes(Plain, Needle.ToArray())
            If Pos < 0 Then Throw New InvalidDataException("level.dat 里找不到 LevelName 字段")
            ' 名字之后是 2 字节大端字符串长度 + 字符串内容
            Dim LenOffset = Pos + Needle.Count
            If LenOffset + 2 > Plain.Length Then Throw New InvalidDataException("LevelName 字段残缺")
            Dim OldLen = (Plain(LenOffset) << 8) Or Plain(LenOffset + 1)
            If OldLen < 0 OrElse LenOffset + 2 + OldLen > Plain.Length Then Throw New InvalidDataException("LevelName 长度异常")

            ' 3) 拼接：前缀 + 新长度(2BE) + 新值 + 后缀
            Dim NewBytes = Encoding.UTF8.GetBytes(NewName)
            If NewBytes.Length > 65535 Then Throw New ArgumentException("名称过长")
            Dim Result As New List(Of Byte)(Plain.Length - OldLen + NewBytes.Length)
            Result.AddRange(Plain.Take(LenOffset))
            Result.Add(CByte((NewBytes.Length >> 8) And &HFF))
            Result.Add(CByte(NewBytes.Length And &HFF))
            Result.AddRange(NewBytes)
            Result.AddRange(Plain.Skip(LenOffset + 2 + OldLen))
            Dim OutPlain = Result.ToArray()

            ' 4) 按原格式压缩写回（先写临时文件成功后再替换，避免写坏原存档）
            Dim OutBytes As Byte()
            If CompressedAsGzip Then
                Using Out = New MemoryStream()
                    Using Gz = New GZipStream(Out, CompressionLevel.Optimal, True)
                        Gz.Write(OutPlain, 0, OutPlain.Length)
                    End Using
                    OutBytes = Out.ToArray()
                End Using
            ElseIf CompressedAsZlib Then
                Using Out = New MemoryStream()
                    Using Z = New DeflateStream(Out, CompressionLevel.Optimal, True)
                        Z.Write(OutPlain, 0, OutPlain.Length)
                    End Using
                    OutBytes = Out.ToArray()
                End Using
            Else
                OutBytes = OutPlain
            End If
            Dim Tmp = LevelPath & ".rename.tmp"
            File.WriteAllBytes(Tmp, OutBytes)
            File.Copy(Tmp, LevelPath, True)
            File.Delete(Tmp)
            Entry.WorldName = NewName
            Logger.Info($"存档改名：{Entry.Folder.Name} -> {NewName}")
        End Sub

        ' 在字节数组里查找子序列，返回起始下标；找不到返回 -1。
        Private Shared Function FindBytes(Haystack As Byte(), Needle As Byte()) As Integer
            If Needle Is Nothing OrElse Needle.Length = 0 Then Return 0
            For i = 0 To Haystack.Length - Needle.Length
                Dim Ok = True
                For j = 0 To Needle.Length - 1
                    If Haystack(i + j) <> Needle(j) Then Ok = False : Exit For
                Next
                If Ok Then Return i
            Next
            Return -1
        End Function

        ' 统计实体 / 掉落物 / 方块实体。
        '
        ' 大存档全量扫描代价过高（几 GB 的世界要几十秒），因此分两遍：
        ' 第一遍只读各 region 的 4 KB 头，拿到区块总数；第二遍按均匀间隔抽样扫描
        ' 最多 600 个区块，超出时按比例估算，界面上会标注「抽样估算」。
        Public Shared Sub CountEntities(Entry As SaveEntry)
            Dim Files As New List(Of String)
            For Each SubName In {"region", "entities"}
                Dim Dir = DirectoryUtils.GetInfo(Path.Combine(Entry.Folder.FullName, SubName))
                If Not Dir.Exists Then Continue For
                For Each Mca In Dir.EnumerateFiles("*.mca")
                    Files.Add(Mca.FullName)
                Next
            Next

            Dim Plans As New List(Of RegionPlan)
            Dim TotalChunks = 0
            For Each FilePath In Files
                Try
                    Dim Plan = ReadRegionHeader(FilePath)
                    If Plan Is Nothing OrElse Plan.Indexes.Count = 0 Then Continue For
                    Plans.Add(Plan)
                    TotalChunks += Plan.Indexes.Count
                Catch ex As Exception
                    Logger.Warn(ex, $"读取区块头失败：{Path.GetFileName(FilePath)}")
                End Try
            Next

            Dim Stride = If(TotalChunks <= MaxScanChunks, 1, CInt(Math.Ceiling(TotalChunks / CDbl(MaxScanChunks))))
            Dim TotalEntities = 0, Items = 0, Tiles = 0, Scanned = 0, Order = 0
            For Each Plan In Plans
                Try
                    ScanRegion(Plan, Stride, Order, TotalEntities, Items, Tiles, Scanned)
                Catch ex As Exception
                    Logger.Warn(ex, $"扫描区块文件失败：{Path.GetFileName(Plan.FilePath)}")
                End Try
            Next

            Entry.ChunkTotal = TotalChunks
            Entry.ChunksScanned = Scanned
            Entry.StatsEstimated = Scanned > 0 AndAlso Scanned < TotalChunks
            If Entry.StatsEstimated Then
                Dim Factor = TotalChunks / CDbl(Scanned)
                Entry.EntityCount = CInt(Math.Round(TotalEntities * Factor))
                Entry.ItemCount = CInt(Math.Round(Items * Factor))
                Entry.TileEntityCount = CInt(Math.Round(Tiles * Factor))
            Else
                Entry.EntityCount = TotalEntities
                Entry.ItemCount = Items
                Entry.TileEntityCount = Tiles
            End If
            StoreStats(Entry)
        End Sub

        ' 只读 region 的 4 KB 头，记录哪些索引位置真的有区块。
        Private Shared Function ReadRegionHeader(FilePath As String) As RegionPlan
            Using Fs = New FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                If Fs.Length < 8192 Then Return Nothing
                Dim Header(4095) As Byte
                If Fs.Read(Header, 0, 4096) < 4096 Then Return Nothing
                Dim Plan As New RegionPlan With {.FilePath = FilePath}
                For Index = 0 To 1023
                    Dim Offset = (Header(Index * 4) << 16) Or (Header(Index * 4 + 1) << 8) Or Header(Index * 4 + 2)
                    If Offset = 0 OrElse Header(Index * 4 + 3) = 0 Then Continue For
                    Plan.Indexes.Add(Index)
                Next
                Return Plan
            End Using
        End Function

        ' 解析 Anvil region 文件，按 Order 的全局序号抽样统计实体。
        Private Shared Sub ScanRegion(Plan As RegionPlan, Stride As Integer, ByRef Order As Integer,
                                      ByRef TotalEntities As Integer, ByRef Items As Integer,
                                      ByRef Tiles As Integer, ByRef Scanned As Integer)
            Using Fs = New FileStream(Plan.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                If Fs.Length < 8192 Then Return
                Dim Header(4095) As Byte
                If Fs.Read(Header, 0, 4096) < 4096 Then Return
                For Each Index In Plan.Indexes
                    Order += 1
                    If (Order - 1) Mod Stride <> 0 Then Continue For
                    Dim Offset = (Header(Index * 4) << 16) Or (Header(Index * 4 + 1) << 8) Or Header(Index * 4 + 2)
                    Dim SectorCount = Header(Index * 4 + 3)
                    If Offset = 0 OrElse SectorCount = 0 Then Continue For
                    Try
                        Fs.Position = Offset * 4096L
                        Dim LenBytes(3) As Byte
                        If Fs.Read(LenBytes, 0, 4) < 4 Then Continue For
                        Dim DataLen = (LenBytes(0) << 24) Or (LenBytes(1) << 16) Or (LenBytes(2) << 8) Or LenBytes(3)
                        If DataLen < 1 OrElse DataLen > 8 * 1024 * 1024 Then Continue For
                        Dim Compression = Fs.ReadByte()
                        Dim Payload(DataLen - 2) As Byte
                        If Fs.Read(Payload, 0, Payload.Length) < Payload.Length Then Continue For
                        Scanned += 1
                        Using Ms = New MemoryStream(Payload)
                        Using Raw = If(Compression = 1, CType(New GZipStream(Ms, CompressionMode.Decompress), Stream),
                                      If(Compression = 2, CType(New DeflateStream(Ms, CompressionMode.Decompress), Stream), Ms))
                        Using Reader = NbtReader.Open(Raw)
                            Dim Root = Reader.ReadRoot()
                            If Root Is Nothing Then Continue For
                            Dim Level = If(Root.Item("Level"), Root)
                            Dim Entities = Level.Item("Entities")
                            If Entities IsNot Nothing AndAlso Entities.List IsNot Nothing Then
                                TotalEntities += Entities.List.Count
                                For Each E In Entities.List
                                    Dim Id = E.Str("id", "")
                                    If Id = "minecraft:item" OrElse Id = "Item" Then Items += 1
                                Next
                            End If
                            Dim TileNode = Level.Item("TileEntities")
                            If TileNode IsNot Nothing AndAlso TileNode.List IsNot Nothing Then Tiles += TileNode.List.Count
                            ' 1.18+ 把方块实体放在根级别
                            Dim BlockEntities = Root.Item("block_entities")
                            If BlockEntities IsNot Nothing AndAlso BlockEntities.List IsNot Nothing Then Tiles += BlockEntities.List.Count
                        End Using
                        End Using
                        End Using
                    Catch ex As Exception
                        Logger.Warn(ex, $"解析区块失败：{Path.GetFileName(Plan.FilePath)} #{Index}")
                    End Try
                Next
            End Using
        End Sub

        ' region 头部解析计划。
        Private Class RegionPlan
            Public FilePath As String
            Public Indexes As New List(Of Integer)
        End Class

        Private Shared Function GetStamp(Entry As SaveEntry) As Long
            Try
                Dim LevelPath = Path.Combine(Entry.Folder.FullName, "level.dat")
                If File.Exists(LevelPath) Then Return File.GetLastWriteTimeUtc(LevelPath).Ticks
            Catch
            End Try
            Return Entry.Folder.LastWriteTimeUtc.Ticks
        End Function

        Private Shared Sub StoreStats(Entry As SaveEntry)
            Dim Snapshot As New SaveStatsSnapshot With {
                .Stamp = GetStamp(Entry),
                .Entity = Entry.EntityCount,
                .Item = Entry.ItemCount,
                .Tile = Entry.TileEntityCount,
                .ChunkTotal = Entry.ChunkTotal,
                .Scanned = Entry.ChunksScanned,
                .Estimated = Entry.StatsEstimated}
            SyncLock StatsLock
                StatsCache(Entry.Folder.FullName) = Snapshot
            End SyncLock
        End Sub

        ' 刷新列表会重新构造 SaveEntry，这里把本轮会话里已经统计过的结果回填，避免重复扫描。
        Private Shared Sub ApplyCachedStats(Entry As SaveEntry)
            Dim Snapshot As SaveStatsSnapshot = Nothing
            SyncLock StatsLock
                If Not StatsCache.TryGetValue(Entry.Folder.FullName, Snapshot) Then Return
            End SyncLock
            If Snapshot Is Nothing OrElse Snapshot.Stamp <> GetStamp(Entry) Then Return
            Entry.EntityCount = Snapshot.Entity
            Entry.ItemCount = Snapshot.Item
            Entry.TileEntityCount = Snapshot.Tile
            Entry.ChunkTotal = Snapshot.ChunkTotal
            Entry.ChunksScanned = Snapshot.Scanned
            Entry.StatsEstimated = Snapshot.Estimated
        End Sub

    End Class

End Namespace
