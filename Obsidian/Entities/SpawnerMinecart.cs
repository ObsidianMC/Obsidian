using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A minecart with a monster spawner, like vanilla's <c>MinecartSpawner</c>: its spawner works like the block's,
/// from wherever the cart is. Breaking it drops a plain minecart.
/// </summary>
/// <remarks>
/// The spawner's fields (<c>SpawnData</c>, <c>Delay</c>, <c>SpawnPotentials</c>, ...) are saved with the cart, like
/// vanilla's <c>BaseSpawner.save</c>.
/// </remarks>
[MinecraftEntity("minecraft:spawner_minecart")]
public sealed partial class SpawnerMinecart : AbstractMinecart
{
    // The spawner's fields vanilla's BaseSpawner saves, which the cart keeps in its own save.
    private static readonly string[] spawnerFields =
    [
        "Delay", "MinSpawnDelay", "MaxSpawnDelay", "SpawnCount", "MaxNearbyEntities", "RequiredPlayerRange",
        "SpawnRange", "SpawnData", "SpawnPotentials"
    ];

    // The spawner, run like a spawner block's at the cart's block.
    private readonly DataBlockEntity spawner = new() { Id = "minecraft:mob_spawner" };

    public SpawnerMinecart() => this.Type = EntityType.SpawnerMinecart;

    /// <summary>The spawner's fields, as a spawner block's would be saved.</summary>
    public NbtCompound SpawnerData => this.spawner.Data;

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.Minecart);

    private protected override ValueTask AfterMoveAsync()
    {
        if (this.Level is AbstractLevel level)
        {
            this.spawner.BlockPosition = (Vector)this.Position.Floor();
            level.TickSpawnerMinecart(this.spawner);
        }

        return default;
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        lock (this.spawner.Data)
        {
            foreach (var (name, value) in this.spawner.Data)
            {
                tag.Remove(name);
                tag.Add(name, value);
            }
        }
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        foreach (var name in spawnerFields)
        {
            if (tag.TryGetTag(name, out var value))
                this.spawner.Set(value);
        }
    }
}
