using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net;
using System.Buffers.Binary;
using System.Text.Json;

namespace Obsidian.Entities;

public partial class Mob
{
    private NbtCompound? originalSave;
    private static readonly HashSet<string> saveFields = ["id", "UUID", "Pos", "Motion", "Rotation", "Health", "AbsorptionAmount", "Air", "Fire", "OnGround",
        "NoAI", "LeftHanded", "CanPickUpLoot", "PersistenceRequired", "Silent", "NoGravity", "Glowing", "CustomNameVisible", "CustomName",
        "attributes", "ObsidianEquipment", "Age", "InLove", "IsBaby", "ObsidianVariant", "Type", "EggLayTime", "IsChickenJockey", "Color",
        "Sheared", "sheared", "Size", "powered", "ignited", "Fuse", "ExplosionRadius", "ObsidianEffects", "InWaterTime",
        "DrownedConversionTime", "StrayConversionTime", "ObsidianPowderSnowTicks", "ObsidianStewEffect", "CanBreakDoors", "ObsidianReinforcementChance",
        "Tame", "Temper", "Owner", "EatingHaystack", "Bred", "Variant", "PlayerCreated", "Trusting", "Sitting", "variant",
        "CollarColor", "LastPoseTick", "ObsidianDashCooldown"];
    internal void WriteSave(INbtWriter writer, bool writeCompound = true)
    {
        if (writeCompound)
            writer.WriteCompoundStart();
        writer.WriteString("id", EntityNbt.TypeId(Type));
        var uuidBytes = Uuid.ToByteArray(true);
        var uuid = new int[4];
        for (var index = 0; index < 4; index++)
            uuid[index] = BinaryPrimitives.ReadInt32BigEndian(uuidBytes.AsSpan(index * 4, 4));
        writer.WriteArray("UUID", uuid);
        WriteVector(writer, "Pos", Position);
        WriteVector(writer, "Motion", Motion);
        writer.WriteListStart("Rotation", NbtTagType.Float, 2);
        writer.WriteFloat(Yaw.Degrees);
        writer.WriteFloat(Pitch.Degrees);
        writer.EndList();
        writer.WriteFloat("Health", Health);
        writer.WriteFloat("AbsorptionAmount", AbsorbtionAmount);
        writer.WriteShort("Air", Air);
        writer.WriteShort("Fire", (short)Math.Clamp(FireTicks, 0, short.MaxValue));
        writer.WriteBool("OnGround", MovementFlags.HasFlag(MovementFlags.OnGround));
        writer.WriteBool("NoAI", MobBitMask.HasFlag(MobBitmask.NoAi));
        writer.WriteBool("LeftHanded", MobBitMask.HasFlag(MobBitmask.LeftHanded));
        writer.WriteBool("CanPickUpLoot", CanPickUpLoot);
        writer.WriteBool("PersistenceRequired", PersistenceRequired);
        writer.WriteBool("Silent", Silent);
        writer.WriteBool("NoGravity", NoGravity);
        writer.WriteBool("Glowing", Glowing);
        writer.WriteBool("CustomNameVisible", CustomNameVisible);
        if (CustomName != null)
        {
            writer.WriteCompoundStart("CustomName");
            writer.WriteChatMessage(CustomName);
            writer.EndCompound();
        }
        writer.WriteListStart("attributes", NbtTagType.Compound, Attributes.Count);
        foreach (var attribute in Attributes)
        {
            writer.WriteCompoundStart();
            writer.WriteString("id", attribute.Key);
            writer.WriteDouble("base", attribute.Value);
            writer.EndCompound();
        }
        writer.EndList();
        writer.WriteListStart("ObsidianEquipment", NbtTagType.Compound, equipment.Count);
        foreach (var (slot, item) in equipment)
        {
            writer.WriteCompoundStart();
            writer.WriteInt("slot", (int)slot);
            writer.WriteFloat("drop_chance", GetEquipmentDropChance(slot));
            writer.WriteString("id", item.Holder.UnlocalizedName);
            writer.WriteInt("count", item.Count);
            var buffer = new NetworkBuffer();
            buffer.WriteItemStack(item);
            writer.WriteArray("data", buffer.AsSpan(0, buffer.Size));
            writer.EndCompound();
        }
        writer.EndList();
        writer.WriteListStart("ObsidianEffects", NbtTagType.Compound, ActivePotionEffects.Count);
        foreach (var (id, effect) in ActivePotionEffects)
        {
            writer.WriteCompoundStart();
            writer.WriteInt("id", id);
            writer.WriteInt("duration", effect.CurrentDuration);
            writer.WriteInt("amplifier", effect.EffectData.Amplifier);
            writer.EndCompound();
        }
        writer.EndList();
        if (this is AgeableMob ageable)
            writer.WriteInt("Age", ageable.Age);
        if (this is Animal animal)
            writer.WriteInt("InLove", animal.LoveTicks);
        if (this is AbstractHorse horse)
        {
            writer.WriteBool("Tame", horse.HorseMask.HasFlag(HorseMask.Tamed));
            writer.WriteBool("Bred", horse.HorseMask.HasFlag(HorseMask.HasBred));
            writer.WriteBool("EatingHaystack", horse.HorseMask.HasFlag(HorseMask.Eating));
            writer.WriteInt("Temper", horse.Temper);
            WriteOwner(writer, horse.Owner);
            if (horse is Horse normalHorse)
                writer.WriteInt("Variant", normalHorse.Variant);
        }
        if (this is Ocelot ocelot)
            writer.WriteBool("Trusting", ocelot.Trusting);
        if (this is Cat cat)
        {
            WriteOwner(writer, cat.Owner);
            writer.WriteBool("Sitting", cat.OrderedToSit);
            writer.WriteString("variant", $"minecraft:{Cat.VariantNames[cat.Variant]}");
            writer.WriteString("CollarColor", Cat.DyeNames[cat.CollarColor]);
        }
        if (this is Camel camel)
        {
            writer.WriteLong("LastPoseTick", camel.LastPoseChangeTick);
            writer.WriteInt("ObsidianDashCooldown", camel.DashCooldown);
        }
        if (this is IronGolem golem)
            writer.WriteBool("PlayerCreated", golem.PlayerCreated);
        if (this is Zombie zombie)
        {
            writer.WriteBool("IsBaby", zombie.IsBaby);
            writer.WriteBool("CanBreakDoors", zombie.CanBreakDoors);
            writer.WriteFloat("ObsidianReinforcementChance", zombie.ReinforcementChance);
        }
        if (this is Husk husk)
        {
            writer.WriteInt("InWaterTime", husk.WaterTicks);
            writer.WriteInt("DrownedConversionTime", husk.ConversionTicks);
        }
        if (this is Skeleton { Type: EntityType.Skeleton } skeleton)
        {
            writer.WriteInt("StrayConversionTime", skeleton.StrayConversionTicks);
            writer.WriteInt("ObsidianPowderSnowTicks", skeleton.PowderSnowTicks);
        }
        if (this is Cow cow)
            writer.WriteInt("ObsidianVariant", cow.Variant);
        if (this is Mooshroom mooshroom)
        {
            writer.WriteString("Type", mooshroom.Brown ? "brown" : "red");
            if (mooshroom.StewEffect is { } effect)
                writer.WriteArray("ObsidianStewEffect", new[] { effect.EffectId, effect.Duration });
        }
        if (this is Pig pig)
            writer.WriteInt("ObsidianVariant", pig.Variant);
        if (this is Chicken chicken)
        {
            writer.WriteInt("ObsidianVariant", chicken.Variant);
            writer.WriteInt("EggLayTime", chicken.EggLayTime);
            writer.WriteBool("IsChickenJockey", chicken.IsChickenJockey);
        }
        if (this is Sheep sheep)
        {
            writer.WriteByte("Color", sheep.Color);
            writer.WriteBool("Sheared", sheep.Sheared);
        }
        if (this is Bogged bogged)
            writer.WriteBool("sheared", bogged.Sheared);
        if (this is Slime slime)
            writer.WriteInt("Size", slime.Size - 1);
        if (this is Creeper creeper)
        {
            writer.WriteBool("powered", creeper.Powered);
            writer.WriteBool("ignited", creeper.Ignited);
            writer.WriteShort("Fuse", (short)creeper.Fuse);
            writer.WriteByte("ExplosionRadius", (byte)creeper.ExplosionRadius);
        }
        if (originalSave != null)
            foreach (var tag in originalSave)
                if (!saveFields.Contains(tag.Key))
                    writer.WriteTag(tag.Value);
        if (writeCompound)
            writer.EndCompound();
    }

