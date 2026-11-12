Imports System.IO

Public Class PageSetupJava

    Private Sub PageSetupJava_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        RefreshList()
    End Sub

    ''' <summary>
    ''' 重新渲染已有的 Java 列表。切换进本页、以及页面内的增删改都会调用。
    ''' </summary>
    Public Sub RefreshList()
        Try
            PanJavaList.Children.Clear()
            '按安装路径去重，避免同一个 Java 在列表里出现两次
            Dim Entries As New List(Of Java)
            Dim Seen As New HashSet(Of String)
            For Each J In Configs.JavaList.Get()
                If J IsNot Nothing AndAlso J.Folder IsNot Nothing AndAlso Seen.Add(J.Folder.ToLowerInvariant()) Then
                    Entries.Add(J)
                End If
            Next
            Dim Searching As Boolean = JavaListRefreshWorker.Running
            BtnJavaSearch.IsEnabled = Not Searching
            LabJavaCount.Text = If(Searching, "正在搜索 Java…",
                                   If(Entries.Count > 0, "共 " & Entries.Count & " 个 Java", "未找到 Java，可点击「导入已有 Java」"))
            If Entries.Count = 0 Then
                PanJavaList.Children.Add(New MyListItem With {
                    .Title = If(Searching, "正在搜索…", "暂无可用的 Java"),
                    .Info = "点击上方「重新搜索」或「导入已有 Java」",
                    .Height = 46,
                    .Type = MyListItem.CheckType.Clickable
                })
                Return
            End If
            For i = 0 To Entries.Count - 1
                Dim Entry As Java = Entries(i)
                Dim Index As Integer = i
                Dim Item As New MyListItem With {
                    .Logo = PathImage & "Blocks/GoldBlock.png",
                    .LogoScale = 0.8,
                    .Title = Entry.ToString,
                    .Info = Entry.Folder,
                    .Height = 46,
                    .Type = MyListItem.CheckType.Clickable,
                    .Tag = Entry,
                    .SnapsToDevicePixels = True
                }
                Dim Buttons As New List(Of MyIconButton)
                '提高优先级
                Dim Up As New MyIconButton With {
                    .Logo = Logo.IconButtonArrowUp, .LogoScale = 0.95, .Height = 24, .Width = 24,
                    .IsEnabled = Index > 0, .ToolTip = "提高优先级"
                }
                ToolTipService.SetShowOnDisabled(Up, True)
                AddHandler Up.Click, Sub() MoveJava(Entry, -1)
                Buttons.Add(Up)
                '降低优先级
                Dim Down As New MyIconButton With {
                    .Logo = Logo.IconButtonArrowDown, .LogoScale = 0.95, .Height = 24, .Width = 24,
                    .IsEnabled = Index < Entries.Count - 1, .ToolTip = "降低优先级"
                }
                ToolTipService.SetShowOnDisabled(Down, True)
                AddHandler Down.Click, Sub() MoveJava(Entry, 1)
                Buttons.Add(Down)
                '删除（整个安装目录移入回收站，官方运行时同样可删）
                Dim Del As New MyIconButton With {
                    .Logo = Logo.IconButtonStop, .Height = 24, .Width = 24,
                    .ToolTip = "彻底删除此 Java"
                }
                AddHandler Del.Click, Sub() DeleteJava(Entry)
                Buttons.Add(Del)
                '打开文件夹
                Dim Open As New MyIconButton With {
                    .Logo = Logo.IconButtonOpen, .LogoScale = 1.1, .Height = 24, .Width = 24, .ToolTip = "打开文件夹"
                }
                AddHandler Open.Click, Sub() OpenExplorer(Entry.JavaExePath)
                Buttons.Add(Open)
                Item.Buttons = Buttons
                PanJavaList.Children.Add(Item)
            Next
        Catch ex As Exception
            Logger.Error(ex, "刷新 Java 管理列表失败")
        End Try
    End Sub

    ' 二次确认后彻底删除整个 Java 安装目录（不可恢复），并从 PCL 的 Java 列表中移除。
    ''' <summary>
    ''' 结束正在使用指定 Java 安装目录的 java / javaw 进程，返回结束数量。
    ''' 其它目录下的 Java 进程会被保留，避免误关用另一个 Java 运行的游戏或服务。
    ''' </summary>
    Private Function KillJavaProcessesUnder(Root As String) As Integer
        Dim Count As Integer = 0
        Dim Target As String = Root.TrimEnd("\"c).ToLowerInvariant() & "\"
        Try
            For Each Proc As Diagnostics.Process In Diagnostics.Process.GetProcesses()
                Try
                    Dim ProcName As String = If(Proc.ProcessName, "").ToLowerInvariant()
                    If ProcName <> "java" AndAlso ProcName <> "javaw" Then Continue For
                    Dim ExePath As String = Nothing
                    Try
                        ExePath = Proc.MainModule.FileName
                    Catch ex As Exception
                        '取不到路径（权限或进程位数不匹配）时也结束，用户已确认删除该 Java
                    End Try
                    If ExePath IsNot Nothing AndAlso Not ExePath.ToLowerInvariant().StartsWith(Target) Then Continue For
                    Proc.Kill()
                    Proc.WaitForExit(2000)
                    Count += 1
                Catch ex As Exception
                    '进程已退出或无权操作，跳过
                Finally
                    Try
                        Proc.Dispose()
                    Catch ex As Exception
                    End Try
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, "结束 Java 进程时出现异常")
        End Try
        Return Count
    End Function

    Private Sub DeleteJava(Entry As Java)
        Dim Root As String = Nothing
        Try
            Dim Folder As String = If(Entry.Folder, "")
            If String.IsNullOrEmpty(Folder) OrElse Not File.Exists(Entry.JavaExePath) Then
                Hint("找不到该 Java 的文件，无法删除。", HintType.Red)
                Return
            End If
            Root = Path.GetDirectoryName(Folder.TrimEnd("\"c))
            '安全校验：必须是独立的 Java 安装目录，避免误删系统或游戏目录
            If String.IsNullOrEmpty(Root) OrElse
               Root.Equals(Path.GetPathRoot(Root), StringComparison.OrdinalIgnoreCase) OrElse
               Not File.Exists(Path.Combine(Root, "bin", "java.exe")) Then
                Hint("该 Java 的目录结构异常，已取消删除。", HintType.Red)
                Return
            End If
            Dim Name As String = Path.GetFileName(Root).ToLower
            If Array.IndexOf(New String() {"windows", "users", "program files", "programdata",
                                           "appdata", ".minecraft", "system32", "desktop", "documents"}, Name) >= 0 Then
                Hint("该目录不是独立的 Java 安装目录，已取消删除。", HintType.Red)
                Return
            End If
            Dim IsOfficial As Boolean = PathUtils.IsParentOf(Paths.AppData & ".minecraft\runtime\", Folder)
            Dim Warn As String = If(IsOfficial, vbCrLf & "注意：这是 Mojang 官方运行时，官方启动器或其他游戏可能仍在使用它。", "")
            '权限探测：当前用户能否写该目录。不提权、不弹 UAC。
            Dim Probe As String = Path.Combine(Root, "_pcl_java_del_probe.tmp")
            Dim Writable As Boolean = False
            Try
                File.WriteAllText(Probe, "")
                File.Delete(Probe)
                Writable = True
            Catch ex As Exception
                Try
                    File.Delete(Probe)
                Catch ex2 As Exception
                End Try
            End Try
            Dim Caption As String
            If Writable Then
                Caption = $"确定要删除这个 Java 吗？{vbCrLf}{vbCrLf}版本：{Entry}{vbCrLf}目录：{Root}{vbCrLf}{vbCrLf}整个目录会被彻底删除，无法恢复，也不会进入回收站。删除前会自动结束正在使用该 Java 的 java.exe / javaw.exe 进程（可能关闭正在运行的游戏）。{Warn}"
            Else
                Caption = $"这个 Java 位于受保护目录，PCL 当前权限无法删除磁盘文件（需要管理员权限）。{vbCrLf}{vbCrLf}版本：{Entry}{vbCrLf}目录：{Root}{vbCrLf}{vbCrLf}是否改为把它移出 PCL 的 Java 列表？移出后 PCL 不会再使用它，也不会被「重新搜索」找回；磁盘文件会保留，可在文件资源管理器中手动删除。{Warn}"
            End If
            If MyMsgBox(Caption, "删除 Java 确认", If(Writable, "彻底删除", "移出列表"), "取消", IsWarn:=True) <> 1 Then Return
            Hint(If(Writable, "正在删除 Java……", "正在移出 Java 列表……"), HintType.Blue)
            RunInThread(
                Sub()
                    Try
                        If Not Writable Then
                            RunInUi(
                                Sub()
                                    ManuallyRemoveJava(Entry)
                                    Hint("已移出 PCL 的 Java 列表（受保护目录，磁盘文件未删除）。", HintType.Green)
                                    RefreshList()
                                End Sub)
                            Return
                        End If
                        '先结束占用该目录的 Java 进程，释放 jvm.dll 等文件句柄
                        Dim Killed As Integer = KillJavaProcessesUnder(Root)
                        If Killed > 0 Then System.Threading.Thread.Sleep(600)
                        '逐文件删除：清只读 → 删文件 → 自底向上删空目录，避免单个文件卡住整棵树
                        Dim Failed As New List(Of String)
                        For Each F In Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                            Try
                                File.SetAttributes(F, FileAttributes.Normal)
                                File.Delete(F)
                            Catch ex As Exception
                                Failed.Add(F)
                            End Try
                        Next
                        Dim Dirs As String() = Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)
                        Array.Sort(Dirs)
                        Array.Reverse(Dirs)
                        For Each D In Dirs
                            Try
                                Directory.Delete(D, False)
                            Catch ex As Exception
                            End Try
                        Next
                        Directory.Delete(Root, False)
                        If Failed.Count > 0 Then Throw New IOException($"有 {Failed.Count} 个文件无法删除，例如：{Failed(0)}")
                        RunInUi(
                            Sub()
                                ManuallyRemoveJava(Entry)
                                Hint($"已彻底删除 Java：{Root}" & If(Killed > 0, $"（已结束 {Killed} 个 Java 进程）", ""), HintType.Green)
                                RefreshList()
                            End Sub)
                    Catch ex As Exception
                        Logger.Error(ex, "删除 Java 目录失败")
                        RunInUi(
                            Sub()
                                ManuallyRemoveJava(Entry)
                                Hint("无法彻底删除（" & ex.Message & "），已移出 PCL 列表。可点「打开文件夹」手动删除。", HintType.Red)
                                RefreshList()
                            End Sub)
                    End Try
                End Sub)
        Catch ex As Exception
            Logger.Error(ex, "准备删除 Java 失败")
            Hint("无法删除：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub MoveJava(Entry As Java, Offset As Integer)
        Configs.JavaList.Edit(
            Sub(ByRef List)
                Dim Index = List.IndexOf(Entry)
                Dim NewIndex = Index + Offset
                If Index < 0 OrElse NewIndex < 0 OrElse NewIndex >= List.Count Then Return
                List.RemoveAt(Index)
                List.Insert(NewIndex, Entry)
            End Sub)
        UpdateJavaLists()
        RefreshList()
    End Sub

    Private Sub BtnJavaSearch_Click() Handles BtnJavaSearch.Click
        ManuallySearchJava()
        '搜索在后台进行，结束后刷新本页（UpdateJavaLists 只负责原有的两个下拉框）
        RunInThread(
            Sub()
                For i = 0 To 120
                    If Not JavaListRefreshWorker.Running Then Exit For
                    System.Threading.Thread.Sleep(500)
                Next
                RunInUi(Sub() RefreshList())
            End Sub)
    End Sub

    Private Sub BtnJavaImport_Click() Handles BtnJavaImport.Click
        ManuallyImportJava()
        RefreshList()
    End Sub

    Private Sub BtnJavaRefresh_Click() Handles BtnJavaRefresh.Click
        RefreshList()
    End Sub

    Private Sub BtnJavaOpen_Click() Handles BtnJavaOpen.Click
        Try
            Dim Dir As String = Paths.AppData & ".minecraft\runtime\"
            If Not Directory.Exists(Dir) Then Directory.CreateDirectory(Dir)
            OpenExplorer(Dir)
        Catch ex As Exception
            Hint("无法打开运行时目录：" & ex.Message, HintType.Red)
        End Try
    End Sub

End Class
