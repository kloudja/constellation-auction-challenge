using Infrastructure.InMemory;
using Services;
using Domain.Abstractions;
using Domain.Model;
using FluentAssertions;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace IntegrationTests;

public class VehicleRegionIsolationTests
{
    [Fact(DisplayName = "Vehicles created in US do not appear in EU and vice-versa")]
    public async Task Vehicles_Are_Isolated_Per_Region()
    {
        InMemoryVehicleRepository repository = new();
        VehicleService vehicleService = new(repository);

        Vehicle vUS = await vehicleService.CreateAsync(new CreateVehicleRequest("US", "Sedan", "Honda", "Accord", 2023));
        Vehicle vEU = await vehicleService.CreateAsync(new CreateVehicleRequest("EU", "SUV", "Peugeot", "3008", 2022));

        IReadOnlyList<Vehicle> listUS = await vehicleService.ListByRegionAsync("US");
        IReadOnlyList<Vehicle> listEU = await vehicleService.ListByRegionAsync("EU");

        listUS.Should().ContainSingle(x => x.Id == vUS.Id);
        listUS.Should().NotContain(x => x.Id == vEU.Id);

        listEU.Should().ContainSingle(x => x.Id == vEU.Id);
        listEU.Should().NotContain(x => x.Id == vUS.Id);
    }
}
