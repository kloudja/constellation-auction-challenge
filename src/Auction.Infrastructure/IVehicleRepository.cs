using Domain.Model;

namespace Infrastructure;

public interface IVehicleRepository
{
    Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Vehicle>> ListByRegionAsync(string regionId, CancellationToken ct = default);
    Task InsertAsync(Vehicle vehicle, CancellationToken ct = default);
    Task UpdateAsync(Vehicle vehicle, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}
