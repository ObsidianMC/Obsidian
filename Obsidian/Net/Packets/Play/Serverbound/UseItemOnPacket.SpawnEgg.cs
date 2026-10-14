using Obsidian.API.Entities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    // 1.21.11 SpawnEggItem resolves the type from entity_data, including per-stack overrides. There is no
    // colour-tinted generic egg model: each item supplies its own model and texture through normal assets.
    private async ValueTask<bool> TryUseSpawnEggAsync(
        IClientPlayer player,
        int slot,
        ItemStack? stack,
        IBlock block)
    {
        var component = stack?.GetComponent<EntityDataComponent>(DataComponentType.EntityData);
        if (stack is not { Count: > 0 } || component is null)
        {
            return false;
        }

        player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
        if (player.Level is not AbstractLevel level
            || !Plugins.ItemBehaviors.CanUseItems(player)
            || player.GameMode == GameMode.Adventure
            || (uint)this.Face > (uint)BlockFace.East
            || (player.Position - (VectorD)this.Position).MagnitudeSquared() > 36)
        {
            return true;
        }

        var definition = EntityDefinitionsRegistry.PluginDefinitions
            .FirstOrDefault(type => type.Id == component.RegistryId);
        Mob? mob = definition is null && Enum.IsDefined((EntityType)component.RegistryId)
            ? EntitySpawner.CreateMob(level, (EntityType)component.RegistryId)
            : definition is { Kind: EntityKind.Mob, Binding: not null }
                ? (Mob)PluginEntity.Create(definition, level)
                : null;
        if (mob is null)
            return true;

        var id = EntityNbt.TypeId(mob);
        if (block.Is(BlockIds.Spawner)
            && await level.GetBlockEntityAsync(this.Position) is DataBlockEntity spawner)
        {
            spawner.Set(new NbtCompound("SpawnData")
            {
                new NbtCompound("entity") { new NbtTag<string>("id", id) }
            });
            spawner.Remove("SpawnPotentials");
            spawner.SetChanged();
            spawner.SendUpdate();
        }
        else
        {
            // A clicked solid block uses the neighbouring block; air at the clicked point uses that point.
            var target = block.IsAir ? this.Position : this.Position + this.Face.ToVector();
            if (level.IsOutsideBuildHeight(target.Y))
                return true;

            mob.EntityId = Server.GetNextEntityId();
            mob.Position = (VectorD)target + new VectorD(0.5, 0, 0.5);
            mob.Yaw = Random.Shared.NextSingle() * 360;
            mob.InitializeAi();
            var saved = EntityNbt.Save(mob)!;
            foreach (var (name, value) in component.Data)
            {
                if (name is not ("id" or "UUID" or "Pos" or "Motion" or "Rotation"))
                    saved.Set(value);
            }

            mob.ReadNbt(saved);
            var terrain = new MobTerrain(level);
            if (!terrain.IsFree(mob.Dimension.CreateBBFromPosition(mob.Position)))
                return true;

            level.SpawnEntity(mob);
        }

        if (player.GameMode != GameMode.Creative)
        {
            stack.Count--;
            player.Inventory.SetItem(slot, stack.Count > 0 ? stack : null);
            await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
            {
                ContainerId = 0,
                Slot = (short)slot,
                SlotData = player.Inventory.GetItem(slot)
            });
        }

        return true;
    }
}
