using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

/// <summary>
/// An armor stand, like vanilla's <c>ArmorStand</c>: a living entity that wears and holds what players give it (see
/// <see cref="UseAsync"/>), with flags for its size, arms, base plate and marker form, slots that can be locked, and
/// a pose for each limb.
/// </summary>
[MinecraftEntity("minecraft:armor_stand")]
public sealed partial class ArmorStand : Living
{
    // Vanilla's default pose, which saves leave out (ArmorStand.ArmorStandPose.DEFAULT).
    private static readonly VectorF defaultHead = new(0, 0, 0);
    private static readonly VectorF defaultBody = new(0, 0, 0);
    private static readonly VectorF defaultLeftArm = new(-10, 0, -10);
    private static readonly VectorF defaultRightArm = new(-15, 0, 10);
    private static readonly VectorF defaultLeftLeg = new(-1, 0, -1);
    private static readonly VectorF defaultRightLeg = new(1, 0, 1);

    // Every slot in vanilla's order (EquipmentSlot.values()), with its saved name. Vanilla's living equipment codec
    // saves them all, the body and saddle too, though a player's use never fills those on a stand.
    private static readonly (EquipmentSlot Slot, string Name)[] savedSlots =
    [
        (EquipmentSlot.MainHand, "mainhand"),
        (EquipmentSlot.OffHand, "offhand"),
        (EquipmentSlot.Boots, "feet"),
        (EquipmentSlot.Leggings, "legs"),
        (EquipmentSlot.Chestplate, "chest"),
        (EquipmentSlot.Helmet, "head"),
        (EquipmentSlot.Body, "body"),
        (EquipmentSlot.Saddle, "saddle")
    ];

    // Changed on the level's tick under its tick lock; saves and players starting to see the stand read it too.
    private readonly Dictionary<EquipmentSlot, ItemStack> equipment = [];

    // Vanilla's armor stand has 20 health, its max_health attribute.
    public ArmorStand()
    {
        Type = EntityType.ArmorStand;
        Health = 20;
    }

    // Vanilla's ArmorStand.thunderHit does nothing: lightning neither burns nor hurts it.
    internal override ValueTask ThunderHitAsync(LightningBolt bolt) => ValueTask.CompletedTask;

    /// <summary>A campfire first lights the stand for five seconds, then deals 0.15 health per contact.</summary>
    internal ValueTask BurnFromCampfireAsync()
    {
        if (this.IsRemoved || this.Marker || this.Invisible)
            return default;

        if (this.ContactFireTicks <= 0)
        {
            this.Ignite(5);
            return default;
        }

        return this.DamageByBurningAsync(0.15f);
    }

    internal bool IsRemoved { get; private set; }

    public override ValueTask RemoveAsync()
    {
        this.IsRemoved = true;
        return base.RemoveAsync();
    }

    internal ValueTask DamageByBurningAsync(float amount)
    {
        if (this.IsRemoved || this.Marker || this.Invisible)
            return default;

        var remaining = this.Health - amount;
        if (remaining <= 0.5f)
            return this.BreakFromBurningAsync();

        this.Health = remaining;
        if (this.Level is WorldData.AbstractLevel world)
            world.EmitGameEvent(WorldData.MobGameEvent.EntityDamage, this.Position);

        return default;
    }

    private ValueTask BreakFromBurningAsync()
    {
        if (this.Level is WorldData.AbstractLevel world)
        {
            WorldData.LevelSounds.Play(world, this.Position, "minecraft:entity.armor_stand.break", SoundCategory.Neutral);
            var dropEquipment = world.LevelData.GetBooleanRule("block_drops");
            foreach (var (slot, _) in savedSlots)
            {
                ItemStack? item;
                lock (this.equipment)
                    this.equipment.Remove(slot, out item);

                if (item is null || CombatEffects.HasEnchantment(item, EnchantmentsRegistry.VanishingCurse.Id))
                    continue;

                // Vanilla's popResource draws three level doubles per drop, in equipment order. The new item's
                // motion uses its own random and must not consume the level or the stand's random.
                var random = world.GrowthRandom;
                var block = (Vector)this.Position.Floor() + Vector.Up;
                var point = new VectorD(block.X + 0.25 + random.NextDouble() * 0.5,
                    block.Y + 0.125 + random.NextDouble() * 0.5, block.Z + 0.25 + random.NextDouble() * 0.5);
                // popResource still makes those location draws when block_drops suppresses the item spawn.
                if (!dropEquipment)
                    continue;

                world.DropItemAt(point, item,
                    new VectorD(Globals.Random.NextDouble() * 0.2 - 0.1, 0.2, Globals.Random.NextDouble() * 0.2 - 0.1));
            }
            world.EmitGameEvent(WorldData.MobGameEvent.EntityDie, this.Position, this);
        }
        return this.RemoveAsync();
    }

