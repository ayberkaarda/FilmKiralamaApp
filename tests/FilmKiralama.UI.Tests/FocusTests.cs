using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>
/// The focus is moved only after the guarded controls are enabled again; a disabled control cannot take
/// it. Forms get window handles but are never shown.
/// </summary>
public sealed class FocusTests
{
    [Fact]
    public void FocusAfterUnlock_SelectsTheTargetOnlyOnceItIsEnabledAgain() => Run(() =>
    {
        using var form = new Form();
        using var button = new Button();
        using var field = new TextBox();
        form.Controls.AddRange([button, field]);
        CreateHandles(form);
        var operation = new ExclusiveOperation();
        var pending = new TaskCompletionSource();

        var run = operation.RunAsync(
            async () =>
            {
                await pending.Task;
                Assert.False(field.Enabled);
                operation.FocusAfterUnlock(field);
                Assert.NotSame(field, form.ActiveControl);
            },
            button,
            field);
        pending.SetResult();

        Assert.True(run.IsCompletedSuccessfully);
        Assert.True(field.Enabled);
        Assert.Same(field, form.ActiveControl);
    });

    [Fact]
    public void RefusedSignIn_ClearsAndSelectsThePasswordAfterUnlock() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new LoginForm(auth, new NoFormsFactory());
        CreateHandles(form);
        var password = Find<Control>(form, "txtSifre2");
        Find<Control>(form, "txtKullanici").Text = "ornek";
        password.Text = "Yanlis.Sifre1";

        var signIn = form.SignInAsync();
        auth.RefuseSignIn();

        Assert.True(signIn.IsCompletedSuccessfully);
        Assert.Null(signIn.Result);
        Assert.Equal("Yanlis.Sifre1", auth.LastPassword);
        Assert.Empty(password.Text);
        Assert.True(Find<Control>(form, "btnGiris2").Enabled);
        Assert.True(password.ContainsFocus || form.ActiveControl == password);

        // The refusal is shown in the window's message strip, not in a message window.
        Assert.Equal("Kullanıcı adı veya şifre hatalı.", form.StatusBar.Text);
        Assert.True(form.StatusBar.IsShown);
    });

    private static void CreateHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
        {
            CreateHandles(child);
        }
    }
}
