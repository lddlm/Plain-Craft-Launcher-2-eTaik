Imports System.Windows.Threading

''' <summary>
''' 工具箱 → UHD 页面。
''' Ultra：微星小飞机式的浮窗（彩色缩写 + 数值，无边框），可勾选显示的参数，3 秒刷新一次。
''' </summary>
Public Class PageToolHud

    Private WithEvents StatusTimer As New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(400)}

    Private Sub PageToolHud_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        Static Reloaded As Boolean = False
        If Not Reloaded Then
            Reloaded = True
            AniControlEnabled += 1
            Try
                Reload()
            Finally
                AniControlEnabled -= 1
            End Try
            AddHandler SliderSize.Change, AddressOf OnSizeChanged
            AddHandler ComboRefresh.SelectionChanged, AddressOf OnLayoutChanged
        Else
            '每次回到这个页面都以设置文件为准，免得界面显示的和实际用的不一致
            AniControlEnabled += 1
            Try
                SettingService.RefreshSettings(Me)
            Finally
                AniControlEnabled -= 1
            End Try
        End If
        RefreshTip()
        InitItemChecks()
        InitAutoAttachCheck()
        RefreshState()
        StatusTimer.Start()
    End Sub

    Private Sub PageToolHud_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        StatusTimer.Stop()
    End Sub

    Public Sub Reload()
        SettingService.RefreshSettings(Me)
    End Sub

    Private Sub RefreshTip()
        '只留一行快捷键说明，其余说明都省了
        LabTip.Text = $"快捷键：{UltraHud.HotkeyText}"
    End Sub

    ''' <summary>刷新状态区与按钮文字。</summary>
    Private Sub RefreshState()
        Try
            LabState.Text = UltraHud.StateText
            Dim Running As Boolean = UltraHud.GameRunning
            If UltraHud.IsShowing AndAlso UltraHud.IsRunning Then
                Dim Fps As Long = UltraHud.CurrentFps
                If Fps > 0 Then
                    LabFps.Text = $"{Fps} FPS · {UltraHud.FrameTimeMs:0.0} ms"
                Else
                    LabFps.Text = "正在统计，稍等一下"
                End If
            Else
                LabFps.Text = "—"
            End If
            Dim W As Long = UltraHud.RenderWidth
            Dim H As Long = UltraHud.RenderHeight
            LabRes.Text = If(W > 0 AndAlso H > 0, $"{W} × {H}", "—")
            LabSize.Text = $"{UltraHud.FontSizePx} px"
            BtnToggle.Text = If(UltraHud.IsShowing, "隐藏 UHD", "显示 UHD")
            '只有游戏跑起来才让开；游戏没开时按钮置灰，鼠标悬停提示原因
            BtnToggle.IsEnabled = Running
            BtnToggle.ToolTip = If(Running, "在游戏画面上显示 / 隐藏 UHD。",
                                   "还没检测到游戏窗口。先启动游戏，进到游戏画面后再打开。")
        Catch ex As Exception
            Logger.Error(ex, "刷新帧率 HUD 状态失败")
        End Try
    End Sub

    ''' <summary>「进游戏就自动挂接内核」这个开关。</summary>
    Private Sub InitAutoAttachCheck()
        AniControlEnabled += 1
        Try
            CheckAutoAttach.Checked = Settings.Get(Of Boolean)("HudAutoAttach")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Sub CheckAutoAttach_Change(sender As Object, user As Boolean) Handles CheckAutoAttach.Change
        If AniControlEnabled <> 0 Then Return
        Settings.Set("HudAutoAttach", CheckAutoAttach.Checked)
        RefreshTip()
    End Sub

    Private Sub StatusTimer_Tick(sender As Object, e As EventArgs) Handles StatusTimer.Tick
        RefreshState()
    End Sub

    Private Sub BtnToggle_Click() Handles BtnToggle.Click
        UltraHud.ToggleHud()
        RefreshState()
    End Sub

    '==== 外观设置（位置 / 字号 / 帧时间）====

    Private Sub OnLayoutChanged(sender As Object, e As SelectionChangedEventArgs)
        UltraHud.RefreshLayout()
    End Sub

    '==== 字号滑块 ====

    ''' <summary>拖动字号滑块：立刻把新字号推给内核，游戏里的 HUD 跟着变。</summary>
    Private Sub OnSizeChanged(sender As Object, user As Boolean)
        Try
            Dim V As Integer = SliderSize.Value
            If V < UltraHud.FontPxMin Then V = UltraHud.FontPxMin
            If V > UltraHud.FontPxMax Then V = UltraHud.FontPxMax
            LabSize.Text = $"{V} px"
            UltraHud.RefreshLayout()
        Catch ex As Exception
            Logger.Error(ex, "调整 UHD 字号失败")
        End Try
    End Sub

    ''' <summary>字号太小时直接拦下，不让它滑到看不清的范围。</summary>
    Private Sub SliderSize_PreviewChange(sender As Object, e As RouteEventArgs) Handles SliderSize.PreviewChange
        If SliderSize.Value < UltraHud.FontPxMin Then e.Handled = True
    End Sub

    Private Sub RadioLeftTop_Changed(sender As Object, e As RouteEventArgs) Handles RadioLeftTop.Changed
        UltraHud.RefreshLayout()
    End Sub

    Private Sub RadioRightTop_Changed(sender As Object, e As RouteEventArgs) Handles RadioRightTop.Changed
        UltraHud.RefreshLayout()
    End Sub

    Private Sub RadioLeftBottom_Changed(sender As Object, e As RouteEventArgs) Handles RadioLeftBottom.Changed
        UltraHud.RefreshLayout()
    End Sub

    Private Sub RadioRightBottom_Changed(sender As Object, e As RouteEventArgs) Handles RadioRightBottom.Changed
        UltraHud.RefreshLayout()
    End Sub

    '==== 显示项目（存成 HudItems 一行字符串，逗号分隔）====

    Private Sub InitItemChecks()
        Dim Items As List(Of String) = UltraHud.SelectedItems
        AniControlEnabled += 1
        Try
            CheckItemCpu.Checked = Items.Contains(UltraHud.ItemCpu)
            CheckItemGpu.Checked = Items.Contains(UltraHud.ItemGpu)
            CheckItemVram.Checked = Items.Contains(UltraHud.ItemVram)
            CheckItemRam.Checked = Items.Contains(UltraHud.ItemRam)
            CheckItemFps.Checked = Items.Contains(UltraHud.ItemFps)
            CheckItemFrameTime.Checked = Items.Contains(UltraHud.ItemFrameTime)
            CheckItemLow1.Checked = Items.Contains(UltraHud.ItemLow1)
            CheckItemLow01.Checked = Items.Contains(UltraHud.ItemLow01)
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Sub CheckItem_Change(sender As Object, user As Boolean) Handles CheckItemCpu.Change, CheckItemGpu.Change,
            CheckItemVram.Change, CheckItemRam.Change, CheckItemFps.Change, CheckItemFrameTime.Change,
            CheckItemLow1.Change, CheckItemLow01.Change
        If AniControlEnabled <> 0 Then Return
        Dim List As New List(Of String)
        If CheckItemCpu.Checked Then List.Add(UltraHud.ItemCpu)
        If CheckItemGpu.Checked Then List.Add(UltraHud.ItemGpu)
        If CheckItemVram.Checked Then List.Add(UltraHud.ItemVram)
        If CheckItemRam.Checked Then List.Add(UltraHud.ItemRam)
        If CheckItemFps.Checked Then List.Add(UltraHud.ItemFps)
        If CheckItemFrameTime.Checked Then List.Add(UltraHud.ItemFrameTime)
        If CheckItemLow1.Checked Then List.Add(UltraHud.ItemLow1)
        If CheckItemLow01.Checked Then List.Add(UltraHud.ItemLow01)
        If List.Count = 0 Then
            '一个都不留的话浮窗就空了，自动把帧率勾回去
            Hint("至少要勾一个显示项目", HintType.Red)
            CheckItemFps.Checked = True
            Return
        End If
        Settings.Set("HudItems", String.Join(",", List))
        UltraHud.RefreshLayout()
        RefreshTip()
    End Sub

End Class
