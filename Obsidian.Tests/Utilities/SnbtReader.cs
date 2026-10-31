using Obsidian.Nbt;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Obsidian.Tests.Utilities;

/// <summary>
/// Reads the compact SNBT vanilla's <c>Tag.toString</c> writes
/// (<c>{BB:[I;1,2,3,4,5,6],hr:1b,id:"minecraft:jigsaw"}</c>), for fixtures that record vanilla's NBT.
/// </summary>
internal sealed class SnbtReader
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly string text;

    private int position;

    private SnbtReader(string text) => this.text = text;

    public static NbtCompound ReadCompound(string text)
    {
        var reader = new SnbtReader(text);
        var tag = reader.ReadValue(string.Empty);

        if (reader.position != text.Length)
            throw reader.Error("expected the end");

        return tag as NbtCompound ?? throw new FormatException("Expected a compound: " + text);
    }

    private INbtTag ReadValue(string name)
    {
        return this.Peek() switch
        {
            '{' => this.ReadCompoundTag(name),
            '[' => this.ReadListOrArray(name),
            '"' or '\'' => new NbtTag<string>(name, this.ReadQuoted()),
            _ => this.ReadNumber(name)
        };
    }

    private NbtCompound ReadCompoundTag(string name)
    {
        var compound = new NbtCompound(name);
        this.Expect('{');

        while (this.Peek() != '}')
        {
            var key = this.Peek() is '"' or '\'' ? this.ReadQuoted() : this.ReadBare();
            this.Expect(':');
            compound.Add(this.ReadValue(key));

            if (this.Peek() == ',')
                this.position++;
        }

        this.Expect('}');
        return compound;
    }

    private INbtTag ReadListOrArray(string name)
    {
        this.Expect('[');

        if (this.position + 1 < this.text.Length && this.text[this.position + 1] == ';')
        {
            var type = this.text[this.position];
            this.position += 2;
            var values = new List<string>();

            while (this.Peek() != ']')
            {
                values.Add(this.ReadBare());
                if (this.Peek() == ',')
                    this.position++;
            }

            this.Expect(']');
            return type switch
            {
                'I' => new NbtArray<int>(name, values.ConvertAll(value => int.Parse(value, Invariant)).ToArray()),
                'L' => new NbtArray<long>(name, values.ConvertAll(value => long.Parse(value.TrimEnd('L'), Invariant))
                    .ToArray()),
                'B' => new NbtArray<byte>(name, values.ConvertAll(Byte).ToArray()),
                _ => throw this.Error("unknown array type " + type)
            };
        }

        var elements = new List<INbtTag>();
        while (this.Peek() != ']')
        {
            elements.Add(this.ReadValue(string.Empty));
            if (this.Peek() == ',')
                this.position++;
        }

        this.Expect(']');

        var list = new NbtList(elements.Count == 0 ? NbtTagType.End : elements[0].Type, name);
        foreach (var element in elements)
            list.Add(element);

        return list;
    }

    private INbtTag ReadNumber(string name)
    {
        var value = this.ReadBare();
        var number = value[..^1];

        return char.ToLowerInvariant(value[^1]) switch
        {
            'b' => new NbtTag<byte>(name, (byte)sbyte.Parse(number, Invariant)),
            's' => new NbtTag<short>(name, short.Parse(number, Invariant)),
            'l' => new NbtTag<long>(name, long.Parse(number, Invariant)),
            'f' => new NbtTag<float>(name, float.Parse(number, Invariant)),
            'd' => new NbtTag<double>(name, double.Parse(number, Invariant)),
            _ when value.Contains('.') => new NbtTag<double>(name, double.Parse(value, Invariant)),
            _ => new NbtTag<int>(name, int.Parse(value, Invariant))
        };
    }

    private static byte Byte(string value) => (byte)sbyte.Parse(value.TrimEnd('B'), Invariant);

    private string ReadBare()
    {
        var start = this.position;
        while (this.position < this.text.Length && (char.IsAsciiLetterOrDigit(this.text[this.position])
            || this.text[this.position] is '_' or '-' or '.' or '+'))
        {
            this.position++;
        }

        if (start == this.position)
            throw this.Error("expected a value");

        return this.text[start..this.position];
    }

    private string ReadQuoted()
    {
        var quote = this.text[this.position++];
        var builder = new StringBuilder();

        while (this.text[this.position] != quote)
        {
            if (this.text[this.position] == '\\')
                this.position++;

            builder.Append(this.text[this.position++]);
        }

        this.position++;
        return builder.ToString();
    }

    private char Peek() =>
        this.position < this.text.Length ? this.text[this.position] : throw this.Error("unexpected end");

    private void Expect(char expected)
    {
        if (this.Peek() != expected)
            throw this.Error($"expected '{expected}'");

        this.position++;
    }

    private FormatException Error(string message) => new($"{message} at {this.position} of {this.text}");
}
