using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// A woodland mansion, like vanilla's <c>WoodlandMansionStructure</c>.
/// </summary>
[StructureType("minecraft:woodland_mansion")]
public sealed class WoodlandMansionStructure : Structure
{
    private static readonly IBlock cobblestone = BlocksRegistry.Get(Material.Cobblestone);

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        var rotation = StructureRotationExtensions.Random(context.Random);
        var position = GetLowestYIn5By5BoxOffset7Blocks(context, rotation);
        if (position.Y < 60)
            return null;

        return new StructureStub(position, builder =>
        {
            foreach (var piece in WoodlandMansionLayout.Generate(position, rotation, context.Random))
                builder.AddPiece(piece);
        });
    }

    /// <summary>
    /// Vanilla <c>afterPlace</c>: cobblestone fills the space below the mansion's floor down to the ground.
    /// </summary>
    internal override void AfterPlace(StructurePieceContext context, IReadOnlyList<StructurePiece> pieces)
    {
        var level = context.Level;
        var structureBox = BlockBox.Encapsulating(pieces.Select(piece => piece.BoundingBox))!.Value;
        var floorY = structureBox.MinY;
        var box = context.Box;

        for (var x = box.MinX; x <= box.MaxX; x++)
        {
            for (var z = box.MinZ; z <= box.MaxZ; z++)
            {
                var position = new Vector(x, floorY, z);
                if (level.GetBlock(position).IsAir || !structureBox.IsInside(position) || !pieces.Any(piece => piece.BoundingBox.IsInside(position)))
                    continue;

                for (var y = floorY - 1; y > level.MinY; y--)
                {
                    position = position with { Y = y };
                    var block = level.GetBlock(position);
                    if (!block.IsAir && !block.IsLiquid)
                        break;

                    level.SetBlock(position, cobblestone);
                }
            }
        }
    }
}

/// <summary>
/// A room, wall, roof or carpet of a woodland mansion, like vanilla's <c>WoodlandMansionPieces.WoodlandMansionPiece</c>.
/// Its template name is the name within <c>woodland_mansion/</c>, e.g. <c>entrance</c>.
/// </summary>
public sealed class WoodlandMansionPiece : TemplateStructurePiece
{
    internal WoodlandMansionPiece(string name, Vector position, StructureRotation rotation, StructureMirror mirror = StructureMirror.None)
        : base(0, "minecraft:woodland_mansion/" + name, name, MakeSettings(mirror, rotation), position)
    {
    }

    protected override void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box)
    {
        var level = context.Level;
        if (metadata.StartsWith("Chest", StringComparison.Ordinal))
        {
            var rotation = this.PlaceSettings.Rotation;
            var chest = BlocksRegistry.Get(Material.Chest);
            BlockFace? facing = metadata switch
            {
                "ChestWest" => BlockFace.West,
                "ChestEast" => BlockFace.East,
                "ChestSouth" => BlockFace.South,
                "ChestNorth" => BlockFace.North,
                _ => null
            };

            if (facing is not null)
                chest = chest.WithProperty("facing", FeatureHelpers.FaceName(rotation.Rotate(facing.Value)));

            this.CreateChest(level, box, context.Random, position, "minecraft:chests/woodland_mansion", chest);
            return;
        }

        string? type;
        var count = 1;
        switch (metadata)
        {
            case "Mage":
                type = "minecraft:evoker";
                break;
            case "Warrior":
                type = "minecraft:vindicator";
                break;
            case "Group of Allays":
                type = "minecraft:allay";
                // Vanilla draws the count from the region's random, whose state depends on what used it before; a random
                // seeded by the world seed and the marker stands in for it.
                count = new LegacyRandomSource(level.Seed).ForkPositional().At(position.X, position.Y, position.Z).NextInt(3) + 1;
                break;
            default:
                return;
        }

        for (var index = 0; index < count; index++)
        {
            // Vanilla finalizes the spawn (equipment and the like) when the mob is created.
            level.AddEntity(new GeneratedEntity(type, new VectorF(position.X + 0.5f, position.Y, position.Z + 0.5f))
            {
                Data = new NbtCompound { new NbtTag<bool>("PersistenceRequired", true) }
            });
            level.SetBlock(position, BlocksRegistry.Air);
        }
    }

    private static StructurePlaceSettings MakeSettings(StructureMirror mirror, StructureRotation rotation)
    {
        var settings = new StructurePlaceSettings { IgnoreEntities = true, Rotation = rotation, Mirror = mirror };
        settings.Processors.Add(BlockIgnoreProcessor.StructureBlock);
        return settings;
    }
}
