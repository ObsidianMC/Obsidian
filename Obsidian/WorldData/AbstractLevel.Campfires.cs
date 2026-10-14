using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Nbt;
using Obsidian.Registries;
using Microsoft.Extensions.Logging;
using Obsidian.WorldData.Furnaces;

namespace Obsidian.WorldData;

/// <summary>
/// Cooks food in campfires like vanilla <c>CampfireBlockEntity.serverTick</c> and <c>cooldownTick</c>. A lit campfire cooks
/// its food and drops each finished item; an unlit one cools its progress down. Campfires without food aren't ticked.
/// </summary>
public abstract partial class AbstractLevel
{
    private readonly ConcurrentDictionary<Vector, byte> activeCampfires = [];

    /// <summary>Starts ticking the campfire at <paramref name="point"/> once it holds food.</summary>
    internal void TrackCampfire(Vector point) => activeCampfires[point] = 0;

    private void RegisterChunkCampfires(Chunk chunk)
    {
        foreach (var entity in chunk.GetBlockEntities())
        {
            if (entity is CampfireBlockEntity campfire && campfire.HasFood())
                TrackCampfire(campfire.BlockPosition);
        }
    }

    private void TickCampfires()
    {
        foreach (var point in activeCampfires.Keys.ToArray())
        {
            TickStage = $"campfire at {point}";
            TickCampfire(point);
        }
    }

    private void TickCampfire(Vector point)
    {
        var chunk = GetLoadedChunk(point.X >> 4, point.Z >> 4);
        if (chunk?.GetBlockEntity(point.X, point.Y, point.Z) is not CampfireBlockEntity campfire)
        {
            activeCampfires.TryRemove(point, out _);
            return;
        }

        var block = GameEventBlock(point);
        if (block?.Material is not (Material.Campfire or Material.SoulCampfire))
        {
            activeCampfires.TryRemove(point, out _);
            return;
        }

        var lit = block.GetProperty("lit") == "true";
        var changed = false;
        for (var slot = 0; slot < CampfireBlockEntity.SlotCount; slot++)
        {
            if (campfire.Items[slot] is not { IsAir: false, Count: > 0 })
                continue;

            if (lit)
            {
                campfire.CookingProgress[slot]++;
                if (campfire.CookingProgress[slot] >= campfire.CookingTotal[slot])
                {
                    FinishCampfireSlot(point, campfire, slot);
                    changed = true;
                }
            }
            else
            {
                campfire.CookingProgress[slot] = Math.Max(0, campfire.CookingProgress[slot] - 2);
            }
        }

        if (changed)
            BroadcastCampfire(point, campfire, chunk);

        if (!campfire.HasFood())
            activeCampfires.TryRemove(point, out _);
    }

    /// <summary>Sends the campfire's items to the players that can see it, like vanilla's block entity update.</summary>
    private void BroadcastCampfire(Vector point, CampfireBlockEntity campfire, IChunk chunk)
    {
        var nbt = BlockEntityNbt.Save(campfire, chunk) ?? new NbtCompound();
        var packet = BlockEntityDataPacket.Create(point, BlockEntityType.Campfire, nbt);
        foreach (var player in PlayersInRange(point))
            player.SendPacket(packet);
    }

    private void FinishCampfireSlot(Vector point, CampfireBlockEntity campfire, int slot)
    {
        var food = campfire.Items[slot]!;
        // Copy the recipe's shared result stack: the spawned entity mutates its count.
        var source = FurnaceRules.FindRecipe(CraftingType.CampfireCooking, food)?.Result.FirstOrDefault() ?? food;
        var result = new ItemStack(source, source.Count);

        campfire.Items[slot] = null;
        campfire.CookingProgress[slot] = 0;
        campfire.CookingTotal[slot] = 0;
        // Vanilla Containers.dropItemStack: a random spot inside the block, with a small upward and sideways toss.
        const double size = 0.25; // item entity width
        var random = Random.Shared;
        var position = new VectorD(
            point.X + random.NextDouble() * (1 - size) + size / 2,
            point.Y + random.NextDouble() * (1 - size),
            point.Z + random.NextDouble() * (1 - size) + size / 2);
        var item = new ItemEntity { Level = this, EntityId = Server.GetNextEntityId(), Position = position, Item = result };
        var motion = new VectorD(random.NextDouble() * 0.11485, 0.2 + random.NextDouble() * (0.11485 - 0.2), random.NextDouble() * 0.11485);
        item.Motion = motion;
        // Like ItemEntity.Drop, pass the velocity so clients receive it in the spawn packet.
        if (!TryAddEntity(item))
            return;
        item.SpawnEntity(new Velocity(motion.X, motion.Y, motion.Z));
    }

    /// <summary>
    /// Puts one <paramref name="food"/> into the first empty slot of the lit campfire at <paramref name="point"/>, like vanilla
    /// <c>CampfireBlockEntity.placeFood</c>. Returns <c>false</c> when the campfire is unlit, full, or the item has no campfire recipe.
    /// </summary>
    internal bool TryPlaceCampfireFood(Vector point, ItemStack food)
    {
        var chunk = GetLoadedChunk(point.X >> 4, point.Z >> 4);
        if (chunk is null)
            return false;

        var block = GameEventBlock(point);
        if (block?.Material is not (Material.Campfire or Material.SoulCampfire) || block.GetProperty("lit") != "true")
            return false;

        var recipe = FurnaceRules.FindRecipe(CraftingType.CampfireCooking, food);
        if (recipe is null)
            return false;

        if (chunk.GetBlockEntity(point.X, point.Y, point.Z) is not CampfireBlockEntity campfire)
        {
            campfire = new CampfireBlockEntity { Id = "minecraft:campfire", BlockPosition = point };
            chunk.SetBlockEntity(point.X, point.Y, point.Z, campfire);
        }

        var slot = campfire.FirstEmptySlot();
        if (slot < 0)
            return false;

        campfire.Items[slot] = new ItemStack(food.Holder, 1);
        campfire.CookingProgress[slot] = 0;
        campfire.CookingTotal[slot] = recipe.CookingTime;
        TrackCampfire(point);
        BroadcastCampfire(point, campfire, chunk);
        return true;
    }
}
