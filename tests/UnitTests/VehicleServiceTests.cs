using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Infrastructure.InMemory;
using Services;
using Domain.Abstractions;
using Domain.Model;
using FluentAssertions;
using Xunit;

namespace UnitTests
{
    public class VehicleServiceTests
    {
        [Fact(DisplayName = "Create vehicle of allowed types succeeds and is region-scoped")]
        public async Task CreateVehicle_AllowedTypes_Success()
        {
            InMemoryVehicleRepository inMemoryVehicleRepository = new();
            VehicleService vehicleService = new(inMemoryVehicleRepository);

            CreateVehicleRequest createVehicleRequest = new("US", "SUV", "Toyota", "RAV4", 2022);
            Vehicle createdVehicle = await vehicleService.CreateAsync(createVehicleRequest);

            createdVehicle.Id.Should().NotBeEmpty();
            createdVehicle.RegionId.Should().Be("US");
            createdVehicle.VehicleType.Should().Be("Suv"); // Capitalized
            createdVehicle.Make.Should().Be("Toyota");
            createdVehicle.Model.Should().Be("RAV4");
            createdVehicle.Year.Should().Be(2022);

            IReadOnlyList<Vehicle> vehiclesInUS = await vehicleService.ListByRegionAsync("US");
            vehiclesInUS.Should().ContainSingle(v => v.Id == createdVehicle.Id);

            IReadOnlyList<Vehicle> vehiclesInEU = await vehicleService.ListByRegionAsync("EU");
            vehiclesInEU.Should().BeEmpty("vehicles are region-specific and not replicated");
        }

        [Fact(DisplayName = "Create vehicle with unsupported type fails with clear message")]
        public async Task CreateVehicle_UnsupportedType_Fails()
        {
            InMemoryVehicleRepository inMemoryVehicleRepository = new();
            VehicleService vehicleService = new(inMemoryVehicleRepository);

            Func<Task> createAction = () => vehicleService.CreateAsync(new CreateVehicleRequest("US", "Boat", "Yamaha", "Waverunner", 2025));
            await createAction.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*VehicleType must be one of*");
        }

        [Fact(DisplayName = "Update vehicle modifies Make/Model/Year and preserves Region/Type")]
        public async Task UpdateVehicle_Success()
        {
            InMemoryVehicleRepository repository = new();
            VehicleService vehicleService = new(repository);

            Vehicle created = await vehicleService.CreateAsync(new CreateVehicleRequest("EU", "Hatchback", "VW", "Polo", 2019));
            UpdateVehicleRequest updateRequest = new(created.Id, "VW", "Polo GTI", 2021);

            Vehicle updated = await vehicleService.UpdateAsync(updateRequest);
            updated.Id.Should().Be(created.Id);
            updated.RegionId.Should().Be("EU");
            updated.VehicleType.Should().Be("Hatchback");
            updated.Make.Should().Be("VW");
            updated.Model.Should().Be("Polo GTI");
            updated.Year.Should().Be(2021);

            Vehicle? reloaded = await vehicleService.GetAsync(created.Id);
            reloaded!.Model.Should().Be("Polo GTI");
            reloaded.Year.Should().Be(2021);
        }

        [Fact(DisplayName = "Soft delete hides the vehicle from queries and GetAsync returns null")]
        public async Task SoftDeleteVehicle_HidesFromQueries()
        {
            InMemoryVehicleRepository repository = new();
            VehicleService vehicleService = new(repository);

            Vehicle created = await vehicleService.CreateAsync(new CreateVehicleRequest("US", "Truck", "Ford", "F-150", 2020));
            (await vehicleService.GetAsync(created.Id)).Should().NotBeNull();

            await vehicleService.SoftDeleteAsync(created.Id);

            (await vehicleService.GetAsync(created.Id)).Should().BeNull("soft-deleted vehicles are hidden");
            (await vehicleService.ListByRegionAsync("US")).Should().BeEmpty();
        }
    }
}
