using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZseAdmin.Contracts.Protocol;

/// <summary>Wspólne ustawienia serializacji — muszą być identyczne po obu stronach.</summary>
public static class ProtocolJson
{
    public const int ProtocolVersion = 1;

    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
