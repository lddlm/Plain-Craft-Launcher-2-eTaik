Imports System.IO
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading
Imports PCL.World

Public Class PageInstanceSaves

    Private IsLoadedOnce As Boolean = False
    Private Saves As New List(Of SaveEntry)
    Private SelectedSave As SaveEntry = Nothing
    Private Hosts As New Dictionary(Of SaveEntry, StackPanel)

    Private Sub PageInstanceSaves_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        RefreshList()
        If IsLoadedOnce Then Return
        IsLoadedOnce = True
    End Sub

    ' 重新读取存档列表。刷新后尽量保持当前展开的存档。
    Private Sub RefreshList()
        Try
            If PageInstanceLeft.Instance Is Nothing Then Return
            Dim KeepPath = If(SelectedSave Is Nothing, Nothing, SelectedSave.Folder.FullName)
            DetachDetail()
            Saves = SaveManager.LoadAll(PageInstanceLeft.Instance.PathIndie)
            Hosts.Clear()
            PanSaves.Children.Clear()
            LabEmpty.Visibility = If(Saves.Count = 0, Visibility.Visible, Visibility.Collapsed)
            For Each Save In Saves
                Dim Host As New StackPanel
                Host.Children.Add(CreateSaveItem(Save))
                PanSaves.Children.Add(Host)
                Hosts(Save) = Host
            Next
            If KeepPath Is Nothing Then Return
            Dim Again = Saves.FirstOrDefault(Function(s) String.Equals(s.Folder.FullName, KeepPath, StringComparison.OrdinalIgnoreCase))
            If Again IsNot Nothing Then ExpandDetail(Again)
        Catch ex As Exception
            Logger.Warn(ex, "加载存档列表失败")
            Hint("加载存档列表失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    ' 单个存档条目：4:3 封面 + 名称 + 种子/版本/最后游玩。
    ' 用自绘 Grid 实现封面（MyListItem.Logo 是固定 34px 竖槽，ImageBrush 拉伸，无法做到 4:3 横图）。
    Private Function CreateSaveItem(Save As SaveEntry) As FrameworkElement
        Dim Cover = SavePreview.GetCoverPath(Save)
        Dim Item As New MyListItem With {
            .Title = Save.Title,
            .Info = BuildInfo(Save),
            .Height = 80,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = Save,
            .Margin = New Thickness(0, 1, 0, 1),
            .SnapsToDevicePixels = True,
            .PaddingLeft = 92
        }
        ' 封面底框：84x63 = 4:3，圆角跟随全局按钮圆角资源
        Dim CoverBox As New Border With {
            .Width = 84, .Height = 63,
            .CornerRadius = New CornerRadius(3),
            .ClipToBounds = True,
            .IsHitTestVisible = False,
            .HorizontalAlignment = HorizontalAlignment.Left,
            .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(4, 0, 0, 0),
            .SnapsToDevicePixels = True
        }
        CoverBox.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
        Dim CoverImg As New Image With {
            .Stretch = Stretch.UniformToFill,
            .IsHitTestVisible = False,
            .SnapsToDevicePixels = True
        }
        RenderOptions.SetBitmapScalingMode(CoverImg, BitmapScalingMode.HighQuality)
        Dim CoverPath = If(String.IsNullOrEmpty(Cover), "pack://application:,,,/images/Blocks/Grass.png", Cover)
        Try
            CoverImg.Source = New MyBitmap(CoverPath)
        Catch ex As Exception
            Logger.Warn(ex, "加载存档封面失败：" & CoverPath)
        End Try
        CoverBox.Child = CoverImg
        Grid.SetColumn(CoverBox, 1)   ' 立在 PaddingLeft 腾出的空列
        Grid.SetRowSpan(CoverBox, 4)
        Item.Children.Add(CoverBox)
        AddHandler Item.Click, Sub(sender, e) ToggleDetail(Save)
        Return Item
    End Function

    Private Function BuildInfo(Save As SaveEntry) As String
        Dim Parts As New List(Of String)
        If Save.Corrupted Then Parts.Add("存档数据损坏或缺少 level.dat")
        If Not String.IsNullOrEmpty(Save.Seed) Then Parts.Add("种子：" & Save.Seed)
        If Not String.IsNullOrEmpty(Save.GameVersion) Then Parts.Add("版本：" & Save.GameVersion)
        If Not String.IsNullOrEmpty(Save.GameMode) Then Parts.Add(Save.GameMode)
        If Save.LastPlayed > DateTime.MinValue Then Parts.Add("最后游玩：" & Save.LastPlayed.ToString("yyyy/MM/dd HH:mm"))
        Return String.Join("   ", Parts)
    End Function

    ' 详情正文：种子、版本、模式、最后游玩、实体统计。
    Private Function BuildDetail(Save As SaveEntry) As String
        Dim Lines As New List(Of String)
        Lines.Add("种子：" & If(String.IsNullOrEmpty(Save.Seed), "未知", Save.Seed))
        Lines.Add("游戏版本：" & If(String.IsNullOrEmpty(Save.GameVersion), "未知", Save.GameVersion))
        Lines.Add($"游戏模式：{Save.GameMode}　难度：{Save.Difficulty}")
        Lines.Add("最后游玩：" & If(Save.LastPlayed > DateTime.MinValue, Save.LastPlayed.ToString("yyyy/MM/dd HH:mm"), "未知"))
        If Save.ChunkTotal > 0 Then Lines.Add("区块总数：" & Save.ChunkTotal.ToString())
        Return String.Join(vbCrLf, Lines)
    End Function

    ' 点同一条存档：展开 / 收起。
    Private Sub ToggleDetail(Save As SaveEntry)
        If SelectedSave Is Save Then
            DetachDetail()
        Else
            ExpandDetail(Save)
        End If
    End Sub

    ' 把详情面板移动到该存档条目的正下方。
    Private Sub ExpandDetail(Save As SaveEntry)
        Dim Host As StackPanel = Nothing
        If Not Hosts.TryGetValue(Save, Host) Then Return
        DetachDetail()
        SelectedSave = Save
        Host.Children.Add(PanDetail)
        PanDetail.Visibility = Visibility.Visible
        LabDetailTitle.Text = Save.Title
        LabDetailInfo.Text = BuildDetail(Save)
        CountStats(Save)
        ' 展开后保证详情落在可视区域内
        Dispatcher.InvokeAsync(Sub() PanDetail.BringIntoView(), DispatcherPriority.Background)
    End Sub

    ' 从列表里摘掉详情面板（面板实例保留，下次直接复用）。
    Private Sub DetachDetail()
        Dim ParentPanel = TryCast(PanDetail.Parent, Panel)
        If ParentPanel IsNot Nothing Then ParentPanel.Children.Remove(PanDetail)
        PanDetail.Visibility = Visibility.Collapsed
        SelectedSave = Nothing
    End Sub

    ' 统计实体：后台跑，完成后回填到当前展开的详情（已去掉地形/种子预览）。
    Private Sub CountStats(Save As SaveEntry)
        Dim Work As Action =
            Sub()
                Try
                    If Save.EntityCount < 0 Then SaveManager.CountEntities(Save)
                Catch ex As Exception
                    Logger.Warn(ex, "统计存档实体失败")
                End Try
                RunInUi(
                    Sub()
                        If SelectedSave IsNot Save Then Return
                        LabDetailInfo.Text = BuildDetail(Save)
                    End Sub)
            End Sub
        RunInNewThread(Work, "存档详情")
    End Sub

    ' 更改存档封面：复制图片到存档目录，命名为 PclCover.png。
    Private Sub ChangeCover(Save As SaveEntry)
        Dim Dialog As New Microsoft.Win32.OpenFileDialog With {
            .Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*",
            .Title = "选择存档封面"
        }
        If Dialog.ShowDialog() <> True Then Return
        Try
            Dim Target = Path.Combine(Save.Folder.FullName, "PclCover.png")
            FileUtils.Delete(Target)
            Using Source As System.Drawing.Image = System.Drawing.Image.FromFile(Dialog.FileName)
                Source.Save(Target, System.Drawing.Imaging.ImageFormat.Png)
            End Using
            Save.CustomCover = Target
            Hint("封面已更新", HintType.Green)
            RefreshList()
        Catch ex As Exception
            Logger.Warn(ex, "设置存档封面失败")
            Hint("设置封面失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    ' 存档改名：改 level.dat 里的 LevelName（世界名称），列表与详情同步刷新。
    Private Sub RenameSave(Save As SaveEntry)
        Dim NewName = MyMsgBoxInput("存档改名", "输入新的存档名称（只改显示名称，存档文件夹名不变）。", Save.Title,
            New ObjectModel.Collection(Of Validate) From {New ValidateNullOrWhiteSpace, New ValidateLength(1, 100), New ValidateExcept({">", "|"})})
        If String.IsNullOrWhiteSpace(NewName) OrElse NewName = Save.Title Then Return
        Try
            SaveManager.RenameWorld(Save, NewName)
            Hint("存档已改名", HintType.Green)
            RefreshList()   ' 刷新后按 Folder.FullName 找回并重新展开详情
        Catch ex As Exception
            Logger.Warn(ex, "存档改名失败")
            Hint("改名失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    ' 删除存档（默认移到回收站，按住 Shift 永久删除）。
    Private Sub DeleteSave(Save As SaveEntry)
        Dim ShiftPressed = My.Computer.Keyboard.ShiftKeyDown
        Dim Warning = $"确定要删除存档「{Save.Title}」吗？{vbCrLf}{vbCrLf}" &
                      If(ShiftPressed, "此操作将永久删除，无法恢复！", "存档将被移到回收站。")
        If MyMsgBox(Warning, "删除存档", "取消", "确认删除") <> 2 Then Return
        Try
            DirectoryUtils.Delete(Save.Folder.FullName, Not ShiftPressed)
            Hint("存档已删除", HintType.Green)
            DetachDetail()
            RefreshList()
        Catch ex As Exception
            Logger.Warn(ex, "删除存档失败")
            Hint("删除存档失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub BtnRefresh_Click() Handles BtnRefresh.Click
        RefreshList()
    End Sub

    Private Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click
        If PageInstanceLeft.Instance Is Nothing Then Return
        Dim Folder = PageInstanceLeft.Instance.PathIndie & "saves"
        If DirectoryUtils.Exists(Folder) Then OpenExplorer(Folder)
    End Sub

    Private Sub BtnChangeCover_Click() Handles BtnChangeCover.Click
        If SelectedSave Is Nothing Then Return
        ChangeCover(SelectedSave)
    End Sub

    Private Sub BtnRename_Click() Handles BtnRename.Click
        If SelectedSave Is Nothing Then Return
        RenameSave(SelectedSave)
    End Sub

    Private Sub BtnOpenSave_Click() Handles BtnOpenSave.Click
        If SelectedSave Is Nothing Then Return
        OpenExplorer(SelectedSave.Folder.FullName)
    End Sub

    Private Sub BtnDelete_Click() Handles BtnDelete.Click
        If SelectedSave Is Nothing Then Return
        DeleteSave(SelectedSave)
    End Sub

End Class
