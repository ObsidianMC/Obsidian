using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// An end city of towers and bridges, sometimes with a ship, like vanilla's <c>EndCityStructure</c>.
/// </summary>
[StructureType("minecraft:end_city")]
public sealed class EndCityStructure : Structure
{
    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        var rotation = StructureRotationExtensions.Random(context.Random);
        var position = GetLowestYIn5By5BoxOffset7Blocks(context, rotation);
        if (position.Y < 60)
            return null;

        return new StructureStub(position, builder =>
        {
            foreach (var piece in new EndCityBuilder(context.Random).StartHouseTower(position, rotation))
                builder.AddPiece(piece);
        });
    }

    /// <summary>
    /// Vanilla <c>EndCityPieces</c>' section generators. Vanilla keeps whether a ship was built in a static field; here it
    /// belongs to one city.
    /// </summary>
    private sealed class EndCityBuilder(IRandomSource random)
    {
        private const int MaxGenDepth = 8;

        private static readonly (StructureRotation Rotation, Vector Offset)[] towerBridges =
        [
            (StructureRotation.None, new Vector(1, -1, 0)),
            (StructureRotation.Clockwise90, new Vector(6, -1, 1)),
            (StructureRotation.CounterClockwise90, new Vector(0, -1, 5)),
            (StructureRotation.Clockwise180, new Vector(5, -1, 6))
        ];

        private static readonly (StructureRotation Rotation, Vector Offset)[] fatTowerBridges =
        [
            (StructureRotation.None, new Vector(4, -1, 0)),
            (StructureRotation.Clockwise90, new Vector(12, -1, 4)),
            (StructureRotation.CounterClockwise90, new Vector(0, -1, 8)),
            (StructureRotation.Clockwise180, new Vector(8, -1, 12))
        ];

        private bool shipCreated;

        private delegate bool SectionGenerator(int depth, EndCityPiece parent, Vector? offset, List<StructurePiece> pieces);

        public List<StructurePiece> StartHouseTower(Vector position, StructureRotation rotation)
        {
            List<StructurePiece> pieces = [];
            var piece = AddHelper(pieces, new EndCityPiece("base_floor", position, rotation, true));
            piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 0, -1), "second_floor_1", rotation, false));
            piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 4, -1), "third_floor_1", rotation, false));
            piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 8, -1), "third_roof", rotation, true));
            this.RecursiveChildren(this.Tower, 1, piece, null, pieces);
            return pieces;
        }

        private bool HouseTower(int depth, EndCityPiece parent, Vector? offset, List<StructurePiece> pieces)
        {
            if (depth > MaxGenDepth)
                return false;

            var rotation = parent.PlaceSettings.Rotation;
            var piece = AddHelper(pieces, AddPiece(parent, offset!.Value, "base_floor", rotation, true));
            switch (random.NextInt(3))
            {
                case 0:
                    AddHelper(pieces, AddPiece(piece, new Vector(-1, 4, -1), "base_roof", rotation, true));
                    break;
                case 1:
                    piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 0, -1), "second_floor_2", rotation, false));
                    piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 8, -1), "second_roof", rotation, false));
                    this.RecursiveChildren(this.Tower, depth + 1, piece, null, pieces);
                    break;
                default:
                    piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 0, -1), "second_floor_2", rotation, false));
                    piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 4, -1), "third_floor_2", rotation, false));
                    piece = AddHelper(pieces, AddPiece(piece, new Vector(-1, 8, -1), "third_roof", rotation, true));
                    this.RecursiveChildren(this.Tower, depth + 1, piece, null, pieces);
                    break;
            }

            return true;
        }

        private bool Tower(int depth, EndCityPiece parent, Vector? offset, List<StructurePiece> pieces)
        {
            var rotation = parent.PlaceSettings.Rotation;
            var piece = AddHelper(pieces, AddPiece(parent, new Vector(3 + random.NextInt(2), -3, 3 + random.NextInt(2)), "tower_base", rotation, true));
            piece = AddHelper(pieces, AddPiece(piece, new Vector(0, 7, 0), "tower_piece", rotation, true));
            var bridgeBase = random.NextInt(3) == 0 ? piece : null;
            var height = 1 + random.NextInt(3);

            for (var floor = 0; floor < height; floor++)
            {
                piece = AddHelper(pieces, AddPiece(piece, new Vector(0, 4, 0), "tower_piece", rotation, true));
                if (floor < height - 1 && random.NextBoolean())
                    bridgeBase = piece;
            }

            if (bridgeBase is not null)
            {
                foreach (var (bridgeRotation, bridgeOffset) in towerBridges)
                {
                    if (!random.NextBoolean())
                        continue;

                    var bridge = AddHelper(pieces, AddPiece(bridgeBase, bridgeOffset, "bridge_end", rotation.GetRotated(bridgeRotation), true));
                    this.RecursiveChildren(this.TowerBridge, depth + 1, bridge, null, pieces);
                }

                AddHelper(pieces, AddPiece(piece, new Vector(-1, 4, -1), "tower_top", rotation, true));
            }
            else
            {
                if (depth != 7)
                    return this.RecursiveChildren(this.FatTower, depth + 1, piece, null, pieces);

                AddHelper(pieces, AddPiece(piece, new Vector(-1, 4, -1), "tower_top", rotation, true));
            }

            return true;
        }

        private bool TowerBridge(int depth, EndCityPiece parent, Vector? offset, List<StructurePiece> pieces)
        {
            var rotation = parent.PlaceSettings.Rotation;
            var length = random.NextInt(4) + 1;
            var piece = AddHelper(pieces, AddPiece(parent, new Vector(0, 0, -4), "bridge_piece", rotation, true));
            piece.GenDepth = -1;
            var y = 0;

            for (var segment = 0; segment < length; segment++)
            {
                if (random.NextBoolean())
                {
                    piece = AddHelper(pieces, AddPiece(piece, new Vector(0, y, -4), "bridge_piece", rotation, true));
                    y = 0;
                }
                else
                {
                    piece = random.NextBoolean()
                        ? AddHelper(pieces, AddPiece(piece, new Vector(0, y, -4), "bridge_steep_stairs", rotation, true))
                        : AddHelper(pieces, AddPiece(piece, new Vector(0, y, -8), "bridge_gentle_stairs", rotation, true));
                    y = 4;
                }
            }

            if (!this.shipCreated && random.NextInt(10 - depth) == 0)
            {
                AddHelper(pieces, AddPiece(piece, new Vector(-8 + random.NextInt(8), y, -70 + random.NextInt(10)), "ship", rotation, true));
                this.shipCreated = true;
            }
            else if (!this.RecursiveChildren(this.HouseTower, depth + 1, piece, new Vector(-3, y + 1, -11), pieces))
            {
                return false;
            }

            piece = AddHelper(pieces, AddPiece(piece, new Vector(4, y, 0), "bridge_end", rotation.GetRotated(StructureRotation.Clockwise180), true));
            piece.GenDepth = -1;
            return true;
        }

        private bool FatTower(int depth, EndCityPiece parent, Vector? offset, List<StructurePiece> pieces)
        {
            var rotation = parent.PlaceSettings.Rotation;
            var piece = AddHelper(pieces, AddPiece(parent, new Vector(-3, 4, -3), "fat_tower_base", rotation, true));
            piece = AddHelper(pieces, AddPiece(piece, new Vector(0, 4, 0), "fat_tower_middle", rotation, true));

            for (var floor = 0; floor < 2 && random.NextInt(3) != 0; floor++)
            {
                piece = AddHelper(pieces, AddPiece(piece, new Vector(0, 8, 0), "fat_tower_middle", rotation, true));

                foreach (var (bridgeRotation, bridgeOffset) in fatTowerBridges)
                {
                    if (!random.NextBoolean())
                        continue;

                    var bridge = AddHelper(pieces, AddPiece(piece, bridgeOffset, "bridge_end", rotation.GetRotated(bridgeRotation), true));
                    this.RecursiveChildren(this.TowerBridge, depth + 1, bridge, null, pieces);
                }
            }

            AddHelper(pieces, AddPiece(piece, new Vector(-2, 8, -2), "fat_tower_top", rotation, true));
            return true;
        }

        /// <summary>
        /// Vanilla <c>recursiveChildren</c>: generates a section into a scratch list and keeps it unless it collides with a
        /// piece of another section (pieces of a section share a random depth).
        /// </summary>
        private bool RecursiveChildren(SectionGenerator generator, int depth, EndCityPiece parent, Vector? offset, List<StructurePiece> pieces)
        {
            if (depth > MaxGenDepth)
                return false;

            List<StructurePiece> section = [];
            if (!generator(depth, parent, offset, section))
                return false;

            var sectionDepth = random.NextInt();
            foreach (var piece in section)
            {
                piece.GenDepth = sectionDepth;
                var collision = StructurePiece.FindCollisionPiece(pieces, piece.BoundingBox);
                if (collision is not null && collision.GenDepth != parent.GenDepth)
                    return false;
            }

            pieces.AddRange(section);
            return true;
        }

        private static EndCityPiece AddHelper(List<StructurePiece> pieces, EndCityPiece piece)
        {
            pieces.Add(piece);
            return piece;
        }

        /// <summary>Vanilla <c>addPiece</c>: a piece placed so <paramref name="offset"/> of the parent is its origin.</summary>
        private static EndCityPiece AddPiece(EndCityPiece parent, Vector offset, string name, StructureRotation rotation, bool overwrite)
        {
            var piece = new EndCityPiece(name, parent.TemplatePosition, rotation, overwrite);
            var connection = StructureTemplate.CalculateRelativePosition(parent.PlaceSettings, offset)
                - StructureTemplate.CalculateRelativePosition(piece.PlaceSettings, Vector.Zero);
            piece.Move(connection.X, connection.Y, connection.Z);
            return piece;
        }
    }
}

