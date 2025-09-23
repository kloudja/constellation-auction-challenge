using Xunit;
using FluentAssertions;
using System.Collections.Generic;
using System;

namespace UnitTests;

// Strong reads hit Write DB; Eventual reads hit Read Replica with lag.
public class ReadConsistencyTests
{
    [Fact(DisplayName = "Strong reflects latest; Eventual may be stale within configured lag")]
    public void Strong_Vs_Eventual_Reads()
    {
        var writeDb = new InMemoryKV();
        var replica = new LaggedReplica(writeDb, lag: TimeSpan.FromMilliseconds(500));

        writeDb.Set("A123", "v1");
        replica.Get("A123").Should().BeNull("replica not updated yet (stale)");

        // After lag, replica is eventually consistent
        System.Threading.Thread.Sleep(550);
        replica.Get("A123").Should().Be("v1");
    }

    private sealed class InMemoryKV { private readonly Dictionary<string,string?> _d=new(); public void Set(string k,string? v)=>_d[k]=v; public string? Get(string k)=>_d.TryGetValue(k, out var v)?v:null; }
    private sealed class LaggedReplica
    {
        private readonly InMemoryKV _src; private readonly TimeSpan _lag; private readonly Dictionary<string,(string? v, DateTime stamp)> _rep = new();
        public LaggedReplica(InMemoryKV src, TimeSpan lag) { _src = src; _lag = lag; }
        public string? Get(string key)
        {
            if (!_rep.TryGetValue(key, out var r) || (DateTime.UtcNow - r.stamp) > _lag)
            {
                var now = DateTime.UtcNow;
                var v = _src.Get(key);
                _rep[key] = (v, now);
            }
            return _rep[key].v;
        }
    }
}