    internal void FreezeFromPowderSnow()
    {
        foreach (var (slot, _) in savedSlots)
        {
            if (slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand or EquipmentSlot.Saddle)
                continue;

            if (TagsRegistry.Item.FreezeImmuneWearables.Entries.Contains(this.GetEquipment(slot).Holder.Id))
                return;
        }
        this.PowderedSnowTicks = Math.Min(140, this.PowderedSnowTicks + 1);
    }

    /// <summary>A small stand, half the size, like one placed as a baby.</summary>
    public bool Small { get; set; }

    public bool ShowArms { get; set; }

    public bool NoBasePlate { get; set; }

    /// <summary>A marker: no box, no physics, and nothing to interact with.</summary>
    public bool Marker { get; set; }

    /// <summary>
    /// Vanilla's <c>DisabledSlots</c> bits: for each slot's filter bit, bit <c>b</c> disables the slot, bit
    /// <c>b + 8</c> stops its item being taken and bit <c>b + 16</c> stops an item being put in it.
    /// </summary>
    public int DisabledSlots { get; set; }

    public VectorF HeadPose { get; set; } = defaultHead;

    public VectorF BodyPose { get; set; } = defaultBody;

    public VectorF LeftArmPose { get; set; } = defaultLeftArm;

    public VectorF RightArmPose { get; set; } = defaultRightArm;

    public VectorF LeftLegPose { get; set; } = defaultLeftLeg;

    public VectorF RightLegPose { get; set; } = defaultRightLeg;

    /// <summary>What the stand wears or holds in <paramref name="slot"/>, or air.</summary>
    public ItemStack GetEquipment(EquipmentSlot slot)
    {
        lock (this.equipment)
        {
            return this.equipment.GetValueOrDefault(slot, ItemStack.Air);
        }
    }

    /// <summary>
    /// Puts <paramref name="item"/> in <paramref name="slot"/> and shows the players who see the stand, without the
    /// equip sound and game event a player's use gives.
    /// </summary>
    public void SetEquipment(EquipmentSlot slot, ItemStack item)
    {
        ArgumentNullException.ThrowIfNull(item);
        this.StoreEquipment(slot, item);
        this.BroadcastEquipment(slot);
    }

    // Stores without telling anyone, for a use to publish once its locks are released.
    private void StoreEquipment(EquipmentSlot slot, ItemStack item)
    {
        lock (this.equipment)
        {
            if (item is { IsAir: false, Count: > 0 })
                this.equipment[slot] = item;
            else
                this.equipment.Remove(slot);
        }
    }

