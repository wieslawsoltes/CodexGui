using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGui.AppServer.Models;

public partial class ThreadItem
{
    private IDictionary<string, JsonElement>? _additionalPropertiesJson;

    [JsonIgnore]
    public JsonElement? ContentJson => AsNullableJsonElement(Content);

    [JsonIgnore]
    public JsonElement? SummaryJson => AsNullableJsonElement(Summary);

    [JsonIgnore]
    public JsonElement? CommandActionsJson => AsNullableJsonElement(CommandActions);

    [JsonIgnore]
    public McpToolCallError? ItemError => Error;

    [JsonIgnore]
    public IDictionary<string, JsonElement>? AdditionalPropertiesJson => _additionalPropertiesJson ??= ConvertAdditionalProperties();

    private static JsonElement? AsNullableJsonElement(JsonElement element) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : element;

    private IDictionary<string, JsonElement>? ConvertAdditionalProperties()
    {
        if (AdditionalProperties is null || AdditionalProperties.Count == 0)
        {
            return null;
        }

        var converted = new Dictionary<string, JsonElement>(AdditionalProperties.Count, StringComparer.Ordinal);
        foreach (var (key, value) in AdditionalProperties)
        {
            if (value is null)
            {
                continue;
            }

            if (value is JsonElement element)
            {
                converted[key] = element;
                continue;
            }

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            converted[key] = document.RootElement.Clone();
        }

        return converted.Count == 0 ? null : converted;
    }
}
