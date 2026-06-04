using System.Text.Json.Serialization;

namespace NeumannOrbital.Api.Models;

public record EstimatedObjects(
    bool? Star,
    int? PlanetCountMin,
    int? PlanetCountMax,
    double? BlackHoleProbability,
    DangerLevel? DangerEstimate,
    string? SignalAge
);

public record SectorScan(
    long CurrentSectorResidenceSeconds,
    long RequiredResidenceSeconds,
    double ScanQuality
);

public record MinableTarget(
    string Id,
    [property: JsonPropertyName("type")] SectorObjectType ObjectType,
    string? Name,
    double? Mass,
    List<string>? Resources,
    List<string>? ResourceTypes,
    Dictionary<string, double>? ResourceComposition
);

public record BookmarkTarget(
    string Id,
    [property: JsonPropertyName("type")] SectorObjectType ObjectType,
    string? Name,
    double? Mass,
    double? Radius
);

public record SectorObject(
    string? Id,
    [property: JsonPropertyName("type")] SectorObjectType ObjectType,
    string? Name,
    bool? Estimated,
    string? Summary,
    double? Mass,
    double? Radius,
    DangerLevel? DangerLevel,
    string? MannyState,
    string? MannyUid,
    List<MinableTarget>? MinableTargets,
    int? StarCount,
    int? PlanetCount,
    int? OrbitalBodyCount,
    List<BookmarkTarget>? BookmarkTargets
);

public record SectorObservation(
    Vector RelativeCoordinates,
    long Distance,
    KnowledgeLevel KnowledgeLevel,
    double Confidence,
    List<SectorObject>? Objects,
    List<string>? PossibleObjects,
    EstimatedObjects? EstimatedObjects,
    string? NavigationalRisk,
    string? Message,
    SensorMode? SensorMode,
    DataFreshness? DataFreshness,
    SectorScan Scan
);