    internal void ReadSave(NbtCompound tag)
    {
        originalSave = tag;
        Position = ReadVector(tag, "Pos");
        LastPosition = Position;
        InitializeAi(false);
        if (tag.TryGetTag<NbtArray<int>>("UUID", out var uuid) && uuid.Count == 4)
        {
            Span<byte> bytes = stackalloc byte[16];
            for (var index = 0; index < 4; index++)
                BinaryPrimitives.WriteInt32BigEndian(bytes.Slice(index * 4, 4), uuid[index]);
            Uuid = new Guid(bytes, true);
        }
        Motion = ReadVector(tag, "Motion");
        if (tag.TryGetTag<NbtList>("Rotation", out var rotation) && rotation is [NbtTag<float> yaw, NbtTag<float> pitch, ..])
        {
            Yaw = yaw.Value;
            Pitch = pitch.Value;
        }
        if (tag.TryGetTagValue<float>("Health", out var health))
            Health = health;
        if (tag.TryGetTagValue<short>("Air", out var air))
            Air = air;
        if (tag.TryGetTagValue<short>("Fire", out var fire))
            FireTicks = Math.Max(0, (int)fire);
        if (tag.TryGetTag<NbtList>("ObsidianEffects", out var effects))
            foreach (var effect in effects.OfType<NbtCompound>())
                RestorePotionEffect(effect.GetInt("id"), effect.GetInt("duration"), effect.GetInt("amplifier"));
        MovementFlags = ReadFlag(tag, "OnGround") ? MovementFlags.OnGround : MovementFlags.None;
        MobBitMask = (ReadFlag(tag, "NoAI") ? MobBitmask.NoAi : MobBitmask.None) |
            (ReadFlag(tag, "LeftHanded") ? MobBitmask.LeftHanded : MobBitmask.None);
        CanPickUpLoot = ReadFlag(tag, "CanPickUpLoot");
        PersistenceRequired = ReadFlag(tag, "PersistenceRequired") || this is Villager or IronGolem;
        Silent = ReadFlag(tag, "Silent");
        NoGravity = ReadFlag(tag, "NoGravity");
        Glowing = ReadFlag(tag, "Glowing");
        CustomNameVisible = ReadFlag(tag, "CustomNameVisible");
        if (tag.TryGetTag("CustomName", out var name))
            CustomName = name is NbtTag<string> { Value: { } legacy } && legacy.TrimStart().StartsWith('{')
                ? JsonSerializer.Deserialize<ChatMessage>(legacy) : name.TextFromNbt();
        if (tag.TryGetTag<NbtList>("attributes", out var attributes))
            foreach (var attribute in attributes.OfType<NbtCompound>())
                if (attribute.TryGetTagValue<string>("id", out var id) && attribute.TryGetTagValue<double>("base", out var value))
                    Attributes[id] = (float)value;
        if (tag.TryGetTag<NbtList>("ObsidianEquipment", out var savedEquipment))
            foreach (var entry in savedEquipment.OfType<NbtCompound>())
            {
                var slot = (EquipmentSlot)entry.GetInt("slot");
                if (!Enum.IsDefined(slot))
                    continue;
                var item = entry.TryGetTag<NbtArray<byte>>("data", out var data)
                    ? new NetworkBuffer(data.GetArray()).ReadItemStack() : null;
                if (item != null)
                    equipment[slot] = item;
                equipmentDropChances[slot] = entry.GetFloat("drop_chance");
            }
        if (this is AgeableMob ageable && tag.TryGetTagValue<int>("Age", out var age))
            ageable.Age = age;
        if (this is Animal animal && tag.TryGetTagValue<int>("InLove", out var love))
            animal.LoveTicks = love;
        if (this is AbstractHorse horse)
        {
            horse.HorseMask = (ReadFlag(tag, "Tame") ? HorseMask.Tamed : HorseMask.None) |
                (ReadFlag(tag, "Bred") ? HorseMask.HasBred : HorseMask.None);
            if (tag.TryGetTagValue<int>("Temper", out var temper)) horse.Temper = Math.Clamp(temper, 0, 100);
            horse.Owner = ReadOwner(tag);
            if (horse is Horse normalHorse && tag.TryGetTagValue<int>("Variant", out var horseVariant))
                normalHorse.Variant = horseVariant;
        }
        if (this is Ocelot ocelot)
        {
            ocelot.Trusting = ReadFlag(tag, "Trusting");
            PersistenceRequired |= ocelot.Trusting;
        }
        if (this is Cat cat)
        {
            cat.Owner = ReadOwner(tag);
            PersistenceRequired |= cat.Tamed;
            cat.OrderedToSit = cat.Tamed && ReadFlag(tag, "Sitting");
            cat.IsSitting = cat.OrderedToSit;
            if (tag.TryGetTagValue<string>("variant", out var catVariant))
            {
                var index = Array.IndexOf(Cat.VariantNames, catVariant.StartsWith("minecraft:", StringComparison.Ordinal) ? catVariant[10..] : catVariant);
                if (index >= 0) cat.Variant = index;
            }
            if (tag.TryGetTagValue<string>("CollarColor", out var collar))
            {
                var index = Array.IndexOf(Cat.DyeNames, collar);
                if (index >= 0) cat.CollarColor = (byte)index;
            }
            else if (tag.TryGetTagValue<byte>("CollarColor", out var legacyCollar))
                cat.CollarColor = (byte)(legacyCollar & 15);
        }
        if (this is Camel camel)
        {
            camel.HorseMask |= HorseMask.Tamed;
            if (tag.TryGetTagValue<long>("LastPoseTick", out var poseTick)) camel.LastPoseChangeTick = poseTick;
            camel.Pose = camel.IsSitting ? Pose.Sitting : Pose.Standing;
            if (tag.TryGetTagValue<int>("ObsidianDashCooldown", out var cooldown))
                camel.DashCooldown = Math.Clamp(cooldown, 0, Camel.DashCooldownTicks);
        }
        if (this is IronGolem golem)
            golem.PlayerCreated = ReadFlag(tag, "PlayerCreated");
        if (this is Zombie zombie)
        {
            zombie.IsBaby = ReadFlag(tag, "IsBaby");
            zombie.CanBreakDoors = ReadFlag(tag, "CanBreakDoors");
            if (tag.TryGetTagValue<float>("ObsidianReinforcementChance", out var chance)) zombie.ReinforcementChance = chance;
        }
        if (this is Husk husk)
        {
            if (tag.TryGetTagValue<int>("InWaterTime", out var water)) husk.WaterTicks = water;
            if (tag.TryGetTagValue<int>("DrownedConversionTime", out var conversion)) husk.ConversionTicks = conversion;
        }
        if (this is Skeleton { Type: EntityType.Skeleton } skeleton)
        {
            if (tag.TryGetTagValue<int>("StrayConversionTime", out var conversion)) skeleton.StrayConversionTicks = conversion;
            if (tag.TryGetTagValue<int>("ObsidianPowderSnowTicks", out var snow)) skeleton.PowderSnowTicks = snow;
        }
        if (tag.TryGetTagValue<int>("ObsidianVariant", out var variant))
        {
            if (this is Cow cow) cow.Variant = variant;
            if (this is Pig pig) pig.Variant = variant;
            if (this is Chicken chicken) chicken.Variant = variant;
        }
        if (this is Mooshroom mooshroom)
        {
            mooshroom.Brown = tag.TryGetTagValue<string>("Type", out var mushroomType) && mushroomType == "brown";
            if (tag.TryGetTag<NbtArray<int>>("ObsidianStewEffect", out var stew) && stew.Count == 2)
                mooshroom.StewEffect = new() { EffectId = stew[0], Duration = stew[1] };
        }
        if (this is Chicken bird)
        {
            if (tag.TryGetTagValue<int>("EggLayTime", out var eggTime)) bird.EggLayTime = eggTime;
            bird.IsChickenJockey = ReadFlag(tag, "IsChickenJockey");
        }
        if (this is Sheep sheep)
        {
            if (tag.TryGetTagValue<byte>("Color", out var color)) sheep.Color = (byte)(color & 15);
            sheep.Sheared = ReadFlag(tag, "Sheared");
        }
        if (this is Bogged bogged) bogged.Sheared = ReadFlag(tag, "sheared");
        if (this is Slime slime && tag.TryGetTagValue<int>("Size", out var size)) slime.Size = size + 1;
        if (this is Creeper creeper)
        {
            creeper.Powered = ReadFlag(tag, "powered");
            creeper.Ignited = ReadFlag(tag, "ignited");
            if (tag.TryGetTagValue<short>("Fuse", out var fuse)) creeper.Fuse = Math.Max(1, (int)fuse);
            if (tag.TryGetTagValue<byte>("ExplosionRadius", out var radius)) creeper.ExplosionRadius = Math.Max(1, (int)radius);
        }
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }

