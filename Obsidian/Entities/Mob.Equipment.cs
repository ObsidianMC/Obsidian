using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Mob
{
    private readonly Dictionary<EquipmentSlot, ItemStack> equipment = [];
    private readonly Dictionary<EquipmentSlot, float> equipmentDropChances = [];
    public bool CanPickUpLoot { get; set; }
    private static readonly EquipmentSlot[] armorSlots = [EquipmentSlot.Helmet, EquipmentSlot.Chestplate, EquipmentSlot.Leggings, EquipmentSlot.Boots];
    private static readonly Dictionary<string, (int Helmet, int Chest, int Legs, int Boots, int Durability, float Toughness)> armorMaterials = new()
    {
        ["leather"] = (1, 3, 2, 1, 5, 0), ["copper"] = (2, 4, 3, 1, 11, 0),
        ["chainmail"] = (2, 5, 4, 1, 15, 0), ["iron"] = (2, 6, 5, 2, 15, 0),
        ["golden"] = (2, 5, 3, 1, 7, 0), ["diamond"] = (3, 8, 6, 3, 33, 2),
        ["turtle"] = (2, 6, 5, 2, 25, 0), ["netherite"] = (3, 8, 6, 3, 37, 3)
    };

    public float GetEquipmentDropChance(EquipmentSlot slot) => equipmentDropChances.GetValueOrDefault(slot, 0.085f);

    private static (int Armor, int Durability, float Toughness) GetArmorStats(ItemStack item, EquipmentSlot slot)
    {
        var name = item.Holder.UnlocalizedName.Replace("minecraft:", "", StringComparison.Ordinal);
        var separator = name.IndexOf('_');
        var suffix = slot switch { EquipmentSlot.Helmet => "_helmet", EquipmentSlot.Chestplate => "_chestplate", EquipmentSlot.Leggings => "_leggings", EquipmentSlot.Boots => "_boots", _ => "" };
        if (suffix.Length == 0 || !name.EndsWith(suffix, StringComparison.Ordinal))
            return default;
        if (separator < 0 || !armorMaterials.TryGetValue(name[..separator], out var material))
            return default;
        return slot switch
        {
            EquipmentSlot.Helmet => (material.Helmet, material.Durability * 11, material.Toughness),
            EquipmentSlot.Chestplate => (material.Chest, material.Durability * 16, material.Toughness),
            EquipmentSlot.Leggings => (material.Legs, material.Durability * 15, material.Toughness),
            EquipmentSlot.Boots => (material.Boots, material.Durability * 13, material.Toughness),
            _ => default
        };
    }

    private float EquipmentArmor => armorSlots.Sum(slot => GetArmorStats(GetEquipment(slot), slot).Armor);
    private float EquipmentToughness => armorSlots.Sum(slot => GetArmorStats(GetEquipment(slot), slot).Toughness);

    protected void PopulateDefaultArmor()
    {
        if (Random.NextSingle() >= 0.15f * SpecialDifficulty)
            return;
        var tier = Random.Next(3);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (Random.NextSingle() < 0.1087f)
                tier++;
        }
        ReadOnlySpan<string> materials = ["leather", "golden", "copper", "chainmail", "iron", "diamond"];
        for (var index = 0; index < armorSlots.Length; index++)
        {
            if (index > 0 && Random.NextSingle() < (Level.LevelData.Difficulty == Difficulty.Hard ? 0.1f : 0.25f))
                break;
            var slot = armorSlots[index];
            if (!GetEquipment(slot).IsAir)
                continue;
            var name = slot switch { EquipmentSlot.Helmet => "helmet", EquipmentSlot.Chestplate => "chestplate", EquipmentSlot.Leggings => "leggings", _ => "boots" };
            SetEquipment(slot, ItemsRegistry.GetSingleItem($"minecraft:{materials[tier]}_{name}"));
        }
    }

    private async ValueTask PickupEquipmentAsync()
    {
        if (!CanPickUpLoot || !Alive)
            return;
        foreach (var entity in GetEntitiesNear(1.5f).OfType<ItemEntity>().Where(item => item.CanPickup && item.Item.Count > 0).ToArray())
        {
            var item = entity.Item;
            var name = item.Holder.UnlocalizedName;
            var slot = name.EndsWith("_helmet", StringComparison.Ordinal) ? EquipmentSlot.Helmet :
                name.EndsWith("_chestplate", StringComparison.Ordinal) ? EquipmentSlot.Chestplate :
                name.EndsWith("_leggings", StringComparison.Ordinal) ? EquipmentSlot.Leggings :
                name.EndsWith("_boots", StringComparison.Ordinal) ? EquipmentSlot.Boots : EquipmentSlot.MainHand;
            var current = GetEquipment(slot);
            var incomingScore = slot == EquipmentSlot.MainHand ? GetWeaponDamage(item) : GetArmorStats(item, slot).Armor + GetArmorStats(item, slot).Toughness * 0.01f;
            var currentScore = slot == EquipmentSlot.MainHand ? GetWeaponDamage(current) : GetArmorStats(current, slot).Armor + GetArmorStats(current, slot).Toughness * 0.01f;
            if (!current.IsAir && (incomingScore < currentScore || incomingScore == currentScore && item.Damage >= current.Damage))
                continue;
            if (!current.IsAir && Math.Max(Random.NextSingle() - 0.1f, 0) < GetEquipmentDropChance(slot))
                DropItem(current);
            SetEquipment(slot, new ItemStack(item));
            equipmentDropChances[slot] = 2;
            PersistenceRequired = true;
            item.Count--;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TakeItemEntityPacket
            {
                CollectedEntityId = entity.EntityId, CollectorEntityId = EntityId, PickupItemCount = 1
            }, EntityId);
            if (item.Count == 0)
                await entity.RemoveAsync();
        }
    }

    protected static float GetWeaponDamage(ItemStack item)
    {
        var name = item.Holder.UnlocalizedName.Replace("minecraft:", "", StringComparison.Ordinal);
        var separator = name.IndexOf('_');
        var material = separator < 0 ? "" : name[..separator];
        var tier = material switch { "wooden" or "golden" => 0, "stone" or "copper" => 1, "iron" => 2, "diamond" => 3, "netherite" => 4, _ => 0 };
        if (name.EndsWith("_sword", StringComparison.Ordinal)) return 3 + tier;
        if (name.EndsWith("_spear", StringComparison.Ordinal)) return tier;
        if (name.EndsWith("_shovel", StringComparison.Ordinal)) return 1.5f + tier;
        if (name.EndsWith("_pickaxe", StringComparison.Ordinal)) return 1 + tier;
        if (name.EndsWith("_axe", StringComparison.Ordinal)) return tier + (material is "stone" or "copper" ? 7 : material is "diamond" or "netherite" ? 5 : 6);
        return item.Type switch { Material.Trident => 8, Material.Mace => 5, _ => 0 };
    }

    public ItemStack GetEquipment(EquipmentSlot slot) => equipment.GetValueOrDefault(slot, ItemStack.Air);

    public void SetEquipment(EquipmentSlot slot, ItemStack item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Enum.IsDefined(slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        equipment[slot] = item;
        var maximum = GetArmorStats(item, slot).Durability;
        if (maximum > 0 && !item.ContainsKey(DataComponentType.MaxDamage))
        {
            var durability = ComponentBuilder.MaxDamage;
            durability.Value = maximum;
            item[DataComponentType.MaxDamage] = durability;
        }
        if (initialized)
            SynchronizeEquipment();
    }

    private void SynchronizeEquipment()
    {
        if (equipment.Count > 0)
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEquipmentPacket
            {
                EntityId = EntityId,
                Equipment = equipment.Select(pair => new Equipment { Slot = pair.Key, Item = pair.Value }).ToList()
            }, EntityId);
    }

    internal ValueTask SendEquipmentToAsync(Player player) => equipment.Count == 0 ? default : player.Client.QueuePacketAsync(new SetEquipmentPacket
    {
        EntityId = EntityId,
        Equipment = equipment.Select(pair => new Equipment { Slot = pair.Key, Item = pair.Value }).ToList()
    });

    internal bool DamageEquipment(EquipmentSlot slot, int amount)
    {
        var item = GetEquipment(slot);
        var maximum = item.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? 0;
        if (item.IsAir || item.Unbreakable || maximum == 0)
            return false;
        var damage = ComponentBuilder.Damage;
        damage.Value = item.Damage + amount;
        item[DataComponentType.Damage] = damage;
        if (damage.Value < maximum)
            return false;
        SetEquipment(slot, ItemStack.Air);
        return true;
    }

    protected void DropItem(Material material, int count = 1)
    {
        if (count <= 0)
            return;
        var item = ItemsRegistry.GetSingleItem(material);
        item.Count = count;
        DropItem(item);
    }

    protected void DropItem(ItemStack item)
    {
        if (item.Count <= 0 || item.IsAir)
            return;
        var entity = new ItemEntity
        {
            Level = Level, EntityId = Server.GetNextEntityId(), Position = Position + new VectorD(0, 0.5f, 0), Item = item
        };
        if (Level.TryAddEntity(entity))
            entity.SpawnEntity(new Velocity(0, 0.2f, 0));
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity, additionalData);
        SynchronizeEquipment();
        if (Rider != null)
            SynchronizePassengers();
    }
}
