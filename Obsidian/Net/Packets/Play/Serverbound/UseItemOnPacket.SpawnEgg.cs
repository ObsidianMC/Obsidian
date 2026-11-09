using Obsidian.API.Entities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    // 1.21.11 SpawnEggItem resolves the type from entity_data, including per-stack overrides. There is no
    // colour-tinted generic egg model: each item supplies its own model and texture through normal assets.
    private async ValueTask<UseResult> UseSpawnEggAsync(BlockUse use)
    {
        var player = use.Player;
        var stack = use.Item!;
        var block = use.Block;
        var level = use.Level;
        var component = stack.GetComponent<EntityDataComponent>(DataComponentType.EntityData) ?? DefaultEntity(stack);
        if (component is null)
            return UseResult.Pass;

        if (!Plugins.ItemBehaviors.CanUseItems(player) || (uint)this.Face > (uint)BlockFace.East)
            return UseResult.Fail;

        var definition = EntityDefinitionsRegistry.PluginDefinitions
            .FirstOrDefault(type => type.Id == component.RegistryId);
        Mob? mob = definition is null && Enum.IsDefined((EntityType)component.RegistryId)
            ? EntitySpawner.CreateMob(level, (EntityType)component.RegistryId)
            : definition is { Kind: EntityKind.Mob, Binding: not null }
                ? (Mob)PluginEntity.Create(definition, level)
                : null;
        if (mob is null)
            return UseResult.Fail;

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
            var clickedShape = BlockCollisionShapes.Get(level, this.Position, block);
            var target = clickedShape.Count == 0 ? this.Position : this.Position + this.Face.ToVector();
            if (level.IsOutsideBuildHeight(target.Y))
                return UseResult.Fail;

            mob.EntityId = Server.GetNextEntityId();
            mob.Position = (VectorD)target + new VectorD(0.5, 0, 0.5);
            // SpawnEggItem / EntityType.getYOffset starts one block up and settles against block collisions.
            // A click on the upper face can settle through the target and onto the clicked block below it.
            var raised = mob.Dimension.CreateBBFromPosition(mob.Position + new VectorD(0, 1, 0));
            var search = new BoundingBox((VectorD)target, (VectorD)target + new VectorD(1, 1, 1));
            var descent = -1d;
            if (target != this.Position && this.Face == BlockFace.Up)
            {
                search = new BoundingBox(search.Min - new VectorD(0, 1, 0), search.Max);
                descent = -2;
            }

            var terrain = new MobTerrain(level);
            var collisions = terrain.GetCollisions(search).Where(shape =>
                shape.Max.X > search.Min.X && shape.Min.X < search.Max.X
                && shape.Max.Y > search.Min.Y && shape.Min.Y < search.Max.Y
                && shape.Max.Z > search.Min.Z && shape.Min.Z < search.Max.Z).ToArray();
            var offset = EntityMovement.Clip(
                raised, new VectorD(0, descent, 0), collisions).Y;
            mob.Position += new VectorD(0, 1 + offset, 0);
            mob.Yaw = Random.Shared.NextSingle() * 360;
            mob.InitializeAi();
            var saved = EntityNbt.Save(mob)!;
            foreach (var (name, value) in component.Data)
            {
                if (name is not ("id" or "UUID" or "Pos" or "Motion" or "Rotation"))
                    saved.Set(value);
            }

            mob.ReadNbt(saved);
            level.SpawnEntity(mob);
            level.EmitGameEvent(MobGameEvent.EntityPlace, mob.Position, player);
        }

        await use.ConsumeItemAsync();
        return UseResult.Success;
    }

    // The entity a spawn egg's own entity_data names, for a stack that doesn't carry the component itself.
    private static EntityDataComponent? DefaultEntity(ItemStack stack)
    {
        var data = CombatItems.Component(stack, "entity_data");
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object || !data.TryGetProperty("id", out var id))
            return null;

        if (!EntityNbt.TryParseType(id.GetString(), out var type))
            return null;

        return new EntityDataComponent(DataComponentType.EntityData) { RegistryId = (int)type };
    }
}
