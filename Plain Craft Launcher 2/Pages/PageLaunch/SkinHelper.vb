Imports System.IO
Imports Newtonsoft.Json.Linq

''' <summary>
''' 皮肤管理：导入皮肤文件，并与 Quick Skin 模组联动（复制到它的 skins 目录、缺模组时自动安装）。
''' Quick Skin 支持 Fabric / Forge / NeoForge，Modrinth 项目 ID 为 zAIE84Ch，皮肤放在 .minecraft/quickskin/skins/。
''' </summary>
Public Module SkinHelper

    Public Const QuickSkinProjectId As String = "zAIE84Ch"

    ''' <summary>启动器自己保存的皮肤副本（用于预览与记忆）。</summary>
    Public ReadOnly Property LauncherSkinFolder As String = Paths.Base & "PCL\Skins\"

    ''' <summary>当前选中的版本。</summary>
    Public Function CurrentInstance() As McInstance
        Return McInstanceSelected
    End Function

    ''' <summary>Quick Skin 读取皮肤文件的目录。</summary>
    Public Function QuickSkinFolder(Instance As McInstance) As String
        If Instance Is Nothing Then Return ""
        Return Instance.PathIndie & "quickskin\skins\"
    End Function

    ''' <summary>该实例的加载器：fabric / forge / neoforge；不是模组加载器则返回空字符串。</summary>
    Public Function LoaderOf(Instance As McInstance) As String
        If Instance Is Nothing Then Return ""
        Try
            Dim Libs = Instance.JsonObject("libraries")
            If Libs Is Nothing Then Return ""
            For Each LibJson In Libs
                Dim Name = LibJson("name")?.ToString()
                If String.IsNullOrEmpty(Name) Then Continue For
                If Name.StartsWithF("net.fabricmc:fabric-loader") Then Return "fabric"
                If Name.StartsWithF("net.neoforged:neoforge") Then Return "neoforge"
                If Name.StartsWithF("net.minecraftforge:forge") Then Return "forge"
            Next
        Catch ex As Exception
            Logger.Warn(ex, "判断实例加载器失败")
        End Try
        Return ""
    End Function

    ''' <summary>实例对应的原版版本号，用于查模组版本。</summary>
    Public Function GameVersionOf(Instance As McInstance) As String
        If Instance Is Nothing Then Return ""
        Try
            Return Instance.Version.VanillaName
        Catch ex As Exception
            Return Instance.Name
        End Try
    End Function

    ''' <summary>mods 文件夹里是否已经有 Quick Skin。</summary>
    Public Function IsQuickSkinInstalled(Instance As McInstance) As Boolean
        If Instance Is Nothing Then Return False
        Try
            Dim Mods = Instance.PathIndie & "mods\"
            If Not Directory.Exists(Mods) Then Return False
            For Each File In Directory.GetFiles(Mods, "*.jar")
                'Quick Skin 的发布名是「Quick Skin - Fabric - 26.3-4.0.0.jar」，
                '带空格和短横线，先把它们去掉再匹配
                Dim Key = Path.GetFileName(File).ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "")
                If Key.Contains("quickskin") Then Return True
            Next
        Catch ex As Exception
            Logger.Warn(ex, "检查 Quick Skin 是否安装失败")
        End Try
        Return False
    End Function

    ''' <summary>
    ''' 导入皮肤：复制到启动器目录；同步开启时同时复制进 Quick Skin 的 skins 目录。
    ''' </summary>
    Public Function ImportSkin(SourcePath As String, Instance As McInstance, SyncToGame As Boolean) As String
        If Not File.Exists(SourcePath) Then Throw New Exception("皮肤文件不存在：" & SourcePath)
        Dim Ext = Path.GetExtension(SourcePath).ToLowerInvariant()
        If Ext <> ".png" AndAlso Ext <> ".jpg" AndAlso Ext <> ".jpeg" Then
            Throw New Exception("只支持 PNG（推荐）或 JPG 皮肤文件")
        End If
        DirectoryUtils.Create(LauncherSkinFolder)
        Dim Name = Path.GetFileNameWithoutExtension(SourcePath)
        Dim Target = LauncherSkinFolder & Name & Ext
        File.Copy(SourcePath, Target, True)
        Settings.Set("SkinLastFile", Target)
        If SyncToGame AndAlso Instance IsNot Nothing Then
            Dim Folder = QuickSkinFolder(Instance)
            DirectoryUtils.Create(Folder)
            File.Copy(SourcePath, Folder & Name & Ext, True)
            Logger.Info($"皮肤已复制到 Quick Skin 目录：{Folder & Name & Ext}")
        End If
        Return Target
    End Function

    ''' <summary>把已有的启动器皮肤同步到 Quick Skin 目录。</summary>
    Public Sub SyncSkinToGame(SkinPath As String, Instance As McInstance)
        If String.IsNullOrEmpty(SkinPath) OrElse Not File.Exists(SkinPath) OrElse Instance Is Nothing Then Return
        Dim Folder = QuickSkinFolder(Instance)
        DirectoryUtils.Create(Folder)
        File.Copy(SkinPath, Folder & Path.GetFileName(SkinPath), True)
    End Sub

    ''' <summary>从 Modrinth 自动安装 Quick Skin 及其必需依赖；回调在 UI 线程执行。</summary>
    Public Sub InstallQuickSkin(Instance As McInstance, OnDone As Action(Of Boolean, String))
        If Instance Is Nothing Then
            OnDone(False, "请先在左侧选择一个版本")
            Return
        End If
        RunInNewThread(
        Sub()
            Try
                Dim Loader = LoaderOf(Instance)
                If Loader = "" Then
                    RunInUi(Sub() OnDone(False, "当前版本不是 Fabric / Forge / NeoForge，无法自动安装 Quick Skin"))
                    Return
                End If
                Dim GameVersion = GameVersionOf(Instance)
                Dim Versions = QueryVersions(QuickSkinProjectId, Loader, GameVersion)
                If Versions.Count = 0 Then
                    RunInUi(Sub() OnDone(False, $"Quick Skin 暂不支持 {GameVersion}（{Loader}）"))
                    Return
                End If
                Dim ModsFolder = Instance.PathIndie & "mods\"
                DirectoryUtils.Create(ModsFolder)
                DownloadPrimaryFile(Versions(0), ModsFolder)
                '必需依赖：Architectury API、Fabric API 等
                Dim Deps = Versions(0)("dependencies")
                If Deps IsNot Nothing Then
                    For Each Dep In Deps
                        Try
                            If Dep("dependency_type")?.ToString() <> "required" Then Continue For
                            Dim ProjectId = Dep("project_id")?.ToString()
                            If String.IsNullOrEmpty(ProjectId) Then Continue For
                            Dim DepVersions = QueryVersions(ProjectId, Loader, GameVersion)
                            If DepVersions.Count > 0 Then DownloadPrimaryFile(DepVersions(0), ModsFolder)
                        Catch ex As Exception
                            Logger.Warn(ex, "安装 Quick Skin 的依赖失败")
                        End Try
                    Next
                End If
                RunInUi(Sub() OnDone(True, "Quick Skin 及其依赖已安装"))
            Catch ex As Exception
                Logger.Error(ex, "安装 Quick Skin 失败")
                RunInUi(Sub() OnDone(False, "安装失败：" & ex.Message))
            End Try
        End Sub, "Install QuickSkin")
    End Sub

    Private Function QueryVersions(ProjectId As String, Loader As String, GameVersion As String) As JArray
        Dim Url = $"https://api.modrinth.com/v2/project/{ProjectId}/version?loaders=[""{Loader}""]&game_versions=[""{GameVersion}""]"
        Return JArray.Parse(NetRequestByClient(Url, RequireJson:=True, Timeout:=15000))
    End Function

    Private Sub DownloadPrimaryFile(Version As JToken, ModsFolder As String)
        Dim Pick As JToken = Nothing
        For Each File In Version("files")
            If Pick Is Nothing Then Pick = File
            If File("primary") IsNot Nothing AndAlso File("primary").Value(Of Boolean)() Then
                Pick = File
                Exit For
            End If
        Next
        If Pick Is Nothing Then Return
        Dim FileName = Pick("filename").ToString()
        Dim Target = ModsFolder & FileName
        If IO.File.Exists(Target) Then Return
        NetDownloadByClient(Pick("url").ToString(), Target, SimulateBrowserHeaders:=True)
        Logger.Info($"已下载模组文件：{Target}")
    End Sub

End Module
