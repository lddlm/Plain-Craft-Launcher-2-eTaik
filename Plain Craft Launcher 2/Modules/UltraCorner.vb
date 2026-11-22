Imports System

''' <summary>
''' 按钮圆角与窗口圆角的调节。
''' 按钮圆角通过 DynamicResource 作用到所有 MyButton；
''' 窗口圆角直接改 FormMain 的裁剪矩形（BorderForm.Clip）。
''' </summary>
Public Module UltraCorner

    ''' <summary>按钮圆角（px）。</summary>
    Public ButtonCorner As Integer = 3
    ''' <summary>窗口圆角（px）。</summary>
    Public WindowCorner As Integer = 6

    ''' <summary>从设置读取并立即应用。</summary>
    Public Sub Refresh()
        Try
            ButtonCorner = Math.Max(0, Math.Min(12, Settings.Get(Of Integer)("UltraButtonCorner")))
            WindowCorner = Math.Max(0, Math.Min(16, Settings.Get(Of Integer)("UltraWindowCorner")))
        Catch ex As Exception
            Logger.Warn(ex, "读取圆角设置失败")
        End Try
        ApplyButton()
        ApplyWindow()
    End Sub

    ''' <summary>应用到所有 MyButton（模板里引用 DynamicResource）。</summary>
    Public Sub ApplyButton()
        If Application.Current Is Nothing Then Return
        Application.Current.Resources("UltraButtonCorner") = New CornerRadius(ButtonCorner)
    End Sub

    ''' <summary>应用到主窗口四角。</summary>
    Public Sub ApplyWindow()
        If FrmMain Is Nothing Then Return
        FrmMain.ApplyWindowCorner(WindowCorner)
    End Sub

End Module
