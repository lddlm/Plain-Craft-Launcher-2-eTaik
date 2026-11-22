Imports System.ComponentModel

''' <summary>
''' 社区资源条目的分类彩色描边。
''' 每个分类持有一个共享 SolidColorBrush，设置页修改 Color 时，
''' 已经渲染出来的所有条目会同步变色；Enabled 通过 WPF 绑定控制可见性。
''' </summary>
Public Class ResourceOutline
    Implements INotifyPropertyChanged

#Region "分类与默认配色"

    ''' <summary>
    ''' 设置页中可自定义颜色的分类。未列出的标签统一使用「其他」。
    ''' </summary>
    Public Shared ReadOnly CategoryNames As String() = {
        "科技", "魔法", "冒险", "世界元素", "生物", "装饰", "实用", "装备",
        "性能优化", "支持库", "食物/烹饪", "运输", "仓储", "红石", "农业", "能源",
        "整合包", "模组", "其他"
    }

    ''' <summary>
    ''' 默认配色（HEX，不带 #）。
    ''' </summary>
    Public Shared ReadOnly DefaultColors As New Dictionary(Of String, String) From {
        {"科技", "3B82F6"}, {"魔法", "8B5CF6"}, {"冒险", "F59E0B"},
        {"世界元素", "10B981"}, {"生物", "EF4444"}, {"装饰", "EC4899"},
        {"实用", "06B6D4"}, {"装备", "F97316"}, {"性能优化", "14B8A6"},
        {"支持库", "64748B"}, {"食物/烹饪", "EAB308"}, {"运输", "6366F1"},
        {"仓储", "A16207"}, {"红石", "DC2626"}, {"农业", "65A30D"},
        {"能源", "FACC15"}, {"整合包", "7C3AED"}, {"模组", "2563EB"},
        {"其他", "94A3B8"}
    }

    ''' <summary>
    ''' 设置页色板里的预设颜色。
    ''' </summary>
    Public Shared ReadOnly PresetColors As String() = {
        "3B82F6", "8B5CF6", "EC4899", "EF4444", "F97316", "EAB308",
        "65A30D", "10B981", "14B8A6", "06B6D4", "6366F1", "64748B"
    }

#End Region

