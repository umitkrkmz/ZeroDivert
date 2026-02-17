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
    /// UDP fake settings
    /// </summary>
    public UdpFakeConfig UdpFake { get; set; } = new();
}

public class TcpFragmentConfig
{
    /// <summary>
    /// Fragment size in bytes (default: 2)
    /// Smaller = more packets but better bypass
    /// </summary>
    public int FragmentSize { get; set; } = 2;

    /// <summary>
    /// Only fragment first N bytes of payload (0 = all)
    /// DPI usually only checks first ~100 bytes
    /// </summary>
    public int FragmentFirstBytes { get; set; } = 0;

    /// <summary>
    /// Maximum fragments to create (0 = unlimited)
    /// </summary>
    public int MaxFragments { get; set; } = 0;
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

    /// <summary>
    /// Use fake packet with bad checksum
    /// </summary>
    public bool UseFakePacket { get; set; } = false;
}

public class FakeTtlConfig
{
    /// <summary>
    /// Enable fake TTL technique
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// TTL for fake packets (should expire before server)
    /// </summary>
    public byte FakeTtl { get; set; } = 1;

    /// <summary>
    /// TTL for real packets
    /// </summary>
    public byte RealTtl { get; set; } = 64;
}

public class UdpFakeConfig
{
    /// <summary>
    /// Enable UDP fake packets
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of fake packets to send
    /// </summary>
    public int FakeCount { get; set; } = 1;
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
    Combined
}

/// <summary>
/// Profile manager - handles loading/saving profiles
/// </summary>
public class ProfileManager
{
    private readonly string _profilePath;
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

    public async Task UpdateStrategyAsync(BypassTechnique technique, bool success, int latencyMs = 0, string? error = null)
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

        // Update success rate
        var recentTests = _currentProfile.TestHistory.TakeLast(20).ToList();
        if (recentTests.Count > 0)
        {
            _currentProfile.SuccessRate = (int)(recentTests.Count(t => t.Success) * 100.0 / recentTests.Count);
        }

        if (success)
        {
            _currentProfile.Strategy.Technique = technique;
            _currentProfile.IsVerified = true;
            _currentProfile.LastTested = DateTime.UtcNow;
        }

        await SaveAsync();
    }

    public string GetProfilePath() => _profilePath;
}
