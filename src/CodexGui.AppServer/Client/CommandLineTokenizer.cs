using System.Text;

namespace CodexGui.AppServer.Client;

internal static class CommandLineTokenizer
{
    public static IReadOnlyList<string> Tokenize(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        char quoteCharacter = '\0';

        foreach (var character in commandLine)
        {
            if ((character == '"' || character == '\'') && (!inQuotes || quoteCharacter == character))
            {
                if (inQuotes && quoteCharacter == character)
                {
                    inQuotes = false;
                    quoteCharacter = '\0';
                }
                else if (!inQuotes)
                {
                    inQuotes = true;
                    quoteCharacter = character;
                }

                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}