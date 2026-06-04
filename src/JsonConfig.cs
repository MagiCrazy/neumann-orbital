using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeumannOrbital;

internal static class JsonConfig
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy          = JsonNamingPolicy.CamelCase,
        Converters                    = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition        = JsonIgnoreCondition.WhenWritingNull,
    };
}
