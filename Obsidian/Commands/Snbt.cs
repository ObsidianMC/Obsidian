using Obsidian.Nbt;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Obsidian.Commands;

/// <summary>
/// Writes NBT as vanilla's stringified NBT, as <c>/data get</c> shows it: <c>{Health: 20.0f, Pos: [0.5d, 64.0d, 0.5d]}</c>.
/// </summary>
internal static partial class Snbt
{
    public static string Write(INbtTag tag)
    {
        var builder = new StringBuilder();
        Append(builder, tag);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, INbtTag tag)
    {
        switch (tag)
        {
            case NbtCompound compound:
                builder.Append('{');
                var first = true;
                foreach (var (name, child) in compound.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (!first)
                        builder.Append(", ");
                    first = false;

                    builder.Append(BareKey().IsMatch(name) ? name : Quote(name)).Append(": ");
                    Append(builder, child);
                }
                builder.Append('}');
                break;
            case NbtList list:
                builder.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    Append(builder, list[i]);
                }
                builder.Append(']');
                break;
            case NbtArray<byte> bytes:
                AppendArray(builder, "B", bytes.Cast<byte>().Select(x => $"{(sbyte)x}B"));
                break;
            case NbtArray<sbyte> signedBytes:
                AppendArray(builder, "B", signedBytes.Cast<sbyte>().Select(x => $"{x}B"));
                break;
            case NbtArray<int> ints:
                AppendArray(builder, "I", ints.Cast<int>().Select(x => x.ToString(CultureInfo.InvariantCulture)));
                break;
            case NbtArray<long> longs:
                AppendArray(builder, "L", longs.Cast<long>().Select(x => $"{x}L"));
                break;
            case NbtTag<byte> value:
                builder.Append((sbyte)value.Value).Append('b');
                break;
            case NbtTag<sbyte> value:
                builder.Append(value.Value).Append('b');
                break;
            case NbtTag<bool> value:
                builder.Append(value.Value ? "1b" : "0b");
                break;
            case NbtTag<short> value:
                builder.Append(value.Value).Append('s');
                break;
            case NbtTag<int> value:
                builder.Append(value.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case NbtTag<long> value:
                builder.Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append('L');
                break;
            case NbtTag<float> value:
                builder.Append(Number(value.Value)).Append('f');
                break;
            case NbtTag<double> value:
                builder.Append(Number(value.Value)).Append('d');
                break;
            case NbtTag<string> value:
                builder.Append(Quote(value.Value));
                break;
            default:
                builder.Append(Quote(tag.ToString() ?? string.Empty));
                break;
        }
    }

    private static void AppendArray(StringBuilder builder, string type, IEnumerable<string> values) =>
        builder.Append('[').Append(type).Append("; ").Append(string.Join(", ", values)).Append(']');

    // Like vanilla's, whole numbers keep a ".0".
    private static string Number(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || !double.IsFinite(value) ? text : text + ".0";
    }

    private static string Quote(string text)
    {
        var quote = text.Contains('"') && !text.Contains('\'') ? '\'' : '"';
        return quote + text.Replace("\\", "\\\\").Replace(quote.ToString(), "\\" + quote) + quote;
    }

    [GeneratedRegex("^[A-Za-z0-9._+-]+$")]
    private static partial Regex BareKey();
}
