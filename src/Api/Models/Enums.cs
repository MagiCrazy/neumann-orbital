using System.Text.Json.Serialization;

namespace NeumannOrbital.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProbeStatus
{
    Idle, Preparing, Accelerating, Cruising, Decelerating, Orbiting, Disabled, Dead, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SensorMode
{
    Normal, Degraded, Blind, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MovementPhase
{
    Idle, Preparing, Accelerating, Cruising, Decelerating, Arrived, Failed, Destroyed, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MannyTask
{
    Repair, Mining, Returning, WaitingForSpace, Crafting, Salvage, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MannyLocationType
{
    Probe, Sector, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SectorObjectType
{
    Star, Planet, Asteroid, DustCloud, BlackHole, SolarSystem, Manny, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DangerLevel
{
    Low, Moderate, Extreme, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KnowledgeLevel
{
    Detailed, NeighborScan, DistantScan, LongRangeEstimation, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DataFreshness
{
    Live, DegradedLive, Historical, Unavailable, Unknown
}
