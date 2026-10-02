using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public abstract class FarmAnimal : Animal
{
    protected override bool UsesAi => true;
    protected virtual float PanicSpeed => 1.25f;
    protected virtual float TemptSpeed => 1.25f;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.Wheat };
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PanicGoal(this, PanicSpeed));
        actions.AddGoal(2, new BreedGoal(this));
        actions.AddGoal(3, new TemptGoal(this, CanEat, TemptSpeed));
        actions.AddGoal(4, new FollowParentGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
    }

    protected ValueTask GiveInteractionItemAsync(IPlayer player, Hand hand, Material result) =>
        GiveInteractionItemAsync(player, hand, ItemsRegistry.GetSingleItem(result));

    protected async ValueTask GiveInteractionItemAsync(IPlayer player, Hand hand, ItemStack item)
    {
        var slot = hand == Hand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var held = player.Inventory.GetItem(slot);
        if (held is not { Count: > 0 })
            return;
        if (player.Gamemode != Gamemode.Creative && held.Count == 1)
            player.Inventory.SetItem(slot, item);
        else
        {
            if (player.Gamemode != Gamemode.Creative)
                player.Inventory.RemoveItem(slot, 1);
            var addedSlot = player.Inventory.AddItem(item);
            if (addedSlot < 0)
                DropItem(item);
            else
                await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
                { ContainerId = 0, Slot = (short)addedSlot, SlotData = player.Inventory.GetItem(addedSlot) });
        }
        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
    }

    protected int GetFarmVariant()
    {
        var (x, z) = Position.ToChunkCoord();
        if (Level is not Obsidian.WorldData.AbstractLevel level || level.GetLoadedChunk(x, z) is not { } chunk)
            return 1;
        var biome = chunk.GetBiome((int)Position.X, (int)Position.Y, (int)Position.Z);
        return TagsRegistry.Worldgen.Biome.SpawnsColdVariantFarmAnimals.Entries.Contains(biome.Id) ? 0 :
            TagsRegistry.Worldgen.Biome.SpawnsWarmVariantFarmAnimals.Entries.Contains(biome.Id) ? 2 : 1;
    }
}
