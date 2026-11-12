Imports System.IO
Imports Newtonsoft.Json.Linq

Public Class PageDownloadJava

#Region "数据结构"

    Private Class JavaEntry
        Public Property Component As String
        Public Property VersionName As String
        Public Property Major As Integer
        Public Property Compat As String
        Public Property Group As Integer
        Public Property Raw As JObject
        Public ReadOnly Property Installed As Boolean
            Get
                Try
                    Dim RuntimeDir As String = Paths.AppData & ".minecraft\runtime\" & Component & "\"
                    If File.Exists(RuntimeDir & "bin\java.exe") Then Return True
                    '同一个运行时即便被 PCL 以别的路径收录，也算已安装，不允许再装一份
                    For Each J In Configs.JavaList.Get()
                        If J IsNot Nothing AndAlso J.Folder IsNot Nothing AndAlso
                           PathUtils.IsParentOf(RuntimeDir, J.Folder) Then Return True
                    Next
                    Return False
                Catch ex As Exception
                    Return False
                End Try
            End Get
        End Property
    End Class

    Private Const JavaListUrl As String = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json"
    Private Const JavaListMirror As String = "https://bmclapi2.bangbang93.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json"

    ''' <summary>
    ''' Mojang 运行时组件 → 适用 Minecraft 版本对照（依据官方版本清单 javaVersion.component，2026-11 核对）。
    ''' 断网时页面仍能显示完整对照信息；下载所需文件清单则必须联网获取。
    ''' </summary>
    Private Shared Function StaticEntries() As List(Of JavaEntry)
        Return New List(Of JavaEntry) From {
            New JavaEntry With {.Component = "java-runtime-epsilon", .VersionName = "25", .Major = 25, .Group = 0, .Compat = "Minecraft 26.1 ~ 26.3 及 26.4 预览版（当前最新版本）"},
            New JavaEntry With {.Component = "java-runtime-delta", .VersionName = "21", .Major = 21, .Group = 1, .Compat = "Minecraft 1.20.5 ~ 1.21.11"},
            New JavaEntry With {.Component = "java-runtime-gamma", .VersionName = "17", .Major = 17, .Group = 1, .Compat = "Minecraft 1.19 ~ 1.20.4"},
            New JavaEntry With {.Component = "java-runtime-beta", .VersionName = "17", .Major = 17, .Group = 1, .Compat = "Minecraft 1.18 ~ 1.18.2"},
            New JavaEntry With {.Component = "java-runtime-alpha", .VersionName = "16", .Major = 16, .Group = 2, .Compat = "Minecraft 1.17 ~ 1.17.1"},
            New JavaEntry With {.Component = "jre-legacy", .VersionName = "8u51", .Major = 8, .Group = 2, .Compat = "Minecraft 1.0 ~ 1.16.5（含远古版）"}
        }
    End Function

    ''' <summary>
    ''' 对官方清单中未内置对照的组件，按 Java 主版本推断适用 Minecraft 范围。
    ''' </summary>
    Private Shared Function GuessCompat(Major As Integer, Component As String) As String
        Dim Base As String
        Select Case Major
            Case Is >= 25
                Base = "Minecraft 26.1 及以上（含 26.4 预览版）"
            Case 21
                Base = "Minecraft 1.20.5 ~ 1.21.11"
            Case 17
                Base = "Minecraft 1.18 ~ 1.20.4"
            Case 16
                Base = "Minecraft 1.17 ~ 1.17.1"
            Case 8
                Base = "Minecraft 1.0 ~ 1.16.5（含远古版）"
            Case Else
                Base = "未提供官方兼容信息"
        End Select
        If Component.Contains("gamma") AndAlso Component.Contains("snapshot") Then
            Base = "Minecraft 1.19 ~ 1.20.4 的预览版 / 快照"
        ElseIf Component.Contains("snapshot") Then
            Base &= " 的预览版 / 快照"
        End If
        Return Base & "（" & Component & "）"
    End Function

#End Region

#Region "加载"

    Private IsLoadedOnce As Boolean = False
    Private IsLoading As Boolean = False
    Private Entries As New List(Of JavaEntry)
    Private DownloadingComponents As New HashSet(Of String)

    Private Sub PageDownloadJava_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If Not IsLoadedOnce Then
            IsLoadedOnce = True
            RefreshList()
        End If
    End Sub

    ''' <summary>
    ''' 重新获取并渲染 Java 版本列表。页面首次进入与左侧刷新按钮调用。
    ''' </summary>
    Public Sub RefreshList()
        If IsLoading Then Return
        IsLoading = True
        PanLoad.Visibility = Visibility.Visible
        PanJava.Children.Clear()
        RunInThread(
            Sub()
                Dim Result As List(Of JavaEntry)
                Try
                    Result = Fetch()
                Catch ex As Exception
                    Logger.Warn(ex, "获取 Java 版本列表失败，使用内置数据")
                    Result = StaticEntries()
                End Try
                RunInUi(
                    Sub()
                        Try
                            Entries = Result
                            BuildCards()
                        Catch ex As Exception
                            Logger.Error(ex, "渲染 Java 版本列表失败")
                        Finally
                            IsLoading = False
                            PanLoad.Visibility = Visibility.Collapsed
                        End Try
                    End Sub)
            End Sub)
    End Sub

    Private Function Fetch() As List(Of JavaEntry)
        Dim List As List(Of JavaEntry) = StaticEntries()
        Dim Json As String
        Try
            Json = NetRequestByLoader(DlVersionListOrder({JavaListUrl}, {JavaListMirror}), IsJson:=True)
        Catch ex As Exception
            Logger.Warn(ex, "获取 Mojang Java 运行时列表失败，仅显示内置数据")
            Return List
        End Try
        Try
            Dim Root As JObject = JObject.Parse(Json)
            Dim Platform As JToken = Root("windows-x64")
            If Platform Is Nothing Then Return List
            For Each Prop As JProperty In CType(Platform, JObject).Properties()
                If Prop.Name = "minecraft-java-exe" Then Continue For
                Dim First As JToken = Prop.Value.FirstOrDefault
                If First Is Nothing Then Continue For
                Dim Name As String = ""
                Dim Ver As JToken = First("version")
                If Ver IsNot Nothing AndAlso Ver("name") IsNot Nothing Then Name = Ver("name").ToString
                Dim Entry As JavaEntry = List.FirstOrDefault(Function(j) j.Component = Prop.Name)
                If Entry Is Nothing Then
                    Dim Major As Integer = CInt(Val(Name.Split("."c)(0).Split("u"c)(0)))
                    If Major <= 0 Then Major = CInt(Val(Name))
                    Entry = New JavaEntry With {
                        .Component = Prop.Name,
                        .VersionName = Name,
                        .Major = Major,
                        .Group = If(Major >= 25, 0, If(Major >= 17, 1, 2)),
                        .Compat = GuessCompat(Major, Prop.Name)
                    }
                    List.Add(Entry)
                End If
                Entry.Raw = CType(First.DeepClone(), JObject)
                If Name <> "" AndAlso Not Name.Contains("cacert") Then Entry.VersionName = Name
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析 Mojang Java 运行时列表失败")
        End Try
        Return List.OrderByDescending(Function(j) j.Major).ThenBy(Function(j) j.Component).ToList()
    End Function

