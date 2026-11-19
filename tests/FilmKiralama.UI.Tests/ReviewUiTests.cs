using FilmKiralama.Business.Reviews;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>Reviews on the screens: shown as typed, written once at a time, loaded latest first, bound without a selection.</summary>
public sealed class ReviewUiTests
{
    /// <summary>A comment full of markup, an ampersand and a formula shows exactly as it was typed.</summary>
    [Fact]
    public void RentalForm_ShowsReviewsAsPlainText() => Run(() =>
    {
        const string Comment = "<b>Harika</b> &amp; & =1+1 <script>x</script>";
        var reviews = new ControlledReviewService { FilmReviews = new FilmReviews(4.5m, 2, [new PublicReview("O***", 4, Comment, new DateOnly(2026, 3, 1))]) };
        using var form = new KiralamaForm(new ControlledFilmService(), new ControlledRentalService(), reviews, Sessions.User, 1) { Prompts = new RecordingPrompts() };

        Assert.True(form.LoadFilmAsync().IsCompletedSuccessfully);

        var rating = Find<Label>(form, "lblPuan");
        var text = Find<TextBox>(form, "txtYorumlar");
        Assert.Equal("Puan: 4,5 / 5 (2 değerlendirme)", rating.Text);
        Assert.False(rating.UseMnemonic);
        Assert.True(text.ReadOnly);
        Assert.Contains(Comment, text.Text, StringComparison.Ordinal);
        Assert.StartsWith("O***  ★★★★☆  01.03.2026", text.Text, StringComparison.Ordinal);
    });

    [Fact]
    public void RentalForm_FilmWithoutReviews_SaysSo() => Run(() =>
    {
        using var form = new KiralamaForm(new ControlledFilmService(), new ControlledRentalService(), new ControlledReviewService(), Sessions.User, 1) { Prompts = new RecordingPrompts() };

        Assert.True(form.LoadFilmAsync().IsCompletedSuccessfully);

        Assert.Equal("Puan: Henüz değerlendirme yok", Find<Label>(form, "lblPuan").Text);
        Assert.Equal("Bu film için henüz yorum yapılmadı.", Find<TextBox>(form, "txtYorumlar").Text);
    });

    [Fact]
    public void ReviewForm_SecondSaveWhilePending_IsIgnored() => Run(() =>
    {
        var reviews = new ControlledReviewService();
        using var form = new DegerlendirmeForm(reviews, Sessions.User, reviews.Reviewable[0]) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ComboBox>(form, "cmbPuan").SelectedIndex = 0;
        Find<TextBox>(form, "txtYorum").Text = "Güzel";

        var first = form.SaveAsync();
        var second = form.SaveAsync();

        Assert.False(second.Result);
        Assert.Equal([new ReviewInput(5, "Güzel")], reviews.Saved);
        Assert.False(Find<Button>(form, "btnKaydet").Enabled);
        Assert.False(Find<Button>(form, "btnSil").Enabled);

        reviews.CompleteSave();
        Assert.True(first.Result);
        Assert.Equal(DialogResult.OK, form.DialogResult);
    });

    [Fact]
    public void ReviewForm_WithoutARating_AsksForOneAndSavesNothing() => Run(() =>
    {
        var reviews = new ControlledReviewService();
        var prompts = new RecordingPrompts();
        using var form = new DegerlendirmeForm(reviews, Sessions.User, reviews.Reviewable[0]) { Prompts = prompts };

        Assert.False(form.SaveAsync().Result);

        Assert.Empty(reviews.Saved);
        Assert.Equal(["Lütfen 1 ile 5 arasında bir puan seçin."], prompts.Messages);
        Assert.True(Find<Button>(form, "btnKaydet").Enabled);
    });

