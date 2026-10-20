using Obsidian.API.Inventory;
using Obsidian.API.Registry.Codecs.PaintingVariant;
using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A painting, like vanilla's <c>Painting</c>: placed on a wall, it shows one of the largest placeable variants that
/// fit there, picked at random.
/// </summary>
[MinecraftEntity("minecraft:painting")]
public sealed partial class Painting : HangingEntity
{
    // The variants outside vanilla's #minecraft:placeable tag, which placing a painting never picks.
    private static readonly HashSet<string> unplaceable = ["minecraft:earth", "minecraft:fire", "minecraft:water", "minecraft:wind"];

    public Painting() => this.Type = EntityType.Painting;

    public PaintingVariantCodec Variant { get; set; } = CodecRegistry.PaintingVariant.Kebab;

    private int Width => Math.Max(1, this.Variant.Element.Width);

    private int Height => Math.Max(1, this.Variant.Element.Height);

    protected override Item DropItem => ItemsRegistry.Painting;

    protected override string BreakSound => "minecraft:entity.painting.break";

    protected override string PlaceSound => "minecraft:entity.painting.place";

    /// <summary>
    /// Like vanilla's, the painting's box covers its whole picture, a sixteenth of a block deep.
    /// </summary>
    public override BoundingBox BoundingBox
    {
        get
        {
            var center = this.Position;
            var step = (VectorD)this.Facing.ToVector();
            var side = (VectorD)CounterClockwise(this.Facing).ToVector();

            var half = new VectorD(
                Math.Abs(step.X) * 0.03125 + Math.Abs(side.X) * this.Width / 2.0,
                this.Height / 2.0,
                Math.Abs(step.Z) * 0.03125 + Math.Abs(side.Z) * this.Width / 2.0);

            // Shrunk a little, as vanilla's is, so that paintings side by side don't touch.
            half -= new VectorD(0.0625 * Math.Abs(side.X), 0.0625, 0.0625 * Math.Abs(side.Z));
            return new BoundingBox(center - half, center + half);
        }
    }

    /// <summary>
    /// Vanilla's <c>Painting.create</c>: a painting hung in <paramref name="block"/>, facing
    /// <paramref name="facing"/>, with one of the largest placeable variants that survive there, or none when nothing
    /// fits. Paintings only hang on walls.
    /// </summary>
    internal static async ValueTask<Painting?> CreateAsync(AbstractLevel level, Vector block, BlockFace facing)
    {
        if (facing is BlockFace.Up or BlockFace.Down)
            return null;

        var painting = new Painting { Level = level };
        var fitting = new List<PaintingVariantCodec>();

        foreach (var variant in CodecRegistry.PaintingVariant.All.Values)
        {
            if (unplaceable.Contains(variant.Name))
                continue;

            painting.Variant = variant;
            painting.Hang(block, facing);

            if (await painting.SurvivesAsync(level))
                fitting.Add(variant);
        }

        if (fitting.Count == 0)
            return null;

        var largest = fitting.Max(variant => variant.Element.Width * variant.Element.Height);
        var choices = fitting.Where(variant => variant.Element.Width * variant.Element.Height == largest).ToArray();

        painting.Variant = choices[Random.Shared.Next(choices.Length)];
        painting.Hang(block, facing);
        return painting;
    }

    /// <summary>
    /// Like vanilla's, a painting of even width or height is centered half a block along that axis from its block.
    /// </summary>
    protected override VectorD CalculatePosition()
    {
        var side = (VectorD)CounterClockwise(this.Facing).ToVector();
        var along = this.Width % 2 == 0 ? 0.5 : 0;
        var up = this.Height % 2 == 0 ? 0.5 : 0;

        return base.CalculatePosition() + side * along + new VectorD(0, up, 0);
    }

    // The blocks the picture covers, around its block as vanilla centers it.
    protected override IEnumerable<Vector> CoveredBlocks()
    {
        var side = CounterClockwise(this.Facing).ToVector();

        for (var x = -(this.Width - 1) / 2; x <= this.Width / 2; x++)
        for (var y = -(this.Height - 1) / 2; y <= this.Height / 2; y++)
            yield return this.BlockPosition + side * x + new Vector(0, y, 0);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        // A reference to a registered variant is its id plus one; zero would be one written out in full.
        writer.WriteEntityMetadataType(9, EntityMetadataType.PaintingVariant);
        writer.WriteVarInt(this.Variant.Id + 1);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<string>("variant", this.Variant.Name));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        // The variant comes first, since where the painting hangs depends on its size.
        if (tag.TryGetTag<NbtTag<string>>("variant", out var variant)
            && CodecRegistry.PaintingVariant.All.TryGetValue(variant.Value!, out var codec))
            this.Variant = codec;

        base.ReadNbt(tag);
    }
}
