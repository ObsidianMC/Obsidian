namespace Obsidian.WorldData.Structures;

/// <summary>
/// The nether brick fortress of bridges and castle corridors, like vanilla's <c>NetherFortressStructure</c>.
/// </summary>
[StructureType("minecraft:fortress")]
public sealed class NetherFortressStructure : Structure
{
    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        new(new Vector(context.ChunkX << 4, 64, context.ChunkZ << 4), builder => GeneratePieces(builder, context));

    /// <summary>
    /// Vanilla <c>generatePieces</c>: grows the fortress from a bridge crossing, expanding the pending pieces in random order,
    /// then moves it to a random height.
    /// </summary>
    private static void GeneratePieces(StructurePiecesBuilder builder, StructureGenerationContext context)
    {
        var random = context.Random;
        var start = new NetherFortressPieces.StartPiece(random, (context.ChunkX << 4) + 2, (context.ChunkZ << 4) + 2);
        builder.AddPiece(start);
        start.AddChildren(start, builder, random);

        var pending = start.PendingChildren;
        while (pending.Count > 0)
        {
            var index = random.NextInt(pending.Count);
            var piece = pending[index];
            pending.RemoveAt(index);
            piece.AddChildren(start, builder, random);
        }

        builder.MoveInsideHeights(random, 48, 70);
    }
}
