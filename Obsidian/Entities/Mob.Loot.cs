using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class Mob
{
    // This is intentionally a bounded migration. Others keep their special death handling until the predicates
    // and functions their vanilla tables need are supported (see known-gaps.md).
    private static readonly HashSet<EntityType> tableLootTypes =
    [
        EntityType.Zombie, EntityType.Husk, EntityType.Drowned, EntityType.ZombieVillager,
        EntityType.Skeleton, EntityType.Stray, EntityType.Bogged, EntityType.Parched, EntityType.WitherSkeleton,
        EntityType.Spider, EntityType.CaveSpider, EntityType.Creeper, EntityType.Enderman,
        EntityType.Blaze, EntityType.Breeze, EntityType.Witch, EntityType.ZombifiedPiglin,
        EntityType.Cow, EntityType.Mooshroom, EntityType.Pig, EntityType.Chicken, EntityType.Rabbit,
        EntityType.Hoglin, EntityType.Zoglin
    ];

    /// <summary>Uses the type's entity table for the explicitly migrated vanilla mobs.</summary>
    protected void DropVanillaLoot(IEntity source)
    {
        if (this.Type is not EntityType type || !tableLootTypes.Contains(type))
            return;

        // Baby animals do not run their death loot table in vanilla. Baby monsters still do.
        if (this is not Animal { IsBaby: true })
            this.DropEntityTable(ReferenceEquals(source, this) ? null : source, this.IncomingDirectSource ?? source);
    }

    /// <summary>
    /// Rolls the type's entities/path table with a loot context shared by vanilla and plugin mobs.
    /// </summary>
    internal void DropEntityTable(IEntity? attacker, IEntity? direct)
    {
        if (!this.Level.LevelData.GetBooleanRule("mob_drops"))
            return;

        var id = EntityNbt.TypeId(this);
        var separator = id.IndexOf(':');
        var identifier = $"{id[..separator]}:entities/{id[(separator + 1)..]}";
        var resolve = PluginLootTables.CaptureResolver();
        if (resolve(identifier) is not LootTable table)
            return;

        var context = this.CreateEntityLootContext(table, attacker, direct, resolve);
        foreach (var item in table.GetRandomItems(context))
            this.DropItem(item);
    }

    // A supplied seed is used by fixture replays; normal deaths use the level's shared named random sequence,
    // like vanilla LivingEntity.dropFromLootTable, rather than drawing a fresh seed per death.
    internal LootContext CreateEntityLootContext(
        LootTable table,
        IEntity? attacker,
        IEntity? direct,
        Func<string, LootTable?>? resolve = null,
        IRandomSource? random = null)
    {
        var player = this.RecentPlayerAttacker;
        var recipes = (this.Level as AbstractLevel)?.CookingRecipes;
        return new LootContext
        {
            Random = random ?? table.CreateRandom(0, BlockLoot.SequencesFor(this.Level)),
            Luck = player?.GetAttributeValue("minecraft:luck") ?? 0,
            Origin = this.Position,
            ThisEntity = this,
            AttackingEntity = attacker,
            DirectAttackingEntity = direct,
            LastDamagePlayer = player,
            EntityEquipment = LootEquipment,
            EntityIsOnFire = entity => entity.Burning || entity is Living { FireTicks: > 0 },
            EntityIsBaby = entity => entity switch
            {
                AgeableMob ageable => ageable.IsBaby,
                Zombie zombie => zombie.IsBaby,
                Piglin piglin => piglin.IsBaby,
                Zoglin zoglin => zoglin.IsBaby,
                _ => false
            },
            SmeltItem = stack => recipes?.FindCooking(CraftingType.Smelting, stack)?.Result.FirstOrDefault(),
            ResolveAttackerEnchantment = id => CombatItems.LootEnchantmentLevel(attacker, id),
            ResolveTable = resolve ?? PluginLootTables.CaptureResolver()
        };
    }

    private static ItemStack? LootEquipment(IEntity entity, string slot)
    {
        if (!Enum.TryParse<EquipmentSlot>(slot switch
        {
            "head" => "Helmet",
            "chest" => "Chestplate",
            "legs" => "Leggings",
            "feet" => "Boots",
            _ => slot
        }, true, out var equipment))
        {
            return null;
        }

        return entity switch
        {
            Mob mob => mob.GetEquipment(equipment),
            IPlayer player => equipment switch
            {
                EquipmentSlot.MainHand => player.GetHeldItem(),
                EquipmentSlot.OffHand => player.GetOffHandItem(),
                EquipmentSlot.Helmet => player.Inventory.GetItem(5),
                EquipmentSlot.Chestplate => player.Inventory.GetItem(6),
                EquipmentSlot.Leggings => player.Inventory.GetItem(7),
                EquipmentSlot.Boots => player.Inventory.GetItem(8),
                _ => null
            },
            _ => null
        };
    }
}