#End Region

#Region "界面"

    Private Sub BuildCards()
        PanJava.Children.Clear()
        If Not Entries.Any() Then Return
        '最新版本：默认展开
        Dim MaxMajor As Integer = Entries.Max(Function(j) j.Major)
        Dim LatestItems As List(Of JavaEntry) = Entries.Where(Function(j) j.Major = MaxMajor).ToList()
        Dim CardLatest As New MyCard With {.Title = "最新版本", .Margin = New Thickness(0, 0, 0, 15)}
        Dim StackLatest As New StackPanel With {
            .Margin = New Thickness(20, MyCard.SwapedHeight, 18, 0),
            .VerticalAlignment = VerticalAlignment.Top,
            .RenderTransform = New TranslateTransform(0, 0)
        }
        CardLatest.Children.Add(StackLatest)
        CardLatest.SwapControl = StackLatest
        CardLatest.SetContentBuilder(LatestItems, Function(j) JavaListItem(j))
        CardLatest.BuildContent()
        PanJava.Children.Add(CardLatest)
        '全部版本：默认折叠
        Dim CardAll As New MyCard With {
            .Title = "全部版本（" & Entries.Count & "）",
            .Margin = New Thickness(0, 0, 0, 15),
            .IsSwapped = True
        }
        Dim StackAll As New StackPanel With {
            .Margin = New Thickness(20, MyCard.SwapedHeight, 18, 0),
            .VerticalAlignment = VerticalAlignment.Top,
            .RenderTransform = New TranslateTransform(0, 0)
        }
        CardAll.Children.Add(StackAll)
        CardAll.SwapControl = StackAll
        CardAll.SetContentBuilder(Entries, Function(j) JavaListItem(j))
        CardAll.BuildContent()
        PanJava.Children.Add(CardAll)
    End Sub

    Private Function JavaListItem(Entry As JavaEntry) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
            Function()
                Dim Title As String = "Java " & Entry.Major
                If Entry.VersionName <> "" AndAlso Entry.VersionName <> Entry.Major.ToString Then
                    Title &= "（" & Entry.VersionName & "）"
                End If
                Select Case Entry.Component
                    Case "java-runtime-beta"
                        Title &= " · 1.18 世代"
                    Case "java-runtime-gamma"
                        Title &= " · 1.19 世代"
                    Case "java-runtime-gamma-snapshot"
                        Title &= " · 快照"
                End Select
                Dim Item As New MyListItem With {
                    .Logo = PathImage & "Blocks/GoldBlock.png",
                    .LogoScale = 0.8,
                    .Title = Title,
                    .Info = If(Entry.Installed, "已安装 · ", "") & "兼容 " & Entry.Compat,
                    .Height = 46,
                    .Type = MyListItem.CheckType.Clickable,
                    .Tag = Entry,
                    .SnapsToDevicePixels = True
                }
                AddHandler Item.Click, AddressOf JavaItem_Click
                Return Item
            End Function) With {.Height = 46}
    End Function

