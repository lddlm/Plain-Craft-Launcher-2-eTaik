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
    ''' <summary>按钮最小高度（px）。0 = 不干预，用各按钮原尺寸；默认 30 让偏扁的按钮不再扁。</summary>
    Public ButtonHeight As Integer = 30
    ''' <summary>按钮最小宽度（px）。0 = 不干预（各按钮按自身内容/MinWidth）；>0 强制最小宽度。</summary>
    Public ButtonWidth As Integer = 0

    ''' <summary>按钮尺寸变更时触发，已存在的 MyButton 订阅后实时套用。</summary>
    Public Event ButtonSizeChanged()

    ''' <summary>从设置读取并立即应用。</summary>
    Public Sub Refresh()
        Try
            ButtonCorner = Math.Max(0, Math.Min(12, Settings.Get(Of Integer)("UltraButtonCorner")))
            WindowCorner = Math.Max(0, Math.Min(16, Settings.Get(Of Integer)("UltraWindowCorner")))
            ButtonHeight = Math.Max(0, Math.Min(60, Settings.Get(Of Integer)("UltraButtonHeight")))
            ButtonWidth = Math.Max(0, Math.Min(300, Settings.Get(Of Integer)("UltraButtonWidth")))
        Catch ex As Exception
            Logger.Warn(ex, "读取圆角/按钮尺寸设置失败")
        End Try
        ApplyButton()
        ApplyWindow()
        ApplyButtonSize()
    End Sub

    ''' <summary>应用到所有 MyButton（模板里引用 DynamicResource）。</summary>
    Public Sub ApplyButton()
        If Application.Current Is Nothing Then Return
        Application.Current.Resources("UltraButtonCorner") = New CornerRadius(ButtonCorner)
    End Sub

    ''' <summary>通知所有 MyButton 重新套用全局最小宽高。</summary>
    Public Sub ApplyButtonSize()
        RaiseEvent ButtonSizeChanged()
    End Sub

    ''' <summary>应用到主窗口四角。</summary>
    Public Sub ApplyWindow()
        If FrmMain Is Nothing Then Return
        FrmMain.ApplyWindowCorner(WindowCorner)
    End Sub

End Module
