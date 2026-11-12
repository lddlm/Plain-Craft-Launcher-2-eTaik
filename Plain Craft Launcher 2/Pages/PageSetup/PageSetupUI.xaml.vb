Public Class PageSetupUI

    Private Sub PageSetupUI_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '重复加载部分
        PanBack.ScrollToHome()
        ThemeCheckAll(True)
        UnlockAllThemes()
        BuildThemeColorPicker()
        LabColorIntensity.Text = Settings.Get(Of Integer)("UltraColorIntensity") & "%"
        If ThemeDontClick <> 0 Then
            Dim NewText As String = Nothing
            Select Case ThemeDontClick
                Case 1
                    NewText = "眼瞎白"
                Case 2
                    NewText = "真·滑稽彩"
            End Select
            For Each Control In PanLauncherTheme.Children
                If (TypeOf Control Is MyRadioBox) AndAlso CType(Control, MyRadioBox).IsEnabled Then CType(Control, MyRadioBox).Text = NewText
            Next
        End If

        AniControlEnabled += 1
        Refresh() '#4826
        AniControlEnabled -= 1

        '非重复加载部分
        Static Reloaded As Boolean = False
        If Reloaded Then Return
        Reloaded = True

        SliderLoad()
        MigrateBgList()
        RefreshUltraBackgroundLabels()
        RefreshUltraBackgroundPickers()

        PanLauncherHide.Visibility = Visibility.Collapsed 'Ultra：不显示赞助遮罩，避免挡住主题按钮

        '设置解锁
        If Not RadioLauncherTheme8.IsEnabled Then LabLauncherTheme8Copy.ToolTip = $"累积赞助达到 ¥23.33 后，在爱发电私信发送【土豆 {Identify}】以解锁。" & vbCrLf & "右键打开赞助页面，如果觉得 PCL 做得还不错就支持一下吧 =w=！"
        RadioLauncherTheme8.ToolTip = $"累积赞助达到 ¥23.33 后，在爱发电私信发送【土豆 {Identify}】以解锁"
        If Not RadioLauncherTheme9.IsEnabled Then LabLauncherTheme9Copy.ToolTip = "· 反馈一个 Bug，在标记为 [完成] 后回复识别码要求解锁（右键打开反馈页面）" & vbCrLf & "· 提交一个 Pull Request 或主页预设，在标记为 [完成] 后回复识别码要求解锁"
        RadioLauncherTheme9.ToolTip = "· 反馈一个 Bug，在标记为 [完成] 后回复识别码要求解锁" & vbCrLf & "· 提交一个 Pull Request 或主页预设，在标记为 [完成] 后回复识别码要求解锁"
        '极客蓝的处理在 ThemeCheck 中

    End Sub
    Public Sub Refresh()
        Try
            SettingService.RefreshSettings(Me)
            BackgroundRefresh(False, False)

            '标题栏
            CheckLogoLeft.Visibility = If(RadioLogoType0.Checked, Visibility.Visible, Visibility.Collapsed)
            PanLogoText.Visibility = If(RadioLogoType2.Checked, Visibility.Visible, Visibility.Collapsed)
            PanLogoChange.Visibility = If(RadioLogoType3.Checked, Visibility.Visible, Visibility.Collapsed)

            '背景音乐
            MusicRefreshUI()

            '主页
            OnMainPageTypeChanged()
        Catch ex As NullReferenceException
            Logger.Error(ex, "个性化设置项存在异常，已被自动重置", LogBehavior.Alert)
            Reset()
        Catch ex As Exception
            Logger.Error(ex, "重载个性化设置时出错")
        End Try
    End Sub

    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化个性化设置！")
            Hint("已初始化个性化设置", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化个性化设置失败", LogBehavior.Alert)
        End Try
        Refresh()
    End Sub

    '背景图片
    Private Sub BtnUIBgOpen_Click(sender As Object, e As EventArgs) Handles BtnBackgroundOpen.Click
        OpenExplorer(Paths.Base & "PCL\Pictures\")
    End Sub
    Private Sub BtnBackgroundRefresh_Click(sender As Object, e As EventArgs) Handles BtnBackgroundRefresh.Click
        BackgroundRefresh(True, True)
    End Sub
    Public Sub BackgroundRefreshUI(Show As Boolean, Count As Integer)
        If IsNothing(PanBackgroundOpacity) Then Return
        If Show Then
            PanBackgroundOpacity.Visibility = Visibility.Visible
            PanBackgroundBlur.Visibility = Visibility.Visible
            PanBackgroundSuit.Visibility = Visibility.Visible
            BtnBackgroundClear.Visibility = Visibility.Visible
            CardBackground.Title = "背景图片（" & Count & " 张）"
        Else
            PanBackgroundOpacity.Visibility = Visibility.Collapsed
            PanBackgroundBlur.Visibility = Visibility.Collapsed
            PanBackgroundSuit.Visibility = Visibility.Collapsed
            BtnBackgroundClear.Visibility = Visibility.Collapsed
            CardBackground.Title = "背景图片"
        End If
        CardBackground.TriggerForceResize()
    End Sub
    Private Sub BtnBackgroundClear_Click(sender As Object, e As EventArgs) Handles BtnBackgroundClear.Click
        If MyMsgBox("即将删除背景图片文件夹中的所有文件。" & vbCrLf & "此操作不可撤销，是否确定？", "警告",, "取消", IsWarn:=True) = 1 Then
            DirectoryUtils.Delete(Paths.Base & "PCL\Pictures")
            BackgroundRefresh(False, True)
            Hint("背景图片已清空！", HintType.Green)
        End If
    End Sub
    ''' <summary>
    ''' 刷新背景图片及设置页 UI。
    ''' </summary>
    ''' <param name="IsHint">是否显示刷新提示。</param>
    ''' <param name="Refresh">是否刷新图片显示。</param>
    Public Shared Sub BackgroundRefresh(IsHint As Boolean, Refresh As Boolean)
        Try

            '获取可用的图片文件
            DirectoryUtils.Create(Paths.Base & "PCL\Pictures\")
            Dim Pic As New List(Of String)
            For Each File In DirectoryUtils.EnumerateFiles(Paths.Base & "PCL\Pictures\", True)
                Dim Extension As String = PathUtils.GetExtension(File)
                If Extension <> "ini" AndAlso Extension <> "db" Then Pic.Add(File) '文件夹可能会被加入 .ini 和 thumbs.db
            Next
            '加载
            If Not Pic.Any() Then
                If Refresh Then
                    If FrmMain.ImgBack.Visibility = Visibility.Collapsed Then
                        If IsHint Then Hint("未检测到可用背景图片！", HintType.Red)
                    Else
                        FrmMain.ImgBack.Visibility = Visibility.Collapsed
                        If IsHint Then Hint("背景图片已清除！", HintType.Green)
                    End If
                End If
                If Not IsNothing(FrmSetupUI) Then FrmSetupUI.BackgroundRefreshUI(False, 0)
            Else
                If Refresh Then
                    Dim Address As String = RandomOne(Pic)
                    Try
                        Logger.Info($"加载背景图片：{Address}")
                        FrmMain.ImgBack.Background = New MyBitmap(Address)
                        FrmMain.ImgBack.Visibility = Visibility.Visible
                        FrmMain.UpdateBackgroundAndTitleBar()
                        If IsHint Then Hint("背景图片已刷新：" & PathUtils.GetLastPart(Address), HintType.Green, False)
                    Catch ex As Exception
                        If ex.Message.Contains("参数无效") Then
                            Logger.Error($"刷新背景图片失败，该图片文件可能并非标准格式。{vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{vbCrLf}文件：{Address}", LogBehavior.Alert)
                        Else
                            Logger.Error(ex, $"刷新背景图片失败（{Address}）", LogBehavior.Alert)
                        End If
                    End Try
                End If
                If Not IsNothing(FrmSetupUI) Then FrmSetupUI.BackgroundRefreshUI(True, Pic.Count)
            End If

        Catch ex As Exception
            Logger.Error(ex, "刷新背景图片时出现未知错误")
        End Try
    End Sub

    '顶部栏
    Private Sub BtnLogoChange_Click(sender As Object, e As EventArgs) Handles BtnLogoChange.Click
        Dim FileName As String = Dialogs.SelectFile("选择图片", False, filter:={({"png", "jpg", "jpeg", "gif", "webp"}, "常用图片文件")}).FirstOrDefault()
        If String.IsNullOrEmpty(FileName) Then Return
        Dim TargetPath As String = Paths.Base & "PCL\Logo.png"
        Try
            '复制文件
            FileUtils.Copy(FileName, TargetPath)
            '设置当前显示
            FrmMain.ImageTitleLogo.Source = Nothing '防止因为 Source 属性前后的值相同而不更新 (#5628)
            FrmMain.ImageTitleLogo.Source = TargetPath
        Catch ex As Exception
            If ex.Message.Contains("参数无效") Then
                Logger.Error($"改变标题栏图片失败，该图片文件可能并非标准格式。{vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{vbCrLf}文件：{TargetPath}", LogBehavior.Alert)
            Else
                Logger.Error(ex, "设置标题栏图片失败", LogBehavior.Alert)
            End If
            FrmMain.ImageTitleLogo.Source = Nothing
        End Try
    End Sub
    Private Sub RadioLogoType3_Check(sender As Object, e As RouteEventArgs) Handles RadioLogoType3.PreviewCheck
        If Not (AniControlEnabled = 0 AndAlso e.RaiseByMouse) Then Return
Refresh:
        '已有图片则不再选择
        Dim TargetPath As String = Paths.Base & "PCL\Logo.png"
        If FileUtils.Exists(TargetPath) Then
            Try
                FrmMain.ImageTitleLogo.Source = Nothing '防止因为 Source 属性前后的值相同而不更新 (#5628)
                FrmMain.ImageTitleLogo.Source = TargetPath
            Catch ex As Exception
                If ex.Message.Contains("参数无效") Then
                    Logger.Error($"改变标题栏图片失败，该图片文件可能并非标准格式。{vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{vbCrLf}文件：{TargetPath}", LogBehavior.Alert)
                Else
                    Logger.Error(ex, "调整标题栏图片失败", LogBehavior.Alert)
                End If
                FrmMain.ImageTitleLogo.Source = Nothing
                e.Handled = True
                Try
                    FileUtils.Delete(TargetPath)
                Catch exx As Exception
                    Logger.Error(exx, "清理错误的标题栏图片失败", LogBehavior.Alert)
                End Try
            End Try
            Return
        End If
        '没有图片则要求选择
        Dim FileName As String = Dialogs.SelectFile("选择图片", False, filter:={({"png", "jpeg", "jpg", "gif", "webp"}, "常用图片文件")}).FirstOrDefault()
        If String.IsNullOrEmpty(FileName) Then
            FrmMain.ImageTitleLogo.Source = Nothing
            e.Handled = True
        Else
            Try
                FileUtils.Copy(FileName, TargetPath)
                GoTo Refresh
            Catch ex As Exception
                Logger.Error(ex, "复制标题栏图片失败", LogBehavior.Alert)
            End Try
        End If
    End Sub
    Private Sub BtnLogoDelete_Click(sender As Object, e As EventArgs) Handles BtnLogoDelete.Click
        Try
            FileUtils.Delete(Paths.Base & "PCL\Logo.png")
            RadioLogoType1.SetChecked(True, True)
            Hint("标题栏图片已清空！", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "清空标题栏图片失败", LogBehavior.Alert)
        End Try
    End Sub

    '背景音乐
    Private Sub BtnMusicOpen_Click(sender As Object, e As EventArgs) Handles BtnMusicOpen.Click
        OpenExplorer(Paths.Base & "PCL\Musics\")
    End Sub
    Private Sub BtnMusicRefresh_Click(sender As Object, e As EventArgs) Handles BtnMusicRefresh.Click
        MusicRefreshPlay(True)
    End Sub
    Public Sub MusicRefreshUI()
        If PanBackgroundOpacity Is Nothing Then Return
        If MusicAllList.Any Then
            PanMusicVolume.Visibility = Visibility.Visible
            PanMusicDetail.Visibility = Visibility.Visible
            BtnMusicClear.Visibility = Visibility.Visible
            CardMusic.Title = "背景音乐（" &
                DirectoryUtils.EnumerateFiles(Paths.Base & "PCL\Musics\", True).Count(Function(f) Not {"ini", "jpg", "txt", "cfg", "lrc", "db", "png"}.Contains(PathUtils.GetExtension(f))) &
                " 首）"
        Else
            PanMusicVolume.Visibility = Visibility.Collapsed
            PanMusicDetail.Visibility = Visibility.Collapsed
            BtnMusicClear.Visibility = Visibility.Collapsed
            CardMusic.Title = "背景音乐"
        End If
        CardMusic.TriggerForceResize()
    End Sub
    Private Sub BtnMusicClear_Click(sender As Object, e As EventArgs) Handles BtnMusicClear.Click
        If MyMsgBox("即将删除背景音乐文件夹中的所有文件。" & vbCrLf & "此操作不可撤销，是否确定？", "警告",, "取消", IsWarn:=True) = 1 Then
            RunInThread(
            Sub()
                Hint("正在删除背景音乐……")
                '停止播放音乐
                MusicNAudio = Nothing
                MusicWaitingList = New List(Of String)
                MusicAllList = New List(Of String)
                Thread.Sleep(200)
                '删除文件
                Try
                    DirectoryUtils.Delete(Paths.Base & "PCL\Musics")
                    Hint("背景音乐已删除！", HintType.Green)
                Catch ex As Exception
                    Logger.Error(ex, "删除背景音乐失败", LogBehavior.Alert)
                End Try
                Try
                    DirectoryUtils.Create(Paths.Base & "PCL\Musics\")
                    RunInUi(Sub() MusicRefreshPlay(False))
                Catch ex As Exception
                    Logger.Error(ex, "重建背景音乐文件夹失败", LogBehavior.Alert)
                End Try
            End Sub)
        End If
    End Sub
    Private Sub CheckMusicStart_Change() Handles CheckMusicStart.Change
        If AniControlEnabled <> 0 Then Return
        If CheckMusicStart.Checked Then CheckMusicStop.Checked = False
    End Sub
    Private Sub CheckMusicStop_Change() Handles CheckMusicStop.Change
        If AniControlEnabled <> 0 Then Return
        If CheckMusicStop.Checked Then CheckMusicStart.Checked = False
    End Sub

    '主页
    Private Sub BtnCustomFile_Click(sender As Object, e As EventArgs) Handles BtnCustomFile.Click
        Try
            If FileUtils.Exists(Paths.Base & "PCL\Custom.xaml") Then
                If MyMsgBox("当前已存在布局文件，继续生成教学文件将会覆盖现有布局文件！", "覆盖确认", "继续", "取消", IsWarn:=True) = 2 Then Return
            End If
            ExtractResources(Paths.Base & "PCL\Custom.xaml", "Custom")
            Hint("教学文件已生成！", HintType.Green)
            OpenExplorer(Paths.Base & "PCL\Custom.xaml")
        Catch ex As Exception
            Logger.Error(ex, "生成教学文件失败")
        End Try
    End Sub
    Private Sub BtnCustomRefresh_Click() Handles BtnCustomRefresh.Click
        FrmLaunchRight.ForceRefresh()
        Hint("已刷新主页！", HintType.Green)
    End Sub
    Private Sub BtnCustomTutorial_Click(sender As Object, e As EventArgs) Handles BtnCustomTutorial.Click
        MyMsgBox("1. 点击 生成教学文件 按钮，这会在 PCL 文件夹下生成 Custom.xaml 布局文件。" & vbCrLf &
                 "2. 使用记事本等工具打开这个文件并进行修改，修改完记得保存。" & vbCrLf &
                 "3. 点击 刷新主页 按钮，查看主页现在长啥样了。" & vbCrLf &
                 vbCrLf &
                 "你可以在生成教学文件后直接刷新主页，对照着进行修改，更有助于理解。" & vbCrLf &
                 "直接将主页文件拖进 PCL 窗口也可以快捷加载。", "主页自定义教程")
    End Sub
    Public Shared Sub OnMainPageTypeChanged()
        If FrmSetupUI Is Nothing Then Return
        Select Case CInt(Settings.Get(Of Integer)("UiCustomType"))
            Case 0 '无
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustom.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustomWarn.Visibility = Visibility.Collapsed
            Case 1 '本地
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Visible
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustom.Visibility = Visibility.Visible
                FrmSetupUI.HintCustomWarn.Visibility = If(Settings.Get(Of Boolean)("HintCustomWarn"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupUI.HintCustom.Text = $"从 PCL 文件夹下的 Custom.xaml 读取主页内容。{vbCrLf}你可以手动编辑该文件，向主页添加文本、图片、常用网站、快捷启动等功能。"
                CustomEventService.SetEventType(FrmSetupUI.HintCustom, CustomEvent.EventType.None)
            Case 2 '联网
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Visible
                FrmSetupUI.HintCustom.Visibility = Visibility.Visible
                FrmSetupUI.HintCustomWarn.Visibility = If(Settings.Get(Of Boolean)("HintCustomWarn"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupUI.HintCustom.Text = $"从指定网址联网获取主页内容。服主也可以用于动态更新服务器公告。{vbCrLf}如果你制作了稳定运行的联网主页，可以点击这条提示投稿，若合格即可加入预设！"
                CustomEventService.SetEventType(FrmSetupUI.HintCustom, CustomEvent.EventType.打开网页)
                CustomEventService.SetEventData(FrmSetupUI.HintCustom, "https://github.com/Meloong-Git/PCL/discussions/2528")
            Case 3 '预设
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Visible
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustom.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustomWarn.Visibility = Visibility.Collapsed
        End Select
        FrmSetupUI.CardCustom.TriggerForceResize()
    End Sub
    Private Sub BtnCustomFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCustomFolder.Click
        Dim CustomPath As String = Paths.Base & "PCL\Custom.xaml"
        If FileUtils.Exists(CustomPath) Then
            OpenExplorer(CustomPath)
        Else
            OpenExplorer(Paths.Base & "PCL\")
        End If
    End Sub

    '主题
    Private Sub LabLauncherTheme5Unlock_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles LabLauncherTheme5Unlock.MouseLeftButtonUp
        RadioLauncherTheme5Gray.Opacity -= 0.7
        RadioLauncherTheme5.Opacity += 0.7
        AniStart({
            AaOpacity(RadioLauncherTheme5Gray, 1, 1000 * AniSpeed, 500 * AniSpeed, New AniEaseInFluent),
            AaOpacity(RadioLauncherTheme5, -1, 1000 * AniSpeed, 500 * AniSpeed, New AniEaseInFluent)
        }, "ThemeUnlock")
        If RadioLauncherTheme5Gray.Opacity < 0.02 Then
            ThemeUnlock(5, UnlockHint:="隐藏主题 玄素黑 已解锁！")
            AniStop("ThemeUnlock")
            RadioLauncherTheme5.Checked = True
        End If
    End Sub
    Private Sub LabLauncherTheme11Click_MouseLeftButtonUp() Handles LabLauncherTheme11Click.MouseLeftButtonUp, RadioLauncherTheme11.MouseRightButtonUp
        If LabLauncherTheme11Click.Visibility = Visibility.Collapsed OrElse If(LabLauncherTheme11Click.ToolTip, "").ToString.Contains("点击") Then
            If MyMsgBox(
                "1. 不爬取或攻击相关服务或网站，不盗取相关账号，没有谜题可以或需要以此来解决。" & vbCrLf &
                "2. 不得篡改或损毁相关公开信息，请尽量让它们保持原状。" & vbCrLf &
                "3. 在你感到迷茫的时候，看看回声洞可能会给你带来惊喜。" & vbCrLf & vbCrLf &
                "若违规，可能会被从任意相关群中踢出！",
                "解密游戏的基本规则", "我知道了", "恕我拒绝") = 1 Then
                MyMsgBox("你需要用自己的智慧来找到下一步的线索……" & vbCrLf &
                         "初始线索：gnp.dorC61\60\20\0202\moc.x1xa.2s\\:sp" & "T".Lower & "th", "解密游戏") '防止触发病毒检测规则
            End If
        End If
    End Sub
    Private Sub LabLauncherTheme8Copy_MouseRightButtonUp() Handles LabLauncherTheme8Copy.MouseRightButtonUp, RadioLauncherTheme8.MouseRightButtonUp
        OpenWebsite("https://meloong.com/afd/a/LTCat")
    End Sub
    Private Sub LabLauncherTheme9Copy_MouseRightButtonUp() Handles LabLauncherTheme9Copy.MouseRightButtonUp, RadioLauncherTheme9.MouseRightButtonUp
        PageOtherLeft.TryFeedback()
    End Sub

    '主题自定义
    Private Sub RadioLauncherTheme14_Change(sender As Object, e As RouteEventArgs) Handles RadioLauncherTheme14.Changed
        If RadioLauncherTheme14.Checked Then
            If LabLauncherHue.Visibility = Visibility.Visible Then Return
            LabLauncherHue.Visibility = Visibility.Visible
            SliderLauncherHue.Visibility = Visibility.Visible
            LabLauncherSat.Visibility = Visibility.Visible
            SliderLauncherSat.Visibility = Visibility.Visible
            LabLauncherDelta.Visibility = Visibility.Visible
            SliderLauncherDelta.Visibility = Visibility.Visible
            LabLauncherLight.Visibility = Visibility.Visible
            SliderLauncherLight.Visibility = Visibility.Visible
        Else
            If LabLauncherHue.Visibility = Visibility.Collapsed Then Return
            LabLauncherHue.Visibility = Visibility.Collapsed
            SliderLauncherHue.Visibility = Visibility.Collapsed
            LabLauncherSat.Visibility = Visibility.Collapsed
            SliderLauncherSat.Visibility = Visibility.Collapsed
            LabLauncherDelta.Visibility = Visibility.Collapsed
            SliderLauncherDelta.Visibility = Visibility.Collapsed
            LabLauncherLight.Visibility = Visibility.Collapsed
            SliderLauncherLight.Visibility = Visibility.Collapsed
        End If
        CardLauncher.TriggerForceResize()
    End Sub
    ''' <summary>颜色浓度：数值实时显示，颜色变化由设置的 OnChanged 回调刷新。</summary>
    Private Sub SliderColorIntensity_Change(sender As Object, user As Boolean) Handles SliderColorIntensity.Change
        If LabColorIntensity Is Nothing Then Return
        LabColorIntensity.Text = SliderColorIntensity.Value & "%"
    End Sub

    Private Sub HSL_Change() Handles SliderLauncherHue.Change, SliderLauncherLight.Change, SliderLauncherSat.Change, SliderLauncherDelta.Change
        If AniControlEnabled <> 0 OrElse SliderLauncherSat Is Nothing OrElse Not SliderLauncherSat.IsLoaded Then Return
        ThemeRefresh()
    End Sub

#Region "功能隐藏"

    Private Shared _HiddenForceShow As Boolean = False
    ''' <summary>
    ''' 是否强制显示被禁用的功能。
    ''' </summary>
    Public Shared Property HiddenForceShow As Boolean
        Get
            Return _HiddenForceShow
        End Get
        Set(value As Boolean)
            _HiddenForceShow = value
            HiddenRefresh()
        End Set
    End Property

    ''' <summary>
    ''' 更新功能隐藏带来的显示变化。
    ''' </summary>
    Public Shared Sub HiddenRefresh() Handles Me.Loaded
        If FrmMain.PanTitleSelect Is Nothing OrElse Not FrmMain.PanTitleSelect.IsLoaded Then Return
        Try
            '顶部栏
            If Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageDownload") AndAlso Settings.Get(Of Boolean)("UiHiddenPageLink") AndAlso Settings.Get(Of Boolean)("UiHiddenPageSetup") AndAlso Settings.Get(Of Boolean)("UiHiddenPageOther") Then
                '顶部栏已被全部隐藏
                FrmMain.PanTitleSelect.Visibility = Visibility.Collapsed
            Else
                '顶部栏未被全部隐藏
                FrmMain.PanTitleSelect.Visibility = Visibility.Visible
                FrmMain.BtnTitleSelect1.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageDownload"), Visibility.Collapsed, Visibility.Visible)
                FrmMain.BtnTitleSelect2.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageLink"), Visibility.Collapsed, Visibility.Visible)
                FrmMain.BtnTitleSelect3.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageSetup"), Visibility.Collapsed, Visibility.Visible)
                FrmMain.BtnTitleSelect4.Visibility = Visibility.Collapsed '「更多」已整合进设置页，顶部不再显示
            End If
            '功能
            FrmLaunchLeft.RefreshButtonsUI()
            If FrmSetupUI IsNot Nothing Then
                FrmSetupUI.CardSwitch.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenFunctionHidden"), Visibility.Collapsed, Visibility.Visible)
            End If
            '版本设置子页面
            If FrmInstanceLeft IsNot Nothing Then
                FrmInstanceLeft.ItemModify.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenFunctionInstanceModify"), Visibility.Collapsed, Visibility.Visible)
            End If
            '设置子页面
            If FrmSetupLeft IsNot Nothing Then
                FrmSetupLeft.ItemLaunch.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLaunch"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupLeft.ItemUI.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupUi"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupLeft.ItemLink.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLink"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupLeft.ItemSystem.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupSystem"), Visibility.Collapsed, Visibility.Visible)
                '隐藏左边选择卡（「动画」「内存优化」页始终可见，计入两个）
                Dim AvaliableCount As Integer = 2
                If Not Settings.Get(Of Boolean)("UiHiddenSetupLaunch") Then AvaliableCount += 1
                If Not Settings.Get(Of Boolean)("UiHiddenSetupUi") Then AvaliableCount += 1
                If Not Settings.Get(Of Boolean)("UiHiddenSetupLink") Then AvaliableCount += 1
                If Not Settings.Get(Of Boolean)("UiHiddenSetupSystem") Then AvaliableCount += 1
                FrmSetupLeft.PanItem.Visibility = If(AvaliableCount < 2 AndAlso Not HiddenForceShow, Visibility.Collapsed, Visibility.Visible)
            End If
            '更多子页面
            Dim OtherAvaliableCount As Integer = 0
            If Not Settings.Get(Of Boolean)("UiHiddenOtherHelp") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherAbout") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherTest") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherFeedback") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherVote") Then OtherAvaliableCount += 1
            If FrmOtherLeft IsNot Nothing Then
                FrmOtherLeft.ItemHelp.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherHelp"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemFeedback.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherFeedback"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemVote.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherVote"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemAbout.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemTest.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest"), Visibility.Collapsed, Visibility.Visible)
                '隐藏左边选择卡
                FrmOtherLeft.PanItem.Visibility = If(OtherAvaliableCount < 2 AndAlso Not HiddenForceShow, Visibility.Collapsed, Visibility.Visible)
            End If
            '整合进设置页后的「更多」左栏（与上面的原更多页保持同一套隐藏规则）
            If FrmSetupMore IsNot Nothing Then
                FrmSetupMore.ItemHelp.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherHelp"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupMore.ItemFeedback.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherFeedback"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupMore.ItemVote.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherVote"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupMore.ItemAbout.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupMore.ItemTest.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupMore.PanItem.Visibility = If(OtherAvaliableCount < 2 AndAlso Not HiddenForceShow, Visibility.Collapsed, Visibility.Visible)
            End If
            If OtherAvaliableCount = 1 AndAlso Not HiddenForceShow Then
                If Not Settings.Get(Of Boolean)("UiHiddenOtherHelp") Then
                    FrmMain.BtnTitleSelect4.Text = "帮助"
                ElseIf Not Settings.Get(Of Boolean)("UiHiddenOtherAbout") Then
                    FrmMain.BtnTitleSelect4.Text = "关于"
                Else
                    FrmMain.BtnTitleSelect4.Text = "百宝箱"
                End If
            Else
                FrmMain.BtnTitleSelect4.Text = "更多"
            End If
            '各个页面的入口
            If FrmMain.PageCurrent = FormMain.PageType.InstanceSelect Then FrmSelectRight.BtnEmptyDownload_Loaded()
            If FrmMain.PageCurrent = FormMain.PageType.Launch Then FrmLaunchLeft.RefreshButtonsUI()
            If FrmMain.PageCurrent = FormMain.PageType.InstanceSetup AndAlso FrmInstanceModDisabled IsNot Nothing Then FrmInstanceModDisabled.BtnDownload_Loaded()
            '备注
            If FrmSetupUI IsNot Nothing Then FrmSetupUI.CardSwitch.Title = If(HiddenForceShow, "功能隐藏（已暂时关闭，按 F12 以重新启用）", "功能隐藏")
        Catch ex As Exception
            Logger.Error(ex, "刷新功能隐藏项目失败")
        End Try
    End Sub

    'UI 协同改变
    Private Sub HiddenSetupMain() Handles CheckHiddenPageSetup.Change
        '设置主页面
        If CheckHiddenPageSetup.Checked Then
            '开启
            CheckHiddenSetupLaunch.Checked = True
            CheckHiddenSetupSystem.Checked = True
            CheckHiddenSetupLink.Checked = True
            CheckHiddenSetupUI.Checked = True
        Else
            '关闭
            If Settings.Get(Of Boolean)("UiHiddenSetupLaunch") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupUi") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupSystem") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLink") Then
                CheckHiddenSetupLaunch.Checked = False
                CheckHiddenSetupSystem.Checked = False
                CheckHiddenSetupLink.Checked = False
                CheckHiddenSetupUI.Checked = False
            End If
        End If
    End Sub
    Private Sub HiddenSetupSub() Handles CheckHiddenSetupLaunch.Change, CheckHiddenSetupSystem.Change, CheckHiddenSetupLink.Change, CheckHiddenSetupUI.Change
        '设置子页面
        If Settings.Get(Of Boolean)("UiHiddenSetupLaunch") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupUi") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupSystem") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLink") Then
            '已被全部隐藏
            CheckHiddenPageSetup.Checked = True
        Else
            '未被全部隐藏
            CheckHiddenPageSetup.Checked = False
        End If
    End Sub
    Private Sub HiddenOtherMain() Handles CheckHiddenPageOther.Change
        '更多主页面
        If CheckHiddenPageOther.Checked Then
            '开启
            CheckHiddenOtherAbout.Checked = True
            CheckHiddenOtherTest.Checked = True
            CheckHiddenOtherFeedback.Checked = True
            CheckHiddenOtherVote.Checked = True
            CheckHiddenOtherHelp.Checked = True
        Else
            '关闭
            If Settings.Get(Of Boolean)("UiHiddenOtherHelp") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest") AndAlso
                Settings.Get(Of Boolean)("UiHiddenOtherVote") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherFeedback") Then
                CheckHiddenOtherAbout.Checked = False
                CheckHiddenOtherTest.Checked = False
                CheckHiddenOtherFeedback.Checked = False
                CheckHiddenOtherVote.Checked = False
                CheckHiddenOtherHelp.Checked = False
            End If
        End If
    End Sub
    Private Sub HiddenOtherSub(sender As Object, user As Boolean) Handles CheckHiddenOtherHelp.Change, CheckHiddenOtherAbout.Change, CheckHiddenOtherTest.Change
        '更多子页面（有具体内容的）
        If Settings.Get(Of Boolean)("UiHiddenOtherHelp") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest") Then
            '已被全部隐藏
            CheckHiddenPageOther.Checked = True
        Else
            '未被全部隐藏
            CheckHiddenPageOther.Checked = False
        End If
        '修改无具体内容的项
        If Not user Then Return
        If Settings.Get(Of Boolean)("UiHiddenOtherHelp") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest") Then
            CheckHiddenOtherFeedback.Checked = True
            CheckHiddenOtherVote.Checked = True
        End If
    End Sub
    Private Sub HiddenOtherNet(sender As Object, user As Boolean) Handles CheckHiddenOtherFeedback.Change, CheckHiddenOtherVote.Change
        '更多子页面（无具体内容的）
        If Not user Then Return
        If Settings.Get(Of Boolean)("UiHiddenOtherHelp") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest") AndAlso
            (Not Settings.Get(Of Boolean)("UiHiddenOtherFeedback") OrElse Not Settings.Get(Of Boolean)("UiHiddenOtherVote")) Then
            CheckHiddenOtherAbout.Checked = False
            CheckHiddenOtherTest.Checked = False
            CheckHiddenOtherHelp.Checked = False
        End If
    End Sub

    '警告提示
    Private Sub HiddenHint(sender As Object, user As Boolean) Handles CheckHiddenFunctionHidden.Change, CheckHiddenPageSetup.Change, CheckHiddenSetupUI.Change
        If AniControlEnabled = 0 AndAlso sender.Checked Then Hint("按 F12 即可暂时关闭功能隐藏设置。千万别忘了，要不然设置就改不回来了……")
    End Sub

