using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeumannOrbital.Api.Models;

public record MannyCargo(
    double Capacity,
    double Deuterium,
    double Metals,
    double Ice,
    double OrganicCompounds
);

public record MannyLocation(
    [property: JsonPropertyName("type")] MannyLocationType LocationType,
    JsonElement? Sector
);

public record Manny(
    string Id,
    string Name,
    MannyLocation Location,
    MannyTask? CurrentTask,
    double TaskProgressPercent,
    MannyCargo Cargo,
    bool CanReceiveOrders,
    DateTimeOffset? TaskEstimatedEndTime
);
