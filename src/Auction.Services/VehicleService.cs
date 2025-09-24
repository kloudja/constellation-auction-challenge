using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Abstractions;
using Domain.Model;
using System.Threading;

namespace Services
{
    /// <summary>
    /// Vehicle CRUD service constrained by the spec:
    /// - Supported types: Sedan, SUV, Hatchback, Truck
    /// - Region-specific (no cross-region replication; this service never emits events)
    /// - Strong operations (write-side) only
    /// </summary>
    public sealed class VehicleService
    {
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    { "Sedan", "SUV", "Hatchback", "Truck" };

        private readonly IVehicleRepository _vehicleRepository;

        public VehicleService(IVehicleRepository vehicleRepository)
        {
            _vehicleRepository = vehicleRepository;
        }

        public async Task<Vehicle> CreateAsync(CreateVehicleRequest request, CancellationToken ct = default)
        {
            ValidateVehicleType(request.VehicleType);
            ValidateRegionId(request.RegionId);
            ValidateYear(request.Year);

            var newVehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                RegionId = request.RegionId.ToUpperInvariant(),
                VehicleType = Capitalize(request.VehicleType),
                Make = request.Make,
                Model = request.Model,
                Year = request.Year,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _vehicleRepository.InsertAsync(newVehicle, ct);
            return newVehicle;
        }

        public async Task<Vehicle?> GetAsync(Guid vehicleId, CancellationToken ct = default)
        {
            return await _vehicleRepository.GetAsync(vehicleId, ct);
        }

        public async Task<IReadOnlyList<Vehicle>> ListByRegionAsync(string regionId, CancellationToken ct = default)
        {
            ValidateRegionId(regionId);
            return await _vehicleRepository.ListByRegionAsync(regionId.ToUpperInvariant(), ct);
        }

        public async Task<Vehicle> UpdateAsync(UpdateVehicleRequest request, CancellationToken ct = default)
        {
            ValidateYear(request.Year);
            var existing = await _vehicleRepository.GetAsync(request.VehicleId, ct)
                           ?? throw new InvalidOperationException("Vehicle not found");

            var updated = existing.WithUpdated(request.Make, request.Model, request.Year);
            await _vehicleRepository.UpdateAsync(updated, ct);
            return updated;
        }

        public Task SoftDeleteAsync(Guid vehicleId, CancellationToken ct = default)
        {
            return _vehicleRepository.SoftDeleteAsync(vehicleId, ct);
        }

        private static void ValidateVehicleType(string vehicleType)
        {
            if (!AllowedTypes.Contains(vehicleType))
                throw new ArgumentException("VehicleType must be one of: Sedan, SUV, Hatchback, Truck");
        }

        private static void ValidateRegionId(string regionId)
        {
            if (string.IsNullOrWhiteSpace(regionId) || regionId.Length > 8)
                throw new ArgumentException("RegionId must be non-empty and up to 8 characters");
        }

        private static void ValidateYear(int year)
        {
            if (year < 1900 || year > 2100) throw new ArgumentOutOfRangeException(nameof(year));
        }

        private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpper(value[0]) + value[1..].ToLower();
    }

}
