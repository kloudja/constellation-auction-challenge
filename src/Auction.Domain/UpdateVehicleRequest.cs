namespace Domain;
public sealed record UpdateVehicleRequest(Guid VehicleId, string Make, string Model, int Year);
