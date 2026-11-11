using Obsidian.API.World.Features;
using Obsidian.Entities;
using Obsidian.Nbt;
using System.IO;

namespace Obsidian.WorldData.Structures;

public sealed partial class StructureTemplate
{
    /// <summary>The author, kept in memory by vanilla but omitted from its template file.</summary>
    internal string Author { get; init; } = "?";

    internal StructureTemplate WithAuthor(string author) =>
        new(this.Size, this.palettes, this.entities) { Author = author };

    /// <summary>Copies a loaded area, including air and saved block entities, excluding structure voids.</summary>
    internal static StructureTemplate Capture(AbstractLevel level, Vector origin, Vector size, bool entities,
        string author)
    {
        var states = new List<IBlock>();
        var indices = new Dictionary<int, int>();
        var blocks = new List<TemplateBlock>();
        for (var y = 0; y < size.Y; y++)
        {
            for (var x = 0; x < size.X; x++)
            {
                for (var z = 0; z < size.Z; z++)
                {
                    var relative = new Vector(x, y, z);
                    var position = origin + relative;
                    var state = level.BlockUpdates.GetBlock(position);
                    if (state.Is(BlockIds.StructureVoid))
                        continue;

                    if (!indices.TryGetValue(state.StateId(), out var index))
                    {
                        index = states.Count;
                        indices.Add(state.StateId(), index);
                        states.Add(state);
                    }

                    NbtCompound? data = null;
                    if (level.GetResidentBlockEntity(position) is API.BlockEntities.BlockEntity entity)
                    {
                        data = BlockEntityNbt.Save(entity);
                        data?.Remove("x");
                        data?.Remove("y");
                        data?.Remove("z");
                        data?.Remove("keepPacked");
                    }

                    blocks.Add(new TemplateBlock(relative, index, data));
                }
            }
        }

        var captured = new List<StructureEntityInfo>();
        if (entities)
        {
            foreach (var entity in level.GetNonPlayerEntitiesInRange((VectorD)origin, float.MaxValue).OfType<Entity>())
            {
                if (entity is IPlayer || !Inside(entity.CurrentBox, origin, size))
                    continue;

                if (EntityNbt.Save(entity) is not NbtCompound data)
                    continue;

                var relative = entity.Position - (VectorD)origin;
                var position = new EntityPosition(relative.X, relative.Y, relative.Z);
                var blockPosition = (Vector)relative.Floor();
                if (entity is Obsidian.Entities.Painting painting)
                    blockPosition = painting.BlockPosition - origin;

                captured.Add(new StructureEntityInfo(position, blockPosition, DataBlockEntity.Copy(data)));
            }
        }

        return new StructureTemplate(size, [BuildPalette(states, blocks)], captured) { Author = author };
    }

    private static bool Inside(BoundingBox box, Vector origin, Vector size) =>
        box.Max.X > origin.X && box.Min.X < origin.X + size.X
        && box.Max.Y > origin.Y && box.Min.Y < origin.Y + size.Y
        && box.Max.Z > origin.Z && box.Min.Z < origin.Z + size.Z;

    /// <summary>Writes vanilla's generated template format to a caller-supplied worker stream.</summary>
    internal void Save(Stream stream)
    {
        var tag = this.ToNbt();
        using var writer = new NbtWriterStream(stream, NbtCompression.GZip, "");
        foreach (var (_, field) in tag)
            writer.WriteTag(field);

        writer.EndCompound();
    }

    internal NbtCompound ToNbt()
    {
        var blocks = new NbtList(NbtTagType.Compound, "blocks");
        var states = new List<IBlock>();
        var indices = new Dictionary<int, int>();
        if (this.palettes.Count > 0)
        {
            foreach (var info in this.palettes[0].Blocks)
            {
                if (!indices.TryGetValue(info.Block.StateId(), out var index))
                {
                    index = states.Count;
                    indices.Add(info.Block.StateId(), index);
                    states.Add(info.Block);
                }

                var block = new NbtCompound
                {
                    IntList("pos", info.Position),
                    new NbtTag<int>("state", index)
                };
                if (info.Nbt is not null)
                    block.Add("nbt", DataBlockEntity.Copy(info.Nbt));

                blocks.Add(block);
            }
        }

        var palette = new NbtList(NbtTagType.Compound, "palette");
        foreach (var state in states)
        {
            var entry = new NbtCompound { new NbtTag<string>("id", state.UnlocalizedName) };
            var properties = new NbtCompound("properties");
            foreach (var (key, value) in state.GetProperties())
                properties.Add(new NbtTag<string>(key, value));

            if (properties.Count > 0)
                entry.Add(properties);

            palette.Add(entry);
        }

        var entities = new NbtList(NbtTagType.Compound, "entities");
        foreach (var info in this.entities)
        {
            var entity = new NbtCompound
            {
                new NbtList(NbtTagType.Double, "pos")
                {
                    new NbtTag<double>("", info.Position.X),
                    new NbtTag<double>("", info.Position.Y),
                    new NbtTag<double>("", info.Position.Z)
                },
                IntList("blockPos", info.BlockPosition)
            };
            entity.Add("nbt", DataBlockEntity.Copy(info.Nbt));
            entities.Add(entity);
        }

        return new NbtCompound
        {
            blocks,
            palette,
            entities,
            IntList("size", this.Size),
            new NbtTag<int>("DataVersion", ServerConstants.DataVersion)
        };
    }

    private static NbtList IntList(string name, Vector value) => new(NbtTagType.Int, name)
    {
        new NbtTag<int>("", value.X),
        new NbtTag<int>("", value.Y),
        new NbtTag<int>("", value.Z)
    };
}
