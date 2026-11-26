using System.Text.Json;

namespace ZseAdmin.Contracts.Protocol;

/// <summary>
/// Koperta każdej wiadomości WebSocket. Reguła: serwer odpowiada tym samym <see cref="Type"/>,
/// co żądanie, a <see cref="RequestId"/> koreluje odpowiedź z żądaniem.
/// Błędy mają zawsze <see cref="Type"/> równy <see cref="MessageTypes.Error"/>.
/// </summary>
public sealed class WsEnvelope
{
    public string Type { get; set; } = string.Empty;

    public string? RequestId { get; set; }

    public JsonElement? Payload { get; set; }

    public static WsEnvelope Create(string type, string? requestId = null) => new()
    {
        Type = type,
        RequestId = requestId
    };

    public static WsEnvelope Create<T>(string type, string? requestId, T payload) => new()
    {
        Type = type,
        RequestId = requestId,
        Payload = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options)
    };

    /// <summary>Odczytuje payload jako typ <typeparamref name="T"/>; zwraca <c>null</c>, gdy payloadu brak.</summary>
    public T? ReadPayload<T>()
    {
        if (Payload is not { } payload)
        {
            return default;
        }

        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        return payload.Deserialize<T>(ProtocolJson.Options);
    }
}
