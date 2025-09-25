using Domain.Model;
using System.Collections.Concurrent;


namespace Infrastructure.InMemory;

public sealed class InMemoryVehicleRepository : IVehicleRepository
{
    private readonly ConcurrentDictionary<Guid, Vehicle> _vehicleStore = new();

    public Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default)
    {
        _vehicleStore.TryGetValue(id, out var vehicle);
        return Task.FromResult(vehicle is { DeletedAtUtc: null } ? vehicle : null);
    }

    public Task<IReadOnlyList<Vehicle>> ListByRegionAsync(string regionId, CancellationToken ct = default)
    {
        var list = _vehicleStore.Values
            .Where(v => v.DeletedAtUtc is null && v.RegionId.Equals(regionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Make, StringComparer.Ordinal).ThenBy(v => v.Model, StringComparer.Ordinal).ThenBy(v => v.Year)
            .ToList()
            .AsReadOnly();
        return Task.FromResult<IReadOnlyList<Vehicle>>(list);
    }

    public Task InsertAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        _vehicleStore[vehicle.Id] = vehicle;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        _vehicleStore[vehicle.Id] = vehicle;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (_vehicleStore.TryGetValue(id, out var current))
        {
            _vehicleStore[id] = current.WithSoftDeleted();
        }
        return Task.CompletedTask;
    }
}