    private void BroadcastEquipment(EquipmentSlot slot)
    {
        var packet = new SetEquipmentPacket
        {
            EntityId = this.EntityId,
            Equipment = [new Equipment { Slot = slot, Item = this.GetEquipment(slot) }]
        };

        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, packet, this.EntityId);
    }

    /// <summary>Sends what the stand wears and holds to a player starting to see it.</summary>
    internal ValueTask SendEquipmentToAsync(Player player)
    {
        List<Equipment> worn;
        lock (this.equipment)
        {
            worn = this.equipment.Select(pair => new Equipment { Slot = pair.Key, Item = pair.Value }).ToList();
        }

        if (worn.Count == 0)
            return default;

        return player.Client.QueuePacketAsync(new SetEquipmentPacket { EntityId = this.EntityId, Equipment = worn });
    }

    // Vanilla's ArmorStand data: its flags byte, then the six limbs' rotations.
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(LivingMetadataCount, EntityMetadataType.Byte);
        writer.WriteByte(this.ClientFlags());

        VectorF[] poses = [this.HeadPose, this.BodyPose, this.LeftArmPose, this.RightArmPose, this.LeftLegPose,
            this.RightLegPose];
        for (var index = 0; index < poses.Length; index++)
        {
            writer.WriteEntityMetadataType((byte)(LivingMetadataCount + 1 + index), EntityMetadataType.Rotations);
            writer.WriteSingle(poses[index].X);
            writer.WriteSingle(poses[index].Y);
            writer.WriteSingle(poses[index].Z);
        }
    }

    private byte ClientFlags()
    {
        var flags = 0;
        if (this.Small)
            flags |= 1;

        if (this.ShowArms)
            flags |= 4;

        if (this.NoBasePlate)
            flags |= 8;

        if (this.Marker)
            flags |= 16;

        return (byte)flags;
    }

    // Vanilla's ArmorStand.addAdditionalSaveData and its living entity's equipment.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        var worn = new NbtCompound("equipment");
        foreach (var (slot, name) in savedSlots)
        {
            if (this.GetEquipment(slot) is { IsAir: false } item)
                worn.Add(item.ToNbt(name));
        }

        tag.SetOrRemove("equipment", worn.Count > 0 ? worn : null);
        tag.Set(new NbtTag<bool>("Invisible", this.Invisible));
        tag.Set(new NbtTag<bool>("Small", this.Small));
        tag.Set(new NbtTag<bool>("ShowArms", this.ShowArms));
        tag.Set(new NbtTag<int>("DisabledSlots", this.DisabledSlots));
        tag.Set(new NbtTag<bool>("NoBasePlate", this.NoBasePlate));
        tag.SetFlag("Marker", this.Marker);
        tag.Set(this.SavePose());
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        lock (this.equipment)
        {
            this.equipment.Clear();
        }

        if (tag.TryGetTag<NbtCompound>("equipment", out var worn))
        {
            foreach (var (slot, name) in savedSlots)
            {
                if (worn.TryGetTag<NbtCompound>(name, out var saved) && saved.ItemFromNbt() is ItemStack item)
                    this.StoreEquipment(slot, item);
            }
        }

        this.Invisible = tag.TryGetBool("Invisible", out var invisible) && invisible;
        this.Small = tag.TryGetBool("Small", out var small) && small;
        this.ShowArms = tag.TryGetBool("ShowArms", out var arms) && arms;
        this.DisabledSlots = tag.TryGetNumber<int>("DisabledSlots", out var disabled) ? disabled : 0;
        this.NoBasePlate = tag.TryGetBool("NoBasePlate", out var noBasePlate) && noBasePlate;
        this.Marker = tag.TryGetBool("Marker", out var marker) && marker;
        this.LoadPose(tag.TryGetTag<NbtCompound>("Pose", out var pose) ? pose : new NbtCompound("Pose"));
    }

    // Each limb whose rotation isn't the default, as a list of three floats.
    private NbtCompound SavePose()
    {
        var pose = new NbtCompound("Pose");
        AddRotation(pose, "Head", this.HeadPose, defaultHead);
        AddRotation(pose, "Body", this.BodyPose, defaultBody);
        AddRotation(pose, "LeftArm", this.LeftArmPose, defaultLeftArm);
        AddRotation(pose, "RightArm", this.RightArmPose, defaultRightArm);
        AddRotation(pose, "LeftLeg", this.LeftLegPose, defaultLeftLeg);
        AddRotation(pose, "RightLeg", this.RightLegPose, defaultRightLeg);
        return pose;
    }

    private void LoadPose(NbtCompound pose)
    {
        this.HeadPose = ReadRotation(pose, "Head", defaultHead);
        this.BodyPose = ReadRotation(pose, "Body", defaultBody);
        this.LeftArmPose = ReadRotation(pose, "LeftArm", defaultLeftArm);
        this.RightArmPose = ReadRotation(pose, "RightArm", defaultRightArm);
        this.LeftLegPose = ReadRotation(pose, "LeftLeg", defaultLeftLeg);
        this.RightLegPose = ReadRotation(pose, "RightLeg", defaultRightLeg);
    }

    private static void AddRotation(NbtCompound pose, string name, VectorF rotation, VectorF fallback)
    {
        if (rotation == fallback)
            return;

        pose.Add(new NbtList(NbtTagType.Float, name)
        {
            new NbtTag<float>(string.Empty, rotation.X),
            new NbtTag<float>(string.Empty, rotation.Y),
            new NbtTag<float>(string.Empty, rotation.Z)
        });
    }

    private static VectorF ReadRotation(NbtCompound pose, string name, VectorF fallback)
    {
        if (!pose.TryGetTag<NbtList>(name, out var list) || list.Count != 3)
            return fallback;

        if (!list[0].TryGetNumber(out float x) || !list[1].TryGetNumber(out float y)
            || !list[2].TryGetNumber(out float z))
        {
            return fallback;
        }

        return new VectorF(x, y, z);
    }
}
