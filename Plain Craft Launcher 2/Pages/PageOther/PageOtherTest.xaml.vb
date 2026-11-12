Imports System.Runtime.InteropServices

Public Class PageOtherTest

#Region "页面"

    Private Sub PageOtherTest_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If String.IsNullOrWhiteSpace(TextFolder.Text) Then TextFolder.Text = DefaultDownloadFolder()
        If String.IsNullOrWhiteSpace(LabCave.Text) Then LabCave.Text = GetRandomCave()
    End Sub

    Private Shared Function DefaultDownloadFolder() As String
        Return Paths.Base & "PCL\MyDownload\"
    End Function

#End Region

#Region "百宝箱按钮"

    Private Sub BtnJrrp_Click() Handles BtnJrrp.Click
        Jrrp()
    End Sub

    Private Sub BtnMemory_Click() Handles BtnMemory.Click
        MemoryOptimize(True)
    End Sub

    Private Sub BtnRubbish_Click() Handles BtnRubbish.Click
        RubbishClear()
    End Sub

    Private Sub BtnDontClick_Click() Handles BtnDontClick.Click
        Dim Jokes As String() = {
            "都说了千万别点……",
            "你点了。恭喜，什么也没有发生。",
            "这是一个按钮。它唯一的功能就是被点。",
            "PCL 没有崩溃，你的好奇心先崩溃了。",
            "别点。下次也别点。",
            "好奇心害死猫，但猫有九条命。"
        }
        MyMsgBox(Jokes(New Random().Next(Jokes.Length)), "千万别点")
    End Sub

#End Region

#Region "今日人品"

    Public Shared Sub Jrrp()
        '用「日期 + 当前用户名」生成稳定的每日人品值
        Dim Seed As Integer = (Date.Now.ToString("yyyyMMdd") & Environment.UserName).GetHashCode() And &H7FFFFFFF
        Dim Value As Integer = Seed Mod 101
        Dim Comment As String
        Select Case Value
            Case 0
                Comment = "……今天还是别开游戏了。"
            Case Is <= 20
                Comment = "运气不太行，挖矿的时候小心点。"
            Case Is <= 50
                Comment = "普普通通的一天，适合老老实实做任务。"
            Case Is <= 80
                Comment = "运气不错，去开个箱子试试？"
            Case Is <= 99
                Comment = "欧气满满，今天很适合挑战末影龙！"
            Case Else
                Comment = "满值人品！建议立刻去买彩票（开玩笑的）。"
        End Select
        MyMsgBox($"今日人品：{Value} / 100{vbCrLf}{Comment}", "今日人品")
    End Sub

#End Region

