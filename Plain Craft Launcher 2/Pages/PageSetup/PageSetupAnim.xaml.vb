''' <summary>
''' 设置 → 动画：高级动画（独立开关，自带三档强度）与普通动画的速度 / 细节。
''' 高级动画开启后接管所有转场，普通的两项暂时不生效；关闭后完全回到原来的逻辑。
''' </summary>
Public Class PageSetupAnim

    Private Sub PageSetupAnim_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        Static Reloaded As Boolean = False
        If Reloaded Then Return
        Reloaded = True

        AniControlEnabled += 1
        Reload()
        AniControlEnabled -= 1
    End Sub

    Public Sub Reload()
        SettingService.RefreshSettings(Me)
        RefreshAnimHints()
    End Sub

    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化动画页设置")
            Hint("已初始化动画页设置！", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化动画页设置失败", LogBehavior.Alert)
        End Try
        Reload()
    End Sub

    ''' <summary>刷新三张卡片的说明文字与可用状态。</summary>
    Public Sub RefreshAnimHints()
        Dim IsAdvanced As Boolean = Settings.Get(Of Boolean)("UltraAnimAdvanced")

        If LabAdvHint IsNot Nothing Then
            If IsAdvanced Then
                Select Case Settings.Get(Of Integer)("UltraAnimAdvancedLevel")
                    Case 0
                        LabAdvHint.Text = "低：只做柔和淡入与极小位移，几乎不打扰，长时间使用也不累眼。可与下方「动画速度」叠加，整体再调快或调慢。"
                    Case 2
                        LabAdvHint.Text = "高：更长的淡入、更大的位移与轻微景深缩放，元素逐项错落入场，质感最接近成品动画，也最吃性能。可与下方「动画速度」叠加。"
                    Case Else
                        LabAdvHint.Text = "中：淡入更缓、位移更轻，末端带一点弹簧回弹，页面像纸张一样平滑推入，是最平衡的一档。可与下方「动画速度」叠加。"
                End Select
            Else
                LabAdvHint.Text = "开启后由高级动画接管全部转场：弹簧缓动、单段位移、逐项错落入场，并提供低 / 中 / 高三档强度。" &
                                  "「动画速度」仍可叠加使用；「动画细节」由这里的强度档位接管。"
            End If
        End If

        If LabAnimSpeedHint IsNot Nothing Then
            Select Case Settings.Get(Of Integer)("UltraAnimSpeed")
                Case 0
                    LabAnimSpeedHint.Text = "低：动画时长缩短到标准的三分之二左右，切页、入场、悬停反馈都更利落，适合追求响应速度。"
                Case 2
                    LabAnimSpeedHint.Text = "高：动画时长放慢约四成半，过渡更从容、更容易看清细节，适合喜欢慢节奏的人。"
                Case Else
                    LabAnimSpeedHint.Text = "中：标准速度，所有动画按设计时长播放。此项只改变播放快慢，不改变动画的表现形式。"
            End Select
            If IsAdvanced Then LabAnimSpeedHint.Text = "与高级动画叠加生效 —— " & LabAnimSpeedHint.Text
        End If

        If LabAnimDetailHint IsNot Nothing Then
            Select Case Settings.Get(Of Integer)("UltraAnimDetail")
                Case 0
                    LabAnimDetailHint.Text = "低：只保留淡入淡出，内容元素不位移、不缩放，也不逐个错开，最省性能。"
                Case 2
                    LabAnimDetailHint.Text = "高：元素错落更明显，位移与回弹更大，卡片还会从略小状态放大浮现，转场更有层次。"
                Case Else
                    LabAnimDetailHint.Text = "中：默认细节。内容元素自下而上逐个淡入，页面切换带轻微位移与缩放。"
            End Select
            If IsAdvanced Then LabAnimDetailHint.Text = "高级动画开启期间由上方「动画强度」接管，关闭高级动画后恢复此项。—— " & LabAnimDetailHint.Text
        End If

        '高级动画的强度档位只在开启时可选
        If PanAdvLevel IsNot Nothing Then
            PanAdvLevel.IsEnabled = IsAdvanced
            PanAdvLevel.Opacity = If(IsAdvanced, 1, 0.45)
        End If
        '「动画速度」始终可用：和高级动画叠加（高级决定怎么动，速度决定多快）
        If CardAnimSpeed IsNot Nothing Then
            CardAnimSpeed.IsEnabled = True
            CardAnimSpeed.Opacity = 1
        End If
        '「动画细节」在高级动画开启时由强度档位接管，置灰
        If CardAnimDetail IsNot Nothing Then
            CardAnimDetail.IsEnabled = Not IsAdvanced
            CardAnimDetail.Opacity = If(IsAdvanced, 0.45, 1)
        End If
    End Sub

    Private Sub CheckAdvAnim_Change(sender As Object, user As Boolean) Handles CheckAdvAnim.Change
        RefreshAnimHints()
    End Sub
    Private Sub RadioAdv_Changed(sender As Object, e As RouteEventArgs) Handles RadioAdvLow.Changed, RadioAdvMid.Changed, RadioAdvHigh.Changed
        RefreshAnimHints()
    End Sub
    Private Sub RadioSpeed_Changed(sender As Object, e As RouteEventArgs) Handles RadioSpeedLow.Changed, RadioSpeedMid.Changed, RadioSpeedHigh.Changed
        RefreshAnimHints()
    End Sub
    Private Sub RadioDetail_Changed(sender As Object, e As RouteEventArgs) Handles RadioDetailLow.Changed, RadioDetailMid.Changed, RadioDetailHigh.Changed
        RefreshAnimHints()
    End Sub

End Class
