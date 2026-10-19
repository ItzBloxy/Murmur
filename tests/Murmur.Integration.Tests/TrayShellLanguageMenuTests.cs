using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Murmur.App;
using Murmur.Core;

namespace Murmur.Integration.Tests;

/// <summary>
/// The Language menu is grouped by model: a header per model, its languages beneath.
/// Each test builds a real TrayShell, so a tray icon flickers briefly during the run.
/// </summary>
[Trait("Category", "UI")]
public sealed class TrayShellLanguageMenuTests
{
    [Fact]
    public void Groups_languages_under_their_model() => OnStaThread(tray =>
    {
        Assert.Equal(
            ["Whisper", "English", "Danish", "-", "Whistle", "German", "French", "Spanish", "Italian", "Dutch", "Polish"],
            tray.LanguageMenuItems.Select(i => i is ToolStripSeparator ? "-" : i.Text));
    });

    [Fact]
    public void Model_headers_cannot_be_chosen() => OnStaThread(tray =>
    {
        var headers = tray.LanguageMenuItems.Where(i => i.Text is "Whisper" or "Whistle").ToList();

        Assert.Equal(2, headers.Count);
        Assert.All(headers, h => Assert.False(h.Enabled));
    });

    [Theory]
    [InlineData("English", "en")]
    [InlineData("Danish", "da")]
    [InlineData("German", "de")]
    [InlineData("French", "fr")]
    [InlineData("Spanish", "es")]
    [InlineData("Italian", "it")]
    [InlineData("Dutch", "nl")]
    [InlineData("Polish", "pl")]
    public void Choosing_a_language_reports_its_code(string label, string code) => OnStaThread(tray =>
    {
        string? reported = null;
        tray.LanguageChanged += c => reported = c;

        tray.LanguageMenuItems.Single(i => i.Text == label).PerformClick();

        Assert.Equal(code, reported);
    });

    [Fact]
    public void Only_the_selected_language_is_checked() => OnStaThread(tray =>
    {
        tray.ApplySettings(new Settings { Language = "nl" });

        var checkedItems = tray.LanguageMenuItems.OfType<ToolStripMenuItem>().Where(i => i.Checked);
        Assert.Equal(["Dutch"], checkedItems.Select(i => i.Text));
    });

    private static void OnStaThread(Action<TrayShell> test)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tray = new TrayShell();
                test(tray);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
