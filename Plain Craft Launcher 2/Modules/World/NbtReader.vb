Imports System.IO
Imports System.IO.Compression
Imports System.Text

Namespace World

    ' Minecraft Java 版 NBT 轻量读取器。只解析需要的字段，不构建整棵树，内存占用低。
    ' 支持 gzip / zlib 压缩与未压缩的 NBT 流。
    Friend NotInheritable Class NbtReader
        Implements IDisposable

        Private ReadOnly Br As BinaryReader

        Private Sub New(Stream As Stream)
            Br = New BinaryReader(Stream, Encoding.UTF8)
        End Sub

        ' 自动识别 gzip / zlib / 裸 NBT 并构造读取器。
        Public Shared Function Open(Stream As Stream) As NbtReader
            Dim B0 = Stream.ReadByte()
            If B0 < 0 Then Throw New EndOfStreamException("NBT 流为空")
            Dim B1 = Stream.ReadByte()
            If Stream.CanSeek Then
                Stream.Position -= 2
            Else
                ' GZipStream 等不可 Seek 的流：把已读出的两个字节与剩余内容拼成内存流
                Dim Rest As New MemoryStream()
                Rest.WriteByte(CByte(B0))
                If B1 >= 0 Then Rest.WriteByte(CByte(B1))
                Stream.CopyTo(Rest)
                Rest.Position = 0
                Stream = Rest
            End If
            ' 压缩流整体解压到内存后再解析：GZipStream 直接包在不可 Seek 的 FileStream 上
            ' 逐字段读取时容易在块边界读到流外，导致 EndOfStreamException。
            ' 单个 level.dat / 区块数据都在 1 MB 以内，整体解压开销可忽略。
            If B0 = &H1F AndAlso B1 = &H8B Then
                Return New NbtReader(DecompressAll(New GZipStream(Stream, CompressionMode.Decompress, True)))
            ElseIf B0 = &H78 AndAlso (B1 And &HF) = 8 Then
                Return New NbtReader(DecompressAll(New DeflateStream(Stream, CompressionMode.Decompress, True)))
            End If
            Return New NbtReader(Stream)
        End Function

        ' 把压缩流完整解压进 MemoryStream，并从开头返回。
        Private Shared Function DecompressAll(Compressed As Stream) As Stream
            Dim Out As New MemoryStream()
            Using Compressed
                Compressed.CopyTo(Out)
            End Using
            Out.Position = 0
            Return Out
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Br.Dispose()
        End Sub

        ' 读取一个完整的 NBT 标签，包含根名称。
        Public Function ReadRoot() As NbtNode
            Dim TypeId = Br.ReadByte()
            If TypeId = 0 Then Return Nothing
            Dim RootName = ReadString()
            Return ReadPayload(TypeId)
        End Function

        ' NBT 规范使用大端序，BinaryReader 的 ReadInt32/ReadUInt16 是小端，不能直接使用。
        Private Function ReadExact(Count As Integer) As Byte()
            If Count <= 0 Then Return New Byte() {}
            Dim Buffer(Count - 1) As Byte
            Dim Got2 = 0
            While Got2 < Count
                Dim N = Br.Read(Buffer, Got2, Count - Got2)
                If N <= 0 Then Throw New EndOfStreamException($"NBT 数据提前结束：需要 {Count} 字节，仅读到 {Got2} 字节")
                Got2 += N
            End While
            Return Buffer
        End Function

        Private Function ReadUInt16BE() As Integer
            Dim B = ReadExact(2)
            Return (CInt(B(0)) << 8) Or CInt(B(1))
        End Function

        Private Function ReadInt16BE() As Short
            Dim V As Integer = ReadUInt16BE()
            If V > Int16.MaxValue Then V -= 65536
            Return CShort(V)
        End Function

        Private Function ReadInt32BE() As Integer
            Dim B = ReadExact(4)
            Return (CInt(B(0)) << 24) Or (CInt(B(1)) << 16) Or (CInt(B(2)) << 8) Or CInt(B(3))
        End Function

        Private Function ReadInt64BE() As Long
            Dim Hi = ReadInt32BE()
            Dim Lo = ReadInt32BE()
            Return (CLng(Hi) << 32) Or (CLng(Lo) And &HFFFFFFFFL)
        End Function

        Private Function ReadSingleBE() As Single
            Dim B = ReadExact(4)
            Array.Reverse(B)
            Return BitConverter.ToSingle(B, 0)
        End Function

        Private Function ReadDoubleBE() As Double
            Dim B = ReadExact(8)
            Array.Reverse(B)
            Return BitConverter.ToDouble(B, 0)
        End Function

        Private Function ReadPayload(TypeId As Byte) As NbtNode
            Select Case TypeId
                Case 1
                    Return NbtNode.NewValue(Br.ReadSByte())
                Case 2
                    Return NbtNode.NewValue(ReadInt16BE())
                Case 3
                    Return NbtNode.NewValue(ReadInt32BE())
                Case 4
                    Return NbtNode.NewValue(ReadInt64BE())
                Case 5
                    Return NbtNode.NewValue(ReadSingleBE())
                Case 6
                    Return NbtNode.NewValue(ReadDoubleBE())
                Case 7
                    Dim Count = ReadInt32BE()
                    If Count < 0 OrElse Count > 16 * 1024 * 1024 Then Throw New InvalidDataException($"NBT byte[] 长度异常：{Count}")
                    Return NbtNode.NewValue(ReadExact(Count))
                Case 8
                    Return NbtNode.NewValue(ReadString())
                Case 9
                    Dim ItemType = Br.ReadByte()
                    Dim Count = ReadInt32BE()
                    If Count < 0 OrElse Count > 4 * 1024 * 1024 Then Throw New InvalidDataException($"NBT List 长度异常：{Count}")
                    Dim Items As New List(Of NbtNode)(Count)
                    For i = 1 To Count
                        Items.Add(ReadPayload(ItemType))
                    Next
                    Return NbtNode.NewList(Items)
                Case 10
                    Dim Dict As New Dictionary(Of String, NbtNode)(StringComparer.OrdinalIgnoreCase)
                    Do
                        Dim ChildType = Br.ReadByte()
                        If ChildType = 0 Then Exit Do
                        Dim ChildName = ReadString()
                        Dict(ChildName) = ReadPayload(ChildType)
                    Loop
                    Return NbtNode.NewCompound(Dict)
                Case 11
                    ' TAG_Int_Array：长度 + N 个 4 字节整数，必须整体跳过否则后续标签错位
                    Dim IntCount = ReadInt32BE()
                    If IntCount < 0 OrElse IntCount > 4 * 1024 * 1024 Then Throw New InvalidDataException($"NBT int[] 长度异常：{IntCount}")
                    Return NbtNode.NewValue(ReadExact(IntCount * 4))
                Case 12
                    ' TAG_Long_Array：1.18+ 区块的 Heightmaps 就是它
                    Dim LongCount = ReadInt32BE()
                    If LongCount < 0 OrElse LongCount > 4 * 1024 * 1024 Then Throw New InvalidDataException($"NBT long[] 长度异常：{LongCount}")
                    Return NbtNode.NewValue(ReadExact(LongCount * 8))
                Case Else
                    Throw New InvalidDataException($"未知的 NBT 标签类型：{TypeId}")
            End Select
        End Function

        Private Function ReadString() As String
            Dim Length = ReadUInt16BE()
            If Length = 0 Then Return ""
            Return Encoding.UTF8.GetString(ReadExact(Length))
        End Function

    End Class

    ' NBT 值的轻量表示。用 Value 存标量，Compound 存字典，List 存列表。
    Friend NotInheritable Class NbtNode
        Public Property Value As Object
        Public Property Compound As Dictionary(Of String, NbtNode)
        Public Property List As List(Of NbtNode)

        Public Shared Function NewValue(V As Object) As NbtNode
            Return New NbtNode With {.Value = V}
        End Function
        Public Shared Function NewCompound(D As Dictionary(Of String, NbtNode)) As NbtNode
            Return New NbtNode With {.Compound = D}
        End Function
        Public Shared Function NewList(L As List(Of NbtNode)) As NbtNode
            Return New NbtNode With {.List = L}
        End Function

        ' 按路径取子节点，例如 "Data/LevelName"。键名大小写不敏感（1.18+ 区块为 sections / palette）。
        Public Function Item(Path As String) As NbtNode
            Dim Current = Me
            For Each Part In Path.Split("/"c)
                If Current Is Nothing OrElse Current.Compound Is Nothing Then Return Nothing
                Dim NextNode As NbtNode = Nothing
                If Not Current.Compound.TryGetValue(Part, NextNode) Then Return Nothing
                Current = NextNode
            Next
            Return Current
        End Function

        Public Function Str(Path As String, Optional Fallback As String = "") As String
            Dim N = Item(Path)
            Return If(N Is Nothing OrElse N.Value Is Nothing, Fallback, N.Value.ToString)
        End Function

        Public Function AsInt(Path As String, Optional Fallback As Integer = 0) As Integer
            Dim N = Item(Path)
            If N Is Nothing OrElse N.Value Is Nothing Then Return Fallback
            Try
                Return Convert.ToInt32(N.Value)
            Catch
                Return Fallback
            End Try
        End Function

        Public Function AsLong(Path As String, Optional Fallback As Long = 0) As Long
            Dim N = Item(Path)
            If N Is Nothing OrElse N.Value Is Nothing Then Return Fallback
            Try
                Return Convert.ToInt64(N.Value)
            Catch
                Return Fallback
            End Try
        End Function

        Public Function Cnt(Path As String) As Integer
            Dim N = Item(Path)
            Return If(N Is Nothing OrElse N.List Is Nothing, 0, N.List.Count)
        End Function
    End Class

End Namespace
