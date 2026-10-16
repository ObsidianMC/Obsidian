using System.Globalization;
using System.Text;

namespace Obsidian.Porting;

/// <summary>
/// Reads the part of TOML that NeoForge's <c>neoforge.mods.toml</c> files use: tables, arrays of tables, dotted and
/// quoted keys, basic, literal and multi-line strings, booleans, numbers, arrays and inline tables.
/// </summary>
/// <remarks>
/// Tables are <see cref="Dictionary{TKey, TValue}"/>s of <see cref="string"/> to values; arrays, arrays of tables
/// included, are <see cref="List{T}"/>s of values. Strings are <see cref="string"/>s, booleans <see cref="bool"/>s,
/// integers <see cref="long"/>s and floats <see cref="double"/>s; dates are kept as their text.
/// </remarks>
public static class Toml
{
    /// <summary>
    /// Reads a TOML document.
    /// </summary>
    /// <param name="text">The document.</param>
    /// <returns>Its root table.</returns>
    /// <exception cref="FormatException">The document isn't TOML this reader understands.</exception>
    public static Dictionary<string, object> Parse(string text) => new Parser(text).ParseDocument();

    private sealed class Parser(string text)
    {
        private int position;

        private bool AtEnd => this.position >= text.Length;

        private char Current => text[this.position];

        public Dictionary<string, object> ParseDocument()
        {
            var root = NewTable();
            var current = root;

            while (true)
            {
                this.SkipTrivia(newlines: true);

                if (this.AtEnd)
                    return root;

                if (this.Current == '[')
                    current = this.ParseHeader(root);
                else
                    this.ParseKeyValue(current);

                this.ExpectLineEnd();
            }
        }

        // [table] or [[array.of.tables]]: the table that the following keys go in.
        private Dictionary<string, object> ParseHeader(Dictionary<string, object> root)
        {
            var array = this.Matches("[[");
            this.position += array ? 2 : 1;

            var keys = this.ParseKey();
            this.SkipTrivia(newlines: false);

            if (!this.Matches(array ? "]]" : "]"))
                throw this.Error("expected the end of a table header");

            this.position += array ? 2 : 1;

            var parent = root;
            foreach (var key in keys[..^1])
                parent = this.Table(parent, key);

            var last = keys[^1];
            if (!array)
                return this.Table(parent, last);

            if (!parent.TryGetValue(last, out var existing))
            {
                existing = new List<object>();
                parent[last] = existing;
            }

            if (existing is not List<object> tables)
                throw this.Error($"{last} isn't an array of tables");

            var table = NewTable();
            tables.Add(table);

            return table;
        }

        // The table at a key, created if missing; for an array of tables, its last table, as TOML says.
        private Dictionary<string, object> Table(Dictionary<string, object> parent, string key)
        {
            if (!parent.TryGetValue(key, out var value))
            {
                var table = NewTable();
                parent[key] = table;

                return table;
            }

            return value switch
            {
                Dictionary<string, object> table => table,
                List<object> { Count: > 0 } list when list[^1] is Dictionary<string, object> last => last,
                _ => throw this.Error($"{key} isn't a table")
            };
        }

        private void ParseKeyValue(Dictionary<string, object> table)
        {
            var keys = this.ParseKey();
            this.SkipTrivia(newlines: false);

            if (this.AtEnd || this.Current != '=')
                throw this.Error("expected '='");

            this.position++;
            this.SkipTrivia(newlines: false);

            var parent = table;
            foreach (var key in keys[..^1])
                parent = this.Table(parent, key);

            if (!parent.TryAdd(keys[^1], this.ParseValue()))
                throw this.Error($"{keys[^1]} is defined twice");
        }

        // A dotted key, of bare and quoted parts.
        private List<string> ParseKey()
        {
            var keys = new List<string>();

            while (true)
            {
                this.SkipTrivia(newlines: false);

                if (this.AtEnd)
                    throw this.Error("expected a key");

                if (this.Current is '"' or '\'')
                {
                    keys.Add(this.ParseString());
                }
                else
                {
                    var start = this.position;
                    while (!this.AtEnd && (char.IsAsciiLetterOrDigit(this.Current) || this.Current is '_' or '-'))
                        this.position++;

                    if (start == this.position)
                        throw this.Error("expected a key");

                    keys.Add(text[start..this.position]);
                }

                this.SkipTrivia(newlines: false);

                if (this.AtEnd || this.Current != '.')
                    return keys;

                this.position++;
            }
        }

        private object ParseValue()
        {
            if (this.AtEnd)
                throw this.Error("expected a value");

            switch (this.Current)
            {
                case '"' or '\'':
                    return this.ParseString();

                case '[':
                    return this.ParseArray();

                case '{':
                    return this.ParseInlineTable();
            }

            var start = this.position;
            while (!this.AtEnd && this.Current is not (',' or ']' or '}' or '#' or '\r' or '\n'))
                this.position++;

            var token = text[start..this.position].Trim();

            if (token == "true")
                return true;

            if (token == "false")
                return false;

            var number = token.Replace("_", "");

            if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                return integer;

            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
                return real;

            if (token.Length == 0 || !char.IsAsciiDigit(token[0]))
                throw this.Error($"'{token}' isn't a value");

