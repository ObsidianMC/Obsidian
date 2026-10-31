using Obsidian.API.Blocks;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

internal sealed class NoteBlockBehavior : IBlockBehavior, IShapeUpdateBehavior
{
    internal sealed record Instrument(string Sound, bool Above, bool Tunable, bool Custom);

    private sealed record Metadata(
        FrozenDictionary<string, string> Blocks,
        FrozenDictionary<string, Instrument> Instruments);

    private static readonly Lazy<Metadata> metadata = new(Load);

    internal static Instrument GetInstrument(IBlock state) =>
        metadata.Value.Instruments.GetValueOrDefault(state.GetProperty("instrument") ?? "harp")
        ?? metadata.Value.Instruments["harp"];

    public IBlock? GetStateForPlacement(BlockPlaceContext context) =>
        Derive(context.Level.GetBlock, context.Position, context.Block);

    public IBlock? UpdateShape(
        IBlockLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor) =>
        direction is BlockFace.Up or BlockFace.Down ? Derive(level.GetBlock, position, state) : state;

    private static IBlock Derive(Func<Vector, IBlock> read, Vector position, IBlock state)
    {
        // Vanilla prioritizes an instrument above the note block, then the ordinary instrument below it.
        var above = InstrumentOf(read(position + Vector.Up));
        if (metadata.Value.Instruments[above].Above)
            return state.WithProperty("instrument", above);

        var below = InstrumentOf(read(position + Vector.Down));
        if (metadata.Value.Instruments[below].Above)
        {
            below = "harp";
        }

        return state.WithProperty("instrument", below);
    }

    private static string InstrumentOf(IBlock block) =>
        metadata.Value.Blocks.GetValueOrDefault(block.UnlocalizedName, "harp");

    private static Metadata Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Assets.note_block_instruments.json")
            ?? throw new InvalidOperationException("Missing note-block instrument data.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var blocks = root.GetProperty("blocks").EnumerateObject()
            .ToFrozenDictionary(property => property.Name, property => property.Value.GetString()!);
        var instruments = root.GetProperty("instruments").EnumerateObject().ToFrozenDictionary(
            property => property.Name,
            property => new Instrument(
                property.Value.GetProperty("sound").GetString()!,
                property.Value.GetProperty("above").GetBoolean(),
                property.Value.GetProperty("tunable").GetBoolean(),
                property.Value.GetProperty("custom").GetBoolean()));
        return new Metadata(blocks, instruments);
    }
}