/// <summary>
/// A part of an end city, like vanilla's <c>EndCityPieces.EndCityPiece</c>. Its template name is the name within
/// <c>end_city/</c>, e.g. <c>base_floor</c>.
/// </summary>
public sealed class EndCityPiece : TemplateStructurePiece
{
    /// <param name="overwrite">Whether the template's air is placed too.</param>
    internal EndCityPiece(string name, Vector position, StructureRotation rotation, bool overwrite)
        : base(0, "minecraft:end_city/" + name, name, MakeSettings(overwrite, rotation), position)
    {
    }

    protected override void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box)
    {
        var level = context.Level;
        if (metadata.StartsWith("Chest", StringComparison.Ordinal))
        {
            var chest = position + Vector.Down;
            if (box.IsInside(chest))
                FeatureHelpers.SetLootTable(level, context.Random, chest, "minecraft:chests/end_city_treasure");
        }
        else if (box.IsInside(position) && IsInSpawnableBounds(position))
        {
            if (metadata.StartsWith("Sentry", StringComparison.Ordinal))
            {
                level.AddEntity(new GeneratedEntity("minecraft:shulker", new VectorF(position.X + 0.5f, position.Y, position.Z + 0.5f)));
            }
            else if (metadata.StartsWith("Elytra", StringComparison.Ordinal))
            {
                var facing = this.PlaceSettings.Rotation.Rotate(BlockFace.South);
                level.AddEntity(CreateElytraFrame(position, facing));
            }
        }
    }

    /// <summary>
    /// An item frame holding an elytra, hung on <paramref name="position"/> facing <paramref name="facing"/>, like vanilla's
    /// <c>new ItemFrame(level, pos, direction)</c> with the item set.
    /// </summary>
    private static GeneratedEntity CreateElytraFrame(Vector position, BlockFace facing)
    {
        // Hanging entities sit against the block's face: the block center moved back by half a block minus the frame's depth.
        var step = facing.ToVector();
        var center = new VectorF(position.X + 0.5f - step.X * 0.46875f, position.Y + 0.5f - step.Y * 0.46875f, position.Z + 0.5f - step.Z * 0.46875f);
        var item = new NbtCompound("Item")
        {
            new NbtTag<string>("id", "minecraft:elytra"),
            new NbtTag<int>("count", 1)
        };

        return new GeneratedEntity("minecraft:item_frame", center, FacingYaw(facing))
        {
            Data = new NbtCompound
            {
                new NbtTag<byte>("Facing", FacingId(facing)),
                new NbtArray<int>("block_pos", [position.X, position.Y, position.Z]),
                item
            }
        };
    }

    /// <summary>Vanilla's direction ids (down, up, north, south, west, east).</summary>
    private static byte FacingId(BlockFace face) => face switch
    {
        BlockFace.Down => 0,
        BlockFace.Up => 1,
        BlockFace.North => 2,
        BlockFace.South => 3,
        BlockFace.West => 4,
        _ => 5
    };

    /// <summary>Vanilla <c>Direction.toYRot</c> for horizontal directions.</summary>
    private static float FacingYaw(BlockFace face) => face switch
    {
        BlockFace.South => 0f,
        BlockFace.West => 90f,
        BlockFace.North => 180f,
        BlockFace.East => 270f,
        _ => 0f
    };

    /// <summary>Vanilla <c>Level.isInSpawnableBounds</c>: within the world border's maximum and build limits.</summary>
    private static bool IsInSpawnableBounds(Vector position) =>
        position.Y >= -20000000 && position.Y < 20000000
        && position.X >= -30000000 && position.X < 30000000 && position.Z >= -30000000 && position.Z < 30000000;

    private static StructurePlaceSettings MakeSettings(bool overwrite, StructureRotation rotation)
    {
        var settings = new StructurePlaceSettings { IgnoreEntities = true, Rotation = rotation };
        settings.Processors.Add(overwrite ? BlockIgnoreProcessor.StructureBlock : BlockIgnoreProcessor.StructureAndAir);
        return settings;
    }
}