#Region "状态"

    ''' <summary>
    ''' 各分类的共享画刷。不要 Freeze，设置页需要直接改 Color。
    ''' </summary>
    Public Shared ReadOnly Brushes As New Dictionary(Of String, SolidColorBrush)

    ''' <summary>
    ''' 用于 WPF 绑定的单例。
    ''' </summary>
    Public Shared ReadOnly Instance As New ResourceOutline()

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Private _Enabled As Boolean = False
    ''' <summary>
    ''' 是否显示描边。绑定到每个条目的 BorderOutline.Visibility。
    ''' </summary>
    Public Property Enabled As Boolean
        Get
            Return _Enabled
        End Get
        Set(Value As Boolean)
            If _Enabled = Value Then Return
            _Enabled = Value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("Enabled"))
        End Set
    End Property

    Private Sub New()
        Reload()
        Try
            _Enabled = Settings.Get(Of Boolean)("UltraResourceOutline")
        Catch ex As Exception
            Logger.Warn(ex, "读取描边开关失败")
        End Try
    End Sub

    ''' <summary>
    ''' 从设置读取颜色配置并重建画刷。
    ''' </summary>
    Public Shared Sub Reload()
        Dim Colors = GetColors()
        For Each TypeKey In CategoryNames
            Dim C As Color = ParseHex(Colors(TypeKey))
            If Brushes.ContainsKey(TypeKey) Then
                Brushes(TypeKey).Color = C
            Else
                Brushes.Add(TypeKey, New SolidColorBrush(C))
            End If
        Next
    End Sub

    ''' <summary>
    ''' 设置项变化时同步 Enabled（由 Setting.OnChanged 调用）。
    ''' </summary>
    Public Shared Sub RefreshEnabled()
        Try
            Instance.Enabled = Settings.Get(Of Boolean)("UltraResourceOutline")
        Catch ex As Exception
            Logger.Warn(ex, "刷新描边开关失败")
        End Try
    End Sub

#End Region

#Region "配置读写"

    ''' <summary>
    ''' 取得完整的分类 → HEX 颜色表（以默认色打底，再覆盖用户配置）。
    ''' </summary>
    Public Shared Function GetColors() As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)
        For Each TypeKey In CategoryNames
            Result(TypeKey) = DefaultColors(TypeKey)
        Next
        Try
            Dim Saved As String = Settings.Get(Of String)("UltraResourceOutlineColors")
            If String.IsNullOrEmpty(Saved) Then Return Result
            For Each Part In Saved.Split("|"c)
                If Not Part.Contains("=") Then Continue For
                Dim KeyName As String = Part.BeforeFirst("=")
                Dim HexValue As String = Part.AfterFirst("=").TrimStart("#"c)
                If Result.ContainsKey(KeyName) AndAlso HexValue.Length = 6 Then Result(KeyName) = HexValue
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取描边配色失败，使用默认配色")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 把当前画刷的颜色写回设置。
    ''' </summary>
    Public Shared Sub Save()
        Dim Parts As New List(Of String)
        For Each TypeKey In CategoryNames
            Dim C As Color = Brushes(TypeKey).Color
            Parts.Add(TypeKey & "=" & $"{C.R:X2}{C.G:X2}{C.B:X2}")
        Next
        Settings.Set("UltraResourceOutlineColors", Parts.Join("|"))
    End Sub

    ''' <summary>
    ''' 修改某个分类的颜色，已渲染的条目会立即变色。
    ''' </summary>
    Public Shared Sub SetColor(TypeKey As String, HexValue As String)
        If Not Brushes.ContainsKey(TypeKey) Then Return
        Brushes(TypeKey).Color = ParseHex(HexValue)
        Save()
    End Sub

    ''' <summary>
    ''' 恢复全部默认配色。
    ''' </summary>
    Public Shared Sub ResetAll()
        For Each TypeKey In CategoryNames
            Brushes(TypeKey).Color = ParseHex(DefaultColors(TypeKey))
        Next
        Save()
    End Sub

    ''' <summary>
    ''' HEX → Color，非法值回退为灰色。
    ''' </summary>
    Public Shared Function ParseHex(HexValue As String) As Color
        Try
            Dim T As String = If(HexValue, "").Trim().TrimStart("#"c)
            If T.Length <> 6 Then Return Color.FromRgb(148, 163, 184)
            Return Color.FromRgb(
                CByte(Convert.ToInt32(T.Substring(0, 2), 16)),
                CByte(Convert.ToInt32(T.Substring(2, 2), 16)),
                CByte(Convert.ToInt32(T.Substring(4, 2), 16)))
        Catch ex As Exception
            Return Color.FromRgb(148, 163, 184)
        End Try
    End Function

    ''' <summary>
    ''' Color → HEX（不带 #）。
    ''' </summary>
    Public Shared Function ToHex(C As Color) As String
        Return $"{C.R:X2}{C.G:X2}{C.B:X2}"
    End Function

#End Region

#Region "应用"

    ''' <summary>
    ''' 按条目的标签选取描边画刷。未匹配到分类时使用「其他」。
    ''' </summary>
    Public Shared Function GetBrushFor(Project As ResourceProject) As Brush
        If Project Is Nothing Then Return Nothing
        If Project.Tags IsNot Nothing Then
            For Each Tag In Project.Tags
                If Brushes.ContainsKey(Tag) Then Return Brushes(Tag)
            Next
        End If
        If Project.Types.HasFlagF(ResourceTypes.ModPack) AndAlso Brushes.ContainsKey("整合包") Then
            Return Brushes("整合包")
        End If
        If Brushes.ContainsKey("其他") Then Return Brushes("其他")
        Return Nothing
    End Function

    ''' <summary>
    ''' 把描边应用到控件。画刷是共享实例，颜色改动会自动反映到所有条目。
    ''' </summary>
    Public Shared Sub ApplyTo(Item As MyResourceItem, Project As ResourceProject)
        If Item Is Nothing Then Return
        Item.SetOutline(GetBrushFor(Project))
    End Sub

#End Region

End Class