#End Region

    '原版主题配色预设：色相 / 饱和度 / 亮度偏移
    Private Shared ReadOnly ThemePresetHue As Integer() = {210, 175, 120, 45, 30, 210, 330, 275, 40, 25, 0, 205, 300, 50}
    Private Shared ReadOnly ThemePresetSat As Integer() = {85, 85, 85, 85, 45, 0, 80, 85, 60, 90, 85, 85, 90, 90}
    Private Shared ReadOnly ThemePresetLight As Integer() = {0, 0, 0, 0, -10, -35, 0, 0, 0, 0, 0, -5, 0, 5}

    Private Sub ThemeRadio_Check(sender As Object, e As RouteEventArgs)
        Dim Radio As MyRadioBox = TryCast(sender, MyRadioBox)
        If Radio IsNot Nothing Then ApplyThemePresetByName(Radio.Name)
    End Sub

    Private Sub ApplyThemePresetByName(ThemeName As String)
        If String.IsNullOrEmpty(ThemeName) OrElse Not ThemeName.StartsWith("RadioLauncherTheme") Then Return
        Dim Id As Integer
        If Not Integer.TryParse(ThemeName.Substring("RadioLauncherTheme".Length), Id) Then Return
        ApplyThemePreset(Id)
    End Sub

    Private Sub ApplyThemePreset(Id As Integer)
        If Id < 0 OrElse Id >= ThemePresetHue.Length Then Return
        ColorHue = ThemePresetHue(Id)
        ColorSat = ThemePresetSat(Id)
        ColorLightAdjust = ThemePresetLight(Id)
        Settings.Set("UltraThemePreset", Id)
        Settings.Set("UltraThemeSat", ColorSat)
        Settings.Set("UltraThemeLight", ColorLightAdjust)
        Settings.Set("UltraThemeHue", ColorHue) '同步 Ultra 色调，避免下次应用外观时被覆盖
        UltraAppearance.RefreshThemeColors() '带上颜色浓度，不用裸 ThemeRefresh
        UpdateThemeColorSelection()
    End Sub

    ''' <summary>Ultra：解锁全部主题。原来的赞助遮罩会把基础卡片下半部分整块盖住，导致主题点不动。</summary>
    Private Sub UnlockAllThemes()
        PanLauncherHide.Visibility = Visibility.Collapsed
        For Each Control As UIElement In PanLauncherTheme.Children
            If TypeOf Control Is MyRadioBox Then
                Dim Radio As MyRadioBox = CType(Control, MyRadioBox)
                If Radio Is RadioLauncherTheme5Gray Then
                    Radio.Visibility = Visibility.Collapsed '灰壳只用于未解锁时的视觉，解锁后隐藏
                Else
                    Radio.IsEnabled = True
                    Radio.Opacity = 1
                    Dim Target As MyRadioBox = Radio
                    RemoveHandler Target.Check, AddressOf ThemeRadio_Check
                    AddHandler Target.Check, AddressOf ThemeRadio_Check
                End If
            ElseIf TypeOf Control Is TextBlock Then
                CType(Control, TextBlock).Visibility = Visibility.Collapsed '点击拦截层
            End If
        Next
        LabLauncherTheme5Unlock.Visibility = Visibility.Collapsed
        LabLauncherTheme8Copy.Visibility = Visibility.Collapsed
        LabLauncherTheme9Copy.Visibility = Visibility.Collapsed
        LabLauncherTheme11Click.Visibility = Visibility.Collapsed
    End Sub

    '赞助 PCL：直接用 ShellExecute 打开，避免路径处理把网址当成文件
    Private Sub BtnSupportPCL_Click() Handles BtnSupportPCL.Click
        Const Url As String = "https://meloong.com/afd/a/LTCat"
        Try
            Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo With {
                .FileName = Url,
                .UseShellExecute = True})
            Hint("正在打开赞助页面，感谢支持！", HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "打开赞助页面失败")
            ClipboardSet(Url, False)
            Hint("无法自动打开浏览器，赞助链接已复制到剪贴板。", HintType.Red)
        End Try
    End Sub

    'Ultra 自定义背景：只有选中静态 / 动态背景时才显示对应的文件选择行
    Public Sub RefreshUltraBackgroundPickers()
        If PanPickImage Is Nothing OrElse PanPickVideo Is Nothing OrElse RadioBgStatic Is Nothing OrElse RadioBgVideo Is Nothing Then Return
        PanPickImage.Visibility = If(RadioBgStatic.Checked, Visibility.Visible, Visibility.Collapsed)
        PanPickVideo.Visibility = If(RadioBgVideo.Checked, Visibility.Visible, Visibility.Collapsed)
        RefreshUltraBackgroundLabels()
    End Sub

    Private Sub RadioBg_Changed(sender As Object, e As RouteEventArgs) Handles RadioBgDefault.Changed, RadioBgStatic.Changed, RadioBgVideo.Changed
        RefreshUltraBackgroundPickers()
    End Sub

#Region "Ultra 多文件背景"

    Private Const BgSplitter As String = "|"

    ''' <summary>读取已导入的文件列表，自动过滤失效路径。</summary>
    Private Function BgFiles(isVideo As Boolean) As List(Of String)
        Dim Raw As String = Settings.Get(Of String)(If(isVideo, "UltraBackgroundVideoList", "UltraBackgroundImageList"))
        Dim Result As New List(Of String)
        If String.IsNullOrEmpty(Raw) Then Return Result
        For Each FileItem As String In Raw.Split("|"c)
            If Not String.IsNullOrWhiteSpace(FileItem) AndAlso IO.File.Exists(FileItem) AndAlso Not Result.Contains(FileItem) Then Result.Add(FileItem)
        Next
        Return Result
    End Function

    Private Function BgIndex(isVideo As Boolean, count As Integer) As Integer
        If count = 0 Then Return 0
        Dim Raw As Integer = Settings.Get(Of Integer)(If(isVideo, "UltraBackgroundVideoIndex", "UltraBackgroundImageIndex"))
        Return Math.Max(0, Math.Min(count - 1, Raw))
    End Function

    ''' <summary>显示「第几个 / 共几个 + 当前文件名」。</summary>
    Private Sub RefreshUltraBackgroundLabels()
        Dim Imgs As List(Of String) = BgFiles(False)
        Dim Vids As List(Of String) = BgFiles(True)
        Dim ImgIndex As Integer = BgIndex(False, Imgs.Count)
        Dim VidIndex As Integer = BgIndex(True, Vids.Count)
        LabImagePath.Text = If(Imgs.Count = 0, "未选择图片", $"{ImgIndex + 1}/{Imgs.Count}　{IO.Path.GetFileName(Imgs(ImgIndex))}")
        LabVideoPath.Text = If(Vids.Count = 0, "未选择视频", $"{VidIndex + 1}/{Vids.Count}　{IO.Path.GetFileName(Vids(VidIndex))}")
        '直接把当前图片显示出来
        If Imgs.Count = 0 Then
            ImgPreview.Source = Nothing
        Else
            Try
                Dim Bmp As New System.Windows.Media.Imaging.BitmapImage()
                Bmp.BeginInit()
                Bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad
                Bmp.UriSource = New Uri(Imgs(ImgIndex), UriKind.Absolute)
                Bmp.EndInit()
                Bmp.Freeze()
                ImgPreview.Source = Bmp
            Catch ex As Exception
                ImgPreview.Source = Nothing
            End Try
        End If
    End Sub

    ''' <summary>把列表里的当前项写回单路径设置，供 UltraAppearance 读取。</summary>
    Private Sub ApplyBgCurrent(isVideo As Boolean)
        Dim Files As List(Of String) = BgFiles(isVideo)
        Dim Key As String = If(isVideo, "UltraBackgroundVideo", "UltraBackgroundImage")
        Settings.Set(Key, If(Files.Count = 0, "", Files(BgIndex(isVideo, Files.Count))))
    End Sub

    ''' <summary>选择并导入文件。append=False 由单选项触发，取消则切回默认白色。</summary>
    Private Sub PickBgFiles(isVideo As Boolean, append As Boolean)
        Dim Dlg As New Microsoft.Win32.OpenFileDialog With {
            .Title = If(isVideo, "选择动态背景视频（可多选）", "选择静态背景图片（可多选）"),
            .Filter = If(isVideo,
                         "视频文件 (*.mp4;*.wmv;*.avi;*.mov)|*.mp4;*.wmv;*.avi;*.mov|所有文件 (*.*)|*.*",
                         "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件 (*.*)|*.*"),
            .CheckFileExists = True,
            .Multiselect = True}
        If Dlg.ShowDialog() <> True Then
            If Not append Then Settings.Set("UltraBackgroundMode", 0) '取消 → 切回默认
            Return
        End If

        Dim ListKey As String = If(isVideo, "UltraBackgroundVideoList", "UltraBackgroundImageList")
        Dim IndexKey As String = If(isVideo, "UltraBackgroundVideoIndex", "UltraBackgroundImageIndex")
        Dim Files As List(Of String) = If(append, BgFiles(isVideo), New List(Of String))
        For Each Item As String In Dlg.FileNames
            If Not Files.Contains(Item) Then Files.Add(Item)
        Next
        If Files.Count = 0 Then
            If Not append Then Settings.Set("UltraBackgroundMode", 0)
            Return
        End If
        Dim NewIndex As Integer = If(append, Math.Max(0, Math.Min(Files.Count - 1, Settings.Get(Of Integer)(IndexKey))), 0)
        Settings.Set(ListKey, String.Join(BgSplitter, Files))
        Settings.Set(IndexKey, NewIndex)
        ApplyBgCurrent(isVideo)
        Settings.Set("UltraBackgroundMode", If(isVideo, 2, 1))
        RefreshUltraBackgroundPickers()
        FormMain.UpdateUltraAppearance(Nothing)
        Hint($"已导入 {Dlg.FileNames.Length} 个文件，共 {Files.Count} 个", HintType.Green)
    End Sub

    ''' <summary>在已导入的多个文件之间循环切换。</summary>
    Private Sub ShiftBg(isVideo As Boolean, delta As Integer)
        Dim Files As List(Of String) = BgFiles(isVideo)
        If Files.Count < 2 Then Return
        Dim IndexKey As String = If(isVideo, "UltraBackgroundVideoIndex", "UltraBackgroundImageIndex")
        Settings.Set(IndexKey, (BgIndex(isVideo, Files.Count) + delta + Files.Count) Mod Files.Count)
        ApplyBgCurrent(isVideo)
        RefreshUltraBackgroundLabels()
        FormMain.UpdateUltraAppearance(Nothing)
    End Sub

    '点单选项时拦截默认选中，直接弹出文件选择（仅用户鼠标点击会触发 PreviewCheck）
    Private Sub RadioBgStatic_PreviewCheck(sender As Object, e As RouteEventArgs) Handles RadioBgStatic.PreviewCheck
        If Not e.RaiseByMouse Then Return
        e.Handled = True
        PickBgFiles(False, False)
    End Sub

    Private Sub RadioBgVideo_PreviewCheck(sender As Object, e As RouteEventArgs) Handles RadioBgVideo.PreviewCheck
        If Not e.RaiseByMouse Then Return
        e.Handled = True
        PickBgFiles(True, False)
    End Sub

    Private Sub BtnPickImage_Click() Handles BtnPickImage.Click
        PickBgFiles(False, True)
    End Sub

    Private Sub BtnPickVideo_Click() Handles BtnPickVideo.Click
        PickBgFiles(True, True)
    End Sub

    Private Sub BtnImageDelete_Click() Handles BtnImageDelete.Click
        DeleteBgCurrent(False)
    End Sub

    Private Sub BtnVideoDelete_Click() Handles BtnVideoDelete.Click
        DeleteBgCurrent(True)
    End Sub

    ''' <summary>删除当前文件；删空后回到默认白色。</summary>
    Private Sub DeleteBgCurrent(isVideo As Boolean)
        Dim Files As List(Of String) = BgFiles(isVideo)
        If Files.Count = 0 Then Return
        Dim Index As Integer = BgIndex(isVideo, Files.Count)
        Dim Removed As String = Files(Index)
        Files.RemoveAt(Index)
        Dim ListKey As String = If(isVideo, "UltraBackgroundVideoList", "UltraBackgroundImageList")
        Dim IndexKey As String = If(isVideo, "UltraBackgroundVideoIndex", "UltraBackgroundImageIndex")
        If Files.Count = 0 Then
            Settings.Set(ListKey, "")
            Settings.Set(IndexKey, 0)
            Settings.Set(If(isVideo, "UltraBackgroundVideo", "UltraBackgroundImage"), "")
            Settings.Set("UltraBackgroundMode", 0)
        Else
            Settings.Set(ListKey, String.Join(BgSplitter, Files))
            Settings.Set(IndexKey, Math.Min(Index, Files.Count - 1))
            ApplyBgCurrent(isVideo)
            FormMain.UpdateUltraAppearance(Nothing)
        End If
        RefreshUltraBackgroundPickers()
        Hint("已删除：" & IO.Path.GetFileName(Removed), HintType.Green)
    End Sub

    Private Sub BtnImagePrev_Click() Handles BtnImagePrev.Click
        ShiftBg(False, -1)
    End Sub
    Private Sub BtnImageNext_Click() Handles BtnImageNext.Click
        ShiftBg(False, 1)
    End Sub
    Private Sub BtnVideoPrev_Click() Handles BtnVideoPrev.Click
        ShiftBg(True, -1)
    End Sub
    Private Sub BtnVideoNext_Click() Handles BtnVideoNext.Click
        ShiftBg(True, 1)
    End Sub

    ''' <summary>兼容旧版单文件配置：把已存在的单路径迁移进列表。</summary>
    Private Sub MigrateBgList()
        For Each isVideo As Boolean In New Boolean() {False, True}
            Dim ListKey As String = If(isVideo, "UltraBackgroundVideoList", "UltraBackgroundImageList")
            Dim SingleKey As String = If(isVideo, "UltraBackgroundVideo", "UltraBackgroundImage")
            If Not String.IsNullOrEmpty(Settings.Get(Of String)(ListKey)) Then Continue For
            Dim Old As String = Settings.Get(Of String)(SingleKey)
            If Not String.IsNullOrEmpty(Old) AndAlso IO.File.Exists(Old) Then
                Settings.Set(ListKey, Old)
                Settings.Set(If(isVideo, "UltraBackgroundVideoIndex", "UltraBackgroundImageIndex"), 0)
            End If
        Next
    End Sub

