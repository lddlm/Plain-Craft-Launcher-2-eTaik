Imports System.Diagnostics
Imports System.Windows.Shapes
Imports System.Windows.Threading

''' <summary>
''' 工具箱 → 录制页面。
''' Ultra：内置原生录制（D3D11 桌面复制 + Media Foundation 硬件编码），不依赖外部程序。
''' </summary>
Public Class PageToolRecord

    Private WithEvents StatusTimer As New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
    Private KernelProbed As Boolean = False
    Private KernelProbing As Boolean = False

    Private Sub PageToolRecord_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        Static Reloaded As Boolean = False
        If Not Reloaded Then
            Reloaded = True
            AniControlEnabled += 1
            Reload()
            AniControlEnabled -= 1
            AddHandler UltraVideo.StatusChanged, AddressOf OnVideoStatusChanged
            AddHandler ComboHeight.SelectionChanged, AddressOf OnSpecChanged
            AddHandler ComboFps.SelectionChanged, AddressOf OnSpecChanged
            AddHandler ComboCodec.SelectionChanged, AddressOf OnSpecChanged
        End If
        LabHotkeyTip.Text = $"游戏内按 {UltraVideo.HotkeyText} 可以随时开始或结束录制，不会打断游戏操作。按 {UltraScreenshot.HotkeyText} 可以截屏。"
        RefreshStatus()
        StatusTimer.Start()
    End Sub

    Private Sub PageToolRecord_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        StatusTimer.Stop()
    End Sub

    ''' <summary>
    ''' 重新载入设置与状态。
    ''' </summary>
    Public Sub Reload()
        SettingService.RefreshSettings(Me)
        LabFolder.Text = UltraVideo.VideoFolder
        RefreshKernel()
        RefreshSpecLimits()
        RefreshStatus()
    End Sub

    ''' <summary>
    ''' 刷新录制状态区与按钮文字。
    ''' </summary>
    Private Sub RefreshStatus()
        Try
            If UltraVideo.IsRecording Then
                LabState.Text = "正在录制"
                ShapeState.SetResourceReference(Shape.FillProperty, "ColorBrushRedLight")
                LabElapsed.Text = "· " & FormatElapsed(UltraVideo.RecordingElapsed)
                LabFile.Visibility = Visibility.Visible
                LabFile.Text = UltraVideo.RecordingName &
                    If(UltraVideo.RecordingInfo = "", "", "　·　" & UltraVideo.RecordingInfo)
                BtnRecord.Text = "停止录制"
            Else
                LabState.Text = "未在录制"
                ShapeState.SetResourceReference(Shape.FillProperty, "ColorBrushGray4")
                LabElapsed.Text = ""
                LabFile.Visibility = Visibility.Collapsed
                BtnRecord.Text = "开始录制"
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新录制状态失败")
        End Try
    End Sub

    Private Shared Function FormatElapsed(Value As TimeSpan) As String
        Return $"{CInt(Value.TotalMinutes):00}:{Value.Seconds:00}"
    End Function

    '==== 分辨率 × 帧率 的互斥置灰 ====

    Private ApplyingLimits As Boolean = False
    Private SpecProbing As Boolean = False

    Private Sub OnSpecChanged(sender As Object, e As SelectionChangedEventArgs)
        If ApplyingLimits Then Return
        RefreshSpecLimits()
    End Sub

    ''' <summary>问一遍编码器：当前分辨率能跑哪些帧率、当前帧率能跑哪些分辨率，跑不了的就置灰。</summary>
    Private Sub RefreshSpecLimits()
        If SpecProbing OrElse ApplyingLimits Then Return
        SpecProbing = True
        Dim Codec As Integer = ComboValue(ComboCodec, 0)
        Dim CurH As Integer = ComboValue(ComboHeight, 1080)
        Dim CurF As Integer = ComboValue(ComboFps, 60)
        Dim FpsList As List(Of Integer) = ComboValues(ComboFps)
        Dim HeightList As List(Of Integer) = ComboValues(ComboHeight)
        RunInNewThread(
        Sub()
            Dim FpsStates As New Dictionary(Of Integer, Boolean)
            For Each F As Integer In FpsList
                FpsStates(F) = UltraVideoNative.IsSupported(CurH, F, Codec)
            Next
            '当前帧率在这个分辨率下用不了的话，自动退到能用的最高帧率
            Dim NewF As Integer = CurF
            Dim CurOk As Boolean = True
            If FpsStates.ContainsKey(CurF) Then CurOk = FpsStates(CurF)
            If Not CurOk Then
                Dim Best As Integer = 0
                For Each F As Integer In FpsList
                    If FpsStates(F) AndAlso F > Best Then Best = F
                Next
                If Best > 0 Then NewF = Best
            End If
            Dim HeightStates As New Dictionary(Of Integer, Boolean)
            For Each H As Integer In HeightList
                HeightStates(H) = UltraVideoNative.IsSupported(H, NewF, Codec)
            Next
            FrmMain.Dispatcher.BeginInvoke(New Action(
            Sub()
                ApplyingLimits = True
                Try
                    If NewF <> CurF Then
                        SetComboValue(ComboFps, NewF)
                        Hint($"这个分辨率下最高只能录 {NewF} 帧，已自动切换", HintType.Blue, False)
                    End If
                    ApplyStates(ComboFps, FpsStates)
                    ApplyStates(ComboHeight, HeightStates)
                    Dim FpsText As String = String.Join(" ", FpsStates.Select(Function(kv) $"{kv.Key}{(If(kv.Value, "✓", "✗"))}"))
                    Dim HeightText As String = String.Join(" ", HeightStates.Select(Function(kv) $"{If(kv.Key = 0, "原画", kv.Key.ToString())}{(If(kv.Value, "✓", "✗"))}"))
                    Logger.Info($"[VideoSpec] 编码 {(If(Codec = 1, "H.265", "H.264"))}，{(If(CurH = 0, "原画", CurH.ToString()))} 下 帧率：{FpsText}；{(NewF)} 帧下 分辨率：{HeightText}")
                Catch ex As Exception
                    Logger.Error(ex, "刷新录制规格限制失败")
                Finally
                    ApplyingLimits = False
                    SpecProbing = False
                End Try
            End Sub))
        End Sub, "VideoSpecLimits")
    End Sub

    Private Shared Function ComboValue(Combo As MyComboBox, Fallback As Integer) As Integer
        If Combo Is Nothing Then Return Fallback
        Dim Item As MyComboBoxItem = TryCast(Combo.SelectedItem, MyComboBoxItem)
        If Item Is Nothing Then Return Fallback
        Dim Value As Integer
        If Integer.TryParse(SettingService.GetValue(Item), Value) Then Return Value
        Return Fallback
    End Function

    Private Shared Sub SetComboValue(Combo As MyComboBox, Value As Integer)
        If Combo Is Nothing Then Return
        For i As Integer = 0 To Combo.Items.Count - 1
            Dim Item As MyComboBoxItem = TryCast(Combo.Items(i), MyComboBoxItem)
            If Item IsNot Nothing AndAlso SettingService.GetValue(Item) = Value.ToString() Then
                Combo.SelectedIndex = i
                Return
            End If
        Next
    End Sub

    Private Shared Function ComboValues(Combo As MyComboBox) As List(Of Integer)
        Dim Result As New List(Of Integer)
        If Combo Is Nothing Then Return Result
        For i As Integer = 0 To Combo.Items.Count - 1
            Dim Item As MyComboBoxItem = TryCast(Combo.Items(i), MyComboBoxItem)
            If Item Is Nothing Then Continue For
            Dim Value As Integer
            If Integer.TryParse(SettingService.GetValue(Item), Value) Then Result.Add(Value)
        Next
        Return Result
    End Function

    Private Shared Sub ApplyStates(Combo As MyComboBox, States As Dictionary(Of Integer, Boolean))
        If Combo Is Nothing Then Return
        For i As Integer = 0 To Combo.Items.Count - 1
            Dim Item As MyComboBoxItem = TryCast(Combo.Items(i), MyComboBoxItem)
            If Item Is Nothing Then Continue For
            Dim Value As Integer
            If Not Integer.TryParse(SettingService.GetValue(Item), Value) Then Continue For
            Dim Ok As Boolean = True
            If States.ContainsKey(Value) Then Ok = States(Value)
            '当前选中的这一项不置灰，否则看起来像坏了
            Item.IsEnabled = Ok OrElse i = Combo.SelectedIndex
        Next
    End Sub

    ''' <summary>检测内置录制内核（显卡、桌面复制、编码器），在后台跑，避免卡界面。</summary>
    Private Sub RefreshKernel(Optional Force As Boolean = False)
        If KernelProbing Then Return
        If KernelProbed AndAlso Not Force Then Return
        KernelProbing = True
        KernelProbed = True
        LabKernel.Text = "正在检测……"
        RunInNewThread(
        Sub()
            Dim Info As String = ""
            Try
                Info = UltraVideoNative.Probe()
            Catch ex As Exception
                Info = "检测失败：" & ex.Message
            End Try
            FrmMain.Dispatcher.BeginInvoke(New Action(
            Sub()
                LabKernel.Text = Info
                KernelProbing = False
            End Sub))
        End Sub, "VideoKernelProbe")
    End Sub

    Private Sub OnVideoStatusChanged(sender As Object, e As EventArgs)
        If Not Dispatcher.CheckAccess() Then
            Dispatcher.BeginInvoke(New Action(AddressOf RefreshStatus))
            Return
        End If
        RefreshStatus()
    End Sub

    Private Sub StatusTimer_Tick(sender As Object, e As EventArgs) Handles StatusTimer.Tick
        If Not UltraVideo.IsRecording Then
            If BtnRecord.Text <> "开始录制" Then RefreshStatus()
            Return
        End If
        RefreshStatus()
    End Sub

    Private Sub BtnRecord_Click() Handles BtnRecord.Click
        If UltraVideo.IsRecording Then
            UltraVideo.StopRecording()
        Else
            UltraVideo.StartRecording()
        End If
        RefreshStatus()
    End Sub

    Private Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click
        Try
            UltraVideo.EnsureFolder()
            Process.Start("explorer.exe", UltraVideo.VideoFolder.TrimEnd("\"c))
        Catch ex As Exception
            Logger.Error(ex, "打开录制文件夹失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub BtnDetect_Click() Handles BtnDetect.Click
        RefreshKernel(Force:=True)
    End Sub

End Class
