using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.Commands;

/// <summary>Reads structured command arguments without discarding their quotes or splitting inline objects.</summary>
internal sealed class CommandInput(string text)
{
    private int position;

    public bool End => string.IsNullOrWhiteSpace(text[this.position..]);

    public static CommandInput Arguments(CommandContext context)
    {
        var space = context.Message.IndexOf(' ');
        return new CommandInput(space < 0 ? string.Empty : context.Message[(space + 1)..]);
    }

    public string Read()
    {
        while (this.position < text.Length && char.IsWhiteSpace(text[this.position]))
        {
            this.position++;
        }

        if (this.position == text.Length)
            throw new FormatException("Expected an argument");

        var start = this.position;
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        while (this.position < text.Length)
        {
            var character = text[this.position];
            if (escaped)
            {
                escaped = false;
            }
            else if (quote != '\0')
            {
                if (character == '\\')
                    escaped = true;
                else if (character == quote)
                    quote = '\0';
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character is '{' or '[' or '(')
            {
                depth++;
            }
            else if (character is '}' or ']' or ')')
            {
                depth--;
            }
            else if (char.IsWhiteSpace(character) && depth == 0)
            {
                break;
            }

            this.position++;
        }

        if (depth != 0 || quote != '\0')
            throw new FormatException("Unclosed structured argument");

        return text[start..this.position];
    }

    public string Position() => $"{this.Read()} {this.Read()} {this.Read()}";

    internal static List<(int Start, string Value)> Words(string text)
    {
        var input = new CommandInput(text);
        var result = new List<(int Start, string Value)>();
        while (!input.End)
        {
            while (input.position < text.Length && char.IsWhiteSpace(text[input.position]))
                input.position++;

            var start = input.position;
            result.Add((start, input.Read()));
        }

        return result;
    }

    public void Expect(string literal)
    {
        if (this.Read() != literal)
            throw new FormatException($"Expected {literal}");
    }

    public void ExpectEnd()
    {
        if (!this.End)
            throw new FormatException("Trailing command arguments");
    }

    public static string Qualify(string identifier)
    {
        if (!ResourceLocation.TryParse(identifier, out var location))
            throw new FormatException($"Invalid identifier {identifier}");

        return location.Value.ToString();
    }

    public static int Ticks(string text, int minimum)
    {
        var unit = text[^1];
        var factor = unit switch
        {
            'd' => 24000,
            's' => 20,
            't' => 1,
            _ => 0
        };
        var number = factor == 0 ? text : text[..^1];
        var value = Float(number);
        var scaled = value * Math.Max(1, factor);
        // Round the float product as Java does, without losing an integer to single-precision addition.
        var rounded = Math.Floor((double)scaled + 0.5);
        var ticks = (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
        if (ticks < minimum)
            throw new FormatException("Time is outside the permitted range");

        return ticks;
    }

    internal static float Float(string text)
    {
        if (text.Length == 0 || text.Any(character => !char.IsAsciiDigit(character) && character is not ('.' or '-'))
            || !float.TryParse(text, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
        {
            throw new FormatException("Invalid floating-point argument");
        }

        return value;
    }

    /// <summary>The data arguments use SNBT, while the shared data codecs consume JSON.</summary>
    public static JsonElement Data(string text)
    {
        var reader = new DataReader(text);
        var value = reader.ReadValue();
        reader.SkipSpace();
        if (!reader.End)
            throw new FormatException("Trailing data");

        return JsonSerializer.SerializeToElement(value);
    }

    internal static JsonElement QualifyTypes(JsonElement data)
    {
        var value = JsonNode.Parse(data.GetRawText());
        Visit(value);
        return JsonSerializer.SerializeToElement(value);

        static void Visit(JsonNode? node)
        {
            if (node is JsonObject map)
            {
                foreach (var entry in map.ToArray())
                {
                    if (entry.Key == "type" && entry.Value is JsonValue scalar
                        && scalar.TryGetValue<string>(out var name))
                    {
                        map[entry.Key] = Qualify(name);
                    }
                    else
                    {
                        Visit(entry.Value);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var entry in array)
                    Visit(entry);
            }
        }
    }

    private sealed class DataReader(string input)
    {
        private int index;

        public bool End => this.index >= input.Length;

        public void SkipSpace()
        {
            while (!this.End && char.IsWhiteSpace(input[this.index]))
            {
                this.index++;
            }
        }

        public JsonNode? ReadValue()
        {
            this.SkipSpace();
            if (this.End)
                throw new FormatException("Expected data");

            if (input[this.index] == '{')
            {
                this.index++;
                var result = new JsonObject();
                this.SkipSpace();
                while (!this.End && input[this.index] != '}')
                {
                    var key = this.ReadText(key: true);
                    this.Require(':');
                    result.Add(key, this.ReadValue());
                    if (!this.Separator('}'))
                        break;
                }

                this.Require('}');
                return result;
            }

            if (input[this.index] == '[')
            {
                this.index++;
                var result = new JsonArray();
                this.SkipSpace();
                while (!this.End && input[this.index] != ']')
                {
                    result.Add(this.ReadValue());
                    if (!this.Separator(']'))
                        break;
                }

                this.Require(']');
                return result;
            }

            var quoted = input[this.index] is '\'' or '"';
            var token = this.ReadText(key: false);
            if (!quoted && bool.TryParse(token, out var boolean))
                return JsonValue.Create(boolean);

            if (!quoted && double.TryParse(token, CultureInfo.InvariantCulture, out var number))
                return JsonValue.Create(number);

            if (!quoted && token.Length > 1 && "bBsSlLfFdD".Contains(token[^1])
                && double.TryParse(token[..^1], CultureInfo.InvariantCulture, out number))
            {
                return JsonValue.Create(number);
            }

            return JsonValue.Create(token);
        }

        private string ReadText(bool key)
        {
            this.SkipSpace();
            if (this.End)
                throw new FormatException("Expected text");

            if (input[this.index] is '\'' or '"')
            {
                var quote = input[this.index++];
                var result = new StringBuilder();
                while (!this.End && input[this.index] != quote)
                {
                    var character = input[this.index++];
                    if (character == '\\' && !this.End)
                        character = input[this.index++];

                    result.Append(character);
                }

                this.Require(quote);
                return result.ToString();
            }

            var start = this.index;
            while (!this.End && !char.IsWhiteSpace(input[this.index])
                && input[this.index] is not (',' or '}' or ']') && (!key || input[this.index] != ':'))
            {
                this.index++;
            }

            if (start == this.index)
                throw new FormatException("Expected text");

            return input[start..this.index];
        }

        private bool Separator(char close)
        {
            this.SkipSpace();
            if (!this.End && input[this.index] == close)
                return false;

            this.Require(',');
            this.SkipSpace();
            return true;
        }

        private void Require(char character)
        {
            this.SkipSpace();
            if (this.End || input[this.index++] != character)
                throw new FormatException($"Expected '{character}'");
        }
    }
}
