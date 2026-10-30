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

    protected virtual void WriteAdditionalSave(INbtWriter writer) { }

    protected virtual void ReadAdditionalSave(NbtCompound tag) { }

    private static readonly HashSet<string> saveFields =
    [
        "id", "UUID", "Pos", "Motion", "Rotation", "Health", "AbsorptionAmount", "Air", "Fire", "OnGround",
        "TicksFrozen", "FallDistance", "NoAI", "LeftHanded", "CanPickUpLoot", "PersistenceRequired", "Silent",
        "NoGravity", "Glowing", "CustomNameVisible", "CustomName", "attributes", "ObsidianEquipment", "Age",
        "InLove", "IsBaby", "ObsidianVariant", "Type", "EggLayTime", "IsChickenJockey", "Color", "Sheared",
        "sheared", "Size", "powered", "ignited", "Fuse", "ExplosionRadius", "ObsidianEffects", "InWaterTime",
        "DrownedConversionTime", "StrayConversionTime", "ObsidianPowderSnowTicks", "ObsidianStewEffect",
        "CanBreakDoors", "ObsidianReinforcementChance", "Tame", "Temper", "Owner", "EatingHaystack", "Bred",
        "Variant", "PlayerCreated", "Trusting", "Sitting", "variant", "CollarColor", "LastPoseTick",
        "ObsidianDashCooldown", "AngerTime", "Pumpkin", "ObsidianCarriedBlock", "sound_variant", "AngryAt",
        "carriedBlockState", "BatFlags", "DarkTicksRemaining", "FromBucket", "ObsidianPregnant",
        "ObsidianPlayDead", "ObsidianHuntingCooldown", "ObsidianDryTicks", "HasNectar", "HasStung",
        "ObsidianStingTicks", "ObsidianHivePos", "ObsidianFlowerPos", "GotFish", "Moistness",
        "ObsidianTreasurePos", "Sleeping", "ObsidianTrusted", "ObsidianSecondTrusted", "ObsidianBreedingPlayer",
        "IsScreamingGoat", "HasLeftHorn", "HasRightHorn", "ObsidianRamCooldown", "ObsidianLongJumpCooldown",
        "MainGene", "HiddenGene", "ObsidianEatingTicks", "SkeletonTrap", "SkeletonTrapTime",
        "ObsidianSkeletonRider", "ChestedHorse", "Strength", "Lifetime", "Johnny", "type", "PuffState",
        "IsImmuneToZombification", "CannotBeHunted", "TimeInOverworld", "RabbitType", "life_ticks", "bound_pos",
        "owner", "AttackTick", "StunTick", "RoarTick", "SpellTicks", "size", "anchor_pos", "ObsidianDrinkTicks",
        "ObsidianDrinkEffect", "ObsidianAdmireTicks", "AttachFace", "Peek", "DuplicationCooldown",
        "ObsidianLikedPlayer", "ObsidianCollected", "state", "scute_time", "ObsidianDigCooldown",
        "ObsidianExplored", "Suffocating", "weather_state", "next_weather_age", "home_pos", "has_egg", "Invul",
        "DragonPhase", "DragonDeathTime", "ObsidianPreviouslyKilled", "ObsidianDragonArena",
        "ObsidianWardenIdle", "ObsidianWardenPoseTicks", "ObsidianWardenPose", "ObsidianWardenSonicCooldown",
        "ObsidianWardenSonicTicks", "ObsidianWardenAnger", "ObsidianWardenSniffCooldown",
        "ObsidianWardenVibrationCooldown", "ObsidianWardenMeleeCooldown", "ObsidianWardenTouchCooldown",
        "ObsidianPhaseTicks", "ObsidianFlameCount", "ObsidianFlightPoint", "ObsidianSittingDamage",
        "ObsidianDestination", "DespawnDelay", "ObsidianTrader", "ConversionTime", "ConversionPlayer", "Xp",
        "WanderTarget", "ObsidianWitherHeadTimers", "ObsidianWitherIdleShots", "ObsidianWitherBreakTicks",
        "VillagerData", "Offers", "ObsidianTradingLevel", "RestocksToday", "LastRestock", "ObsidianRestockDay",
        "ObsidianHome", "ObsidianJobSite", "ObsidianOffers", "ObsidianNoteBlockTicks", "ObsidianThrowCooldown",
        "ObsidianNoteBlock", "still_timeout", "ObsidianBoostTicks", "ObsidianBoostDuration",
        "ObsidianWardenProjectileMemory"
    ];

    internal void WriteSave(INbtWriter writer, bool writeCompound = true)
    {
        if (writeCompound)
            writer.WriteCompoundStart();

        this.WriteEntitySave(writer);
        this.WriteMobFlagsSave(writer);
        writer.WriteTag(this.SaveAttributes());
        this.WriteEquipmentSave(writer);
        this.WriteEffectsSave(writer);
        this.WriteSpeciesSave(writer);

        var additionalTags = this.WriteAdditionalTags(writer);

        // The live attachments, not those it was loaded or spawned with, which the copy below would bring back.
        writer.WriteTag(API.Data.AttachmentStorage.For(this).Snapshot());
        this.WriteUnknownOriginalTags(writer, additionalTags);

        if (writeCompound)
            writer.EndCompound();
    }

    private void WriteEntitySave(INbtWriter writer)
    {
        writer.WriteString("id", EntityNbt.TypeId(this));
        WriteReferenceArray(writer, "UUID", Uuid);
        WriteVector(writer, "Pos", Position);
        WriteVector(writer, "Motion", Motion);

        writer.WriteListStart("Rotation", NbtTagType.Float, 2);
        writer.WriteFloat(Yaw.Degrees);
        writer.WriteFloat(Pitch.Degrees);
        writer.EndList();

        writer.WriteFloat("Health", Health);
        writer.WriteFloat("AbsorptionAmount", AbsorptionHealth);
        writer.WriteShort("Air", Air);
        writer.WriteShort("Fire", (short)Math.Clamp(FireTicks, 0, short.MaxValue));
        writer.WriteBool("OnGround", MovementFlags.HasFlag(MovementFlags.OnGround));
        writer.WriteInt("TicksFrozen", PowderedSnowTicks);
        writer.WriteFloat("FallDistance", fallDistance);
    }

    private void WriteMobFlagsSave(INbtWriter writer)
    {
        writer.WriteBool("NoAI", MobBitMask.HasFlag(MobBitmask.NoAi));
        writer.WriteBool("LeftHanded", MobBitMask.HasFlag(MobBitmask.LeftHanded));
        writer.WriteBool("CanPickUpLoot", CanPickUpLoot);
        writer.WriteBool("PersistenceRequired", PersistenceRequired);
        writer.WriteBool("Silent", Silent);
        writer.WriteBool("NoGravity", NoGravity);
        writer.WriteBool("Glowing", Glowing);
        writer.WriteBool("CustomNameVisible", CustomNameVisible);
        if (CustomName == null)
            return;

        writer.WriteCompoundStart("CustomName");
        writer.WriteChatMessage(CustomName);
        writer.EndCompound();
    }

    private void WriteEquipmentSave(INbtWriter writer)
    {
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
    }

    private void WriteEffectsSave(INbtWriter writer)
    {
        writer.WriteListStart("ObsidianEffects", NbtTagType.Compound, ActivePotionEffects.Count);
        foreach (var (id, effect) in ActivePotionEffects)
        {
            var saved = effect.EffectData with { Id = id, Duration = effect.CurrentDuration };
            writer.WriteCompoundStart();
            WritePotionEffect(writer, saved);
            writer.EndCompound();
        }

        writer.EndList();
    }

    // One check per species, in the order their tags are written; a subclass matches its base's checks too.
    private void WriteSpeciesSave(INbtWriter writer)
    {
        if (this is AgeableMob ageable)
            writer.WriteInt("Age", ageable.Age);
        if (this is Animal animal)
            writer.WriteInt("InLove", animal.LoveTicks);
        if (this is AbstractHorse horse)
            WriteHorseSave(writer, horse);
        if (this is Wolf wolf)
            WriteWolfSave(writer, wolf);
        if (this is Enderman enderman)
            WriteEndermanSave(writer, enderman);
        if (this is SnowGolem snowGolem)
            writer.WriteBool("Pumpkin", snowGolem.Pumpkin);
        if (this is Ocelot ocelot)
            writer.WriteBool("Trusting", ocelot.Trusting);
        if (this is Cat cat)
            WriteCatSave(writer, cat);
        if (this is Camel camel)
            WriteCamelSave(writer, camel);
        if (this is IronGolem golem)
            writer.WriteBool("PlayerCreated", golem.PlayerCreated);

        if (this is Zombie zombie)
            WriteZombieSave(writer, zombie);
        if (this is ZombifiedPiglin piglin)
            WriteAngerSave(writer, piglin.AngerTicks, piglin.AngryAt);
        if (this is PolarBear bear)
            WriteAngerSave(writer, bear.AngerTicks, bear.AngryAt);
        if (this is Skeleton { Type: EntityType.Skeleton } skeleton)
            WriteSkeletonSave(writer, skeleton);

        if (this is Cow cow)
            writer.WriteInt("ObsidianVariant", cow.Variant);
        if (this is Mooshroom mooshroom)
            WriteMooshroomSave(writer, mooshroom);
        if (this is Pig pig)
            writer.WriteInt("ObsidianVariant", pig.Variant);
        if (this is Chicken chicken)
            WriteChickenSave(writer, chicken);
        if (this is Sheep sheep)
            WriteSheepSave(writer, sheep);
        if (this is Bogged bogged)
            writer.WriteBool("sheared", bogged.Sheared);
        if (this is Slime slime)
            writer.WriteInt("Size", slime.Size - 1);
        if (this is Creeper creeper)
            WriteCreeperSave(writer, creeper);
    }

    private static void WriteHorseSave(INbtWriter writer, AbstractHorse horse)
    {
        writer.WriteBool("Tame", horse.HorseMask.HasFlag(HorseMask.Tamed));
        writer.WriteBool("Bred", horse.HorseMask.HasFlag(HorseMask.HasBred));
        writer.WriteBool("EatingHaystack", horse.HorseMask.HasFlag(HorseMask.Eating));
        writer.WriteInt("Temper", horse.Temper);
        WriteOwner(writer, horse.Owner);
        if (horse is Horse normalHorse)
            writer.WriteInt("Variant", normalHorse.Variant);
    }

    private static void WriteWolfSave(INbtWriter writer, Wolf wolf)
    {
        WriteOwner(writer, wolf.Owner);
        writer.WriteBool("Sitting", wolf.OrderedToSit);
        writer.WriteString("variant", $"minecraft:{Wolf.VariantNames[wolf.Variant]}");
        writer.WriteString("sound_variant", $"minecraft:{Wolf.SoundVariantNames[wolf.SoundVariant]}");
        writer.WriteString("CollarColor", Cat.DyeNames[wolf.CollarColor]);
        WriteAngerSave(writer, (int)wolf.AngerTicks, wolf.AngryAt);
    }

    private static void WriteEndermanSave(INbtWriter writer, Enderman enderman)
    {
        WriteAngerSave(writer, (int)enderman.AngerTicks, enderman.AngryAt);
        if (enderman.CarriedBlock is IBlock carried)
            writer.WriteTag(Obsidian.WorldData.BlockStateNbt.Write(carried, "carriedBlockState"));
    }

    private static void WriteCatSave(INbtWriter writer, Cat cat)
    {
        WriteOwner(writer, cat.Owner);
        writer.WriteBool("Sitting", cat.OrderedToSit);
        writer.WriteString("variant", $"minecraft:{Cat.VariantNames[cat.Variant]}");
        writer.WriteString("CollarColor", Cat.DyeNames[cat.CollarColor]);
    }

    private static void WriteCamelSave(INbtWriter writer, Camel camel)
    {
        writer.WriteLong("LastPoseTick", camel.LastPoseChangeTick);
        writer.WriteInt("ObsidianDashCooldown", camel.DashCooldown);
    }

    private static void WriteZombieSave(INbtWriter writer, Zombie zombie)
    {
        writer.WriteBool("IsBaby", zombie.IsBaby);
        writer.WriteBool("CanBreakDoors", zombie.CanBreakDoors);
        writer.WriteFloat("ObsidianReinforcementChance", zombie.ReinforcementChance);

        if (zombie is Husk husk)
        {
            writer.WriteInt("InWaterTime", husk.WaterTicks);
            writer.WriteInt("DrownedConversionTime", husk.ConversionTicks);
        }
        else if (zombie.Type == EntityType.Zombie)
        {
            writer.WriteInt("InWaterTime", zombie.DrowningWaterTicks);
            writer.WriteInt("DrownedConversionTime", zombie.DrowningConversionTicks);
        }
    }

    private static void WriteAngerSave(INbtWriter writer, int angerTicks, Guid angryAt)
    {
        writer.WriteInt("AngerTime", angerTicks);
        WriteReference(writer, "AngryAt", angryAt);
    }

    private static void WriteSkeletonSave(INbtWriter writer, Skeleton skeleton)
    {
        writer.WriteInt("StrayConversionTime", skeleton.StrayConversionTicks);
        writer.WriteInt("ObsidianPowderSnowTicks", skeleton.PowderSnowTicks);
    }

    private static void WriteMooshroomSave(INbtWriter writer, Mooshroom mooshroom)
    {
        writer.WriteString("Type", mooshroom.Brown ? "brown" : "red");
        if (mooshroom.StewEffect is { } effect)
            writer.WriteArray("ObsidianStewEffect", new[] { effect.EffectId, effect.Duration });
    }

    private static void WriteChickenSave(INbtWriter writer, Chicken chicken)
    {
        writer.WriteInt("ObsidianVariant", chicken.Variant);
        writer.WriteInt("EggLayTime", chicken.EggLayTime);
        writer.WriteBool("IsChickenJockey", chicken.IsChickenJockey);
    }

    private static void WriteSheepSave(INbtWriter writer, Sheep sheep)
    {
        writer.WriteByte("Color", sheep.Color);
        writer.WriteBool("Sheared", sheep.Sheared);
    }

    private static void WriteCreeperSave(INbtWriter writer, Creeper creeper)
    {
        writer.WriteBool("powered", creeper.Powered);
        writer.WriteBool("ignited", creeper.Ignited);
        writer.WriteShort("Fuse", (short)creeper.Fuse);
        writer.WriteByte("ExplosionRadius", (byte)creeper.ExplosionRadius);
    }

    // Writes the subclass's own tags and returns them, so the unknown-tag copy can skip them.
    private NbtCompound WriteAdditionalTags(INbtWriter writer)
    {
        using var additionalWriter = new RawNbtWriter(string.Empty);
        WriteAdditionalSave(additionalWriter);
        additionalWriter.EndCompound();

        var additionalTags = (NbtCompound)NbtReader.ReadTag(additionalWriter.Data, true, out _)!;
        writer.WriteEntries(additionalTags);
        return additionalTags;
    }

    // Tags it was loaded with that nothing here understands, kept so a round trip doesn't lose them.
    private void WriteUnknownOriginalTags(INbtWriter writer, NbtCompound additionalTags)
    {
        if (originalSave == null)
            return;

        foreach (var tag in originalSave)
        {
            var isKnown = saveFields.Contains(tag.Key)
                || tag.Key == API.Data.AttachmentStorage.NbtKey
                || additionalTags.HasTag(tag.Key);
            if (!isKnown)
                writer.WriteTag(tag.Value);
        }
    }

    internal void ReadSave(NbtCompound tag)
    {
        originalSave = tag;
        API.Data.AttachmentStorage.For(this).Load(tag);
        Position = ReadVector(tag, "Pos");
        LastPosition = Position;
        InitializeAi(false);

        this.ReadEntitySave(tag);
        this.ReadMobFlagsSave(tag);
        this.LoadAttributes(tag);
        this.ReadEquipmentSave(tag);
        this.ReadSpeciesSave(tag);

        ReadAdditionalSave(tag);
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }

    private void ReadEntitySave(NbtCompound tag)
    {
        if (tag.TryGetTag<NbtArray<int>>("UUID", out var uuid) && uuid.Count == 4)
            Uuid = ReadReferenceArray(uuid);

        Motion = ReadVector(tag, "Motion");
        if (tag.TryGetTag<NbtList>("Rotation", out var rotation)
            && rotation is [NbtTag<float> yaw, NbtTag<float> pitch, ..])
        {
            Yaw = yaw.Value;
            Pitch = pitch.Value;
        }

        if (tag.TryGetNumber<float>("Health", out var health))
            Health = health;
        if (tag.TryGetNumber<float>("AbsorptionAmount", out var absorption))
            AbsorptionHealth = float.IsFinite(absorption) ? Math.Max(0, absorption) : 0;
        if (tag.TryGetNumber<short>("Air", out var air))
            Air = air;
        if (tag.TryGetNumber<short>("Fire", out var fire))
            FireTicks = Math.Max(0, (int)fire);

        if (tag.TryGetTag<NbtList>("ObsidianEffects", out var effects))
        {
            foreach (var effect in effects.OfType<NbtCompound>())
                RestorePotionEffect(effect);
        }

        MovementFlags = ReadFlag(tag, "OnGround") ? MovementFlags.OnGround : MovementFlags.None;
        PowderedSnowTicks = tag.TryGetNumber<int>("TicksFrozen", out var frozen) ? Math.Clamp(frozen, 0, 140) : 0;
        if (tag.TryGetNumber<float>("FallDistance", out var fallen) && float.IsFinite(fallen))
            fallDistance = Math.Max(0, fallen);
    }

    private void ReadMobFlagsSave(NbtCompound tag)
    {
        var noAi = ReadFlag(tag, "NoAI") ? MobBitmask.NoAi : MobBitmask.None;
        var leftHanded = ReadFlag(tag, "LeftHanded") ? MobBitmask.LeftHanded : MobBitmask.None;
        MobBitMask = noAi | leftHanded;
        CanPickUpLoot = ReadFlag(tag, "CanPickUpLoot");
        PersistenceRequired = ReadFlag(tag, "PersistenceRequired") || this is Villager or IronGolem;
        Silent = ReadFlag(tag, "Silent");
        NoGravity = ReadFlag(tag, "NoGravity");
        Glowing = ReadFlag(tag, "Glowing");
        CustomNameVisible = ReadFlag(tag, "CustomNameVisible");
        if (tag.TryGetTag("CustomName", out var name))
            CustomName = ReadCustomName(name);
    }

    private static ChatMessage? ReadCustomName(INbtTag name)
    {
        // Older saves held the name as a JSON string.
        if (name is NbtTag<string> { Value: { } legacy } && legacy.TrimStart().StartsWith('{'))
            return JsonSerializer.Deserialize<ChatMessage>(legacy);

        return name.TextFromNbt();
    }

    private void ReadEquipmentSave(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtList>("ObsidianEquipment", out var savedEquipment))
            return;

        foreach (var entry in savedEquipment.OfType<NbtCompound>())
        {
            var slot = (EquipmentSlot)entry.GetInt("slot");
            if (!Enum.IsDefined(slot))
                continue;

            var item = entry.TryGetTag<NbtArray<byte>>("data", out var data)
                ? new NetworkBuffer(data.GetArray()).ReadItemStack()
                : null;
            if (item != null)
                equipment[slot] = item;

            equipmentDropChances[slot] = entry.GetFloat("drop_chance");
        }
    }

    // One check per species, in the order they apply; a subclass matches its base's checks too.
    private void ReadSpeciesSave(NbtCompound tag)
    {
        if (this is AgeableMob ageable && tag.TryGetNumber<int>("Age", out var age))
            ageable.Age = age;
        if (this is Animal animal && tag.TryGetNumber<int>("InLove", out var love))
            animal.LoveTicks = love;
        if (this is AbstractHorse horse)
            ReadHorseSave(tag, horse);
        if (this is Wolf wolf)
            this.ReadWolfSave(tag, wolf);
        if (this is Enderman enderman)
            ReadEndermanSave(tag, enderman);
        if (this is SnowGolem snowGolem)
            this.ReadSnowGolemSave(tag, snowGolem);
        if (this is Ocelot ocelot)
            this.ReadOcelotSave(tag, ocelot);
        if (this is Cat cat)
            this.ReadCatSave(tag, cat);
        if (this is Camel camel)
            ReadCamelSave(tag, camel);
        if (this is IronGolem golem)
            golem.PlayerCreated = ReadFlag(tag, "PlayerCreated");

        if (this is Zombie zombie)
            ReadZombieSave(tag, zombie);
        if (this is ZombifiedPiglin piglin)
        {
            piglin.AngryAt = ReadReference(tag, "AngryAt");
            piglin.AngerTicks = ReadAngerTime(tag);
        }
        if (this is PolarBear bear)
        {
            bear.AngryAt = ReadReference(tag, "AngryAt");
            bear.AngerTicks = ReadAngerTime(tag);
        }
        if (this is Skeleton { Type: EntityType.Skeleton } skeleton)
            ReadSkeletonSave(tag, skeleton);

        if (tag.TryGetNumber<int>("ObsidianVariant", out var variant))
            this.ReadFarmAnimalVariant(variant);
        if (this is Mooshroom mooshroom)
            ReadMooshroomSave(tag, mooshroom);
        if (this is Chicken bird)
            ReadChickenSave(tag, bird);
        if (this is Sheep sheep)
            ReadSheepSave(tag, sheep);
        if (this is Bogged bogged)
            bogged.Sheared = ReadFlag(tag, "sheared");
        if (this is Slime slime && tag.TryGetNumber<int>("Size", out var size))
            slime.Size = size + 1;
        if (this is Creeper creeper)
            ReadCreeperSave(tag, creeper);
    }

    private static void ReadHorseSave(NbtCompound tag, AbstractHorse horse)
    {
        var tamed = ReadFlag(tag, "Tame") ? HorseMask.Tamed : HorseMask.None;
        var bred = ReadFlag(tag, "Bred") ? HorseMask.HasBred : HorseMask.None;
        horse.HorseMask = tamed | bred;
        if (tag.TryGetNumber<int>("Temper", out var temper))
            horse.Temper = Math.Clamp(temper, 0, 100);

        horse.Owner = ReadOwner(tag);
        if (horse is Horse normalHorse && tag.TryGetNumber<int>("Variant", out var horseVariant))
            normalHorse.Variant = horseVariant;
    }

    private void ReadWolfSave(NbtCompound tag, Wolf wolf)
    {
        wolf.Owner = ReadOwner(tag);
        wolf.AngryAt = ReadReference(tag, "AngryAt");
        PersistenceRequired |= wolf.Tamed;
        wolf.OrderedToSit = wolf.Tamed && ReadFlag(tag, "Sitting");
        wolf.IsSitting = wolf.OrderedToSit;
        if (tag.TryGetNumber<int>("AngerTime", out var anger))
            wolf.AngerTicks = Math.Max(0, anger);

        if (tag.TryGetTagValue<string>("variant", out var wolfVariant))
        {
            var index = Array.IndexOf(Wolf.VariantNames, wolfVariant.Replace("minecraft:", ""));
            if (index >= 0)
                wolf.Variant = index;
        }

        if (tag.TryGetTagValue<string>("sound_variant", out var soundVariant))
        {
            var index = Array.IndexOf(Wolf.SoundVariantNames, soundVariant.Replace("minecraft:", ""));
            if (index >= 0)
                wolf.SoundVariant = index;
        }

        if (TryReadCollarColor(tag, out var collar))
            wolf.CollarColor = collar;
    }

    private static void ReadEndermanSave(NbtCompound tag, Enderman enderman)
    {
        enderman.AngryAt = ReadReference(tag, "AngryAt");
        if (tag.TryGetNumber<int>("AngerTime", out var anger))
            enderman.AngerTicks = Math.Max(0, anger);

        if (tag.TryGetTag("carriedBlockState", out var state))
            enderman.CarriedBlock = Obsidian.WorldData.BlockStateNbt.Read(state);
        else if (tag.TryGetTagValue<string>("ObsidianCarriedBlock", out var carried))
            enderman.CarriedBlock = Obsidian.WorldData.Structures.BlockStateParser.TryParse(carried);
    }

    private void ReadSnowGolemSave(NbtCompound tag, SnowGolem snowGolem)
    {
        if (tag.HasTag("Pumpkin"))
            snowGolem.Pumpkin = ReadFlag(tag, "Pumpkin");

        PersistenceRequired = true;
    }

    private void ReadOcelotSave(NbtCompound tag, Ocelot ocelot)
    {
        ocelot.Trusting = ReadFlag(tag, "Trusting");
        PersistenceRequired |= ocelot.Trusting;
    }

    private void ReadCatSave(NbtCompound tag, Cat cat)
    {
        cat.Owner = ReadOwner(tag);
        PersistenceRequired |= cat.Tamed;
        cat.OrderedToSit = cat.Tamed && ReadFlag(tag, "Sitting");
        cat.IsSitting = cat.OrderedToSit;
        if (tag.TryGetTagValue<string>("variant", out var catVariant))
        {
            var path = catVariant.StartsWith("minecraft:", StringComparison.Ordinal) ? catVariant[10..] : catVariant;
            var index = Array.IndexOf(Cat.VariantNames, path);
            if (index >= 0)
                cat.Variant = index;
        }

        if (TryReadCollarColor(tag, out var collar))
            cat.CollarColor = collar;
    }

    // A dye name, or the legacy byte. An unknown name leaves the collar as it is.
    private static bool TryReadCollarColor(NbtCompound tag, out byte color)
    {
        color = 0;
        if (tag.TryGetTagValue<string>("CollarColor", out var name))
        {
            var index = Array.IndexOf(Cat.DyeNames, name);
            if (index < 0)
                return false;

            color = (byte)index;
            return true;
        }

        if (!tag.TryGetNumber<byte>("CollarColor", out var legacyColor))
            return false;

        color = (byte)(legacyColor & 15);
        return true;
    }

    private static void ReadCamelSave(NbtCompound tag, Camel camel)
    {
        camel.HorseMask |= HorseMask.Tamed;
        if (tag.TryGetNumber<long>("LastPoseTick", out var poseTick))
            camel.LastPoseChangeTick = poseTick;

        camel.Pose = camel.IsSitting ? Pose.Sitting : Pose.Standing;
        if (tag.TryGetNumber<int>("ObsidianDashCooldown", out var cooldown))
            camel.DashCooldown = Math.Clamp(cooldown, 0, Camel.DashCooldownTicks);
    }

    private static void ReadZombieSave(NbtCompound tag, Zombie zombie)
    {
        zombie.IsBaby = ReadFlag(tag, "IsBaby");
        zombie.CanBreakDoors = ReadFlag(tag, "CanBreakDoors");
        if (tag.TryGetNumber<float>("ObsidianReinforcementChance", out var chance))
            zombie.ReinforcementChance = chance;

        if (zombie is Husk husk)
        {
            if (tag.TryGetNumber<int>("InWaterTime", out var water))
                husk.WaterTicks = water;
            if (tag.TryGetNumber<int>("DrownedConversionTime", out var conversion))
                husk.ConversionTicks = conversion;
        }
        else if (zombie.Type == EntityType.Zombie)
        {
            if (tag.TryGetNumber<int>("InWaterTime", out var water))
                zombie.DrowningWaterTicks = water;
            if (tag.TryGetNumber<int>("DrownedConversionTime", out var conversion))
                zombie.DrowningConversionTicks = conversion;
        }
    }

    private static int ReadAngerTime(NbtCompound tag)
    {
        var anger = tag.TryGetNumber<int>("AngerTime", out var saved) ? saved : 0;
        return Math.Max(0, anger);
    }

    private static void ReadSkeletonSave(NbtCompound tag, Skeleton skeleton)
    {
        if (tag.TryGetNumber<int>("StrayConversionTime", out var conversion))
            skeleton.StrayConversionTicks = conversion;
        if (tag.TryGetNumber<int>("ObsidianPowderSnowTicks", out var snow))
            skeleton.PowderSnowTicks = snow;
    }

    private void ReadFarmAnimalVariant(int variant)
    {
        if (this is Cow cow)
            cow.Variant = variant;
        if (this is Pig pig)
            pig.Variant = variant;
        if (this is Chicken chicken)
            chicken.Variant = variant;
    }

    private static void ReadMooshroomSave(NbtCompound tag, Mooshroom mooshroom)
    {
        mooshroom.Brown = tag.TryGetTagValue<string>("Type", out var mushroomType) && mushroomType == "brown";
        if (tag.TryGetTag<NbtArray<int>>("ObsidianStewEffect", out var stew) && stew.Count == 2)
            mooshroom.StewEffect = new() { EffectId = stew[0], Duration = stew[1] };
    }

    private static void ReadChickenSave(NbtCompound tag, Chicken chicken)
    {
        if (tag.TryGetNumber<int>("EggLayTime", out var eggTime))
            chicken.EggLayTime = eggTime;

        chicken.IsChickenJockey = ReadFlag(tag, "IsChickenJockey");
    }

    private static void ReadSheepSave(NbtCompound tag, Sheep sheep)
    {
        if (tag.TryGetNumber<byte>("Color", out var color))
            sheep.Color = (byte)(color & 15);

        sheep.Sheared = ReadFlag(tag, "Sheared");
    }

    private static void ReadCreeperSave(NbtCompound tag, Creeper creeper)
    {
        creeper.Powered = ReadFlag(tag, "powered");
        creeper.Ignited = ReadFlag(tag, "ignited");
        if (tag.TryGetNumber<short>("Fuse", out var fuse))
            creeper.Fuse = Math.Max(1, (int)fuse);
        if (tag.TryGetNumber<byte>("ExplosionRadius", out var radius))
            creeper.ExplosionRadius = Math.Max(1, (int)radius);
    }

    private static bool ReadFlag(NbtCompound tag, string name) => tag.TryGetBool(name, out var value) && value;

    private static void WriteOwner(INbtWriter writer, Guid owner) => WriteReference(writer, "Owner", owner);

    private static void WriteReference(INbtWriter writer, string name, Guid owner)
    {
        if (owner == Guid.Empty)
            return;

        WriteReferenceArray(writer, name, owner);
    }

    // A UUID as vanilla saves it: four big-endian ints.
    private static void WriteReferenceArray(INbtWriter writer, string name, Guid uuid)
    {
        var bytes = uuid.ToByteArray(true);
        var value = new int[4];
        for (var index = 0; index < value.Length; index++)
            value[index] = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)));

        writer.WriteArray(name, value);
    }

    private static Guid ReadOwner(NbtCompound tag) => ReadReference(tag, "Owner");

    private static Guid ReadReference(NbtCompound tag, string name)
    {
        if (tag.TryGetTag<NbtArray<int>>(name, out var owner) && owner.Count == 4)
            return ReadReferenceArray(owner);

        return tag.TryGetTagValue<string>(name, out var text) && Guid.TryParse(text, out var uuid) ? uuid : Guid.Empty;
    }

    private static Guid ReadReferenceArray(NbtArray<int> ints)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < 4; index++)
            BinaryPrimitives.WriteInt32BigEndian(bytes.Slice(index * sizeof(int), sizeof(int)), ints[index]);

        return new Guid(bytes, true);
    }

    internal static void WriteVector(INbtWriter writer, string name, VectorD value)
    {
        writer.WriteListStart(name, NbtTagType.Double, 3);
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Y);
        writer.WriteDouble(value.Z);
        writer.EndList();
    }

    internal static VectorD ReadVector(NbtCompound tag, string name)
    {
        if (tag.TryGetTag<NbtList>(name, out var list)
            && list is [NbtTag<double> x, NbtTag<double> y, NbtTag<double> z, ..])
        {
            return new VectorD(x.Value, y.Value, z.Value);
        }

        return VectorD.Zero;
    }
}
