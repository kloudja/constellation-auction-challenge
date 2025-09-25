namespace Domain;

public sealed record CreateVehicleRequest(string RegionId, string VehicleType, string Make, string Model, int Year);
