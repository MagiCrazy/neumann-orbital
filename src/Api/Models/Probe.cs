using System.Text.Json.Serialization;

namespace NeumannOrbital.Api.Models;

public record Vector(
    double X,
    double Y,
    double Z
);

public record ProbeMovement(
    MovementPhase Status,
    Vector Origin,
    Vector Target,
    long Distance,
    double FuelCostDeuterium,
    DateTimeOffset StartedAt,
    DateTimeOffset ArrivalAt,
    MovementPhase? Phase,
    long? SecondsRemaining,
    SensorMode? SensorMode,
    double? EstimatedVelocityC
);

public record ProbeFuel(
    double? Deuterium
);

public record ProbeSector(
    Vector? Relative
);

public record ProbeSystems(
    double? IntegrityPercent,
    double? DamagePercent,
    double? EnergyStored,
    double? InternalClockRate,
    string? CurrentTask
);

public record ProbeInventoryItem(
    string Id,
    [property: JsonPropertyName("type")] string ItemType,
    string Name,
    double ContainerSpace,
    string? CurrentTask,
    double TaskProgressPercent,
    MannyLocation? Location,
    MannyCargo? Cargo
);

public record ProbeResourceStock(
    string Id,
    [property: JsonPropertyName("type")] string StockType,
    string Name,
    double Amount,
    double ContainerSpace
);

public record ProbeExternalTank(
    string Id,
    [property: JsonPropertyName("type")] string TankType,
    string Name,
    double FillPercent
);

public record ProbeInventory(
    double Capacity,
    double UsedCapacity,
    double FreeCapacity,
    List<ProbeInventoryItem> Items,
    List<ProbeResourceStock> ResourceStocks,
    List<ProbeExternalTank> ExternalTanks
);

public record Probe(
    long Id,
    string Name,
    ProbeStatus Status,
    ProbeFuel Fuel,
    SensorMode SensorMode,
    ProbeSector? Sector,
    ProbeMovement? Movement,
    ProbeSystems? Systems,
    ProbeInventory Inventory
);
