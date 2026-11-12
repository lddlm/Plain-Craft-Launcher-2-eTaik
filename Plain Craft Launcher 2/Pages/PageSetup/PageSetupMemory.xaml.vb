''' <summary>
''' 设置 → 内存优化：手动优化（与百宝箱同源）、达到阈值自动优化、启动游戏时优化。
''' </summary>
Public Class PageSetupMemory

    Private Sub PageSetupMemory_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        Static Reloaded As Boolean = False
        If Not Reloaded Then
            Reloaded = True
            AniControlEnabled += 1
            ApplySliderRange()
            Reload()
            AniControlEnabled -= 1
        End If
        RefreshMemoryUI()
    End Sub

    Public Sub Reload()
        ApplySliderRange()
        SettingService.RefreshSettings(Me)
        RefreshMemoryUI()
    End Sub

    ''' <summary>把滑块上限对齐到本机物理内存，并清理越界的旧值。</summary>
    Private Sub ApplySliderRange()
        Dim TotalGb = CInt(Math.Ceiling(CDbl(My.Computer.Info.TotalPhysicalMemory) / 1024 / 1024 / 1024))
        If TotalGb < 4 Then TotalGb = 4
        SliderMemoryThreshold.MaxValue = TotalGb
        SliderMemoryThreshold.GetHintText = Function(v) v & " GB"
        '旧值超出本机内存范围时（例如从更大内存的机器迁移而来），回落为默认值
        Dim Saved = Settings.Get(Of Integer)("MemoryAutoThresholdGb")
        If Saved < 1 OrElse Saved > TotalGb Then Settings.Reset("MemoryAutoThresholdGb")
    End Sub

    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化内存优化设置")
            Hint("已初始化内存优化设置！", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化内存优化设置失败", LogBehavior.Alert)
        End Try
        Reload()
    End Sub

    ''' <summary>刷新当前内存状态与阈值文字。</summary>
    Private Sub RefreshMemoryUI()
        If LabMemoryNow Is Nothing Then Return
        Dim Total = CDbl(My.Computer.Info.TotalPhysicalMemory)
        Dim Avail = CDbl(My.Computer.Info.AvailablePhysicalMemory)
        Dim UsedPct = CInt(Math.Round((Total - Avail) / Total * 100))
        LabMemoryNow.Text = $"当前可用内存 {Avail / 1024 / 1024 / 1024:F1} GB（系统占用 {UsedPct}%）"
        Dim Threshold = FormMain.MemoryAutoThresholdGb()
        LabMemoryThreshold.Text = $"已用内存高于 {Threshold} GB 时触发"
        LabMemoryAutoNow.Text = $"当前已用 {(Total - Avail) / 1024 / 1024 / 1024:F1} GB / 共 {Total / 1024 / 1024 / 1024:F1} GB"
    End Sub

    Private Sub BtnMemoryNow_Click(sender As Object, e As EventArgs) Handles BtnMemoryNow.Click
        Dim Before = My.Computer.Info.AvailablePhysicalMemory
        PageOtherTest.MemoryOptimize(True)
        Dim Freed = (My.Computer.Info.AvailablePhysicalMemory - Before) / 1024 / 1024 / 1024
        If Freed > 0.005 Then Hint($"本次释放约 {Freed:F2} GB 内存", HintType.Green, False)
        RefreshMemoryUI()
    End Sub

    Private Sub SliderMemoryThreshold_Change(sender As Object, user As Boolean) Handles SliderMemoryThreshold.Change
        If LabMemoryThreshold IsNot Nothing Then LabMemoryThreshold.Text = $"已用内存高于 {SliderMemoryThreshold.Value} GB 时触发"
        RefreshMemoryUI()
    End Sub

End Class
