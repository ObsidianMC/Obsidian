using System.Globalization;

namespace Obsidian.Registries;

/// <summary>
/// Typed handles for the block state properties the hot paths (redstone, hoppers, fluid scheduling, leaves, crops)
/// read and write on every step. A handle reads and sets its property by arithmetic on the state id, where
/// <see cref="BlockStateProperties.GetProperty"/> and <see cref="BlockStateProperties.WithProperty(IBlock, string, string)"/>
/// look the property up by name.
/// </summary>
/// <remarks>
/// A handle gives the same answers as the by-name API: a block without the property has no value and is returned
/// unchanged, as is a block given a value its property doesn't have. Plugins' blocks go through the by-name API.
/// Use a handle only where the cost shows; everything else keeps reading properties by name.
/// </remarks>
internal static class PropertyHandles
{
    public static readonly IntPropertyHandle Power = new("power");

    public static readonly BoolPropertyHandle Powered = new("powered");

    public static readonly FacePropertyHandle Facing = new("facing");

    public static readonly BoolPropertyHandle Open = new("open");

    public static readonly IntPropertyHandle Age = new("age");

    public static readonly BoolPropertyHandle Waterlogged = new("waterlogged");

    public static readonly BoolPropertyHandle Enabled = new("enabled");

    public static readonly BoolPropertyHandle Lit = new("lit");

    public static readonly IntPropertyHandle Distance = new("distance");

    public static readonly IntPropertyHandle Level = new("level");
}

/// <summary>
/// One block state property, read and set by its place in vanilla's state ids. Its values are kept as integer codes:
/// the number itself, 1 and 0 for true and false, or a <see cref="BlockFace"/>.
/// </summary>
internal abstract class PropertyHandle
{
    // The property's layout by registry id, null for blocks without it.
    private readonly Layout?[] layouts;

    private readonly Func<string, int?> parse;

    private readonly Func<int, string> format;

    /// <param name="name">The property's name, as vanilla's blocks report has it.</param>
    /// <param name="parse">A value's code, or null for a value this handle can't type.</param>
    /// <param name="format">A code's value, for the by-name API.</param>
    protected PropertyHandle(string name, Func<string, int?> parse, Func<int, string> format)
    {
        this.Name = name;
        this.parse = parse;
        this.format = format;
        this.layouts = new Layout?[BlocksRegistry.Names.Length];

        foreach (var (property, registryId, stride, values) in BlocksRegistry.TypedPropertyLayouts)
        {
            if (property != name)
                continue;

            var codes = values
                .Select(value => parse(value)
                    ?? throw new InvalidOperationException($"{this.Name} has a value '{value}' its handle can't read."))
                .ToArray();

            this.layouts[registryId] = new Layout(BlocksRegistry.NumericToBase[registryId], stride, codes);
        }
    }

    /// <summary>The property's name, as the by-name API takes it.</summary>
    public string Name { get; }

    /// <summary>The block's value of the property as a code, or <c>false</c> if it doesn't have one.</summary>
    protected bool TryGetCode(IBlock block, out int code)
    {
        var stateId = block.GetHashCode();
        if ((uint)stateId >= (uint)BlocksRegistry.StateToNumeric.Length)
            return this.TryGetPluginCode(block, out code);

        var layout = this.layouts[BlocksRegistry.StateToNumeric[stateId]];
        if (layout is null)
        {
            code = 0;
            return false;
        }

        code = layout.Codes[layout.IndexOf(stateId)];
        return true;
    }

    /// <summary>
    /// The block with the property set to a code's value, or the block itself if it doesn't have the property or
    /// the value.
    /// </summary>
    protected IBlock WithCode(IBlock block, int code)
    {
        var stateId = block.GetHashCode();
        if ((uint)stateId >= (uint)BlocksRegistry.StateToNumeric.Length)
            return block.WithProperty(this.Name, this.format(code));

        var layout = this.layouts[BlocksRegistry.StateToNumeric[stateId]];
        if (layout is null)
            return block;

        var target = layout.IndexOfCode(code);
        if (target < 0)
            return block;

        var current = layout.IndexOf(stateId);
        return BlocksRegistry.Get(stateId + (target - current) * layout.Stride);
    }