    private static bool ReadFlag(NbtCompound tag, string name) => tag.TryGetBool(name, out var value) && value;

    private static void WriteOwner(INbtWriter writer, Guid owner)
    {
        if (owner == Guid.Empty)
            return;
        var bytes = owner.ToByteArray(true);
        var value = new int[4];
        for (var index = 0; index < value.Length; index++)
            value[index] = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)));
        writer.WriteArray("Owner", value);
    }

    private static Guid ReadOwner(NbtCompound tag)
    {
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
        {
            Span<byte> bytes = stackalloc byte[16];
            for (var index = 0; index < owner.Count; index++)
                BinaryPrimitives.WriteInt32BigEndian(bytes.Slice(index * sizeof(int), sizeof(int)), owner[index]);
            return new Guid(bytes, true);
        }
        return tag.TryGetTagValue<string>("Owner", out var text) && Guid.TryParse(text, out var uuid) ? uuid : Guid.Empty;
    }

    internal static void WriteVector(INbtWriter writer, string name, VectorF value)
    {
        writer.WriteListStart(name, NbtTagType.Double, 3);
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Y);
        writer.WriteDouble(value.Z);
        writer.EndList();
    }

    internal static VectorF ReadVector(NbtCompound tag, string name) => tag.TryGetTag<NbtList>(name, out var list) &&
        list is [NbtTag<double> x, NbtTag<double> y, NbtTag<double> z, ..]
        ? new VectorF((float)x.Value, (float)y.Value, (float)z.Value) : VectorF.Zero;
}
