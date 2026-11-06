using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Config;

namespace ZeroDivert.Core.Adaptive;

/// <summary>
/// Adaptive DPI bypass engine - automatically finds working strategy.
///
/// Calibration tries the techniques in <see cref="RotationOrder"/>: three failed
/// Discord handshakes in a row move on to the next one, five successes in a row
/// save the current one to the profile. A technique pinned by the user in Ayarlar
/// is used as-is: no rotation, no profile updates.
/// </summary>
public class AdaptiveEngine
{
    /// <summary>
    /// GoodbyeDpi is last for now: the order is to be revisited after comparative
    /// tests on a Türk Telekom line.
    /// </summary>
    public static readonly IReadOnlyList<BypassTechnique> RotationOrder =
    [
        BypassTechnique.TcpFragmentation,
        BypassTechnique.TcpDesync,
        BypassTechnique.FakeTtl,
        BypassTechnique.GoodbyeDpi
    ];

    private readonly ProfileManager _profileManager;
    private readonly DpiBypassEngine _bypassEngine;

    private StrategyConfig _config = new();
    private BypassTechnique _currentTechnique = RotationOrder[0];
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private bool _isCalibrating;
    private bool _isPinned;

    private const int FailureThreshold = 3;
    private const int SuccessThreshold = 5;

    public event Action<string>? OnLog;
    public event Action<BypassTechnique>? OnTechniqueChanged;

    public BypassTechnique CurrentTechnique => _currentTechnique;
    public bool IsCalibrating => _isCalibrating;
    public bool IsPinned => _isPinned;

    public AdaptiveEngine(ProfileManager profileManager)
    {
        _profileManager = profileManager;
        _bypassEngine = new DpiBypassEngine();
    }

    /// <param name="pinnedTechnique">A technique chosen by the user, or null for automatic.</param>
    public async Task InitializeAsync(BypassTechnique? pinnedTechnique = null)
    {
        var profile = await _profileManager.LoadOrCreateAsync();
        _config = profile.Strategy;

        if (pinnedTechnique is { } pinned && pinned != BypassTechnique.None)
        {
            _isPinned = true;
            Apply(Normalize(pinned));
            Log($"Technique pinned in settings: {_currentTechnique} (no automatic rotation)");
        }
        else if (profile.IsVerified && profile.SuccessRate > 80)
        {
            Apply(Normalize(profile.Strategy.Technique));
            Log($"Loaded verified profile: {_currentTechnique} (Success rate: {profile.SuccessRate}%)");
        }
        else
        {
            _isCalibrating = true;
            Apply(RotationOrder[0]);
            Log("No verified profile found, starting calibration...");
        }
    }

    public DpiBypassEngine GetEngine() => _bypassEngine;

    public void ReportSuccess()
    {
        _consecutiveSuccesses++;
        _consecutiveFailures = 0;
        if (_isPinned) return;

        // Every real success is recorded: the profile's success rate (which decides
        // whether it's trusted on the next start) needs both sides.
        SaveResult(_currentTechnique, success: true, error: null);

        if (_isCalibrating && _consecutiveSuccesses >= SuccessThreshold)
        {
            _isCalibrating = false;
            Log($"Calibration complete! Working technique: {_currentTechnique}");
        }
    }

    public void ReportFailure(string? reason = null)
    {
        _consecutiveFailures++;
        _consecutiveSuccesses = 0;
        if (_isPinned) return;

        // Recorded against the technique that failed, before any switch.
        SaveResult(_currentTechnique, success: false, error: reason);

        if (_consecutiveFailures >= FailureThreshold)
        {
            _consecutiveFailures = 0;
            var index = RotationOrder.ToList().IndexOf(_currentTechnique);
            var next = RotationOrder[(index + 1) % RotationOrder.Count];

            Log($"Switching technique: {_currentTechnique} -> {next}");
            _isCalibrating = true;
            Apply(next);
            OnTechniqueChanged?.Invoke(next);
        }
    }

    private void SaveResult(BypassTechnique technique, bool success, string? error) =>
        _ = _profileManager.UpdateStrategyAsync(technique, success, error: error);

    private void Apply(BypassTechnique technique)
    {
        _currentTechnique = technique;
        _bypassEngine.ClearStrategies();
        _bypassEngine.AddStrategy(CreateStrategy(technique, _config));
    }

    /// <summary>
    /// The one place a technique becomes a strategy, so calibration and a loaded
    /// profile always run the same thing with the profile's parameters. (They
    /// used to differ: a saved TcpFragmentation profile split the whole
    /// ClientHello into 2-byte segments - ~900 packets - while calibration only
    /// split its first 50 bytes.)
    /// </summary>
    internal static IDpiBypassStrategy CreateStrategy(BypassTechnique technique, StrategyConfig config) => technique switch
    {
        BypassTechnique.TcpDesync => new DesyncStrategy(
            Enum.TryParse<DesyncMode>(config.TcpDesync.Mode, out var mode) ? mode : DesyncMode.Split,
            config.TcpDesync.SplitPosition > 0 ? config.TcpDesync.SplitPosition : 3),
        BypassTechnique.FakeTtl => new FakeTtlStrategy(UsableTtl(config.FakeTtl.FakeTtl, FakeTtlStrategy.DefaultFakeTtl)),
        BypassTechnique.GoodbyeDpi => new GoodbyeDpiStrategy(
            UsableTtl(config.GoodbyeDpi.FakeTtl, GoodbyeDpiStrategy.DefaultFakeTtl),
            config.GoodbyeDpi.FragmentSize > 0 ? config.GoodbyeDpi.FragmentSize : GoodbyeDpiStrategy.DefaultFragmentSize),
        _ => new SmartFragmentationStrategy(
            config.TcpFragment.FragmentSize > 0 ? config.TcpFragment.FragmentSize : 2,
            // Older profiles stored 0 ("fragment everything"); that's the
            // ~900-packet behavior, so fall back to the default instead.
            config.TcpFragment.FragmentFirstBytes > 0 ? config.TcpFragment.FragmentFirstBytes : 50)
    };

    /// <summary>
    /// Older profiles stored a fake TTL of 1, which dies at the user's own router
    /// and never reaches the ISP's DPI.
    /// </summary>
    private static byte UsableTtl(byte ttl, byte fallback) => ttl >= 2 ? ttl : fallback;

    /// <summary>
    /// "Combined" never combined anything (the engine stopped at the first
    /// strategy that changed the packet, so it behaved exactly like
    /// TcpFragmentation); saved profiles using it map to that.
    /// </summary>
    private static BypassTechnique Normalize(BypassTechnique technique) =>
        RotationOrder.Contains(technique) ? technique : BypassTechnique.TcpFragmentation;

    private void Log(string message) => OnLog?.Invoke(message);
}
