using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGui.AppServer.Models;

[JsonConverter(typeof(FileUpdateChangeJsonConverter))]
public partial class FileUpdateChange
{
}

internal sealed class FileUpdateChangeJsonConverter : JsonConverter<FileUpdateChange>
{
    public override FileUpdateChange? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new FileUpdateChange
        {
            Path = ReadLooseString(root, "path"),
            Kind = ReadLooseString(root, "kind"),
            Diff = ReadLooseString(root, "diff")
        };
    }

    public override void Write(Utf8JsonWriter writer, FileUpdateChange value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WriteString("kind", value.Kind);
        writer.WriteString("diff", value.Diff);
        writer.WriteEndObject();
    }

    private static string? ReadLooseString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => property.GetRawText()
        };
    }
}
