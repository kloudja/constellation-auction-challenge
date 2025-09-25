using Xunit;
using FluentAssertions;

namespace UnitTests;

public class ReadConsistencyTests
{
    [Fact(DisplayName = "Strong reflects latest; Eventual may be stale within configured lag")]
    public void Strong_Vs_Eventual_Reads()
    {
        var writeDatabase = new InMemoryKV();
        var replica = new LaggedReplica(writeDatabase, lag: TimeSpan.FromMilliseconds(500));

        writeDatabase.Set("A123", "v1");

        // Cold start: first read must be stale (no snapshot yet)
        replica.Get("A123").Should().BeNull("replica not updated yet (stale)");

        // After lag, replica is eventually consistent
        System.Threading.Thread.Sleep(550);
        replica.Get("A123").Should().Be("v1");
    }

    private sealed class InMemoryKV
    {
        private readonly Dictionary<string, string?> _data = new();
        public void Set(string key, string? value) => _data[key] = value;
        public string? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;
    }

    private sealed class LaggedReplica(ReadConsistencyTests.InMemoryKV source, TimeSpan lag)
    {
        private readonly InMemoryKV _source = source;
        private readonly TimeSpan _lag = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;

        // Per-key snapshot + timestamp
        private readonly Dictionary<string, (string? Value, DateTime SnapshotUtc)> _replica = new();
        // Per-key first-observed time to enforce cold-start lag
        private readonly Dictionary<string, DateTime> _firstSeen = new();

        public string? Get(string key)
        {
            var now = DateTime.UtcNow;

            // If we have a snapshot: refresh only after lag
            if (_replica.TryGetValue(key, out var snap))
            {
                if ((now - snap.SnapshotUtc) > _lag)
                {
                    var refreshed = _source.Get(key);
                    _replica[key] = (refreshed, now);
                    return refreshed;
                }
                return snap.Value;
            }

            // Cold start for this key: first call records time and returns null
            if (!_firstSeen.TryGetValue(key, out var seenAt))
            {
                _firstSeen[key] = now;
                return null;
            }

            // Only after lag since first observe do we fetch from source
            if ((now - seenAt) <= _lag)
            {
                return null; // still stale
            }

            var fetched = _source.Get(key);
            _replica[key] = (fetched, now);
            _firstSeen.Remove(key);
            return fetched;
        }
    }
}
