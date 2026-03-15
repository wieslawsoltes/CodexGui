using System.Text.Json;

namespace CodexGui.App.Services;

internal static class AppJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static T? Deserialize<T>(JsonElement parameters)
    {
        try
        {
            return parameters.Deserialize<T>(SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (NotSupportedException)
        {
            return default;
        }
    }

    public static string PrettyPrint(JsonElement? element)
    {
        if (element is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } jsonElement)
        {
            return string.Empty;
        }

        try
        {
            return JsonSerializer.Serialize(jsonElement, SerializerOptions);
        }
        catch (JsonException)
        {
            return jsonElement.GetRawText();
        }
        catch (NotSupportedException)
        {
            return jsonElement.GetRawText();
        }
    }

    public static string ExtractContentText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }

            if (element.TryGetProperty("content", out var content))
            {
                return content.ToString();
            }
        }

        return element.GetRawText();
    }

    public static string? TryGetString(IDictionary<string, JsonElement>? properties, string name)
    {
        if (properties is null)
        {
            return null;
        }

        return properties.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    public static string? TryGetString(JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }
}
