namespace ZeroDivert.Core.Bypass;

/// <summary>
/// DPI Bypass Engine - coordinates multiple bypass strategies
/// </summary>
public class DpiBypassEngine
{
    private readonly List<IDpiBypassStrategy> _strategies = [];
    private readonly object _lock = new();

    public event Action<string>? OnLog;

    public DpiBypassEngine()
    {
    }

    public void AddStrategy(IDpiBypassStrategy strategy)
    {
        lock (_lock)
        {
            _strategies.Add(strategy);
            _strategies.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
    }

    public void RemoveStrategy(string name)
    {
        lock (_lock)
        {
            _strategies.RemoveAll(s => s.Name == name);
        }
    }

    public void ClearStrategies()
    {
        lock (_lock)
        {
            _strategies.Clear();
        }
    }

    public IReadOnlyList<IDpiBypassStrategy> GetStrategies()
    {
        lock (_lock)
        {
            return [.. _strategies];
        }
    }

    public IReadOnlyList<PacketResult> ProcessPacket(ReadOnlySpan<byte> packet, PacketContext context)
    {
        IDpiBypassStrategy[] strategies;
        lock (_lock)
        {
            strategies = [.. _strategies];
        }

        foreach (var strategy in strategies)
        {
            // Check if strategy supports this protocol
            if (context.IsTcp && !strategy.SupportsTcp) continue;
            if (context.IsUdp && !strategy.SupportsUdp) continue;

            var results = strategy.Process(packet, context);

            // If strategy modified the packet (returned different data), use it
            if (results.Count > 1 || (results.Count == 1 && !results[0].Data.AsSpan().SequenceEqual(packet)))
            {
                Log($"[{strategy.Name}] Processed packet: {results.Count} fragments");
                return results;
            }
        }

        // No strategy applied, return original
        return [PacketResult.FromOriginal(packet)];
    }

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }

    /// <summary>
    /// Creates a default engine with recommended strategies for Discord
    /// </summary>
    public static DpiBypassEngine CreateDefault()
    {
        var engine = new DpiBypassEngine();

        // TCP strategies
        engine.AddStrategy(new TcpFragmentationStrategy(fragmentSize: 2));
        engine.AddStrategy(new DesyncStrategy(DesyncMode.Split, splitPosition: 3));
        engine.AddStrategy(new FakeTtlStrategy(fakeTtl: 1, realTtl: 64));

        // UDP strategies
        engine.AddStrategy(new UdpFakeStrategy(fakeCount: 1));

        return engine;
    }
}
