namespace Domain.Model;

public sealed class Vehicle
{
    public Guid Id { get; init; }
    public string RegionId { get; init; } = "US";              // "US", "EU"
    public string VehicleType { get; init; } = "Sedan";        // Sedan|SUV|Hatchback|Truck
    public string Make { get; init; } = "";
    public string Model { get; init; } = "";
    public int Year { get; init; } = DateTime.UtcNow.Year;

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? DeletedAtUtc { get; private set; }

    public Vehicle WithUpdated(string make, string model, int year)
    {
        return new Vehicle
        {
            Id = this.Id,
            RegionId = this.RegionId,
            VehicleType = this.VehicleType,
            Make = make,
            Model = model,
            Year = year,
            CreatedAtUtc = this.CreatedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow,
            DeletedAtUtc = this.DeletedAtUtc
        };
    }

    public Vehicle WithSoftDeleted()
    {
        return new Vehicle
        {
            Id = this.Id,
            RegionId = this.RegionId,
            VehicleType = this.VehicleType,
            Make = this.Make,
            Model = this.Model,
            Year = this.Year,
            CreatedAtUtc = this.CreatedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow,
            DeletedAtUtc = DateTime.UtcNow
        };
    }
}