#End Region

    '滑动条
    Private Sub SliderLoad()
        SliderMusicVolume.GetHintText = Function(v) Math.Ceiling(v * 0.1) & "%"
        SliderLauncherTransparent.GetHintText = Function(v) Math.Round(40 + v * 0.1) & "%"
        SliderLauncherHue.GetHintText = Function(v) v & "°"
        SliderLauncherSat.GetHintText = Function(v) v & "%"
        SliderLauncherDelta.GetHintText =
        Function(Value As Integer) As String
            If Value > 90 Then
                Return "+" & (Value - 90)
            ElseIf Value = 90 Then
                Return 0
            Else
                Return Value - 90
            End If
        End Function
        SliderLauncherLight.GetHintText =
        Function(Value As Integer) As String
            If Value > 20 Then
                Return "+" & (Value - 20)
            ElseIf Value = 20 Then
                Return 0
            Else
                Return Value - 20
            End If
        End Function
        SliderBackgroundOpacity.GetHintText = Function(v) Math.Round(v * 0.1) & "%"
        SliderBgOpacity.GetHintText = Function(v) v & "%"
        SliderBackgroundBlur.GetHintText = Function(v) v & " 像素"
    End Sub

    '=====================================================================
    '  Ultra：8 种默认主题色 + 调色盘自由选色
    '  复用原版 ThemePresetHue / Sat / Light 数组，Id 与 UiLauncherTheme 一致。
    '=====================================================================
    Private Shared ReadOnly ThemeColorIds As Integer() = {0, 1, 2, 3, 6, 7, 9, 10}
    Private Shared ReadOnly ThemeColorNames As String() = {"龙猫蓝", "甜柠青", "小草绿", "菠萝黄", "铁杆粉", "神秘紫", "活跃橙", "跳票红"}

    ''' <summary>生成 8 个圆形预设色块。</summary>
    Private Sub BuildThemeColorPicker()
        If PanThemeColors Is Nothing Then Return
        PanThemeColors.Children.Clear()
        For i As Integer = 0 To ThemeColorIds.Length - 1
            Dim Id As Integer = ThemeColorIds(i)
            Dim C As Color = CType(New MyColor().FromHSL2(ThemePresetHue(Id), ThemePresetSat(Id), 55 + ThemePresetLight(Id)), Color)
            Dim Dot As New Border With {
                .Width = 30, .Height = 30, .CornerRadius = New CornerRadius(15),
                .Background = New SolidColorBrush(C),
                .BorderThickness = New Thickness(3),
                .Margin = New Thickness(0, 0, 10, 8),
                .Cursor = System.Windows.Input.Cursors.Hand,
                .Tag = Id, .ToolTip = ThemeColorNames(i)}
            AddHandler Dot.MouseLeftButtonUp, Sub(s, e) PickThemeColor(CInt(CType(s, Border).Tag))
            PanThemeColors.Children.Add(Dot)
        Next
        UpdateThemeColorSelection()
        HookColorEditor()
    End Sub

    ''' <summary>当前选中的色块画深色外圈；自定义颜色 (-1) 时全部不选中。</summary>
    Private Sub UpdateThemeColorSelection()
        If PanThemeColors Is Nothing Then Return
        Dim Current As Integer = Settings.Get(Of Integer)("UltraThemePreset")
        For Each Dot As Border In PanThemeColors.Children
            Dim Selected As Boolean = CInt(Dot.Tag) = Current
            Dot.BorderBrush = If(Selected,
                New SolidColorBrush(Color.FromRgb(45, 45, 48)),
                New SolidColorBrush(Colors.Transparent))
        Next
    End Sub

    Private Sub PickThemeColor(Id As Integer)
        If Id < 0 OrElse Id >= ThemePresetHue.Length Then Return
        ColorHue = ThemePresetHue(Id)
        ColorSat = ThemePresetSat(Id)
        ColorLightAdjust = ThemePresetLight(Id)
        '最后写 UltraThemeHue：它的 OnChanged 会统一刷新外观，此时其余设置已是新值
        Settings.Set("UltraThemePreset", Id)
        Settings.Set("UltraThemeSat", ColorSat)
        Settings.Set("UltraThemeLight", ColorLightAdjust)
        Settings.Set("UltraThemeHue", ColorHue)
        UltraAppearance.RefreshThemeColors()
        UpdateThemeColorSelection()
    End Sub

    ''' <summary>展开 / 收起内嵌调色盘。</summary>
    Private Sub BtnThemeCustom_Click() Handles BtnThemeCustom.Click
        If PanColorEditor.Visibility = Visibility.Visible Then
            PanColorEditor.Visibility = Visibility.Collapsed
            BtnThemeCustom.Text = "自定义颜色…"
        Else
            PanColorEditor.Visibility = Visibility.Visible
            BtnThemeCustom.Text = "收起调色盘"
            InitColorEditor()
        End If
    End Sub

    ''' <summary>RGB(0-255) → 色相(0-359) / 饱和度(0-100)。</summary>
    Private Shared Sub RgbToHsl(R As Integer, G As Integer, B As Integer, ByRef H As Integer, ByRef S As Integer)
        Dim Rf As Double = R / 255.0, Gf As Double = G / 255.0, Bf As Double = B / 255.0
        Dim Max As Double = Math.Max(Rf, Math.Max(Gf, Bf))
        Dim Min As Double = Math.Min(Rf, Math.Min(Gf, Bf))
        Dim D As Double = Max - Min
        Dim Hh As Double = 0
        If D > 0.000001 Then
            If Math.Abs(Max - Rf) < 0.000001 Then
                Hh = ((Gf - Bf) / D) Mod 6
            ElseIf Math.Abs(Max - Gf) < 0.000001 Then
                Hh = (Bf - Rf) / D + 2
            Else
                Hh = (Rf - Gf) / D + 4
            End If
            Hh *= 60
            If Hh < 0 Then Hh += 360
        End If
        Dim L As Double = (Max + Min) / 2
        Dim Ss As Double = If(D < 0.000001, 0, D / (1 - Math.Abs(2 * L - 1)))
        H = CInt(Math.Round(Hh)) Mod 360
        S = CInt(Math.Round(Math.Max(0, Math.Min(1, Ss)) * 100))
    End Sub


    '=====================================================================
    '  Ultra 内嵌调色盘：HSV 取色，全部在设置页卡片里完成，不弹新窗口
    '=====================================================================
    Private Const SvW As Double = 230, SvH As Double = 130, HueW As Double = 230
    Private EditorHue As Double = 210, EditorSat As Double = 1, EditorVal As Double = 1
    Private EditorBusy As Boolean = False
    Private Shared EditorHooked As Boolean = False

    Private Sub HookColorEditor()
        If EditorHooked Then Return
        EditorHooked = True
        AddHandler PanSv.MouseLeftButtonDown, Sub(s, e) SvStart(e.GetPosition(PanSv))
        AddHandler PanSv.MouseMove, Sub(s, e) SvDrag(e)
        AddHandler PanSv.MouseLeftButtonUp, Sub(s, e) SvEnd()
        AddHandler PanHueBar.MouseLeftButtonDown, Sub(s, e) HueStart(e.GetPosition(PanHueBar))
        AddHandler PanHueBar.MouseMove, Sub(s, e) HueDrag(e)
        AddHandler PanHueBar.MouseLeftButtonUp, Sub(s, e) HueEnd()
    End Sub

    Private Sub SvStart(P As Point)
        PanSv.CaptureMouse()
        PickSv(P)
    End Sub
    Private Sub SvDrag(e As System.Windows.Input.MouseEventArgs)
        If PanSv.IsMouseCaptured Then PickSv(e.GetPosition(PanSv))
    End Sub
    Private Sub SvEnd()
        PanSv.ReleaseMouseCapture()
    End Sub
    Private Sub HueStart(P As Point)
        PanHueBar.CaptureMouse()
        PickHue(P)
    End Sub
    Private Sub HueDrag(e As System.Windows.Input.MouseEventArgs)
        If PanHueBar.IsMouseCaptured Then PickHue(e.GetPosition(PanHueBar))
    End Sub
    Private Sub HueEnd()
        PanHueBar.ReleaseMouseCapture()
    End Sub

    Private Sub PickSv(P As Point)
        EditorSat = Math.Max(0, Math.Min(1, P.X / SvW))
        EditorVal = 1 - Math.Max(0, Math.Min(1, P.Y / SvH))
        Canvas.SetLeft(DotSv, EditorSat * SvW - 6)
        Canvas.SetTop(DotSv, (1 - EditorVal) * SvH - 6)
        UpdateColorPreview()
    End Sub

    Private Sub PickHue(P As Point)
        EditorHue = Math.Max(0, Math.Min(1, P.X / HueW)) * 360
        Canvas.SetLeft(DotHue, EditorHue / 360 * HueW - 2)
        BrushHue.Color = HsvToRgb(EditorHue, 1, 1)
        UpdateColorPreview()
    End Sub

    Private Sub UpdateColorPreview()
        Dim C As Color = HsvToRgb(EditorHue, EditorSat, EditorVal)
        RectColorPreview.Background = New SolidColorBrush(C)
        EditorBusy = True
        BoxColorHex.Text = $"#{C.R:X2}{C.G:X2}{C.B:X2}"
        EditorBusy = False
    End Sub

    ''' <summary>展开时用当前主题色反推 HSV，让取色点与界面一致。</summary>
    Private Sub InitColorEditor()
        Dim BaseHue As Integer = Settings.Get(Of Integer)("UltraThemeHue")
        Dim BaseSat As Integer = Settings.Get(Of Integer)("UltraThemeSat")
        Dim BaseLight As Integer = Settings.Get(Of Integer)("UltraThemeLight")
        Dim Current As Color = CType(New MyColor().FromHSL2(BaseHue, BaseSat, 55 + BaseLight), Color)
        RgbToHsv(Current, EditorHue, EditorSat, EditorVal)
        BrushHue.Color = HsvToRgb(EditorHue, 1, 1)
        Canvas.SetLeft(DotHue, EditorHue / 360 * HueW - 2)
        Canvas.SetLeft(DotSv, EditorSat * SvW - 6)
        Canvas.SetTop(DotSv, (1 - EditorVal) * SvH - 6)
        UpdateColorPreview()
    End Sub

    Private Sub BoxColorHex_TextChanged(sender As Object, e As TextChangedEventArgs) Handles BoxColorHex.TextChanged
        If EditorBusy Then Return
        Dim T As String = BoxColorHex.Text.Trim().TrimStart("#"c)
        If T.Length <> 6 Then Return
        Try
            Dim C As Color = Color.FromRgb(
                CByte(Convert.ToInt32(T.Substring(0, 2), 16)),
                CByte(Convert.ToInt32(T.Substring(2, 2), 16)),
                CByte(Convert.ToInt32(T.Substring(4, 2), 16)))
            RgbToHsv(C, EditorHue, EditorSat, EditorVal)
            BrushHue.Color = HsvToRgb(EditorHue, 1, 1)
            Canvas.SetLeft(DotHue, EditorHue / 360 * HueW - 2)
            Canvas.SetLeft(DotSv, EditorSat * SvW - 6)
            Canvas.SetTop(DotSv, (1 - EditorVal) * SvH - 6)
            RectColorPreview.Background = New SolidColorBrush(C)
        Catch
        End Try
    End Sub

    Private Sub BtnColorApply_Click() Handles BtnColorApply.Click
        Try
            Dim C As Color = HsvToRgb(EditorHue, EditorSat, EditorVal)
            Dim H As Integer, S As Integer
            RgbToHsl(C.R, C.G, C.B, H, S)
            ColorHue = H
            ColorSat = S
            ColorLightAdjust = 0
            Settings.Set("UltraThemePreset", -1)
            Settings.Set("UltraThemeSat", ColorSat)
            Settings.Set("UltraThemeLight", 0)
            Settings.Set("UltraThemeHue", ColorHue)
            UltraAppearance.RefreshThemeColors()
            UpdateThemeColorSelection()
            PanColorEditor.Visibility = Visibility.Collapsed
            BtnThemeCustom.Text = "自定义颜色…"
            Hint("已应用自定义主题颜色", HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "应用自定义主题颜色失败")
        End Try
    End Sub

    Private Sub BtnColorCancel_Click() Handles BtnColorCancel.Click
        PanColorEditor.Visibility = Visibility.Collapsed
        BtnThemeCustom.Text = "自定义颜色…"
    End Sub

    Private Shared Function HsvToRgb(H As Double, S As Double, V As Double) As Color
        Dim C As Double = V * S
        Dim X As Double = C * (1 - Math.Abs((H / 60) Mod 2 - 1))
        Dim M As Double = V - C
        Dim R As Double = 0, G As Double = 0, B As Double = 0
        Select Case CInt(Math.Floor(H / 60)) Mod 6
            Case 0 : R = C : G = X
            Case 1 : R = X : G = C
            Case 2 : G = C : B = X
            Case 3 : G = X : B = C
            Case 4 : R = X : B = C
            Case Else : R = C : B = X
        End Select
        Return Color.FromRgb(CByte(Math.Round((R + M) * 255)), CByte(Math.Round((G + M) * 255)), CByte(Math.Round((B + M) * 255)))
    End Function

    Private Shared Sub RgbToHsv(C As Color, ByRef H As Double, ByRef S As Double, ByRef V As Double)
        Dim R As Double = C.R / 255.0, G As Double = C.G / 255.0, B As Double = C.B / 255.0
        Dim Mx As Double = Math.Max(R, Math.Max(G, B)), Mn As Double = Math.Min(R, Math.Min(G, B))
        Dim D As Double = Mx - Mn
        V = Mx
        S = If(Mx <= 0.000001, 0, D / Mx)
        H = 0
        If D > 0.000001 Then
            If Math.Abs(Mx - R) < 0.000001 Then
                H = ((G - B) / D) Mod 6
            ElseIf Math.Abs(Mx - G) < 0.000001 Then
                H = (B - R) / D + 2
            Else
                H = (R - G) / D + 4
            End If
            H *= 60
            If H < 0 Then H += 360
        End If
    End Sub


End Class
