Public Class PageOtherAbout

    Private Shadows IsLoaded As Boolean = False
    Private Sub PageOtherAbout_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        If IsLoaded Then Return
        IsLoaded = True
        '赞助者名单里有 1300+ 个 TextBlock。给这张卡片做 Opacity 动画，
        'WPF 会在每一帧重绘整张卡片（软件渲染），切页动画会明显掉帧。
        '把它排除出页面级入场/退场动画，直接呈现终态；其余卡片照常错落淡入。
        DisabledPageAnimControls.Add(CardSponsors)

        ItemAboutPcl.Info = ItemAboutPcl.Info.Replace("%VERSION%", VersionDisplay).Replace("%VERSIONCODE%", VersionCode).Replace("%BRANCH%", CInt(BuildType))
        If BuildType = BuildTypes.Debug Then
            BtnDonateDonate.Visibility = Visibility.Collapsed
            BtnDonateOutput.Visibility = Visibility.Visible
        End If

    End Sub

    Public Shared Sub CopyIdentify() Handles BtnDonateCopy.Click
        ClipboardSet(Identify)
    End Sub
    Private Sub BtnDonateCodeInput_Click() Handles BtnDonateInput.Click
        InputPotatoCode(False)
    End Sub
    Private Sub BtnDonateOutput_Click(sender As Object, e As EventArgs) Handles BtnDonateOutput.Click
        GeneratePotatoCode()
    End Sub
    Private Sub BtnAboutUpdate_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAboutUpdate.Click
        UpdateCheckByButton()
    End Sub

End Class
