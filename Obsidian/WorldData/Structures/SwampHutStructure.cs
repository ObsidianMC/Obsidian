using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A witch hut on stilts, like vanilla's <c>SwampHutStructure</c>.
/// </summary>
[StructureType("minecraft:swamp_hut")]
public sealed class SwampHutStructure : Structure
{
    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        OnTopOfChunkCenter(context, HeightmapType.WorldSurfaceWG, builder =>
            builder.AddPiece(new SwampHutPiece(context.Random, context.ChunkX << 4, context.ChunkZ << 4)));
}

/// <summary>
/// Vanilla's <c>SwampHutPiece</c>: the hut, with a witch and a black cat inside.
/// </summary>
public sealed class SwampHutPiece : ScatteredFeaturePiece
{
    private static readonly IBlock sprucePlanks = BlocksRegistry.Get(Material.SprucePlanks);
    private static readonly IBlock oakLog = BlocksRegistry.Get(Material.OakLog);
    private static readonly IBlock oakFence = BlocksRegistry.Get(Material.OakFence);
    private static readonly IBlock spruceStairs = BlocksRegistry.Get(Material.SpruceStairs);

    private bool spawnedWitch;
    private bool spawnedCat;

    public SwampHutPiece(IRandomSource random, int x, int z) : base(x, 64, z, 7, 7, 9, RandomHorizontalDirection(random))
    {
    }

    internal override void SaveState(NbtCompound tag)
    {
        base.SaveState(tag);

        tag.Add(new NbtTag<bool>("Witch", this.spawnedWitch));
        tag.Add(new NbtTag<bool>("Cat", this.spawnedCat));
    }

    internal override void LoadState(NbtCompound tag)
    {
        base.LoadState(tag);

        this.spawnedWitch = tag.TryGetBool("Witch", out var witch) && witch;
        this.spawnedCat = tag.TryGetBool("Cat", out var cat) && cat;
    }

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var box = context.Box;
        if (!this.UpdateAverageGroundHeight(level, box, 0))
            return;

        this.GenerateBox(level, box, 1, 1, 1, 5, 1, 7, sprucePlanks, sprucePlanks, false);
        this.GenerateBox(level, box, 1, 4, 2, 5, 4, 7, sprucePlanks, sprucePlanks, false);
        this.GenerateBox(level, box, 2, 1, 0, 4, 1, 0, sprucePlanks, sprucePlanks, false);
        this.GenerateBox(level, box, 2, 2, 2, 3, 3, 2, sprucePlanks, sprucePlanks, false);
        this.GenerateBox(level, box, 1, 2, 3, 1, 3, 6, sprucePlanks, sprucePlanks, false);
        this.GenerateBox(level, box, 5, 2, 3, 5, 3, 6, sprucePlanks, sprucePlanks, false);
        this.GenerateBox(level, box, 2, 2, 7, 4, 3, 7, sprucePlanks, sprucePlanks, false);
        this.GenerateBox(level, box, 1, 0, 2, 1, 3, 2, oakLog, oakLog, false);
        this.GenerateBox(level, box, 5, 0, 2, 5, 3, 2, oakLog, oakLog, false);
        this.GenerateBox(level, box, 1, 0, 7, 1, 3, 7, oakLog, oakLog, false);
        this.GenerateBox(level, box, 5, 0, 7, 5, 3, 7, oakLog, oakLog, false);
        this.PlaceBlock(level, oakFence, 2, 3, 2, box);
        this.PlaceBlock(level, oakFence, 3, 3, 7, box);
        this.PlaceBlock(level, BlocksRegistry.Air, 1, 3, 4, box);
        this.PlaceBlock(level, BlocksRegistry.Air, 5, 3, 4, box);
        this.PlaceBlock(level, BlocksRegistry.Air, 5, 3, 5, box);
        this.PlaceBlock(level, BlocksRegistry.Get(Material.PottedRedMushroom), 1, 3, 5, box);
        this.PlaceBlock(level, BlocksRegistry.Get(Material.CraftingTable), 3, 2, 6, box);
        this.PlaceBlock(level, BlocksRegistry.Get(Material.Cauldron), 4, 2, 6, box);
        this.PlaceBlock(level, oakFence, 1, 2, 1, box);
        this.PlaceBlock(level, oakFence, 5, 2, 1, box);

        var north = spruceStairs.WithProperty("facing", "north");
        var east = spruceStairs.WithProperty("facing", "east");
        var west = spruceStairs.WithProperty("facing", "west");
        var south = spruceStairs.WithProperty("facing", "south");
        this.GenerateBox(level, box, 0, 4, 1, 6, 4, 1, north, north, false);
        this.GenerateBox(level, box, 0, 4, 2, 0, 4, 7, east, east, false);
        this.GenerateBox(level, box, 6, 4, 2, 6, 4, 7, west, west, false);
        this.GenerateBox(level, box, 0, 4, 8, 6, 4, 8, south, south, false);
        this.PlaceBlock(level, north.WithProperty("shape", "outer_right"), 0, 4, 1, box);
        this.PlaceBlock(level, north.WithProperty("shape", "outer_left"), 6, 4, 1, box);
        this.PlaceBlock(level, south.WithProperty("shape", "outer_left"), 0, 4, 8, box);
        this.PlaceBlock(level, south.WithProperty("shape", "outer_right"), 6, 4, 8, box);

        for (var z = 2; z <= 7; z += 5)
        {
            for (var x = 1; x <= 5; x += 4)
                this.FillColumnDown(level, oakLog, x, -1, z, box);
        }

        // The witch and cat spawn once, with the chunk that holds their spot.
        var spawn = this.GetWorldPos(2, 2, 5);
        if (!box.IsInside(spawn))
            return;

        if (!this.spawnedWitch)
        {
            this.spawnedWitch = true;
            level.AddEntity(Persistent("minecraft:witch", spawn));
        }

        if (!this.spawnedCat)
        {
            this.spawnedCat = true;
            level.AddEntity(Persistent("minecraft:cat", spawn));
        }
    }

    private static GeneratedEntity Persistent(string type, Vector position) =>
        new(type, new VectorF(position.X + 0.5f, position.Y, position.Z + 0.5f)) { Data = { new NbtTag<bool>("PersistenceRequired", true) } };
}
