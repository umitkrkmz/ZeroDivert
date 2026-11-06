using ZeroDivert.Core.Adaptive;
using ZeroDivert.Core.Config;

namespace ZeroDivert.Tests;

public sealed class AdaptiveEngineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("zerodivert-tests-").FullName;
    private string ProfilePath => Path.Combine(_dir, "profile.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ThreeFailures_MoveToTheNextTechnique()
    {
        var engine = new AdaptiveEngine(new ProfileManager(ProfilePath));
        await engine.InitializeAsync();
        Assert.True(engine.IsCalibrating);
        Assert.Equal(AdaptiveEngine.RotationOrder[0], engine.CurrentTechnique);

        engine.ReportFailure();
        engine.ReportFailure();
        Assert.Equal(AdaptiveEngine.RotationOrder[0], engine.CurrentTechnique);

        engine.ReportFailure();
        Assert.Equal(AdaptiveEngine.RotationOrder[1], engine.CurrentTechnique);
    }

    [Fact]
    public async Task PinnedTechnique_NeverRotates()
    {
        var engine = new AdaptiveEngine(new ProfileManager(ProfilePath));
        await engine.InitializeAsync(BypassTechnique.GoodbyeDpi);

        for (var i = 0; i < 10; i++) engine.ReportFailure();

        Assert.True(engine.IsPinned);
        Assert.Equal(BypassTechnique.GoodbyeDpi, engine.CurrentTechnique);
        Assert.IsType<ZeroDivert.Core.Bypass.GoodbyeDpiStrategy>(Assert.Single(engine.GetEngine().GetStrategies()));
    }

    [Fact]
    public async Task SavedCombinedProfile_LoadsAsTcpFragmentation()
    {
        var profiles = new ProfileManager(ProfilePath);
        var profile = await profiles.LoadOrCreateAsync();
        profile.Strategy.Technique = BypassTechnique.Combined;
        profile.IsVerified = true;
        profile.SuccessRate = 100;
        await profiles.SaveAsync();

        var engine = new AdaptiveEngine(new ProfileManager(ProfilePath));
        await engine.InitializeAsync();

        Assert.False(engine.IsCalibrating);
        Assert.Equal(BypassTechnique.TcpFragmentation, engine.CurrentTechnique);
    }

    [Fact]
    public async Task SuccessRate_CountsOnlyTheProfilesOwnTechnique()
    {
        var profiles = new ProfileManager(ProfilePath);
        await profiles.LoadOrCreateAsync();

        // Calibration: the first technique fails three times, the second works.
        for (var i = 0; i < 3; i++)
            await profiles.UpdateStrategyAsync(BypassTechnique.TcpFragmentation, success: false);
        for (var i = 0; i < 5; i++)
            await profiles.UpdateStrategyAsync(BypassTechnique.TcpDesync, success: true);

        var reloaded = await new ProfileManager(ProfilePath).LoadOrCreateAsync();
        Assert.Equal(BypassTechnique.TcpDesync, reloaded.Strategy.Technique);
        Assert.True(reloaded.IsVerified);
        Assert.Equal(100, reloaded.SuccessRate); // was 5/8 = 62% -> recalibration on every start
    }
}
