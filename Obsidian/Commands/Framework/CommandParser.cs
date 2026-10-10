using System.Text;

namespace Obsidian.Commands.Framework;

public sealed class CommandParser(string prefix)
{
    public string Prefix { get; } = prefix;

    public bool IsCommandQualified(string input, out ReadOnlyMemory<char> qualifiedCommand)
    {
        if (input.StartsWith(Prefix))
        {
            qualifiedCommand = input.AsMemory(Prefix.Length);
            return true;
        }

        qualifiedCommand = null;
        return false;
    }

    public static string[] SplitQualifiedString(ReadOnlyMemory<char> qualifiedString)
    {
        var words = SplitWords(qualifiedString.Span);

        // A trailing empty word is one that hasn't been typed yet, not an argument.
        if (words[^1].Value.Length == 0)
            words.RemoveAt(words.Count - 1);

        return [.. words.Select(word => word.Value)];
    }

    /// <summary>
    /// Finds where the word containing <paramref name="index"/> ends: before the space that starts the next word, so a
    /// quoted word with spaces counts as one, or at the end of <paramref name="input"/>.
    /// </summary>
    public static int FindWordEnd(string input, int index)
    {
        foreach (var (start, _) in SplitWords(input))
        {
            if (start > index)
                return start - 1;
        }

        return input.Length;
    }

    /// <summary>
    /// Splits a command line into words at unquoted spaces, removing quotes and resolving backslash escapes.
    /// </summary>
    /// <returns>
    /// Each word with the index in <paramref name="input"/> where it starts. The last word is the one being typed, which
    /// is empty when <paramref name="input"/> is empty or ends with a separating space.
    /// </returns>
    public static List<(int Start, string Value)> SplitWords(ReadOnlySpan<char> input)
    {
        var words = new List<(int Start, string Value)>();

        var buffer = new StringBuilder();
        int start = 0;
        bool inQuote = false;
        bool escape = false;

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == ' ' && !inQuote)
            {
                // flush buffer
                words.Add((start, buffer.ToString()));
                buffer.Clear();
                start = i + 1;
            }
            else if (escape)
            {
                // escape table
                switch (input[i])
                {
                    default: // any escaped char will be pushed back into the buffer.
                        buffer.Append(input[i]);
                        break;
                    case 'n':
                        buffer.Append('\n');
                        break;
                    case 'r':
                        buffer.Append('\r');
                        break;
                    case 't':
                        buffer.Append('\t');
                        break;
                    case '0':
                        buffer.Append('\0');
                        break;
                    case 'b':
                        buffer.Append('\b');
                        break;
                }
                escape = false;
            }
            else if (input[i] == '\\')
            {
                escape = true;
            }
            else if (input[i] == '"')
            {
                // toggle quotes
                inQuote = !inQuote;
            }
            else
            {
                // else, next token
                buffer.Append(input[i]);
            }
        }

        words.Add((start, buffer.ToString()));

        return words;
    }
}