    protected InvalidOperationException Missing(IBlock block) =>
        new($"{block.UnlocalizedName} has no {this.Name} property.");

    // Plugins' blocks number their states their own way, so they're read by name.
    private bool TryGetPluginCode(IBlock block, out int code)
    {
        var parsed = block.GetProperty(this.Name) is string value ? this.parse(value) : null;
        code = parsed ?? 0;
        return parsed is not null;
    }

    private sealed class Layout(int baseId, int stride, int[] codes)
    {
        public int Stride { get; } = stride;

        public int[] Codes { get; } = codes;

        public int IndexOf(int stateId) => (stateId - baseId) / this.Stride % this.Codes.Length;

        // Integer properties count up from their first value, so the guess finds them without a search.
        public int IndexOfCode(int code)
        {
            var guess = code - this.Codes[0];
            if ((uint)guess < (uint)this.Codes.Length && this.Codes[guess] == code)
                return guess;

            return Array.IndexOf(this.Codes, code);
        }
    }
}

/// <summary>An integer property, such as <c>power</c> or <c>age</c>.</summary>
internal sealed class IntPropertyHandle(string name) : PropertyHandle(name, Parse, Format)
{
    /// <summary>The block's value, or <c>false</c> if it doesn't have the property.</summary>
    public bool TryGet(IBlock block, out int value) => this.TryGetCode(block, out value);

    /// <summary>The block's value; throws if it doesn't have the property.</summary>
    public int Get(IBlock block) => this.TryGetCode(block, out var value) ? value : throw this.Missing(block);

    /// <summary>The block with the value set, or the block itself if it doesn't have the property or the value.</summary>
    public IBlock With(IBlock block, int value) => this.WithCode(block, value);

    private static int? Parse(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string Format(int code) => code.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A boolean property, such as <c>powered</c> or <c>waterlogged</c>.</summary>
internal sealed class BoolPropertyHandle(string name) : PropertyHandle(name, Parse, Format)
{
    /// <summary>Whether the block has the property and it's true.</summary>
    public bool IsTrue(IBlock block) => this.TryGetCode(block, out var code) && code != 0;

    /// <summary>The block's value, or <c>false</c> if it doesn't have the property.</summary>
    public bool TryGet(IBlock block, out bool value)
    {
        var found = this.TryGetCode(block, out var code);
        value = code != 0;
        return found;
    }

    /// <summary>The block with the value set, or the block itself if it doesn't have the property.</summary>
    public IBlock With(IBlock block, bool value) => this.WithCode(block, value ? 1 : 0);

    private static int? Parse(string value) => value switch
    {
        "true" => 1,
        "false" => 0,
        _ => null
    };

    private static string Format(int code) => code != 0 ? "true" : "false";
}

/// <summary>A direction property, such as <c>facing</c>.</summary>
internal sealed class FacePropertyHandle(string name) : PropertyHandle(name, Parse, Format)
{
    /// <summary>The block's value, or <c>false</c> if it doesn't have the property.</summary>
    public bool TryGet(IBlock block, out BlockFace value)
    {
        var found = this.TryGetCode(block, out var code);
        value = (BlockFace)code;
        return found;
    }

    /// <summary>The block's value; throws if it doesn't have the property.</summary>
    public BlockFace Get(IBlock block) => this.TryGet(block, out var value) ? value : throw this.Missing(block);

    /// <summary>
    /// The block with the value set, or the block itself if it doesn't have the property or can't face that way.
    /// </summary>
    public IBlock With(IBlock block, BlockFace value) => this.WithCode(block, (int)value);

    private static int? Parse(string value) => value switch
    {
        "down" => (int)BlockFace.Down,
        "up" => (int)BlockFace.Up,
        "north" => (int)BlockFace.North,
        "south" => (int)BlockFace.South,
        "west" => (int)BlockFace.West,
        "east" => (int)BlockFace.East,
        _ => null
    };

    private static string Format(int code) => ((BlockFace)code).ToString().ToLowerInvariant();
}
