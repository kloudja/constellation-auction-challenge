using Domain;
using Domain.Model;
using FluentAssertions;
using Infrastructure.InMemory;
using Services;
using Xunit;

namespace UnitTests;

public class VehicleRegionIsolationTests
{
    [Fact(DisplayName = "Vehicles created in US do not appear in EU and vice-versa")]
    public async Task Vehicles_Are_Isolated_Per_Region()
    {
        InMemoryVehicleRepository repo = new();
        VehicleService service = new(repo);

        Vehicle vUS = await service.CreateAsync(new CreateVehicleRequest("US", "SUV", "Toyota", "RAV4", 2022));
        Vehicle vEU = await service.CreateAsync(new CreateVehicleRequest("EU", "Sedan", "Peugeot", "508", 2021));

        var listUS = await service.ListByRegionAsync("US");
        var listEU = await service.ListByRegionAsync("EU");

        listUS.Should().ContainSingle(x => x.Id == vUS.Id);
        listUS.Should().NotContain(x => x.Id == vEU.Id);

        listEU.Should().ContainSingle(x => x.Id == vEU.Id);
        listEU.Should().NotContain(x => x.Id == vUS.Id);
    }

    [Fact(DisplayName = "Soft delete hides vehicle from Get and List")]
    public async Task SoftDelete_Hides_Vehicle()
    {
        InMemoryVehicleRepository repo = new();
        VehicleService service = new(repo);

        Vehicle v = await service.CreateAsync(new CreateVehicleRequest("US", "Truck", "Ford", "F-150", 2020));
        (await service.GetAsync(v.Id)).Should().NotBeNull();

        await service.SoftDeleteAsync(v.Id);

        (await service.GetAsync(v.Id)).Should().BeNull();
        (await service.ListByRegionAsync("US")).Should().NotContain(x => x.Id == v.Id);
    }

    [Fact(DisplayName = "Invalid vehicle type is rejected")]
    public async Task Invalid_Type_Is_Rejected()
    {
        InMemoryVehicleRepository repo = new();
        VehicleService service = new(repo);

        var act = () => service.CreateAsync(new CreateVehicleRequest("US", "Boat", "Yamaha", "X", 2025));
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Sedan*SUV*Hatchback*Truck*");
    }
}


