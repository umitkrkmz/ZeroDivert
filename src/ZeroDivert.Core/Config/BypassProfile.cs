using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroDivert.Core.Config;

/// <summary>
/// DPI Bypass profile - stores working configuration for a specific network/DPI
/// </summary>
public class BypassProfile
{
    public string ProfileName { get; set; } = "default";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    public DateTime LastTested { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Network fingerprint (ISP name, gateway MAC hash, etc.)
    /// </summary>
    public string? NetworkFingerprint { get; set; }

    /// <summary>
    /// Working strategy configuration
    /// </summary>
    public StrategyConfig Strategy { get; set; } = new();

    /// <summary>
    /// Test results history
    /// </summary>
    public List<TestResult> TestHistory { get; set; } = [];

    /// <summary>
    /// Success rate (0-100)
    /// </summary>
    public int SuccessRate { get; set; }

    /// <summary>
    /// Is this profile confirmed working?
    /// </summary>
    public bool IsVerified { get; set; }
}

public class StrategyConfig
{
    /// <summary>
    /// Which bypass technique to use
    /// </summary>
    public BypassTechnique Technique { get; set; } = BypassTechnique.TcpFragmentation;

    /// <summary>
    /// TCP fragmentation settings
    /// </summary>
    public TcpFragmentConfig TcpFragment { get; set; } = new();

    /// <summary>
    /// TCP desync settings
    /// </summary>
    public TcpDesyncConfig TcpDesync { get; set; } = new();

    /// <summary>
    /// Fake TTL settings
    /// </summary>
    public FakeTtlConfig FakeTtl { get; set; } = new();

    /// <summary>
    /// GoodbyeDPI-compatible technique settings
    /// </summary>
    public GoodbyeDpiConfig GoodbyeDpi { get; set; } = new();
}

// Every field below is read by AdaptiveEngine.CreateStrategy; settings that
// nothing applied (MaxFragments, UseFakePacket, FakeTtl.Enabled, RealTtl) were
// removed. Old profile.json files still load: unknown fields are ignored.

public class TcpFragmentConfig
{
    /// <summary>
    /// Fragment size in bytes (default: 2)
    /// </summary>
    public int FragmentSize { get; set; } = 2;

    /// <summary>
    /// Only the first N bytes of the ClientHello are fragmented; the rest goes in
    /// one segment. Values below 1 (older profiles stored 0) mean the default 50.
    /// </summary>
    public int FragmentFirstBytes { get; set; } = 50;
}

public class TcpDesyncConfig
{
    /// <summary>
    /// Desync mode: Split, Disorder, Fake
    /// </summary>
    public string Mode { get; set; } = "Split";

    /// <summary>
    /// Split position in bytes
    /// </summary>
    public int SplitPosition { get; set; } = 3;
}

public class FakeTtlConfig
{
    /// <summary>
    /// TTL of the decoy ClientHello: must outlive the home router and reach the
    /// ISP's DPI, but expire before the server. Values below 2 mean the default 5.
    /// </summary>
    public byte FakeTtl { get; set; } = 5;
}

public class GoodbyeDpiConfig
{
    /// <summary>
    /// TTL of the decoy ClientHello (GoodbyeDPI-Turkey: --set-ttl 5).
    /// </summary>
    public byte FakeTtl { get; set; } = 5;

    /// <summary>
    /// The real ClientHello is split once, after this many bytes (GoodbyeDPI -e 2).
    /// </summary>
    public int FragmentSize { get; set; } = 2;
}

public class TestResult
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public BypassTechnique Technique { get; set; }
    public bool Success { get; set; }
    public int LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BypassTechnique
{
    None,
    TcpFragmentation,
    TcpDesync,
    FakeTtl,

    /// <summary>
    /// No longer used: it never combined anything and behaved exactly like
    /// TcpFragmentation, which saved profiles using it now map to. Kept so those
    /// profiles still deserialize.
    /// </summary>
    Combined,

    /// <summary>GoodbyeDPI-Turkey's technique, Discord-only (see GoodbyeDpiStrategy).</summary>
    GoodbyeDpi
}

/// <summary>
/// Profile manager - handles loading/saving profiles
/// </summary>
public class ProfileManager
{
    private readonly string _profilePath;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private BypassProfile? _currentProfile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BypassProfile? CurrentProfile => _currentProfile;

    public ProfileManager(string? profilePath = null)
    {
        _profilePath = profilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZeroDivert",
            "profile.json"
        );
    }

    public async Task<BypassProfile> LoadOrCreateAsync()
    {
        if (File.Exists(_profilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_profilePath);
                _currentProfile = JsonSerializer.Deserialize<BypassProfile>(json, JsonOptions);

                if (_currentProfile != null)
                {
                    _currentProfile.LastUsed = DateTime.UtcNow;
                    return _currentProfile;
                }
            }
            catch
            {
                // Corrupt file, create new
            }
        }

        _currentProfile = new BypassProfile();
        return _currentProfile;
    }

    public async Task SaveAsync(BypassProfile? profile = null)
    {
        profile ??= _currentProfile;
        if (profile == null) return;

        var directory = Path.GetDirectoryName(_profilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(_profilePath, json);
    }

    /// <summary>
    /// Records one handshake result. Callers fire and forget it, so updates are
    /// serialized (two writes to profile.json at once would race) and a failed
    /// write is swallowed: losing one history entry beats an unobserved exception.
    /// </summary>
    public async Task UpdateStrategyAsync(BypassTechnique technique, bool success, int latencyMs = 0, string? error = null)
    {
        await _updateGate.WaitAsync();
        try
        {
            await UpdateStrategyCoreAsync(technique, success, latencyMs, error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private async Task UpdateStrategyCoreAsync(BypassTechnique technique, bool success, int latencyMs, string? error)
    {
        if (_currentProfile == null) return;

        _currentProfile.TestHistory.Add(new TestResult
        {
            Technique = technique,
            Success = success,
            LatencyMs = latencyMs,
            ErrorMessage = error
        });

        // Keep only last 100 results
        if (_currentProfile.TestHistory.Count > 100)
        {
            _currentProfile.TestHistory.RemoveRange(0, _currentProfile.TestHistory.Count - 100);
        }

        if (success)
        {
            _currentProfile.Strategy.Technique = technique;
            _currentProfile.IsVerified = true;
            _currentProfile.LastTested = DateTime.UtcNow;
        }

        // Success rate of the profile's own technique: failures of techniques tried
        // earlier during calibration must not drag down the one that works.
        var recentTests = _currentProfile.TestHistory
            .Where(t => t.Technique == _currentProfile.Strategy.Technique)
            .TakeLast(20)
            .ToList();
        if (recentTests.Count > 0)
        {
            _currentProfile.SuccessRate = (int)(recentTests.Count(t => t.Success) * 100.0 / recentTests.Count);
        }

        await SaveAsync();
    }

    public string GetProfilePath() => _profilePath;
}
