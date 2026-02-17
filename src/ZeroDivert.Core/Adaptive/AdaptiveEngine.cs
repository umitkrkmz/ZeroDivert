using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Config;

namespace ZeroDivert.Core.Adaptive;

/// <summary>
/// Adaptive DPI bypass engine - automatically finds working strategy
/// </summary>
public class AdaptiveEngine
{
    private readonly ProfileManager _profileManager;
    private readonly DpiBypassEngine _bypassEngine;

    private BypassTechnique _currentTechnique = BypassTechnique.TcpFragmentation;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private bool _isCalibrating;

    private const int FailureThreshold = 3;
    private const int SuccessThreshold = 5;

    public event Action<string>? OnLog;
    public event Action<BypassTechnique>? OnTechniqueChanged;

    public BypassTechnique CurrentTechnique => _currentTechnique;
    public bool IsCalibrating => _isCalibrating;

    public AdaptiveEngine(ProfileManager profileManager)
    {
        _profileManager = profileManager;
        _bypassEngine = new DpiBypassEngine();
    }

    public async Task InitializeAsync()
    {
        var profile = await _profileManager.LoadOrCreateAsync();

        if (profile.IsVerified && profile.SuccessRate > 80)
        {
            // Use saved strategy
            _currentTechnique = profile.Strategy.Technique;
            ApplyStrategy(profile.Strategy);
            Log($"Loaded verified profile: {_currentTechnique} (Success rate: {profile.SuccessRate}%)");
        }
        else
        {
            // Start calibration
            _isCalibrating = true;
            _currentTechnique = BypassTechnique.TcpFragmentation;
            ApplyDefaultStrategy();
            Log("No verified profile found, starting calibration...");
        }
    }

    public DpiBypassEngine GetEngine() => _bypassEngine;

    public void ReportSuccess()
    {
        _consecutiveSuccesses++;
        _consecutiveFailures = 0;

        if (_isCalibrating && _consecutiveSuccesses >= SuccessThreshold)
        {
            _isCalibrating = false;
            Log($"Calibration complete! Working technique: {_currentTechnique}");

            // Save to profile
            Task.Run(async () =>
            {
                await _profileManager.UpdateStrategyAsync(_currentTechnique, true);
            });
        }
    }

    public void ReportFailure(string? reason = null)
    {
        _consecutiveFailures++;
        _consecutiveSuccesses = 0;

        if (_consecutiveFailures >= FailureThreshold)
        {
            // Try next technique
            TryNextTechnique();
            _consecutiveFailures = 0;
        }

        Task.Run(async () =>
        {
            await _profileManager.UpdateStrategyAsync(_currentTechnique, false, error: reason);
        });
    }

    private void TryNextTechnique()
    {
        var nextTechnique = _currentTechnique switch
        {
            BypassTechnique.TcpFragmentation => BypassTechnique.TcpDesync,
            BypassTechnique.TcpDesync => BypassTechnique.FakeTtl,
            BypassTechnique.FakeTtl => BypassTechnique.Combined,
            BypassTechnique.Combined => BypassTechnique.TcpFragmentation, // Loop back
            _ => BypassTechnique.TcpFragmentation
        };

        Log($"Switching technique: {_currentTechnique} -> {nextTechnique}");
        _currentTechnique = nextTechnique;
        ApplyTechnique(nextTechnique);
        OnTechniqueChanged?.Invoke(nextTechnique);
    }

    private void ApplyStrategy(StrategyConfig config)
    {
        _bypassEngine.ClearStrategies();

        switch (config.Technique)
        {
            case BypassTechnique.TcpFragmentation:
                _bypassEngine.AddStrategy(new TcpFragmentationStrategy(config.TcpFragment.FragmentSize));
                break;

            case BypassTechnique.TcpDesync:
                var mode = Enum.TryParse<DesyncMode>(config.TcpDesync.Mode, out var m) ? m : DesyncMode.Split;
                _bypassEngine.AddStrategy(new DesyncStrategy(mode, config.TcpDesync.SplitPosition));
                break;

            case BypassTechnique.FakeTtl:
                _bypassEngine.AddStrategy(new FakeTtlStrategy(config.FakeTtl.FakeTtl, config.FakeTtl.RealTtl));
                break;

            case BypassTechnique.Combined:
                ApplyDefaultStrategy();
                break;
        }

        if (config.UdpFake.Enabled)
        {
            _bypassEngine.AddStrategy(new UdpFakeStrategy(config.UdpFake.FakeCount));
        }
    }

    private void ApplyTechnique(BypassTechnique technique)
    {
        _bypassEngine.ClearStrategies();

        switch (technique)
        {
            case BypassTechnique.TcpFragmentation:
                // Smart fragmentation - only fragment first 50 bytes where SNI is
                _bypassEngine.AddStrategy(new SmartFragmentationStrategy(
                    fragmentSize: 2,
                    maxFragmentBytes: 50
                ));
                break;

            case BypassTechnique.TcpDesync:
                _bypassEngine.AddStrategy(new DesyncStrategy(DesyncMode.Split, 3));
                break;

            case BypassTechnique.FakeTtl:
                _bypassEngine.AddStrategy(new FakeTtlStrategy(1, 64));
                break;

            case BypassTechnique.Combined:
                ApplyDefaultStrategy();
                break;
        }

        _bypassEngine.AddStrategy(new UdpFakeStrategy(1));
    }

    private void ApplyDefaultStrategy()
    {
        _bypassEngine.ClearStrategies();
        _bypassEngine.AddStrategy(new SmartFragmentationStrategy(2, 50));
        _bypassEngine.AddStrategy(new DesyncStrategy(DesyncMode.Split, 3));
        _bypassEngine.AddStrategy(new UdpFakeStrategy(1));
    }

    private void Log(string message) => OnLog?.Invoke(message);
}
