Imports System.IO
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

Namespace World

    ' 存档封面与「种子预览图」生成。
    '
    ' 种子预览图完全由 level.dat 里的 RandomSeed 推导，不读取任何区块数据，
    ' 所以没有区块、甚至只留了 level.dat 的存档也能出图。
    ' 做法是用多层值噪声模拟原版的大陆度 / 温度 / 湿度分布，再按高度做起伏阴影。
    ' 1.18+ 的原版地形依赖完整的密度函数与多噪声生物群系系统，启动器侧不做逐方块复刻，
    ' 因此这是「同种子的近似样貌」，而非与游戏内逐格一致的地图。
    Friend NotInheritable Class SavePreview

        ' 预览图分辨率，固定 4:3。
        Public Const PreviewWidth As Integer = 320
        Public Const PreviewHeight As Integer = 240

        ' 采样分辨率，输出时按整数倍放大（160×120 → 320×240）。
        Private Const SampleW As Integer = 160
        Private Const SampleH As Integer = 120
        Private Const Upscale As Integer = PreviewWidth \ SampleW

        ' 预览图横向覆盖的方块数（高度按 4:3 自动推算）。
        Private Const BlockWidth As Double = 2560.0

        ' 海平面阈值（大陆度，范围 -1..1）。
        Private Const OceanEdge As Double = -0.22

        Private Const CacheLimit As Integer = 24
        Private Shared ReadOnly PreviewCache As New Dictionary(Of String, BitmapSource)
        Private Shared ReadOnly PreviewLock As New Object

        ' 获取存档封面路径：优先自定义封面，其次游戏原生图标。
        Public Shared Function GetCoverPath(Entry As SaveEntry) As String
            Dim Custom = Path.Combine(Entry.Folder.FullName, "PclCover.png")
            If File.Exists(Custom) Then Return Custom
            Dim Icon = Path.Combine(Entry.Folder.FullName, "icon.png")
            If File.Exists(Icon) Then Return Icon
            Return ""
        End Function

        ' 由种子生成近似地形预览图。返回 Nothing 表示该存档缺少 level.dat。
        Public Shared Function GenerateSeedPreview(Entry As SaveEntry) As BitmapSource
            If Entry Is Nothing OrElse Entry.Corrupted Then Return Nothing
            Dim Key = $"{Entry.SeedValue}|{Entry.SpawnX}|{Entry.SpawnZ}"
            SyncLock PreviewLock
                Dim Hit As BitmapSource = Nothing
                If PreviewCache.TryGetValue(Key, Hit) Then Return Hit
            End SyncLock

            Dim Shot = RenderTerrain(Entry.SeedValue, Entry.SpawnX, Entry.SpawnZ)
            SyncLock PreviewLock
                If PreviewCache.Count >= CacheLimit Then PreviewCache.Clear()
                PreviewCache(Key) = Shot
            End SyncLock
            Return Shot
        End Function

        ' 渲染整幅预览图：先按采样网格算地形，再放大到输出分辨率并打上出生点标记。
        Private Shared Function RenderTerrain(SeedValue As Long, SpawnX As Integer, SpawnZ As Integer) As BitmapSource
            Dim StepSample = BlockWidth / SampleW
            Dim Conts(SampleW * SampleH - 1) As Double
            Dim Temps(SampleW * SampleH - 1) As Double
            Dim Humids(SampleW * SampleH - 1) As Double
            Dim Heights(SampleW * SampleH - 1) As Integer

            For Sz = 0 To SampleH - 1
                Dim Wz = SpawnZ + (Sz - (SampleH - 1) / 2.0) * StepSample
                For Sx = 0 To SampleW - 1
                    Dim Wx = SpawnX + (Sx - (SampleW - 1) / 2.0) * StepSample
                    Dim Cont = (Fbm(SeedValue, Wx / 620.0, Wz / 620.0, 11, 5, 2.4) - 0.5) * 2.0
                    Dim Temp = Fbm(SeedValue, Wx / 900.0, Wz / 900.0, 31, 3, 2.0)
                    Dim Humid = Fbm(SeedValue, Wx / 760.0, Wz / 760.0, 47, 3, 2.0)
                    Dim Rough = Fbm(SeedValue, Wx / 340.0, Wz / 340.0, 61, 3, 1.8)
                    Dim Detail = Fbm(SeedValue, Wx / 110.0, Wz / 110.0, 73, 3, 1.8)
                    Dim Idx = Sz * SampleW + Sx
                    Conts(Idx) = Cont
                    Temps(Idx) = Temp
                    Humids(Idx) = Humid
                    Heights(Idx) = TerrainHeight(Cont, Rough, Detail)
                Next
            Next

            Dim Pixels(PreviewWidth * PreviewHeight * 4 - 1) As Byte
            For Sz = 0 To SampleH - 1
                For Sx = 0 To SampleW - 1
                    Dim Idx = Sz * SampleW + Sx
                    Dim Col = BiomeColor(Conts(Idx), Temps(Idx), Humids(Idx), Heights(Idx))
                    If Conts(Idx) >= OceanEdge Then
                        ' 西北光源：按高度梯度做起伏阴影，山地与丘陵才有立体感
                        Dim L0 = Heights(Sz * SampleW + Math.Max(0, Sx - 1))
                        Dim R0 = Heights(Sz * SampleW + Math.Min(SampleW - 1, Sx + 1))
                        Dim U0 = Heights(Math.Max(0, Sz - 1) * SampleW + Sx)
                        Dim D0 = Heights(Math.Min(SampleH - 1, Sz + 1) * SampleW + Sx)
                        Dim Slope = (R0 - L0) * 0.55 - (D0 - U0) * 0.45
                        Col = Col.Scale(Math.Max(0.74, Math.Min(1.2, 1.0 + Slope * 0.016)))
                    End If
                    For OffZ = 0 To Upscale - 1
                        Dim RowBase = (Sz * Upscale + OffZ) * PreviewWidth * 4
                        For OffX = 0 To Upscale - 1
                            Dim O = RowBase + (Sx * Upscale + OffX) * 4
                            Pixels(O) = Col.B
                            Pixels(O + 1) = Col.G
                            Pixels(O + 2) = Col.R
                            Pixels(O + 3) = 255
                        Next
                    Next
                Next
            Next

            ' 出生点标记：白边红点，位置就是图中心
            Dim CenterX = PreviewWidth \ 2
            Dim CenterZ = PreviewHeight \ 2
            For Dz = -5 To 5
                For Dx = -5 To 5
                    Dim Dist2 = Dx * Dx + Dz * Dz
                    If Dist2 > 30 Then Continue For
                    Dim Px = CenterX + Dx
                    Dim Pz = CenterZ + Dz
                    If Px < 0 OrElse Px >= PreviewWidth OrElse Pz < 0 OrElse Pz >= PreviewHeight Then Continue For
                    Dim O = (Pz * PreviewWidth + Px) * 4
                    If Dist2 <= 5 Then
                        Pixels(O) = 42
                        Pixels(O + 1) = 42
                        Pixels(O + 2) = 220
                    Else
                        Pixels(O) = 255
                        Pixels(O + 1) = 255
                        Pixels(O + 2) = 255
                    End If
                    Pixels(O + 3) = 255
                Next
            Next

            Dim Result = BitmapSource.Create(PreviewWidth, PreviewHeight, 96, 96, PixelFormats.Bgra32, Nothing, Pixels, PreviewWidth * 4)
            Result.Freeze()
            Return Result
        End Function

        ' 由大陆度 / 粗糙度 / 细节噪声推出地表高度（近似原版 38..230 的高度区间）。
        Private Shared Function TerrainHeight(Cont As Double, Rough As Double, Detail As Double) As Integer
            If Cont < OceanEdge Then Return CInt(64 + Cont * 26)
            Dim Base = 64 + (Cont - OceanEdge) * 62
            Dim Relief = (Detail - 0.5) * 44 * (0.35 + Rough * 1.1)
            Dim Peak = Math.Max(0.0, Cont - 0.46) * 150 * (0.4 + Rough)
            Return CInt(Math.Max(38.0, Math.Min(230.0, Base + Relief + Peak)))
        End Function

        ' 按温湿度与高度挑选生物群系配色。
        Private Shared Function BiomeColor(Cont As Double, Temp As Double, Humid As Double, Height As Integer) As TerrainColor
            If Cont < OceanEdge Then
                Dim Depth = Math.Min(1.0, (OceanEdge - Cont) / 1.2)
                Return Blend(New TerrainColor(74, 128, 198), New TerrainColor(22, 48, 112), Depth)
            End If
            If Cont < OceanEdge + 0.05 Then
                If Temp < 0.32 Then Return New TerrainColor(226, 232, 238)
                Return New TerrainColor(224, 212, 166)
            End If

            Dim Col As TerrainColor
            If Temp < 0.26 Then
                Col = If(Humid > 0.55, New TerrainColor(198, 220, 218), New TerrainColor(238, 242, 247))
            ElseIf Temp < 0.42 Then
                Col = If(Humid > 0.5, New TerrainColor(78, 124, 100), New TerrainColor(94, 140, 110))
            ElseIf Temp > 0.74 AndAlso Humid < 0.34 Then
                Col = If(Humid < 0.2 AndAlso Temp > 0.84, New TerrainColor(176, 116, 74), New TerrainColor(222, 204, 142))
            ElseIf Temp > 0.62 AndAlso Humid > 0.68 Then
                Col = New TerrainColor(48, 128, 56)
            ElseIf Humid > 0.62 AndAlso Temp > 0.36 Then
                Col = New TerrainColor(98, 120, 76)
            ElseIf Humid > 0.46 Then
                Col = New TerrainColor(64, 138, 70)
            Else
                Col = New TerrainColor(128, 178, 90)
            End If

            ' 高处露岩，再高积雪
            If Height > 108 Then
                Col = Blend(Col, New TerrainColor(138, 138, 134), Math.Min(1.0, (Height - 108) / 34.0) * 0.85)
            End If
            If Height > 150 Then
                Col = Blend(Col, New TerrainColor(246, 248, 250), Math.Min(1.0, (Height - 150) / 30.0))
            End If
            Return Col
        End Function

        ' 值噪声：把整数格点散列到 [0,1)。
        ' 全程保持在 2^56 以内，避免 VB 的整数溢出检查抛 OverflowException。
        Private Shared Function Lattice(SeedValue As Long, X As Integer, Z As Integer, Salt As Integer) As Double
            Dim H As Long = ((SeedValue And &HFFFFFFL) * 2654435761L + 12345L) And &HFFFFFFFFFFFFFL
            H = (H Xor (CLng(X And &H7FFFF) * 2246822519L)) And &HFFFFFFFFFFFFFL
            H = (H Xor (CLng(Z And &H7FFFF) * 3266489917L)) And &HFFFFFFFFFFFFFL
            H = (H Xor (CLng(Salt And &HFFFF) * 668265263L)) And &HFFFFFFFFFFFFFL
            H = (H Xor (H >> 29)) And &HFFFFFFFFFFFFFL
            H = (H Xor (H >> 17)) And &HFFFFFFFFFFFFFL
            H = (H * 5L + 1L) And &HFFFFFFFFFFFFFL
            Return (H And &HFFFFFFL) / 16777216.0
        End Function

        Private Shared Function ValueNoise(SeedValue As Long, X As Double, Z As Double, Salt As Integer) As Double
            Dim X0 = CInt(Math.Floor(X))
            Dim Z0 = CInt(Math.Floor(Z))
            Dim Fx = X - X0
            Dim Fz = Z - Z0
            Dim Ux = Fx * Fx * (3.0 - 2.0 * Fx)
            Dim Uz = Fz * Fz * (3.0 - 2.0 * Fz)
            Dim V00 = Lattice(SeedValue, X0, Z0, Salt)
            Dim V10 = Lattice(SeedValue, X0 + 1, Z0, Salt)
            Dim V01 = Lattice(SeedValue, X0, Z0 + 1, Salt)
            Dim V11 = Lattice(SeedValue, X0 + 1, Z0 + 1, Salt)
            Dim A0 = V00 + (V10 - V00) * Ux
            Dim B0 = V01 + (V11 - V01) * Ux
            Return A0 + (B0 - A0) * Uz
        End Function

        ' 分形叠加。Gain 用来拉开对比，否则值噪声太接近 0.5，生物群系会挤在中间几档。
        Private Shared Function Fbm(SeedValue As Long, X As Double, Z As Double, Salt As Integer, Octaves As Integer, Gain As Double) As Double
            Dim Sum = 0.0
            Dim Amp = 0.5
            Dim Total = 0.0
            Dim Freq = 1.0
            For i = 0 To Octaves - 1
                Sum += ValueNoise(SeedValue, X * Freq, Z * Freq, Salt + i * 17) * Amp
                Total += Amp
                Amp *= 0.5
                Freq *= 2.0
            Next
            Dim V = 0.5 + (Sum / Total - 0.5) * Gain
            Return Math.Max(0.0, Math.Min(1.0, V))
        End Function

        Private Shared Function Blend(A As TerrainColor, B As TerrainColor, T As Double) As TerrainColor
            T = Math.Max(0.0, Math.Min(1.0, T))
            ' 显式走 Double：Byte 参与减法在部分表达式下会按 Byte 求值并溢出
            Return New TerrainColor(ToByte(CDbl(A.R) + (CDbl(B.R) - CDbl(A.R)) * T),
                                    ToByte(CDbl(A.G) + (CDbl(B.G) - CDbl(A.G)) * T),
                                    ToByte(CDbl(A.B) + (CDbl(B.B) - CDbl(A.B)) * T))
        End Function

        ' 安全取字节：夹紧到 0..255，NaN / 无穷按 0 处理。
        Private Shared Function ToByte(V As Double) As Byte
            If Double.IsNaN(V) OrElse Double.IsInfinity(V) Then Return 0
            Return CByte(Math.Max(0.0, Math.Min(255.0, V)))
        End Function

        ' 预览用的中间颜色。
        Private Structure TerrainColor
            Public R As Byte
            Public G As Byte
            Public B As Byte

            Public Sub New(Red As Byte, Green As Byte, Blue As Byte)
                R = Red
                G = Green
                B = Blue
            End Sub

            Public Function Scale(Fact As Double) As TerrainColor
                Return New TerrainColor(ToByte(CDbl(R) * Fact), ToByte(CDbl(G) * Fact), ToByte(CDbl(B) * Fact))
            End Function
        End Structure

    End Class

End Namespace