    /// <summary>An existing review fills the form; one an admin hid says so, and it can be deleted.</summary>
    [Fact]
    public void ReviewForm_ExistingHiddenReview_IsShownWithItsNotice() => Run(() =>
    {
        var reviews = new ControlledReviewService { Own = new OwnReview(7, 2, 2, "Eski yorum & not", IsHidden: true) };
        using var form = new DegerlendirmeForm(reviews, Sessions.User, reviews.Reviewable[1]) { Prompts = new RecordingPrompts() };

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        Assert.Equal("2 - Vasat", Find<ComboBox>(form, "cmbPuan").Text);
        Assert.Equal("Eski yorum & not", Find<TextBox>(form, "txtYorum").Text);
        Assert.Equal(ReviewPolicy.MaxCommentLength, Find<TextBox>(form, "txtYorum").MaxLength);
        Assert.True(form.ShowsHiddenNotice);
        Assert.True(Find<Button>(form, "btnSil").Enabled);
        Assert.Equal("484 karakter kaldı", Find<Label>(form, "lblKalan").Text);
    });

    /// <summary>Filling the list of films to rate selects none of them, so the rate button stays off.</summary>
    [Fact]
    public void Profile_FillingTheFilmsToRate_SelectsNothing() => Run(() =>
    {
        var reviews = new ControlledReviewService();
        using var form = new KullaniciProfil(new ControlledRentalService(), reviews, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var list = Find<ListBox>(form, "lstPuanlanabilir");
        _ = list.Handle;

        Assert.True(form.LoadReviewableAsync().IsCompletedSuccessfully);

        Assert.Equal(2, list.Items.Count);
        Assert.Equal(-1, list.SelectedIndex);
        Assert.False(Find<Button>(form, "btnPuanVer").Enabled);
        Assert.Equal("Son Vapur (Dram, 2015) - Puanınız: ★★★★☆", list.Items[1].ToString());

        list.SelectedIndex = 0;
        Assert.True(Find<Button>(form, "btnPuanVer").Enabled);
    });

    [Fact]
    public void Moderation_AnswersOutOfOrder_ShowOnlyTheLatestAndSelectNothing() => Run(() =>
    {
        var reviews = new ControlledReviewService();
        using var form = new YorumYonetimForm(reviews, Sessions.Admin) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dgvYorumlar");
        _ = grid.Handle;

        var first = form.LoadAsync(1);
        var second = form.LoadAsync(2);
        reviews.ListRequests[1].Answer.SetResult(new PagedResult<ReviewDetails>([ControlledReviewService.Review(9)], 51, 2, 50));
        reviews.ListRequests[0].Answer.SetResult(new PagedResult<ReviewDetails>([ControlledReviewService.Review(1), ControlledReviewService.Review(2)], 51, 1, 50));

        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully);
        Assert.Equal([9], form.ShownReviews!.Items.Select(r => r.Id));
        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Empty(grid.SelectedRows);
        Assert.False(Find<Button>(form, "btnGizle").Enabled);
        Assert.Equal("Sayfa 2 / 2 (51 yorum)", Find<Label>(form, "lblSayfa").Text);
        Assert.True(Find<Button>(form, "btnOncekiSayfa").Enabled);
        Assert.False(Find<Button>(form, "btnSonrakiSayfa").Enabled);
    });

    [Fact]
    public void Moderation_SecondHideWhilePending_IsIgnored() => Run(() =>
    {
        var reviews = new ControlledReviewService();
        using var form = new YorumYonetimForm(reviews, Sessions.Admin) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dgvYorumlar");
        _ = grid.Handle;
        _ = form.LoadAsync(1);
        reviews.ListRequests[0].Answer.SetResult(new PagedResult<ReviewDetails>([ControlledReviewService.Review(1)], 1, 1, 50));
        grid.Rows[0].Selected = true;
        grid.CurrentCell = grid.Rows[0].Cells[0];
        Assert.True(Find<Button>(form, "btnGizle").Enabled);

        var first = form.SetHiddenAsync(true);
        var second = form.SetHiddenAsync(true);

        Assert.False(second.Result);
        Assert.Equal(1, reviews.HideCalls);
        Assert.False(Find<Button>(form, "btnGizle").Enabled);
        Assert.False(Find<Button>(form, "btnGoster").Enabled);

        reviews.CompleteHide();
        reviews.ListRequests[1].Answer.SetResult(new PagedResult<ReviewDetails>([ControlledReviewService.Review(1, hidden: true)], 1, 1, 50));
        Assert.True(first.Result);
        Assert.Equal(2, reviews.ListRequests.Count);
    });
}
