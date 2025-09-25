namespace Domain.Events;

public sealed record VehicleSnapshot(string VehicleType, string Make, string Model, int Year);
