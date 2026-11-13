Imports System.Runtime.InteropServices
Imports System.Windows.Shapes
Imports System.Windows.Threading

''' <summary>
''' Ultra：录制时的红框 + 计时浮层。
''' 两种形态：
'''   1. 区域录制：红框 + 计时器（ShowFrame）；
'''   2. 全屏 / 锁定游戏窗口：只有计时器，摆在画面右上角（ShowTimerOnly）。
''' 不让它进画面的手段：
'''   - 区域录制时红框比录制矩形**外扩 2px**，方框本身就落在录制区域之外；
'''   - 计时器只要压在画面里，一律加 WDA_EXCLUDEFROMCAPTURE，让桌面复制看不见它。
''' 红框整块红色，用 SetWindowRgn 裁成空心方框，因此不需要 AllowsTransparency。
''' </summary>
Public Class WindowRecordFrame

    ''' <summary>红框厚度（物理像素）。</summary>
    Private Const BorderPx As Integer = 2
    ''' <summary>计时器与红框之间的间隙（DIP）。</summary>
    Private Const TimerGap As Double = 6
    ''' <summary>只有计时器时，距离右上角的内缩距离（DIP）。</summary>
    Private Const TimerMargin As Double = 16
    ''' <summary>WDA_EXCLUDEFROMCAPTURE：让窗口在所有截屏 / 录屏接口里消失。</summary>
    Private Const WDA_EXCLUDEFROMCAPTURE As UInteger = &H11UI

    Private Shared Current As WindowRecordFrame
    Private Shared TimerWindow As Window
    Private Shared TimerText As TextBlock
    Private Shared TimerTick As DispatcherTimer

    ''' <summary>当前是否有红框在显示。</summary>
    Public Shared ReadOnly Property Shown As Boolean
        Get
            Return Current IsNot Nothing AndAlso Current.IsVisible
        End Get
    End Property

    ''' <summary>
    ''' 在指定录制区域外围显示红框与计时器。重复调用会先收掉旧的。
    ''' </summary>
    Public Shared Sub ShowFrame(Area As UltraVideo.GameArea)
        If FrmMain Is Nothing Then Return
        Try
            HideFrame()
            Dim Scale As Double = GetScale()
            Dim W As Double = Area.Width / Scale
            Dim H As Double = Area.Height / Scale
            If W < 8 OrElse H < 8 Then Return

            Dim Win As New WindowRecordFrame()
            Win.Left = (Area.X - BorderPx) / Scale
            Win.Top = (Area.Y - BorderPx) / Scale
            Win.Width = W + BorderPx * 2 / Scale
            Win.Height = H + BorderPx * 2 / Scale
            '先挂上引用再 Show：SourceInitialized 里裁剪失败时会把 Current 清空
            Current = Win
            Win.Show()
            If Not Win.IsVisible Then Return

            ShowTimer(Win, Scale)
        Catch ex As Exception
            Logger.Error(ex, "显示录制红框失败")
            HideFrame()
        End Try
    End Sub

    ''' <summary>
    ''' 只显示计时器，摆在录制区域的右上角（全屏 / 锁定游戏窗口录制用）。
    ''' 计时器必然落在录制区域内，所以固定加 WDA_EXCLUDEFROMCAPTURE，不进成品画面。
    ''' </summary>
    Public Shared Sub ShowTimerOnly(Area As UltraVideo.GameArea)
        If FrmMain Is Nothing Then Return
        Try
            HideFrame()
            Dim Scale As Double = GetScale()
            If Scale <= 0 Then Scale = 1

            If TimerWindow Is Nothing Then BuildTimer()
            '先定文字再排版，否则 SizeToContent 的宽度不准
            UpdateTimerText()
            TimerWindow.Show()
            TimerWindow.UpdateLayout()

            Dim TW As Double = If(TimerWindow.ActualWidth > 0, TimerWindow.ActualWidth, 120)
            Dim TH As Double = If(TimerWindow.ActualHeight > 0, TimerWindow.ActualHeight, 28)
            Dim LeftEdge As Double = Area.X / Scale
            Dim RightEdge As Double = (Area.X + Area.Width) / Scale
            Dim TopEdge As Double = Area.Y / Scale

            Dim X As Double = Math.Max(LeftEdge + 4, RightEdge - TW - TimerMargin)
            Dim Y As Double = TopEdge + TimerMargin
            TimerWindow.Left = X
            TimerWindow.Top = Y

            '压在画面里，只能靠系统级排除让它不出现在成品里
            If UltraVideo.UseCaptureExclude Then SetCaptureExclude(TimerWindow, True)
            Logger.Info($"[RecordFrame] 仅计时器 pos={CInt(X)},{CInt(Y)} size={CInt(TW)}x{CInt(TH)}")

            TimerTick = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(500)}
            AddHandler TimerTick.Tick, AddressOf OnTimerTick
            TimerTick.Start()
        Catch ex As Exception
            Logger.Error(ex, "显示录制计时器失败")
            HideFrame()
        End Try
    End Sub

    ''' <summary>收掉红框与计时器。</summary>
    Public Shared Sub HideFrame()
        Try
            If TimerTick IsNot Nothing Then
                TimerTick.Stop()
                TimerTick = Nothing
            End If
            If TimerWindow IsNot Nothing Then
                TimerWindow.Hide()
                TimerWindow = Nothing
                TimerText = Nothing
            End If
            If Current IsNot Nothing Then
                Try
                    Current.Close()
                Catch
                End Try
                Current = Nothing
            End If
        Catch ex As Exception
            Logger.Warn(ex, "关闭录制红框失败")
        End Try
    End Sub

#Region "红框本体"

    Private Sub WindowRecordFrame_SourceInitialized(sender As Object, e As EventArgs) Handles Me.SourceInitialized
        Try
            Dim Hwnd As IntPtr = New Interop.WindowInteropHelper(Me).Handle
            '不抢焦点、不占任务栏、鼠标穿透
            UltraVideo.MarkOverlayWindow(Hwnd)
            '兜底：万一红框被摆进了录制区域，也让截图 / 录屏拿不到它
            If UltraVideo.UseCaptureExclude Then
                UltraVideo.SetWindowCaptureExclude(Me, True)
                Logger.Info("[RecordFrame] 已排除捕获（WDA_EXCLUDEFROMCAPTURE）")
            End If
            '整块红色裁成空心方框
            Dim R As Native.RECT
            If Not Native.GetClientRect(Hwnd, R) Then
                Logger.Warn($"[RecordFrame] GetClientRect 失败，错误码 {Marshal.GetLastWin32Error()}")
                Return
            End If
            Dim CW As Integer = R.Right - R.Left
            Dim CH As Integer = R.Bottom - R.Top
            Logger.Info($"[RecordFrame] 红框窗口 client={CW}x{CH}  pos={Left},{Top} size={Width}x{Height}")
            If CW <= BorderPx * 2 OrElse CH <= BorderPx * 2 Then
                Logger.Warn($"[RecordFrame] client 尺寸过小（{CW}x{CH}），不裁剪")
                Return
            End If
            Dim Outer As IntPtr = Native.CreateRectRgn(0, 0, CW, CH)
            Dim Inner As IntPtr = Native.CreateRectRgn(BorderPx, BorderPx, CW - BorderPx, CH - BorderPx)
            Dim Ok As Boolean = Native.CombineRgn(Outer, Outer, Inner, Native.RGN_DIFF) <> 0
            Native.DeleteObject(Inner)
            Dim RegionOk As Boolean = Ok AndAlso Native.SetWindowRgn(Hwnd, Outer, True) <> 0
            Logger.Info($"[RecordFrame] 裁剪结果 combine={Ok} setrgn={RegionOk} err={Marshal.GetLastWin32Error()}")
            If RegionOk Then
                '成功后区域归系统所有，不能自己删
            Else
                Native.DeleteObject(Outer)
                Logger.Warn($"裁剪录制红框失败（错误码 {Marshal.GetLastWin32Error()}），本次不显示红框")
                Hide()
                Current = Nothing
            End If
        Catch ex As Exception
            Logger.Error(ex, "初始化录制红框失败")
            Try
                Hide()
            Catch
            End Try
            Current = Nothing
        End Try
    End Sub

#End Region

#Region "计时器"

    Private Shared Sub ShowTimer(Frame As WindowRecordFrame, Scale As Double)
        If TimerWindow Is Nothing Then BuildTimer()
        Frame.UpdateLayout()

        '先把文字定下来再排版：SizeToContent 的宽度要内容确定后才准
        UpdateTimerText()
        TimerWindow.Show()
        TimerWindow.UpdateLayout()

        Dim TW As Double = If(TimerWindow.ActualWidth > 0, TimerWindow.ActualWidth, 120)
        Dim TH As Double = If(TimerWindow.ActualHeight > 0, TimerWindow.ActualHeight, 28)

        Dim FrameLeft As Double = Frame.Left
        Dim FrameTop As Double = Frame.Top
        Dim FrameW As Double = Frame.Width
        Dim FrameH As Double = Frame.Height
        Dim ScreenH As Double = UltraVideo.ScreenHeight / Scale

        '优先放在红框上边缘外侧；上方空间不够就放下面；都不行才落进画面（靠 WDA 排除）
        Dim X As Double = Math.Max(0, FrameLeft + FrameW - TW)
        Dim Y As Double = FrameTop - TH - TimerGap
        If Y < 0 Then
            Dim Below As Double = FrameTop + FrameH + TimerGap
            Y = If(Below + TH <= ScreenH, Below, FrameTop + TimerGap)
        End If

        TimerWindow.Left = X
        TimerWindow.Top = Y
        '只有被迫落进录制区域时才排除捕获；平时留在外面，用户自己截图也能看到计时
        Dim Inside As Boolean = (Y + TH > FrameTop) AndAlso (Y < FrameTop + FrameH)
        If UltraVideo.UseCaptureExclude Then SetCaptureExclude(TimerWindow, Inside)

        TimerTick = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(500)}
        AddHandler TimerTick.Tick, AddressOf OnTimerTick
        TimerTick.Start()
    End Sub

    Private Shared Sub BuildTimer()
        TimerText = New TextBlock With {
            .Foreground = Brushes.White,
            .FontSize = 13,
            .VerticalAlignment = VerticalAlignment.Center}
        Dim Dot As New Ellipse With {
            .Width = 8, .Height = 8,
            .Fill = New SolidColorBrush(Color.FromRgb(&HE5, &H39, &H35)),
            .Margin = New Thickness(0, 0, 8, 0),
            .VerticalAlignment = VerticalAlignment.Center}
        Dim Panel As New StackPanel With {.Orientation = Orientation.Horizontal}
        Panel.Children.Add(Dot)
        Panel.Children.Add(TimerText)
        Dim Box As New Border With {
            .Background = New SolidColorBrush(Color.FromArgb(&HD9, &H10, &H10, &H12)),
            .CornerRadius = New CornerRadius(4),
            .Padding = New Thickness(10, 4, 11, 4),
            .Child = Panel}

        TimerWindow = New Window With {
            .WindowStyle = WindowStyle.None,
            .ResizeMode = ResizeMode.NoResize,
            .ShowInTaskbar = False,
            .ShowActivated = False,
            .Topmost = True,
            .WindowStartupLocation = WindowStartupLocation.Manual,
            .AllowsTransparency = True,
            .Background = Brushes.Transparent,
            .SizeToContent = SizeToContent.WidthAndHeight,
            .Content = Box,
            .Title = "录制时长"}
        AddHandler TimerWindow.SourceInitialized,
            Sub(s, e)
                Try
                    UltraVideo.MarkOverlayWindow(New Interop.WindowInteropHelper(CType(s, Window)).Handle)
                Catch
                End Try
            End Sub
        TimerWindow.Show()
        TimerWindow.UpdateLayout()
        TimerWindow.Hide()
    End Sub

    Private Shared Sub OnTimerTick(sender As Object, e As EventArgs)
        UpdateTimerText()
        If TimerWindow Is Nothing Then Return
        '记录中意外中断时把红框也收掉
        If Not UltraVideo.IsRecording Then HideFrame()
    End Sub

    Private Shared Sub UpdateTimerText()
        If TimerText Is Nothing Then Return
        Dim Elapsed As TimeSpan = UltraVideo.RecordingElapsed
        If Elapsed.TotalHours >= 1 Then
            TimerText.Text = $"{CInt(Elapsed.TotalHours)}:{Elapsed.Minutes:00}:{Elapsed.Seconds:00}"
        Else
            TimerText.Text = $"{CInt(Elapsed.TotalMinutes):00}:{Elapsed.Seconds:00}"
        End If
    End Sub

#End Region

#Region "工具"

    ''' <summary>开关某个窗口的「从截屏 / 录屏里消失」属性。失败只记日志，不影响录制。</summary>
    Private Shared Sub SetCaptureExclude(Win As Window, Exclude As Boolean)
        UltraVideo.SetWindowCaptureExclude(Win, Exclude)
    End Sub

    ''' <summary>物理像素 / DIP 的换算比例。</summary>
    Private Shared Function GetScale() As Double
        Try
            Dim DipW As Double = SystemParameters.PrimaryScreenWidth
            If DipW > 1 Then
                Dim S As Double = UltraVideo.ScreenWidth / DipW
                If S > 0 AndAlso Not Double.IsNaN(S) Then Return S
            End If
        Catch
        End Try
        Return 1
    End Function

    Private NotInheritable Class Native

        <StructLayout(LayoutKind.Sequential)>
        Public Structure RECT
            Public Left As Integer
            Public Top As Integer
            Public Right As Integer
            Public Bottom As Integer
        End Structure

        Public Const RGN_DIFF As Integer = 4

        <DllImport("user32.dll")>
        Public Shared Function GetClientRect(hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
        End Function

        <DllImport("gdi32.dll")>
        Public Shared Function CreateRectRgn(x1 As Integer, y1 As Integer, x2 As Integer, y2 As Integer) As IntPtr
        End Function

        <DllImport("gdi32.dll")>
        Public Shared Function CombineRgn(hrgnDst As IntPtr, hrgnSrc1 As IntPtr, hrgnSrc2 As IntPtr, fnCombineMode As Integer) As Integer
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function SetWindowRgn(hWnd As IntPtr, hRgn As IntPtr, bRedraw As Boolean) As Integer
        End Function

        <DllImport("gdi32.dll")>
        Public Shared Function DeleteObject(hObject As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function SetWindowDisplayAffinity(hWnd As IntPtr, dwAffinity As UInteger) As Boolean
        End Function

    End Class

#End Region

End Class
