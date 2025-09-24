using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Domain.Abstractions;
using Domain.Model;
using System.Collections.Concurrent;
using System.Threading;


namespace Infrastructure.InMemory
{
    public sealed class InMemoryVehicleRepository : IVehicleRepository
    {
        private readonly ConcurrentDictionary<Guid, Vehicle> _store = new();

        public Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default)
        {
            _store.TryGetValue(id, out var vehicle);
            return Task.FromResult(vehicle is { DeletedAtUtc: null } ? vehicle : null);
        }

        public Task<IReadOnlyList<Vehicle>> ListByRegionAsync(string regionId, CancellationToken ct = default)
        {
            var list = _store.Values
                .Where(v => v.DeletedAtUtc is null && v.RegionId.Equals(regionId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => v.Make).ThenBy(v => v.Model).ThenBy(v => v.Year)
                .ToList()
                .AsReadOnly();
            return Task.FromResult<IReadOnlyList<Vehicle>>(list);
        }

        public Task InsertAsync(Vehicle vehicle, CancellationToken ct = default)
        {
            _store[vehicle.Id] = vehicle;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Vehicle vehicle, CancellationToken ct = default)
        {
            _store[vehicle.Id] = vehicle;
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (_store.TryGetValue(id, out var current))
            {
                _store[id] = current.WithSoftDeleted();
            }
            return Task.CompletedTask;
        }
    }

}