#Region "内存优化"

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetProcessWorkingSetSize(hProcess As IntPtr, dwMinimumWorkingSetSize As IntPtr, dwMaximumWorkingSetSize As IntPtr) As Boolean
    End Function

    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        MemoryOptimizeInternal(ShowHint)
    End Sub

    ''' <summary>
    ''' 释放内存：回收 PCL 自身与所有可访问进程的工作集，把闲置的物理内存归还给系统。
    ''' 与 PCL 官方「内存优化」语义一致（不局限于 MC）。
    ''' </summary>
    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        Try
            '先回收自身的垃圾
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()
            '回收自身的工作集
            Try
                Dim SelfProc As Diagnostics.Process = Diagnostics.Process.GetCurrentProcess()
                SetProcessWorkingSetSize(SelfProc.Handle, New IntPtr(-1), New IntPtr(-1))
            Catch
                '忽略自身回收失败
            End Try
            '回收其他进程的工作集（部分系统进程无权限，会自动跳过）
            Dim Count As Integer = 0
            For Each Proc In Diagnostics.Process.GetProcesses()
                Try
                    SetProcessWorkingSetSize(Proc.Handle, New IntPtr(-1), New IntPtr(-1))
                    Count += 1
                Catch
                    '无权限访问的进程直接跳过
                Finally
                    Proc.Dispose()
                End Try
            Next
            Logger.Info($"内存优化完成，已回收 {Count} 个进程的工作集")
            If ShowHint Then Hint("内存优化完成，已尝试释放闲置内存。", HintType.Green)
        Catch ex As Exception
            If ShowHint Then Hint("内存优化失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region

#Region "清理垃圾"

    Public Shared Sub RubbishClear()
        RunInThread(
        Sub()
            Dim Count As Integer = 0
            Dim Bytes As Long = 0
            Try
                '只清理 PCL 自己的临时文件与旧日志，不动游戏文件
                If IO.Directory.Exists(PathTemp) Then
                    For Each FilePath As String In IO.Directory.GetFiles(PathTemp, "*", IO.SearchOption.AllDirectories)
                        Try
                            Dim Info As New IO.FileInfo(FilePath)
                            Bytes += Info.Length
                            IO.File.Delete(FilePath)
                            Count += 1
                        Catch
                        End Try
                    Next
                End If
                For i As Integer = 2 To 5
                    Dim LogPath As String = Paths.Base & "PCL\Log" & i & ".txt"
                    If IO.File.Exists(LogPath) Then
                        Try
                            Bytes += New IO.FileInfo(LogPath).Length
                            IO.File.Delete(LogPath)
                            Count += 1
                        Catch
                        End Try
                    End If
                Next
                Dim SizeText As String = If(Bytes >= 1048576, $"{Bytes / 1048576:0.0} MB", $"{Bytes / 1024:0.0} KB")
                RunInUi(Sub() Hint($"清理完成：删除 {Count} 个文件，释放 {SizeText}。", HintType.Green))
            Catch ex As Exception
                RunInUi(Sub() Hint("清理失败：" & ex.Message, HintType.Red))
            End Try
        End Sub)
    End Sub

#End Region

#Region "回声洞与随机文案"

    Private Shared ReadOnly Caves As String() = {
        "「这个启动器居然真的有人用。」——某位路过的网友",
        "「我打开 PCL 只是为了看看今天的运气，结果忘了开游戏。」",
        "「建议把这句话写进回声洞，这样它就有内容了。」",
        "「PCL 的作者正在某个地方默默看着你点这个按钮。」",
        "「今天也是没有崩溃的一天，值得庆祝。」",
        "「你知道吗？点得越多，回声洞里的回声就越多。」",
        "「这条留言是本地生成的，所以你永远不会收到回复。」",
        "「如果生活给了你柠檬，就把它做成资源包。」",
        "「别看了，这里真的只是一段随机文本。」",
        "「你的 Minecraft 正在等你回去，真的。」"
    }

    Private Shared ReadOnly Hints As String() = {
        "今天也泡杯茶，慢慢玩吧。",
        "遇到问题先看日志，比猜测快得多。",
        "记得给存档做备份，尤其是要装新 Mod 的时候。",
        "偶尔换一个整合包，会有不一样的心情。",
        "启动器只是工具，游戏才是目的。",
        "别把 Java 装在中文路径里，能省很多事。",
        "显卡驱动和 Java 版本，往往决定了帧数。",
        "玩得开心比玩得高效更重要。"
    }

    Private Shared ReadOnly PresetHints As String() = {
        "先选一个版本，再慢慢调设置，不必一次改完。",
        "内存不是越大越好，留一点给系统。",
        "整合包自带配置，别急着全部覆盖。",
        "遇到崩溃先看崩溃报告的第一行。",
        "版本隔离能避免大部分 Mod 冲突。"
    }

    Public Shared Function GetRandomCave() As String
        Return Caves(New Random().Next(Caves.Length))
    End Function

    Public Shared Function GetRandomHint() As String
        Return Hints(New Random().Next(Hints.Length))
    End Function

    Public Shared Function GetRandomPresetHint() As String
        Return PresetHints(New Random().Next(PresetHints.Length))
    End Function

    Private Sub BtnCave_Click() Handles BtnCave.Click
        LabCave.Text = GetRandomCave()
    End Sub

#End Region

#Region "下载自定义文件"

    Private Sub BtnSelectFolder_Click() Handles BtnSelectFolder.Click
        Dim Folder As String = Dialogs.SelectFolder("选择保存文件夹", False).FirstOrDefault
        If Not String.IsNullOrWhiteSpace(Folder) Then TextFolder.Text = Folder
    End Sub

    Private Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click
        Dim Folder As String = TextFolder.Text.Trim()
        If String.IsNullOrEmpty(Folder) Then Folder = DefaultDownloadFolder()
        Try
            If Not IO.Directory.Exists(Folder) Then IO.Directory.CreateDirectory(Folder)
            OpenExplorer(Folder)
        Catch ex As Exception
            Hint("打开文件夹失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub BtnDownload_Click() Handles BtnDownload.Click
        Dim Url As String = TextUrl.Text.Trim()
        If String.IsNullOrEmpty(Url) Then
            Hint("请输入下载地址！", HintType.Red)
            Return
        End If
        Dim FileName As String = TextFileName.Text.Trim()
        If String.IsNullOrEmpty(FileName) Then
            Try
                FileName = IO.Path.GetFileName(New Uri(Url).AbsolutePath)
            Catch
            End Try
            If String.IsNullOrEmpty(FileName) Then FileName = "download.bin"
        End If
        StartCustomDownload(Url, FileName, TextFolder.Text.Trim())
    End Sub

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        If String.IsNullOrWhiteSpace(Folder) Then Folder = DefaultDownloadFolder()
        If Not Folder.EndsWith("\") Then Folder &= "\"
        Dim Target As String = Folder & FileName
        RunInThread(
        Sub()
            Try
                IO.Directory.CreateDirectory(Folder)
                NetDownloadByLoader({Url}, Target)
                RunInUi(Sub() Hint("下载完成：" & FileName, HintType.Green))
            Catch ex As Exception
                RunInUi(Sub() Hint("下载失败：" & ex.Message, HintType.Red))
            End Try
        End Sub)
    End Sub

#End Region

End Class