#End Region

#Region "下载"

    Private Sub JavaItem_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Item As MyListItem = TryCast(sender, MyListItem)
        If Item Is Nothing Then Return
        Dim Entry As JavaEntry = TryCast(Item.Tag, JavaEntry)
        If Entry Is Nothing Then Return
        If Entry.Installed Then
            Hint("Java " & Entry.Major & " 已安装，无需重复下载。可在「设置 → Java 设置」中管理。", HintType.Blue)
            Return
        End If
        SyncLock DownloadingComponents
            If DownloadingComponents.Contains(Entry.Component) Then
                Hint("Java " & Entry.Major & " 正在下载中，请勿重复安装。", HintType.Blue)
                Return
            End If
            DownloadingComponents.Add(Entry.Component)
        End SyncLock
        If Entry.Raw Is Nothing Then
            SyncLock DownloadingComponents
                DownloadingComponents.Remove(Entry.Component)
            End SyncLock
            Hint("暂时无法获取该版本的下载清单，请检查网络后点击左侧刷新重试。", HintType.Red)
            Return
        End If
        Hint("正在准备下载 Java " & Entry.Major & "……", HintType.Blue)
        RunInThread(
            Sub()
                Try
                    Dim Target As New JavaDownloadInfo With {
                        .ComponentName = Entry.Component,
                        .Java = Entry.Raw,
                        .JavaVersion = New Version(Entry.Major, 0)
                    }
                    Dim Loader = CreateJavaDownloadLoader(Target)
                    RunInUi(
                        Sub()
                            Try
                                Loader.Show = True
                                Loader.Start()
                                LoaderTaskbar.Add(Loader)
                                FrmMain.BtnExtraDownload.ShowRefresh()
                                FrmMain.BtnExtraDownload.Ribble()
                                Hint("开始下载 Java " & Entry.Major & "（右下角可查看进度）", HintType.Blue)
                                RunInThread(
                                    Sub()
                                        For i = 0 To 120
                                            If Loader.State <> LoadState.Loading Then Exit For
                                            System.Threading.Thread.Sleep(500)
                                        Next
                                        SyncLock DownloadingComponents
                                            DownloadingComponents.Remove(Entry.Component)
                                        End SyncLock
                                        RunInUi(Sub() RefreshList())
                                    End Sub)
                            Catch ex As Exception
                                Logger.Error(ex, "启动 Java 下载失败")
                                SyncLock DownloadingComponents
                                    DownloadingComponents.Remove(Entry.Component)
                                End SyncLock
                            End Try
                        End Sub)
                Catch ex As Exception
                    Logger.Error(ex, "获取 Java 文件清单失败")
                    SyncLock DownloadingComponents
                        DownloadingComponents.Remove(Entry.Component)
                    End SyncLock
                    RunInUi(Sub() Hint("获取 Java 文件清单失败：" & ex.Message, HintType.Red))
                End Try
            End Sub)
    End Sub

#End Region

End Class
