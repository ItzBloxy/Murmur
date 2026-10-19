using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Tests;

public sealed class LanguageRoutedEngineTests
{
    private readonly FakeTranscriptionEngine _whisper = new();
    private readonly FakeTranscriptionEngine _whistle = new();
    private string _language = "da";

    private LanguageRoutedEngine CreateRouter() => new(_whisper, _whistle, () => _language);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Transcribes_a_whisper_language_with_whisper()
    {
        var router = CreateRouter();

        await router.TranscribeAsync([0.1f], "da", TranscriptionQuality.Final, Ct);

        Assert.Single(_whisper.Calls);
        Assert.Empty(_whistle.Calls);
        Assert.Equal("da", _whisper.LastLanguage);
    }

    [Fact]
    public async Task Transcribes_a_whistle_language_with_whistle()
    {
        var router = CreateRouter();

        await router.TranscribeAsync([0.1f], "de", TranscriptionQuality.Final, Ct);

        Assert.Single(_whistle.Calls);
        Assert.Empty(_whisper.Calls);
        Assert.Equal("de", _whistle.LastLanguage);
    }

    [Fact]
    public async Task Loads_the_engine_for_the_current_language()
    {
        _language = "fr";
        var router = CreateRouter();

        await router.EnsureLoadedAsync(Ct);

        Assert.Equal(1, _whistle.LoadCount);
        Assert.Equal(0, _whisper.LoadCount);
    }

    [Fact]
    public async Task IsLoaded_reports_the_current_languages_engine()
    {
        var router = CreateRouter();
        await router.EnsureLoadedAsync(Ct);
        Assert.True(router.IsLoaded);

        _language = "de";

        Assert.False(router.IsLoaded);
    }

    /// <summary>Whisper holds ~1.5 GB of VRAM. Dictating German must not leave it resident.</summary>
    [Fact]
    public async Task Switching_to_whistle_unloads_whisper()
    {
        var router = CreateRouter();
        await router.EnsureLoadedAsync(Ct);

        _language = "de";
        await router.SwitchToCurrentLanguageAsync(Ct);

        Assert.False(_whisper.IsLoaded);
        Assert.Equal(1, _whisper.UnloadCount);
    }

    [Fact]
    public async Task Transcribing_in_the_other_model_unloads_the_previous_one()
    {
        var router = CreateRouter();
        await router.TranscribeAsync([0.1f], "da", TranscriptionQuality.Final, Ct);
        await _whisper.EnsureLoadedAsync(Ct);

        await router.TranscribeAsync([0.1f], "pl", TranscriptionQuality.Final, Ct);

        Assert.False(_whisper.IsLoaded);
    }

    [Fact]
    public async Task Staying_on_the_same_model_unloads_nothing()
    {
        var router = CreateRouter();
        await router.EnsureLoadedAsync(Ct);

        _language = "en";
        await router.SwitchToCurrentLanguageAsync(Ct);

        Assert.True(_whisper.IsLoaded);
        Assert.Equal(0, _whisper.UnloadCount);
    }

    [Fact]
    public async Task Pinning_follows_a_switch_and_preloads_the_new_engine()
    {
        var router = CreateRouter();
        await router.EnsureLoadedAsync(Ct);
        router.Pin();
        Assert.True(_whisper.IsPinned);

        _language = "it";
        await router.SwitchToCurrentLanguageAsync(Ct);

        Assert.True(router.IsPinned);
        Assert.False(_whisper.IsPinned);
        Assert.True(_whistle.IsPinned);
        Assert.Equal(1, _whistle.LoadCount);
    }

    [Fact]
    public async Task An_unpinned_switch_does_not_preload()
    {
        var router = CreateRouter();

        _language = "nl";
        await router.SwitchToCurrentLanguageAsync(Ct);

        Assert.Equal(0, _whistle.LoadCount);
    }

    [Fact]
    public void Unpin_releases_the_pin_on_both_engines()
    {
        var router = CreateRouter();
        router.Pin();

        router.Unpin();

        Assert.False(router.IsPinned);
        Assert.False(_whisper.IsPinned);
        Assert.False(_whistle.IsPinned);
    }

    [Fact]
    public async Task Unload_releases_both_engines()
    {
        var router = CreateRouter();
        await _whisper.EnsureLoadedAsync(Ct);
        await _whistle.EnsureLoadedAsync(Ct);

        await router.UnloadAsync();

        Assert.False(_whisper.IsLoaded);
        Assert.False(_whistle.IsLoaded);
    }

    [Fact]
    public async Task Reports_the_model_for_the_current_language()
    {
        var router = CreateRouter();
        Assert.Equal(SpeechModel.Whisper, router.ActiveModel);

        _language = "es";

        Assert.Equal(SpeechModel.Whistle, router.ActiveModel);
    }
}