            // A date or time, which mods.toml files don't use; kept as written.
            return token;
        }

        private List<object> ParseArray()
        {
            this.position++;

            var values = new List<object>();

            while (true)
            {
                this.SkipTrivia(newlines: true);

                if (this.AtEnd)
                    throw this.Error("expected ']'");

                if (this.Current == ']')
                {
                    this.position++;
                    return values;
                }

                values.Add(this.ParseValue());
                this.SkipTrivia(newlines: true);

                if (!this.AtEnd && this.Current == ',')
                    this.position++;
                else if (this.AtEnd || this.Current != ']')
                    throw this.Error("expected ',' or ']'");
            }
        }

        private Dictionary<string, object> ParseInlineTable()
        {
            this.position++;

            var table = NewTable();

            while (true)
            {
                this.SkipTrivia(newlines: false);

                if (this.AtEnd)
                    throw this.Error("expected '}'");

                if (this.Current == '}')
                {
                    this.position++;
                    return table;
                }

                this.ParseKeyValue(table);
                this.SkipTrivia(newlines: false);

                if (!this.AtEnd && this.Current == ',')
                    this.position++;
                else if (this.AtEnd || this.Current != '}')
                    throw this.Error("expected ',' or '}'");
            }
        }

        private string ParseString()
        {
            var quote = this.Current;
            var multiline = this.Matches(new string(quote, 3));

            this.position += multiline ? 3 : 1;

            // A newline right after the opening quotes isn't part of a multi-line string.
            if (multiline && this.Matches("\r\n"))
                this.position += 2;
            else if (multiline && this.Matches("\n"))
                this.position++;

            var builder = new StringBuilder();
            var closing = multiline ? new string(quote, 3) : quote.ToString();

            while (true)
            {
                if (this.AtEnd)
                    throw this.Error("expected the end of a string");

                if (this.Matches(closing))
                {
                    this.position += closing.Length;

                    // Up to two more quotes right before the closing ones belong to the string.
                    for (var extra = 0; multiline && extra < 2 && !this.AtEnd && this.Current == quote; extra++)
                    {
                        builder.Append(quote);
                        this.position++;
                    }

                    return builder.ToString();
                }

                var c = this.Current;

                if (!multiline && c is '\r' or '\n')
                    throw this.Error("a string doesn't end on its line");

                if (c == '\\' && quote == '"')
                {
                    this.ParseEscape(builder, multiline);
                    continue;
                }

                builder.Append(c);
                this.position++;
            }
        }

        private void ParseEscape(StringBuilder builder, bool multiline)
        {
            this.position++;

            if (this.AtEnd)
                throw this.Error("expected an escape");

            var c = this.Current;
            this.position++;

            switch (c)
            {
                case 'b':
                    builder.Append('\b');
                    break;

                case 't':
                    builder.Append('\t');
                    break;

                case 'n':
                    builder.Append('\n');
                    break;

                case 'f':
                    builder.Append('\f');
                    break;

                case 'r':
                    builder.Append('\r');
                    break;

                case '"' or '\\':
                    builder.Append(c);
                    break;

                case 'u' or 'U':
                    var length = c == 'u' ? 4 : 8;

                    // Exactly that many hex digits, without the signs or spaces other number styles allow.
                    if (this.position + length > text.Length
                        || !uint.TryParse(
                            text.AsSpan(this.position, length),
                            NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture,
                            out var code))
                    {
                        throw this.Error("expected a unicode escape");
                    }

                    // TOML only escapes Unicode scalar values: no surrogates, nothing past U+10FFFF.
                    if (code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
                        throw this.Error($"U+{code:X} isn't a Unicode scalar value");

                    builder.Append(char.ConvertFromUtf32((int)code));
                    this.position += length;
                    break;

                // A backslash ending a line of a multi-line string joins it to the next non-blank text.
                case ' ' or '\t' or '\r' or '\n' when multiline:
                    this.position--;
                    while (!this.AtEnd && char.IsWhiteSpace(this.Current))
                        this.position++;

                    break;

                default:
                    throw this.Error($"'\\{c}' isn't an escape");
            }
        }

        // Skips spaces, tabs and comments, and line ends too when asked.
        private void SkipTrivia(bool newlines)
        {
            while (!this.AtEnd)
            {
                var c = this.Current;

                if (c is ' ' or '\t' || (newlines && c is '\r' or '\n'))
                {
                    this.position++;
                }
                else if (c == '#')
                {
                    while (!this.AtEnd && this.Current != '\n')
                        this.position++;
                }
                else
                {
                    return;
                }
            }
        }

        private void ExpectLineEnd()
        {
            this.SkipTrivia(newlines: false);

            if (!this.AtEnd && this.Current is not ('\r' or '\n'))
                throw this.Error("expected the end of the line");
        }

        private bool Matches(string value) => text.AsSpan(this.position).StartsWith(value, StringComparison.Ordinal);

        private FormatException Error(string message)
        {
            var line = 1 + text.AsSpan(0, Math.Min(this.position, text.Length)).Count('\n');

            return new FormatException($"Line {line}: {message}.");
        }

        private static Dictionary<string, object> NewTable() => new(StringComparer.Ordinal);
    }
}
