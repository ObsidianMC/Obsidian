using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;

namespace Obsidian.WorldData.Generators;

public sealed class MobTestGenerator : ILevelGenerator
{
    private const int PenSize = 32;
    private const int Columns = 8;
    private const int Floor = -60;
    private const int Roof = -28;
    private readonly SuperflatGenerator terrain = new();
    private ILevel level = null!;
    private MobTestExhibit[] exhibits = [];

    public string Id => "mob-test";

    public void Init(ILevel level)
    {
        this.level = level;
        terrain.Init(level);
        exhibits = MobTestExhibit.Create(level).ToArray();
    }

    public ValueTask<VectorD?> FindSpawnPointAsync() => ValueTask.FromResult<VectorD?>(new VectorD(-4, Floor + 1, -4));

    internal bool TryGetExhibitDestination(EntityType type, string state, out VectorD destination, out string availableStates)
    {
        availableStates = string.Join(", ", exhibits.Where(exhibit => exhibit.Type == type).Select(exhibit => exhibit.Label));
        for (var index = 0; index < exhibits.Length; index++)
        {
            if (exhibits[index].Type != type || !string.Equals(exhibits[index].Label, state, StringComparison.OrdinalIgnoreCase)) continue;
            destination = new VectorD(index % Columns * PenSize + 1.5, Floor + 1, index / Columns * PenSize + 16.5);
            return true;
        }
        destination = default;
        return false;
    }

    public async ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full)
    {
        if (chunk is { IsGenerated: true }) return chunk;
        var result = (Chunk)await terrain.GenerateChunkAsync(x, z, chunk, stage);
        if (x < 0 || z < 0 || x >= Columns * 2) return result;
        var index = z / 2 * Columns + x / 2;
        if (index >= exhibits.Length) return result;
        var exhibit = exhibits[index];
        var originX = x / 2 * PenSize;
        var originZ = z / 2 * PenSize;
        for (var bx = 0; bx < 16; bx++)
        for (var bz = 0; bz < 16; bz++)
        {
            var px = x * 16 + bx - originX;
            var pz = z * 16 + bz - originZ;
            var inside = px is >= 3 and <= 28 && pz is >= 3 and <= 28;
            var edge = inside && (px is 3 or 28 || pz is 3 or 28);
            if (!inside) continue;
            result.SetBlock(bx, Floor, bz, BlocksRegistry.Bedrock);
            if (edge)
            {
                result.SetBlock(bx, Floor + 1, bz, BlocksRegistry.OakFence);
                for (var y = Floor + 2; y < Roof; y++) result.SetBlock(bx, y, bz, BlocksRegistry.Barrier);
            }
            else
            {
                result.SetBlock(bx, Floor, bz, exhibit.Type == EntityType.Turtle ? BlocksRegistry.Sand : BlocksRegistry.GrassBlock);
                if (exhibit.Water || exhibit.Type == EntityType.Strider)
                    for (var y = Floor - 3; y <= Floor; y++)
                        result.SetBlock(bx, y, bz, exhibit.Type == EntityType.Strider ? BlocksRegistry.Lava : BlocksRegistry.Water);
            }
            result.SetBlock(bx, Roof, bz, BlocksRegistry.Barrier);
            for (var y = Floor + 1; y <= Roof; y++) result.SetLightLevel(bx, y, bz, LightType.Sky, 15);
            result.Heightmaps[HeightmapType.MotionBlocking].Set(bx, bz, Roof);
        }

        // Each exhibit belongs to exactly one chunk; reloaded chunks use their saved entities.
        if (x % 2 == 1 && z % 2 == 1)
        {
            var entity = EntitySpawner.Create(exhibit.Type, level);
            entity.Type = exhibit.Type;
            entity.Position = new VectorD(originX + 16.5, exhibit.Water ? Floor - 1 : Floor + 1, originZ + 16.5);
            if (entity is Mob mob) mob.InitializeAi();
            var data = new NbtCompound();
            entity.WriteNbt(data);
            foreach (var (_, value) in exhibit.Data) data.Set(value);
            data.Set(new NbtTag<bool>("PersistenceRequired", true));
            data.Set(new NbtTag<bool>("NoAI", exhibit.Frozen));
            data.Set(new NbtTag<bool>("CustomNameVisible", true));
            data.Set(((ChatMessage)$"{index + 1}: {exhibit.Type} / {exhibit.Label}").ToNbt("CustomName"));
            data.Set(new NbtArray<int>("ObsidianTestPen", [originX + 4, Floor - 3, originZ + 4, originX + 28, Roof, originZ + 28]));
            data.Set(new NbtTag<bool>("ObsidianTestDisplay", exhibit.Frozen));
            data.Set(new NbtTag<bool>("IsImmuneToZombification", true));
            if (exhibit.Type == EntityType.CopperGolem) data.Set(new NbtTag<long>("next_weather_age", -2));
            result.PendingEntities.Add(new GeneratedEntity(EntityNbt.TypeId(exhibit.Type), entity.Position) { Data = data });
            if (level is AbstractLevel concrete) concrete.QueueEntitySpawn(result);
        }
        return result;
    }

    internal static bool AllowsMovement(Entity entity, VectorD position)
    {
        if (!entity.UnmodeledData.TryGetTag<NbtArray<int>>("ObsidianTestPen", out var tag) || tag.Count != 6) return true;
        var bounds = tag.GetArray();
        var halfWidth = entity.Dimension.Width / 2;
        return position.X - halfWidth >= bounds[0] && position.Y >= bounds[1] && position.Z - halfWidth >= bounds[2] &&
            position.X + halfWidth <= bounds[3] && position.Y + entity.Dimension.Height <= bounds[4] && position.Z + halfWidth <= bounds[5];
    }

    internal void AssignPen(Mob entity)
    {
        if (entity.UnmodeledData.HasTag("ObsidianTestPen")) return;
        var column = (int)Math.Floor(entity.Position.X / PenSize);
        var row = (int)Math.Floor(entity.Position.Z / PenSize);
        if (column < 0 || column >= Columns || row < 0 || row * Columns + column >= exhibits.Length) return;
        entity.UnmodeledData.Set(new NbtArray<int>("ObsidianTestPen",
            [column * PenSize + 4, Floor - 3, row * PenSize + 4, column * PenSize + 28, Roof, row * PenSize + 28]));
        entity.PersistenceRequired = true;
    }
}
