'动画引擎模块
'使用 Ani 作为方法或属性的开头，使用 Aa 作为单个动画对象的开头（便于自动补全）

Imports System.Windows.Media.Effects

Public Module ModAnimation

#Region "声明"

    ''' <summary>
    ''' 动画速度倍率（时间倍率）。1.0 为标准速度，大于 1 表示动画播放更快。
    ''' 由「动画速度」设置（UltraAnimSpeed：低 = 快 / 中 = 标准 / 高 = 慢）与调试用的动画速度共同决定。
    ''' 高级动画与普通动画**共用**这个倍率：高级动画负责「怎么动」，这里负责「多快」。
    ''' </summary>
    Public ReadOnly Property AniSpeed As Double
        Get
            If _AniSpeedCache <= 0 Then
                Dim DebugSetting As Integer = Settings.Get(Of Integer)("SystemDebugAnim")
                Dim Base As Double = If(DebugSetting >= 30, 200, (DebugSetting * 0.1 + 0.1).Clamp(0.1, 3))
                Select Case AniSpeedLevel
                    Case 0 '低：更快
                        _AniSpeedCache = Base * 1.75
                    Case 2 '高：更慢
                        _AniSpeedCache = Base / 1.75
                    Case Else
                        _AniSpeedCache = Base
                End Select
            End If
            Return _AniSpeedCache
        End Get
    End Property
    Private _AniSpeedCache As Double = -1
    ''' <summary>
    ''' 是否启用「高级动画」。启用后由高级动画接管所有转场，普通的「动画速度」「动画细节」暂不生效。
    ''' </summary>
    Public ReadOnly Property AniAdvanced As Boolean
        Get
            If _AniAdvancedCache < 0 Then
                Dim Value As Boolean = False
                Try
                    Value = Settings.Get(Of Boolean)("UltraAnimAdvanced")
                Catch ex As Exception
                    Value = False
                End Try
                _AniAdvancedCache = If(Value, 1, 0)
            End If
            Return _AniAdvancedCache = 1
        End Get
    End Property
    Private _AniAdvancedCache As Integer = -1
    ''' <summary>
    ''' 高级动画强度档位：0 = 低，1 = 中（默认），2 = 高。仅在 AniAdvanced 为真时生效。
    ''' </summary>
    Public ReadOnly Property AniAdvancedLevel As Integer
        Get
            If _AniAdvancedLevelCache < 0 Then
                Dim Value As Integer = 1
                Try
                    Value = Settings.Get(Of Integer)("UltraAnimAdvancedLevel")
                Catch ex As Exception
                    Value = 1
                End Try
                _AniAdvancedLevelCache = Math.Max(0, Math.Min(2, Value))
            End If
            Return _AniAdvancedLevelCache
        End Get
    End Property
    Private _AniAdvancedLevelCache As Integer = -1
    ''' <summary>
    ''' 高级动画开关或档位改变后调用。
    ''' </summary>
    Public Sub AniAdvancedRefresh()
        _AniAdvancedCache = -1
        _AniAdvancedLevelCache = -1
        _AniSpeedCache = -1
    End Sub
    ''' <summary>
    ''' 高级动画的三档取值：低 / 中 / 高。
    ''' </summary>
    Public Function AniAdvancedPick(Of T)(Low As T, Middle As T, High As T) As T
        Select Case AniAdvancedLevel
            Case 0
                Return Low
            Case 2
                Return High
            Case Else
                Return Middle
        End Select
    End Function
    ''' <summary>
    ''' 动画速度档位：0 = 低（播放更快），1 = 中（标准），2 = 高（播放更慢）。
    ''' 只影响动画时长，不影响动画细节（位移距离、错落数量等）。
    ''' </summary>
    Public ReadOnly Property AniSpeedLevel As Integer
        Get
            If _AniSpeedLevelCache < 0 Then
                Dim Value As Integer = 1
                Try
                    Value = Settings.Get(Of Integer)("UltraAnimSpeed")
                Catch ex As Exception
                    Value = 1
                End Try
                _AniSpeedLevelCache = Math.Max(0, Math.Min(2, Value))
            End If
            Return _AniSpeedLevelCache
        End Get
    End Property
    Private _AniSpeedLevelCache As Integer = -1
    ''' <summary>
    ''' 设置项改变后调用，让新的动画速度立即生效。
    ''' </summary>
    Public Sub AniSpeedRefresh()
        _AniSpeedLevelCache = -1
        _AniSpeedCache = -1
    End Sub
    ''' <summary>
    ''' 动画细节等级：0 = 低（过渡更少、元素不位移），1 = 中（默认），2 = 高（位移更大、卡片缩放浮现）。
    ''' 对应设置项 UltraAnimDetail，切换后立即生效。
    ''' </summary>
    Public ReadOnly Property AniDetail As Integer
        Get
            If _AniDetailCache < 0 Then
                Dim Value As Integer = 1
                Try
                    Value = Settings.Get(Of Integer)("UltraAnimDetail")
                Catch ex As Exception
                    Value = 1
                End Try
                _AniDetailCache = Math.Max(0, Math.Min(2, Value))
            End If
            Return _AniDetailCache
        End Get
    End Property
    Private _AniDetailCache As Integer = -1
    ''' <summary>
    ''' 设置项改变后调用，让动画细节等级立即生效。
    ''' </summary>
    Public Sub AniDetailRefresh()
        _AniDetailCache = -1
    End Sub
    ''' <summary>
    ''' 是否处于「低」动画细节。
    ''' </summary>
    Public ReadOnly Property AniDetailIsLow As Boolean
        Get
            Return AniDetail = 0
        End Get
    End Property
    ''' <summary>
    ''' 是否处于「高」动画细节。
    ''' </summary>
    Public ReadOnly Property AniDetailIsHigh As Boolean
        Get
            Return AniDetail = 2
        End Get
    End Property

    ''' <summary>
    ''' 动画组列表。
    ''' </summary>
    Public AniGroups As New Dictionary(Of String, AniGroupEntry)
    Public Class AniGroupEntry
        Public Data As List(Of AniData)
        Public StartTick As Long
        Public Uuid As Integer = GetUuid()
        ''' <summary>本动画组在 AniGroups 中的键名，用于 O(1) 移除。</summary>
        Public Key As String = ""
    End Class
    ''' <summary>动画组集合的锁：后台线程可能随时 AniStart / AniStop。</summary>
    Private AniGroupsLock As New Object
    ''' <summary>
    ''' 上一次记刻的时间。
    ''' </summary>
    Private AniLastTick As Long
    ''' <summary>
    ''' 动画模块是否正在运行。
    ''' </summary>
    Public AniRunning As Boolean = False
    Private _AniControlEnabled As Integer = 0
    Private ReadOnly AniControlEnabledLock As New Object
    ''' <summary>
    ''' 控件动画执行是否开启。先 +1，再 -1。
    ''' </summary>
    Public Property AniControlEnabled() As Integer
        Get
            Return _AniControlEnabled
        End Get
        Set(value As Integer)
            SyncLock AniControlEnabledLock
                _AniControlEnabled = value
            End SyncLock
        End Set
    End Property

#End Region

#Region "类与枚举"

    ''' <summary>
    ''' 单个动画对象。
    ''' </summary>
    ''' <remarks></remarks>
    ''' <summary>
    ''' 单个动画对象。
    ''' </summary>
    ''' <remarks>用类而非结构体：动画每帧都要读写这些字段，结构体会产生整份拷贝，明显拖慢帧循环。</remarks>
    Public Class AniData

        ''' <summary>
        ''' 动画种类。
        ''' </summary>
        ''' <remarks></remarks>
        Public TypeMain As AniType
        ''' <summary>
        ''' 动画副种类。
        ''' </summary>
        ''' <remarks></remarks>
        Public TypeSub As AniTypeSub

        ''' <summary>
        ''' 动画总长度。
        ''' </summary>
        ''' <remarks></remarks>
        Public TimeTotal As Integer
        ''' <summary>
        ''' 已经执行的动画长度。如果为负数则为延迟。
        ''' </summary>
        ''' <remarks></remarks>
        Public TimeFinished As Double
        ''' <summary>
        ''' 已经完成的百分比。
        ''' </summary>
        ''' <remarks></remarks>
        Public TimePercent As Double

        ''' <summary>
        ''' 是否为“以后”。
        ''' </summary>
        ''' <remarks></remarks>
        Public IsAfter As Boolean

        ''' <summary>
        ''' 插值器类型。
        ''' </summary>
        ''' <remarks></remarks>
        Public Ease As AniEase
        ''' <summary>
        ''' 动画对象。
        ''' </summary>
        ''' <remarks></remarks>
        Public Obj As Object
        ''' <summary>
        ''' 动画值。
        ''' </summary>
        ''' <remarks></remarks>
        Public Value As Object
        ''' <summary>
        ''' 上次执行时的动画值。
        ''' </summary>
        ''' <remarks></remarks>
        Public ValueLast As Object

        Public Overrides Function ToString() As String
            Return TypeMain.ToString() & " | " & TimeFinished & "/" & TimeTotal & "(" & Math.Round(TimePercent * 100) & "%)" & If(Obj Is Nothing, "", " | " & Obj.ToString & "(" & Obj.GetType.Name & ")")
        End Function

    End Class

    ''' <summary>
    ''' 动画基础种类。
    ''' </summary>
    Public Enum AniType
        ''' <summary>
        ''' 单个Double的动画，包括位置、长宽、透明度等。这需要附属类型。
        ''' </summary>
        ''' <remarks></remarks>
        Number
        ''' <summary>
        ''' 颜色属性的动画。这需要附属类型。
        ''' </summary>
        ''' <remarks></remarks>
        Color
        ''' <summary>
        ''' 缩放控件大小。比起4个DoubleAnimation来说效率更高。
        ''' </summary>
        ''' <remarks></remarks>
        Scale
        ''' <summary>
        ''' 文字一个个出现。
        ''' </summary>
        ''' <remarks></remarks>
        TextAppear
        ''' <summary>
        ''' 执行代码。
        ''' </summary>
        ''' <remarks></remarks>
        Code
        ''' <summary>
        ''' 以 WPF 方式缩放控件。
        ''' </summary>
        ScaleTransform
        ''' <summary>
        ''' 以 WPF 方式旋转控件。
        ''' </summary>
        RotateTransform
    End Enum
    ''' <summary>
    ''' 动画扩展种类。
    ''' </summary>
    Public Enum AniTypeSub
        X
        Y
        Width
        Height
        Opacity
        Value
        Radius
        BorderThickness
        StrokeThickness
        TranslateX
        TranslateY
        [Double]
        DoubleParam
        GridLengthWidth
    End Enum

#End Region

#Region "种类"

    'DoubleAnimation

    ''' <summary>
    ''' 移动X轴的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaX(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.X,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 移动Y轴的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaY(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.Y,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变宽度的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">宽度改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaWidth(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.Width,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变高度的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">高度改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaHeight(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.Height,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变透明度的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">透明度改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaOpacity(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.Opacity,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变对象的Value属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">Value属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaValue(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.Value,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变对象的Radius属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">Radius属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaRadius(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.Radius,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变对象的BorderThickness属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">BorderThickness属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaBorderThickness(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.BorderThickness,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变对象的StrokeThickness属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">StrokeThickness属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaStrokeThickness(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.StrokeThickness,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 改变 Width 的 GridLength 属性的动画。必须为 Star。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">GridLength.Value 改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaGridLengthWidth(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.GridLengthWidth,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    'DoubleAnimation（Obj, Prop, [Res]）

    ''' <summary>
    ''' 改变数字属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Prop">动画的依赖属性。</param>
    ''' <param name="Value">改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaDouble(Obj As Object, Prop As DependencyProperty, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.Double, .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = {Obj, Prop, ""}, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 获取数字动画值。
    ''' </summary>
    ''' <param name="Value">改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaDouble(Lambda As ParameterizedThreadStart, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.DoubleParam, .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Lambda, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    'ColorAnimation（Obj, Prop, [Res]）

    ''' <summary>
    ''' 改变颜色属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Prop">动画的依赖属性。</param>
    ''' <param name="Value">颜色改变的值。以RGB加减法进行计算。不用担心超额。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaColor(Obj As FrameworkElement, Prop As DependencyProperty, Value As MyColor, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Color, .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = {Obj, Prop, ""}, .Value = Value, .IsAfter = After, .TimeFinished = -Delay, .ValueLast = New MyColor(0, 0, 0, 0)}
    End Function
    ''' <summary>
    ''' 改变颜色属性为一个资源的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Prop">动画的依赖属性。</param>
    ''' <param name="Res">要将颜色改变为该资源值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaColor(Obj As FrameworkElement, Prop As DependencyProperty, Res As String, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Color, .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = {Obj, Prop, Res}, .Value = New MyColor(Application.Current.FindResource(Res)) - New MyColor(Obj.GetValue(Prop)), .IsAfter = After, .TimeFinished = -Delay, .ValueLast = New MyColor(0, 0, 0, 0)}
    End Function

    'Scale

    ''' <summary>
    ''' 缩放控件的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">大小改变的百分比（如-0.6）或值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <param name="Absolute">大小改变是否为绝对值。若为 True 则为绝对像素，若为 False 则为相对百分比。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaScale(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False, Optional Absolute As Boolean = False) As AniData
        Dim ChangeRect As MyRect
        If Absolute Then
            ChangeRect = New MyRect(-0.5 * Value, -0.5 * Value, Value, Value)
        Else
            ChangeRect = New MyRect(-0.5 * Obj.ActualWidth * Value, -0.5 * Obj.ActualHeight * Value, Obj.ActualWidth * Value, Obj.ActualHeight * Value)
        End If
        Return New AniData With {.TypeMain = AniType.Scale, .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = ChangeRect, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    'TextAppear

    ''' <summary>
    ''' 让一段文字一个个字出现或消失的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。必须是Label或TextBlock。</param>
    ''' <param name="Hide">是否为一个个字隐藏。默认为False（一个个字出现）。这些字必须已经存在了。</param>
    ''' <param name="TimePerText">是否采用根据文本长度决定时间的方式。</param>
    ''' <param name="Time">动画长度（毫秒）。若TimePerText为True，这代表每个字所占据的时间。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaTextAppear(Obj As Object, Optional Hide As Boolean = False, Optional TimePerText As Boolean = True, Optional Time As Integer = 70, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        'Are we cool yet？
        Return New AniData With {.TypeMain = AniType.TextAppear, .Ease = If(Ease, New AniEaseLinear), .TimeTotal = If(TimePerText, Time * If(TypeOf Obj Is TextBlock, Obj.Text, Obj.Context.ToString).ToString.Length, Time), .Obj = Obj, .Value = {If(TypeOf Obj Is TextBlock, Obj.Text, Obj.Context.ToString), Hide}, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    'Code

    ''' <summary>
    ''' 执行代码。
    ''' </summary>
    ''' <param name="Code">一个ThreadStart。这将会在执行时在主线程调用。</param>
    ''' <param name="Delay">代码延迟执行的时间（毫秒）。</param>
    ''' <param name="After">是否等到以前的动画完成后才执行。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaCode(Code As ThreadStart, Optional Delay As Integer = 0, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Code,
                                   .TimeTotal = 1, .Value = Code, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    'ScaleTransform

    ''' <summary>
    ''' 按照 WPF 方式缩放控件的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。它必须已经拥有了单一的 ScaleTransform 值。</param>
    ''' <param name="Value">大小改变的百分比（如-0.6）。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaScaleTransform(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.ScaleTransform, .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    'RotateTransform

    ''' <summary>
    ''' 按照 WPF 方式旋转控件的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。它必须已经拥有了单一的 ScaleTransform 值。</param>
    ''' <param name="Value">旋转角度（度）。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function AaRotateTransform(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.RotateTransform, .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    'TranslateTransform

    ''' <summary>
    ''' 利用 TranslateTransform 移动 X 轴的动画，这不会造成布局更新。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaTranslateX(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.TranslateX,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function
    ''' <summary>
    ''' 利用 TranslateTransform 移动 Y 轴的动画，这不会造成布局更新。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaTranslateY(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return New AniData With {.TypeMain = AniType.Number, .TypeSub = AniTypeSub.TranslateY,
                                   .TimeTotal = Time, .Ease = If(Ease, New AniEaseLinear), .Obj = Obj, .Value = Value, .IsAfter = After, .TimeFinished = -Delay}
    End Function

    '特殊

    ''' <summary>
    ''' 将一个StackPanel中的各个项目依次显示。
    ''' </summary>
    ''' <remarks></remarks>
    Public Function AaStack(Stack As StackPanel, Optional Time As Integer = 100, Optional Delay As Integer = 25) As List(Of AniData)
        Dim Result = New List(Of AniData)
        Dim AniDelay As Integer = 0
        For Each Item In Stack.Children
            Item.Opacity = 0
            Result.Add(AaOpacity(Item, 1, Time, AniDelay))
            AniDelay += Delay
        Next
        Return Result
    End Function

#End Region

#Region "缓动函数"

    '基类
    Public Enum AniEasePower As Integer
        Weak = 2
        Middle = 3
        Strong = 4
        ExtraStrong = 5
    End Enum
    ''' <summary>
    ''' 缓动函数基类。
    ''' </summary>
    Public MustInherit Class AniEase
        ''' <summary>
        ''' 获取函数值。
        ''' </summary>
        ''' <param name="t">时间百分比。</param>
        Public MustOverride Function GetValue(t As Double) As Double
        ''' <summary>
        ''' 获取增量值。
        ''' </summary>
        ''' <param name="t1">较大的 X。</param>
        ''' <param name="t0">较小的 X。</param>
        Public Overridable Function GetDelta(t1 As Double, t0 As Double) As Double
            Return GetValue(t1) - GetValue(t0)
        End Function
    End Class
    ''' <summary>
    ''' 渐入渐出组合。
    ''' </summary>
    Public Class AniEaseInout
        Inherits AniEase
        Private ReadOnly EaseIn As AniEase, EaseOut As AniEase, EaseInPercent As Double
        Public Sub New(EaseIn As AniEase, EaseOut As AniEase, Optional EaseInPercent As Double = 0.5)
            Me.EaseIn = EaseIn : Me.EaseOut = EaseOut : Me.EaseInPercent = EaseInPercent
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            If t < EaseInPercent Then
                Return EaseInPercent * EaseIn.GetValue(t / EaseInPercent)
            Else
                Return (1 - EaseInPercent) * EaseOut.GetValue((t - EaseInPercent) / (1 - EaseInPercent)) + EaseInPercent
            End If
        End Function
    End Class

    'Linear / 线性
    ''' <summary>
    ''' 线性，无缓动。
    ''' </summary>
    Public Class AniEaseLinear
        Inherits AniEase
        Public Overrides Function GetValue(t As Double) As Double
            Return t.Clamp(0, 1)
        End Function
        Public Overrides Function GetDelta(t1 As Double, t0 As Double) As Double
            Return t1.Clamp(0, 1) - t0.Clamp(0, 1)
        End Function
    End Class

    'Fluent / 平滑
    ''' <summary>
    ''' 平滑开始。
    ''' </summary>
    Public Class AniEaseInFluent
        Inherits AniEase
        Private ReadOnly p As AniEasePower
        Public Sub New(Optional Power As AniEasePower = AniEasePower.Middle)
            p = Power
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            Return t.Clamp(0, 1) ^ p
        End Function
    End Class
    ''' <summary>
    ''' 平滑结束。
    ''' </summary>
    Public Class AniEaseOutFluent
        Inherits AniEase
        Private ReadOnly p As AniEasePower
        Public Sub New(Optional Power As AniEasePower = AniEasePower.Middle)
            p = Power
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            Return 1 - (1 - t).Clamp(0, 1) ^ p
        End Function
    End Class
    ''' <summary>
    ''' 平滑开始与结束。
    ''' </summary>
    Public Class AniEaseInoutFluent
        Inherits AniEase
        Private Ease As AniEaseInout
        Public Sub New(Optional Power As AniEasePower = AniEasePower.Middle, Optional Middle As Double = 0.5)
            Ease = New AniEaseInout(New AniEaseInFluent(Power), New AniEaseOutFluent(Power), Middle)
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            Return Ease.GetValue(t)
        End Function
    End Class
    ''' <summary>
    ''' 以特定速度开始的平滑结束。
    ''' </summary>
    Public Class AniEaseOutFluentWithInitial
        Inherits AniEase
        Private ReadOnly alpha As Double '(初速度 / 平均速度) – 1
        ''' <param name="InitialPixelPerSecond">初速度，px/s</param>
        ''' <param name="TotalSecond">总时长，s</param>
        ''' <param name="TotalDistance">总路程，px</param>
        Public Sub New(InitialPixelPerSecond As Double, TotalSecond As Double, TotalDistance As Double)
            Dim v0_norm As Double = InitialPixelPerSecond * TotalSecond / TotalDistance '归一化初速度
            alpha = v0_norm - 1.0
            If alpha < 0 Then alpha = 0 '初速度小于平均速度时，退化为线性
        End Sub
        Public Overrides Function GetValue(percent As Double) As Double
            Dim p As Double = percent.Clamp(0, 1)
            If alpha = 0 Then Return p '退化到线性
            Return (alpha + 1) * p / (1 + alpha * p)
        End Function
    End Class

    'Back / 回弹
    ''' <summary>
    ''' 回弹开始。有效时间为 1/3。
    ''' </summary>
    Public Class AniEaseInBack
        Inherits AniEase
        Private ReadOnly p As Double
        Public Sub New(Optional Power As AniEasePower = AniEasePower.Middle)
            p = 3 - Power * 0.5
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            t = t.Clamp(0, 1)
            Return t ^ p * Math.Cos(1.5 * Math.PI * (1 - t))
        End Function
    End Class
    ''' <summary>
    ''' 回弹结束。有效时间为 1/3。
    ''' </summary>
    Public Class AniEaseOutBack
        Inherits AniEase
        Private ReadOnly p As Double
        Public Sub New(Optional Power As AniEasePower = AniEasePower.Middle)
            p = 3 - Power * 0.5
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            t = t.Clamp(0, 1)
            Return 1 - (1 - t) ^ p * Math.Cos(1.5 * Math.PI * t)
        End Function
    End Class

    'Car / 平滑-回弹
    ''' <summary>
    ''' 回弹开始，短平滑结束。
    ''' </summary>
    Public Class AniEaseInCar
        Inherits AniEase
        Private Ease As AniEaseInout
        Public Sub New(Optional Middle As Double = 0.7, Optional Power As AniEasePower = AniEasePower.Middle)
            Ease = New AniEaseInout(New AniEaseInBack(Power), New AniEaseOutFluent(Power), Middle)
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            Return Ease.GetValue(t)
        End Function
    End Class
    ''' <summary>
    ''' 短平滑开始，回弹结束。
    ''' </summary>
    Public Class AniEaseOutCar
        Inherits AniEase
        Private Ease As AniEaseInout
        Public Sub New(Optional Middle As Double = 0.3, Optional Power As AniEasePower = AniEasePower.Middle)
            Ease = New AniEaseInout(New AniEaseInFluent(Power), New AniEaseOutBack(Power), Middle)
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            Return Ease.GetValue(t)
        End Function
    End Class

    'Elastic / 弹簧
    ''' <summary>
    ''' 弹簧开始。约在 60% 到达最小值。
    ''' </summary>
    Public Class AniEaseInElastic
        Inherits AniEase
        Private ReadOnly p As Integer '6~9
        Public Sub New(Optional Power As AniEasePower = AniEasePower.Middle)
            p = Power + 4
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            t = t.Clamp(0, 1)
            Return t ^ ((p - 1) * 0.25) * Math.Cos((p - 3.5) * Math.PI * (1 - t) ^ 1.5)
        End Function
    End Class
    ''' <summary>
    ''' 弹簧结束。约在 40% 到达最大值。
    ''' </summary>
    Public Class AniEaseOutElastic
        Inherits AniEase
        Private ReadOnly p As Integer
        Public Sub New(Optional Power As AniEasePower = AniEasePower.Middle)
            p = Power + 4
        End Sub
        Public Overrides Function GetValue(t As Double) As Double
            t = 1 - t.Clamp(0, 1)
            Return 1 - t ^ ((p - 1) * 0.25) * Math.Cos((p - 3.5) * Math.PI * (1 - t) ^ 1.5)
        End Function
    End Class

#End Region

#Region "接口（开始、中断、检测）"

    ''' <summary>
    ''' 开始一个动画组。
    ''' </summary>
    ''' <param name="AniGroup">由 Aa 开头的函数初始化的 AniData 对象集合。</param>
    ''' <param name="Name">动画组的名称。如果重复会直接停止同名动画组。</param>
    Public Sub AniStart(AniGroup As IList, Optional Name As String = "", Optional RefreshTime As Boolean = False)
        If RefreshTime Then AniLastTick = GetTimeMs() '避免处理动画时已经造成了极大的延迟，导致动画突然结束
        '添加到正在执行的动画组
        Dim AllAnis = New List(Of AniData)
        For Each Element In AniGroup
            If TypeOf Element Is ICollection Then
                AllAnis.AddRange(Element)
            Else
                AllAnis.Add(Element)
            End If
        Next
        Dim NewEntry As New AniGroupEntry With {.Data = AllAnis, .StartTick = GetTimeMs()}
        If Name = "" Then
            Name = NewEntry.Uuid
        Else
            AniStop(Name)
        End If
        NewEntry.Key = Name
        SyncLock AniGroupsLock
            AniGroups.Add(Name, NewEntry)
        End SyncLock
    End Sub
    ''' <summary>
    ''' 开始一个动画组。
    ''' </summary>
    Public Sub AniStart(AniGroup As AniData, Optional Name As String = "", Optional RefreshTime As Boolean = False)
        AniStart(New List(Of AniData) From {AniGroup}, Name, RefreshTime)
    End Sub
    ''' <summary>
    ''' 直接停止一个动画组。
    ''' </summary>
    ''' <param name="name">需要停止的动画组的名称。</param>
    Public Sub AniStop(Name As String)
        SyncLock AniGroupsLock
            AniGroups.Remove(Name)
        End SyncLock
    End Sub
    ''' <summary>
    ''' 获取动画是否正在进行中。
    ''' </summary>
    Public Function AniIsRun(Name As String) As Boolean
        SyncLock AniGroupsLock
            Return AniGroups.ContainsKey(Name)
        End SyncLock
    End Function

#End Region

    <Runtime.InteropServices.DllImport("winmm.dll", EntryPoint:="timeBeginPeriod")>
    Private Function WinTimeBeginPeriod(Period As Integer) As Integer
    End Function

    Private AniCount As Integer = 0
    Private AniFPSCounter As Integer = 0
    Private AniFPSTimer As Long = 0
    ''' <summary>
    ''' 当前的动画 FPS。
    ''' </summary>
    Public AniFPS As Integer = 0

    ''' <summary>
    ''' 开始动画执行。
    ''' </summary>
    Public Sub AniStart()
        '把系统计时器精度提到 1ms：否则 Thread.Sleep(1) 实际会睡 15ms，动画一卡一卡的
        Static TimerBoosted As Boolean = False
        If Not TimerBoosted Then
            TimerBoosted = True
            Try
                WinTimeBeginPeriod(1)
            Catch ex As Exception
                Logger.Warn(ex, "提高系统计时器精度失败")
            End Try
        End If
        '初始化计时器
        AniLastTick = GetTimeMs()
        AniFPSTimer = AniLastTick
        AniRunning = True '标记动画执行开始

        RunInNewThread(
        Sub()
            Try
                Logger.Info("动画线程开始")
                Do While True
                    '两帧之间的间隔时间
                    Dim DeltaTime As Long = (GetTimeMs() - AniLastTick).Clamp(0, 100000)
                    If DeltaTime < 3 Then GoTo Sleeper
                    AniLastTick = GetTimeMs()
                    '记录 FPS
                    If ModeDebug Then
                        If (AniLastTick - AniFPSTimer).Clamp(0, 100000) >= 500 Then
                            AniFPS = AniFPSCounter
                            AniFPSCounter = 0
                            AniFPSTimer = AniLastTick
                        End If
                        AniFPSCounter += 2
                    End If
                    '执行动画
                    RunInUiWait(
                    Sub()
                        AniCount = 0
                        AniTimer(DeltaTime * AniSpeed)
                        If RandomInteger(0, 64 * If(ModeDebug, 5, 30)) = 0 AndAlso ((AniFPS < 62 AndAlso AniFPS > 0) OrElse AniCount > 4 OrElse NetManager.FileRemain <> 0) Then
                            Logger.Info($"FPS {AniFPS}, 动画 {AniCount}, 下载中 {NetManager.FileRemain}（{StringUtils.FormatByteSize(NetManager.Speed)}/s）")
                            If NetManager.FileRemain < 5 Then
                                For Each File In NetManager.AllFiles.Values
                                    If File.State < NetState.Finished Then Logger.Trace($"- {File.State} {StringUtils.FormatByteSize(File.Speed)}/s {File.LocalPath}")
                                Next
                            End If
                        End If
                    End Sub)
Sleeper:
                    '控制 FPS
                    Thread.Sleep(1)
                Loop
            Catch ex As Exception
                Logger.Error(ex, "动画帧执行失败", LogBehavior.AlertThenCrash)
            End Try
        End Sub, "Animation", ThreadPriority.AboveNormal)
    End Sub

    ''' <summary>
    ''' 动画定时器事件。
    ''' </summary>
    Public Sub AniTimer(DeltaTick As Double)
        Try
            If DeltaTick / AniSpeed > 100 Then Logger.Info($"两个动画帧间隔 {DeltaTick} ms")
            '每帧只取一次快照：原来反复访问 Dictionary.Values(i)，每次都会新建集合并做 O(n) 查找
            Dim GroupCount As Integer = 0
            Dim Snapshot As AniGroupEntry() = Nothing
            SyncLock AniGroupsLock
                GroupCount = AniGroups.Count
                If GroupCount > 0 Then
                    Snapshot = New AniGroupEntry(GroupCount - 1) {}
                    Dim n As Integer = 0
                    For Each Kv In AniGroups
                        If n >= GroupCount Then Exit For
                        Snapshot(n) = Kv.Value
                        n += 1
                    Next
                End If
            End SyncLock
            If GroupCount = 0 Then Exit Sub
            Dim i As Integer = -1
            '循环每个动画组
            Do While i + 1 < GroupCount
                i += 1
                '初始化
                Dim Entry As AniGroupEntry = Snapshot(i)
                If Entry Is Nothing Then Continue Do
                If Entry.StartTick > AniLastTick Then Continue Do '跳过本刻之后开始的动画
                Dim CanRemoveAfter = True '是否应该去除“之后”标记
                Dim ii = 0

                '循环每个动画
                Do While ii < Entry.Data.Count
                    Dim Anim As AniData = Entry.Data(ii)
                    '执行种类
                    If Anim.IsAfter = False Then '之前
                        CanRemoveAfter = False '取消“之后”标记 
                        '增加执行时间
                        Anim.TimeFinished += DeltaTick
                        '执行动画
                        If Anim.TimeFinished > 0 Then
                            Anim = AniRun(Anim)
                            AniCount += 1
                        End If
                        '如果当前动画已执行完毕
                        If Anim.TimeFinished >= Anim.TimeTotal Then
                            '如果是去向颜色资源的动画，设置引用
                            If Anim.TypeMain = AniType.Color AndAlso Not Anim.Obj(2) = "" Then Anim.Obj(0).SetResourceReference(Anim.Obj(1), Anim.Obj(2))
                            '删除
                            Entry.Data.RemoveAt(ii)
                            GoTo NextAni
                        End If
                    Else '之后
                        If CanRemoveAfter Then
                            '之后改为之前
                            CanRemoveAfter = False
                            Anim.IsAfter = False
                            '重新循环该动画
                            GoTo NextAni
                        Else
                            '不能去除该“之后”标记，结束该动画组
                            Exit Do
                        End If
                    End If
                    ii += 1
NextAni:
                Loop

                '如果当前动画组都执行完毕则删除
                If Not Entry.Data.Any() Then
                    '按 Key + Uuid 双重校验后 O(1) 删除；Uuid 校验是为了避免误删同名的新组
                    If Entry.Key <> "" Then
                        SyncLock AniGroupsLock
                            Dim Current As AniGroupEntry = Nothing
                            If AniGroups.TryGetValue(Entry.Key, Current) AndAlso Current.Uuid = Entry.Uuid Then
                                AniGroups.Remove(Entry.Key)
                            End If
                        End SyncLock
                    End If
                    '注意：这里不能像原版那样 i -= 1。
                    '原版遍历的是 Dictionary.Values(i)，删除元素后索引会整体前移，所以要回退一格；
                    '现在遍历的是本帧开头取好的快照数组，删除并不会让快照前移，
                    '若仍回退就会反复处理同一个已完成的组，形成死循环（程序直接卡死）。
                End If
            Loop

        Catch ex As Exception
            Logger.Error(ex, "动画刻执行失败", LogBehavior.Toast)
        End Try
    End Sub

    ''' <summary>
    ''' 执行一个动画。
    ''' </summary>
    ''' <param name="Ani">执行的动画对象。</param>
    Private Function AniRun(Ani As AniData) As AniData
        Try
            Select Case Ani.TypeMain

                Case AniType.Number
                    Dim Delta As Double = MathUtils.Lerp(0, Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent))
                    If Delta <> 0 Then
                        Select Case Ani.TypeSub
                            Case AniTypeSub.X
                                If TypeOf Ani.Obj Is Window Then
                                    CType(Ani.Obj, Window).Left += Delta
                                Else
                                    Select Case Ani.Obj.HorizontalAlignment
                                        Case HorizontalAlignment.Left, HorizontalAlignment.Stretch
                                            Ani.Obj.Margin = New Thickness(Ani.Obj.Margin.Left + Delta, Ani.Obj.Margin.Top, Ani.Obj.Margin.Right, Ani.Obj.Margin.Bottom)
                                        Case HorizontalAlignment.Right
                                            Ani.Obj.Margin = New Thickness(Ani.Obj.Margin.Left, Ani.Obj.Margin.Top, Ani.Obj.Margin.Right - Delta, Ani.Obj.Margin.Bottom)
                                    End Select
                                End If
                            Case AniTypeSub.Y
                                If TypeOf Ani.Obj Is Window Then
                                    CType(Ani.Obj, Window).Top += Delta
                                Else
                                    Select Case Ani.Obj.VerticalAlignment
                                        Case VerticalAlignment.Top
                                            Ani.Obj.Margin = New Thickness(Ani.Obj.Margin.Left, Ani.Obj.Margin.Top + Delta, Ani.Obj.Margin.Right, Ani.Obj.Margin.Bottom)
                                        Case VerticalAlignment.Bottom
                                            Ani.Obj.Margin = New Thickness(Ani.Obj.Margin.Left, Ani.Obj.Margin.Top, Ani.Obj.Margin.Right, Ani.Obj.Margin.Bottom - Delta)
                                    End Select
                                End If
                            Case AniTypeSub.Opacity
                                If TypeOf Ani.Obj Is UIElement Then
                                    Dim Obj As UIElement = Ani.Obj
                                    Obj.Opacity = (Obj.Opacity + Delta).Clamp(0, 1)
                                ElseIf TypeOf Ani.Obj Is DropShadowEffect Then
                                    Dim Obj As DropShadowEffect = Ani.Obj
                                    Obj.Opacity = (Obj.Opacity + Delta).Clamp(0, 1)
                                End If
                            Case AniTypeSub.Width
                                Dim Obj As FrameworkElement = Ani.Obj
                                Obj.Width = Math.Max(If(Double.IsNaN(Obj.Width), Obj.ActualWidth, Obj.Width) + Delta, 0)
                            Case AniTypeSub.Height
                                Dim Obj As FrameworkElement = Ani.Obj
                                Obj.Height = Math.Max(If(Double.IsNaN(Obj.Height), Obj.ActualHeight, Obj.Height) + Delta, 0)
                            Case AniTypeSub.Value
                                Ani.Obj.Value += Delta
                            Case AniTypeSub.Radius
                                Ani.Obj.Radius += Delta
                            Case AniTypeSub.StrokeThickness
                                Ani.Obj.StrokeThickness = Math.Max(Ani.Obj.StrokeThickness + Delta, 0)
                            Case AniTypeSub.BorderThickness
                                Ani.Obj.BorderThickness = New Thickness(CType(Ani.Obj.BorderThickness, Thickness).Bottom + Delta)
                            Case AniTypeSub.TranslateX
                                If IsNothing(Ani.Obj.RenderTransform) OrElse TypeOf Ani.Obj.RenderTransform IsNot TranslateTransform Then Ani.Obj.RenderTransform = New TranslateTransform(0, 0)
                                CType(Ani.Obj.RenderTransform, TranslateTransform).X += Delta
                            Case AniTypeSub.TranslateY
                                If IsNothing(Ani.Obj.RenderTransform) OrElse TypeOf Ani.Obj.RenderTransform IsNot TranslateTransform Then Ani.Obj.RenderTransform = New TranslateTransform(0, 0)
                                CType(Ani.Obj.RenderTransform, TranslateTransform).Y += Delta
                            Case AniTypeSub.Double
                                Ani.Obj(0).SetValue(Ani.Obj(1), Ani.Obj(0).GetValue(Ani.Obj(1)) + Delta)
                            Case AniTypeSub.DoubleParam
                                CType(Ani.Obj, ParameterizedThreadStart)(Delta)
                            Case AniTypeSub.GridLengthWidth
                                Ani.Obj.Width = New GridLength(Math.Max(Ani.Obj.Width.Value + Delta, 0), GridUnitType.Star)
                        End Select
                    End If

                Case AniType.Color
                    '利用 Last 记录了余下的小数值
                    Dim Delta As MyColor = MyColor.Lerp(New MyColor(0, 0, 0, 0), Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent)) + Ani.ValueLast
                    Dim Obj As FrameworkElement = Ani.Obj(0)
                    Dim Prop As DependencyProperty = Ani.Obj(1)
                    Dim NewColor As MyColor = New MyColor(Obj.GetValue(Prop)) + Delta
                    Obj.SetValue(Prop, If(Prop.PropertyType.Name = "Color", CType(NewColor, Color), CType(NewColor, SolidColorBrush)))
                    Ani.ValueLast = NewColor - New MyColor(Obj.GetValue(Prop))

                Case AniType.Scale
                    Dim Obj As FrameworkElement = Ani.Obj
                    Dim Delta As Double = Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent)
                    Obj.Margin = New Thickness(Obj.Margin.Left + MathUtils.Lerp(0, Ani.Value.Left, Delta), Obj.Margin.Top + MathUtils.Lerp(0, Ani.Value.Top, Delta), Obj.Margin.Right + MathUtils.Lerp(0, Ani.Value.Left, Delta), Obj.Margin.Bottom + MathUtils.Lerp(0, Ani.Value.Top, Delta))
                    Obj.Width = Math.Max(Obj.Width + MathUtils.Lerp(0, Ani.Value.Width, Delta), 0)
                    Obj.Height = Math.Max(Obj.Height + MathUtils.Lerp(0, Ani.Value.Height, Delta), 0)

                Case AniType.TextAppear
                    Dim Text As String = Ani.Value(0)
                    Dim TextCount As Integer = If(Ani.Value(1), Text.Length, 0) +
                                               Math.Round(Text.Length * If(Ani.Value(1), -1, 1) * Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, 0))
                    Dim NewText As String = Text.Substring(0, TextCount)
                    '添加乱码
                    If TextCount < Text.Length Then
                        Dim NextText As String = Text.Substring(TextCount, 1)
                        If Convert.ToInt32(Convert.ToChar(NextText)) >= Convert.ToInt32(Convert.ToChar(128)) Then
                            NewText &= Encoding.GetEncoding("GB18030").GetString({RandomInteger(16 + 160, 87 + 160), RandomInteger(1 + 160, 89 + 160)})
                        Else
                            NewText &= RandomInteger(0, 85).ConvertRadix(86)
                        End If
                    End If
                    '设置文本
                    If TypeOf Ani.Obj Is TextBlock Then
                        Ani.Obj.Text = NewText
                    Else
                        Ani.Obj.Context = NewText
                    End If

                Case AniType.Code
                    Ani.TimeFinished = Ani.TimeTotal
                    CType(Ani.Value, ThreadStart)()

                Case AniType.ScaleTransform
                    Dim Obj As FrameworkElement = Ani.Obj
                    If TypeOf Obj.RenderTransform IsNot ScaleTransform Then
                        If Obj.RenderTransformOrigin = New Point(0, 0) Then Obj.RenderTransformOrigin = New Point(0.5, 0.5)
                        Obj.RenderTransform = New ScaleTransform(1, 1)
                    End If
                    Dim Delta As Double = MathUtils.Lerp(0, Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent))
                    CType(Obj.RenderTransform, ScaleTransform).ScaleX = Math.Max(CType(Obj.RenderTransform, ScaleTransform).ScaleX + Delta, 0)
                    CType(Obj.RenderTransform, ScaleTransform).ScaleY = Math.Max(CType(Obj.RenderTransform, ScaleTransform).ScaleY + Delta, 0)

                Case AniType.RotateTransform
                    Dim Obj As FrameworkElement = Ani.Obj
                    If TypeOf Obj.RenderTransform IsNot RotateTransform Then
                        If Obj.RenderTransformOrigin = New Point(0, 0) Then Obj.RenderTransformOrigin = New Point(0.5, 0.5)
                        Obj.RenderTransform = New RotateTransform(0)
                    End If
                    Dim Delta As Double = MathUtils.Lerp(0, Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent))
                    CType(Obj.RenderTransform, RotateTransform).Angle = CType(Obj.RenderTransform, RotateTransform).Angle + Delta

            End Select
            Ani.TimePercent = Ani.TimeFinished / Ani.TimeTotal '修改执行百分比
        Catch ex As Exception
            Logger.Error(ex, $"执行动画失败：{Ani}", LogBehavior.Toast)
        End Try
        Return Ani
    End Function

End Module
