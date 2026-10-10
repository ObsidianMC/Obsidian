using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Utilities;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobAi
{
    [Fact]
    public async Task InterruptedEmptyRegionHeadersCanBeCompleted()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            await System.IO.File.WriteAllBytesAsync(path, new byte[4096]);
            await using var region = new Obsidian.WorldData.RegionFile(path, Obsidian.Nbt.NbtCompression.ZLib);
            Assert.True(await region.InitializeAsync());
            Assert.Equal(8192L, region.EndOfFile);
            Assert.Null(await region.GetChunkBytesAsync(0, 0));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void SpawnUuidsAlwaysOccupySixteenBytes(string text)
    {
        var uuid = System.Guid.Parse(text);
        var buffer = new Obsidian.Net.NetworkBuffer();
        buffer.WriteUuid(uuid);
        Assert.Equal(16, buffer.Size);
        Assert.Equal(uuid.ToByteArray(true), buffer.AsSpan(0, buffer.Size).ToArray());
    }

    [Fact]
    public void SavedEquipmentComponentsUseTheirProtocolIds()
    {
        Assert.IsType<Obsidian.API.Inventory.DataComponents.CanBreakDataComponent>(
            Obsidian.API.Inventory.DataComponents.ComponentBuilder.Create(DataComponentType.CanBreak));
        var item = Obsidian.API.Registries.ItemsRegistry.GetSingleItem(Material.Bow);
        var buffer = new Obsidian.Net.NetworkBuffer();
        buffer.WriteItemStack(item);
        var reader = new Obsidian.Net.NetworkBuffer(buffer.AsSpan(0, buffer.Size).ToArray());
        var restored = reader.ReadItemStack();
        Assert.NotNull(restored);
        Assert.Equal(item.Type, restored.Type);
        Assert.Equal(item.Count, restored.Count);
        Assert.Equal(item.TotalComponents, restored.TotalComponents);
        Assert.Equal(0, restored.GetComponent<Obsidian.API.Inventory.DataComponents.SimpleDataComponent<int>>(DataComponentType.RepairCost).Value);
        Assert.Equal(reader.Size, reader.Offset);
    }

    [Fact]
    public void WorldsWithoutSavedDifficultyAllowHostileMobs()
    {
        Assert.Equal(Difficulty.Normal, new LevelData().Difficulty);
    }

    [Fact]
    public void FractionalNegativePositionsBelongToThePreviousChunk()
    {
        Assert.Equal((-1, -2), new VectorF(-0.1f, 0, -16.1f).ToChunkCoord());
        Assert.Equal((0, 1), new VectorF(0.1f, 0, 16.1f).ToChunkCoord());
    }

    [Fact]
    public async Task HigherPriorityMovementInterruptsWanderingButPreservesLooking()
    {
        var selector = new GoalSelector();
        var wander = new ProbeGoal(GoalFlags.Move) { Enabled = true };
        var look = new ProbeGoal(GoalFlags.Look) { Enabled = true };
        var panic = new ProbeGoal(GoalFlags.Move);
        selector.AddGoal(6, wander);
        selector.AddGoal(7, look);
        selector.AddGoal(1, panic);

        await selector.TickAsync();
        panic.Enabled = true;
        await selector.TickAsync();

        Assert.Equal(1, wander.Starts);
        Assert.Equal(1, wander.Stops);
        Assert.Equal(1, panic.Starts);
        Assert.Equal(1, look.Starts);
        Assert.Equal(0, look.Stops);
        selector.Pause();
        await selector.TickAsync();
        Assert.False(selector.IsExecuting);
        Assert.Equal(1, panic.Stops);
        Assert.Equal(1, look.Stops);
    }

    [Fact]
    public void CollisionClipsAtWallAndFloorWithoutBlockingSliding()
    {
        var bounds = new BoundingBox(new VectorF(0.1f, 1, 0.1f), new VectorF(0.9f, 2, 0.9f));
        BoundingBox[] obstacles =
        [new(new VectorF(1, 0, 0), new VectorF(2, 3, 1)), new(new VectorF(0, 0, 0), new VectorF(1, 1, 1))];

        var displacement = EntityMovement.Clip(bounds, new VectorF(0.5f, -0.2f, 0.3f), obstacles);

        Assert.Equal(0.1f, displacement.X, 4);
        Assert.Equal(0, displacement.Y);
        Assert.Equal(0.3f, displacement.Z, 4);
    }

    [Fact]
    public void BabiesUseHalfSizedBoundingBoxesAndZombieSpeedBonus()
    {
        var pig = new Pig { Level = null! };
        var zombie = new Zombie { Level = null! };
        var adultPigWidth = pig.Dimension.Width;
        var adultZombieHeight = zombie.Dimension.Height;
        var adultZombieSpeed = zombie.MovementSpeed;
        pig.IsBaby = true;
        zombie.IsBaby = true;

        Assert.Equal(adultPigWidth / 2, pig.Dimension.Width);
        Assert.Equal(adultZombieHeight / 2, zombie.Dimension.Height);
        Assert.Equal(adultZombieSpeed * 1.5f, zombie.MovementSpeed);
        Assert.Equal(-24000, pig.Age);
    }

    [Fact]
    public void ImportedShapesUseDefaultStatesForAirAndFullBlocks()
    {
        Assert.Null(BlocksRegistry.Air.State);
        Assert.Null(BlocksRegistry.Stone.State);
        Assert.Empty(BlockCollisionShapes.Get(BlocksRegistry.Air));
        var stone = Assert.Single(BlockCollisionShapes.Get(BlocksRegistry.Stone));
        Assert.Equal(VectorF.Zero, stone.Min);
        Assert.Equal(new VectorF(1), stone.Max);
    }

    [Theory]
    [InlineData(EntityType.Cow)]
    [InlineData(EntityType.Mooshroom)]
    [InlineData(EntityType.Chicken)]
    [InlineData(EntityType.Sheep)]
    [InlineData(EntityType.Husk)]
    [InlineData(EntityType.Skeleton)]
    [InlineData(EntityType.Stray)]
    [InlineData(EntityType.Bogged)]
    [InlineData(EntityType.Parched)]
    [InlineData(EntityType.Creeper)]
    [InlineData(EntityType.Slime)]
    [InlineData(EntityType.Horse)]
    [InlineData(EntityType.ZombieHorse)]
    [InlineData(EntityType.Villager)]
    [InlineData(EntityType.IronGolem)]
    [InlineData(EntityType.Ocelot)]
    [InlineData(EntityType.Cat)]
    [InlineData(EntityType.Camel)]
    [InlineData(EntityType.Blaze)]
    [InlineData(EntityType.CaveSpider)]
    [InlineData(EntityType.Spider)]
    [InlineData(EntityType.Enderman)]
    [InlineData(EntityType.Ghast)]
    [InlineData(EntityType.MagmaCube)]
    [InlineData(EntityType.Silverfish)]
    [InlineData(EntityType.SnowGolem)]
    [InlineData(EntityType.Squid)]
    [InlineData(EntityType.Wolf)]
    [InlineData(EntityType.Bat)]
    [InlineData(EntityType.Bee)]
    [InlineData(EntityType.Fox)]
    [InlineData(EntityType.Frog)]
    [InlineData(EntityType.Llama)]
    [InlineData(EntityType.SkeletonHorse)]
    [InlineData(EntityType.Tadpole)]
    [InlineData(EntityType.GlowSquid)]
    [InlineData(EntityType.Dolphin)]
    [InlineData(EntityType.Donkey)]
    [InlineData(EntityType.Axolotl)]
    [InlineData(EntityType.Goat)]
    [InlineData(EntityType.Panda)]
    [InlineData(EntityType.Parrot)]
    public void MobSavesRoundTripIdentityPositionHealthAndNoAi(EntityType type)
    {
        var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(null!, type)!;
        mob.Position = new VectorF(-17.5f, 64, 31.5f);
        mob.InitializeAi(false);
        mob.Health = 3;
        mob.PersistenceRequired = true;
        mob.MobBitMask = MobBitmask.NoAi;
        if (mob is AbstractHorse horse)
        {
            horse.HorseMask = HorseMask.Tamed | HorseMask.HasBred;
            horse.Temper = 40;
            horse.Owner = System.Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        }
        if (mob is Horse normalHorse) normalHorse.Variant = 0x0304;
        if (mob is IronGolem golem) golem.PlayerCreated = true;
        if (mob is Ocelot ocelot) ocelot.Trusting = true;
        if (mob is Cat cat)
        {
            cat.Owner = System.Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
            cat.OrderedToSit = true;
            cat.Variant = 10;
            cat.CollarColor = 3;
        }
        if (mob is Wolf wolf)
        {
            wolf.Owner = System.Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
            wolf.OrderedToSit = true;
            wolf.Variant = 8;
            wolf.SoundVariant = 6;
            wolf.CollarColor = 5;
            wolf.AngerTicks = 123;
            wolf.AngryAt = System.Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        }
        if (mob is Enderman enderman)
        {
            enderman.CarriedBlock = BlocksRegistry.Get(Material.GrassBlock).WithProperty("snowy", "true");
            enderman.AngerTicks = 234;
            enderman.AngryAt = System.Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        }
        if (mob is MagmaCube magma) magma.Size = 4;
        if (mob is SnowGolem snowGolem) snowGolem.Pumpkin = false;
        if (mob is Camel camel)
        {
            camel.LastPoseChangeTick = -123;
            camel.DashCooldown = 20;
        }
        using var writer = new Obsidian.Nbt.RawNbtWriter("");
        writer.WriteListStart("Entities", Obsidian.Nbt.NbtTagType.Compound, 1);
        mob.WriteSave(writer);
        writer.EndList();
        writer.EndCompound();
        using var stream = new System.IO.MemoryStream(writer.Data.ToArray());
        var root = (Obsidian.Nbt.NbtCompound)new Obsidian.Nbt.NbtReader(stream).ReadNextTag()!;
        var saved = Assert.IsType<Obsidian.Nbt.NbtList>(root["Entities"]);
        var restored = Obsidian.Entities.Factories.EntitySpawner.CreateMob(null!, type)!;
        restored.ReadSave(Assert.IsType<Obsidian.Nbt.NbtCompound>(Assert.Single(saved)));
        Assert.Equal(mob.Uuid, restored.Uuid);
        Assert.Equal(mob.Position, restored.Position);
        Assert.Equal(3, restored.Health);
        Assert.True(restored.PersistenceRequired);
        Assert.Equal(MobBitmask.NoAi, restored.MobBitMask);
        if (mob is AbstractHorse originalHorse)
        {
            var restoredHorse = Assert.IsAssignableFrom<AbstractHorse>(restored);
            Assert.Equal(originalHorse.HorseMask, restoredHorse.HorseMask);
            Assert.Equal(originalHorse.Temper, restoredHorse.Temper);
            Assert.Equal(originalHorse.Owner, restoredHorse.Owner);
        }
        if (mob is Horse originalNormalHorse)
            Assert.Equal(originalNormalHorse.Variant, Assert.IsType<Horse>(restored).Variant);
        if (mob is IronGolem originalGolem)
            Assert.Equal(originalGolem.PlayerCreated, Assert.IsType<IronGolem>(restored).PlayerCreated);
        if (mob is Ocelot originalOcelot)
            Assert.Equal(originalOcelot.Trusting, Assert.IsType<Ocelot>(restored).Trusting);
        if (mob is Cat originalCat)
        {
            var restoredCat = Assert.IsType<Cat>(restored);
            Assert.Equal(originalCat.Owner, restoredCat.Owner);
            Assert.True(restoredCat.Tamed);
            Assert.True(restoredCat.OrderedToSit);
            Assert.Equal(originalCat.Variant, restoredCat.Variant);
            Assert.Equal(originalCat.CollarColor, restoredCat.CollarColor);
        }
        if (mob is Wolf originalWolf)
        {
            var restoredWolf = Assert.IsType<Wolf>(restored);
            Assert.Equal(originalWolf.Owner, restoredWolf.Owner);
            Assert.Equal(originalWolf.Variant, restoredWolf.Variant);
            Assert.Equal(originalWolf.SoundVariant, restoredWolf.SoundVariant);
            Assert.Equal(originalWolf.CollarColor, restoredWolf.CollarColor);
            Assert.Equal(originalWolf.AngerTicks, restoredWolf.AngerTicks);
            Assert.Equal(originalWolf.AngryAt, restoredWolf.AngryAt);
            Assert.True(restoredWolf.OrderedToSit);
            Assert.Equal(40, restoredWolf.GetAttributeValue("minecraft:generic.max_health"));
        }
        if (mob is Enderman originalEnderman)
        {
            var restoredEnderman = Assert.IsType<Enderman>(restored);
            Assert.Equal(originalEnderman.CarriedBlock!.GetHashCode(), restoredEnderman.CarriedBlock!.GetHashCode());
            Assert.Equal(originalEnderman.AngerTicks, restoredEnderman.AngerTicks);
            Assert.Equal(originalEnderman.AngryAt, restoredEnderman.AngryAt);
        }
        if (mob is MagmaCube originalMagma)
        {
            var restoredMagma = Assert.IsType<MagmaCube>(restored);
            Assert.Equal(originalMagma.Size, restoredMagma.Size);
            Assert.Equal(12, restoredMagma.GetAttributeValue("minecraft:generic.armor"));
            Assert.Equal(6, restoredMagma.GetAttributeValue("minecraft:generic.attack_damage"));
        }
        if (mob is SnowGolem) Assert.False(Assert.IsType<SnowGolem>(restored).Pumpkin);
        if (mob is Camel originalCamel)
        {
            var restoredCamel = Assert.IsType<Camel>(restored);
            Assert.Equal(originalCamel.LastPoseChangeTick, restoredCamel.LastPoseChangeTick);
            Assert.Equal(originalCamel.DashCooldown, restoredCamel.DashCooldown);
            Assert.True(restoredCamel.IsSitting);
            Assert.Equal(Pose.Sitting, restoredCamel.Pose);
        }
    }

    [Theory]
    [InlineData(EntityType.Horse)]
    [InlineData(EntityType.ZombieHorse)]
    [InlineData(EntityType.SkeletonHorse)]
    [InlineData(EntityType.Donkey)]
    [InlineData(EntityType.Llama)]
    [InlineData(EntityType.Camel)]
    public void HorseFamilyMetadataMatches12111ClientFields(EntityType type)
    {
        var entity = Obsidian.Entities.Factories.EntitySpawner.Create(type, null!);
        if (entity is Camel camel)
            camel.LastPoseChangeTick = -123;
        var fields = ReadMetadataTypes(entity);
        Assert.Equal(EntityMetadataType.Boolean, fields[16]);
        Assert.Equal(EntityMetadataType.Byte, fields[17]);
        Assert.DoesNotContain(EntityMetadataType.OptionalLivingEntityReference, fields.Values);
        if (type == EntityType.Horse)
        {
            Assert.Equal(EntityMetadataType.VarInt, fields[18]);
            Assert.Equal(19, fields.Count);
        }
        else if (type is EntityType.Donkey or EntityType.Llama)
        {
            Assert.Equal(EntityMetadataType.Boolean, fields[18]);
            if (type == EntityType.Llama)
            {
                Assert.Equal(EntityMetadataType.VarInt, fields[19]);
                Assert.Equal(EntityMetadataType.VarInt, fields[20]);
                Assert.Equal(21, fields.Count);
            }
            else
                Assert.Equal(19, fields.Count);
        }
        else if (type == EntityType.Camel)
        {
            Assert.Equal(EntityMetadataType.Boolean, fields[18]);
            Assert.Equal(EntityMetadataType.VarLong, fields[19]);
            Assert.Equal(20, fields.Count);
        }
        else
            Assert.Equal(18, fields.Count);
    }

    [Theory]
    [InlineData(EntityType.Ocelot)]
    [InlineData(EntityType.Cat)]
    public void FelineMetadataMatches12111ClientFields(EntityType type)
    {
        var entity = Obsidian.Entities.Factories.EntitySpawner.Create(type, null!);
        if (entity is Cat cat)
        {
            cat.Owner = System.Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
            cat.Variant = 10;
            cat.IsSitting = true;
        }
        var fields = ReadMetadataTypes(entity);
        Assert.Equal(EntityMetadataType.Boolean, fields[16]);
        if (type == EntityType.Ocelot)
        {
            Assert.Equal(EntityMetadataType.Boolean, fields[17]);
            Assert.Equal(18, fields.Count);
        }
        else
        {
            Assert.Equal(EntityMetadataType.Byte, fields[17]);
            Assert.Equal(EntityMetadataType.OptionalLivingEntityReference, fields[18]);
            Assert.Equal(EntityMetadataType.CatVariant, fields[19]);
            Assert.Equal(EntityMetadataType.Boolean, fields[20]);
            Assert.Equal(EntityMetadataType.Boolean, fields[21]);
            Assert.Equal(EntityMetadataType.VarInt, fields[22]);
            Assert.Equal(23, fields.Count);
        }
    }

    [Theory]
    [InlineData(EntityType.Blaze, EntityMetadataType.Byte)]
    [InlineData(EntityType.Spider, EntityMetadataType.Byte)]
    [InlineData(EntityType.CaveSpider, EntityMetadataType.Byte)]
    [InlineData(EntityType.Enderman, EntityMetadataType.OptionalBlockState)]
    [InlineData(EntityType.Ghast, EntityMetadataType.Boolean)]
    [InlineData(EntityType.MagmaCube, EntityMetadataType.VarInt)]
    [InlineData(EntityType.SnowGolem, EntityMetadataType.Byte)]
    public void AddedMobMetadataUsesClientSerializers(EntityType type, EntityMetadataType expected)
    {
        var entity = Obsidian.Entities.Factories.EntitySpawner.Create(type, null!);
        var fields = ReadMetadataTypes(entity);
        Assert.Equal(expected, fields[16]);
        if (type == EntityType.Enderman)
        {
            Assert.Equal(EntityMetadataType.Boolean, fields[17]);
            Assert.Equal(EntityMetadataType.Boolean, fields[18]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FarmVariantMetadataUsesZeroBasedRegistryIds(int variant)
    {
        Assert.Equal(EntityMetadataType.CowVariant, ReadMetadataTypes(new Cow { Level = null!, Variant = variant })[17]);
        Assert.Equal(EntityMetadataType.ChickenVariant, ReadMetadataTypes(new Chicken { Level = null!, Variant = variant })[17]);
        Assert.Equal(EntityMetadataType.PigVariant, ReadMetadataTypes(new Pig { Level = null!, Variant = variant })[18]);
    }

    [Fact]
    public void WolfMetadataIncludesSoundVariantAndLongAngerTimer()
    {
        var wolf = new Wolf { Level = null!, Owner = System.Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), Variant = 8, SoundVariant = 6 };
        var fields = ReadMetadataTypes(wolf);
        Assert.Equal(EntityMetadataType.Byte, fields[17]);
        Assert.Equal(EntityMetadataType.OptionalLivingEntityReference, fields[18]);
        Assert.Equal(EntityMetadataType.VarLong, fields[21]);
        Assert.Equal(EntityMetadataType.WolfVariant, fields[22]);
        Assert.Equal(EntityMetadataType.WolfSoundVariant, fields[23]);
        Assert.Equal(24, fields.Count);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 1)]
    [InlineData(90, 0, -1, 0, 0)]
    [InlineData(0, 90, 0, -1, 0)]
    public void LookDirectionUsesRadians(float yaw, float pitch, float x, float y, float z)
    {
        var direction = new Entity { Level = null!, Yaw = yaw, Pitch = pitch }.GetLookDirection();
        Assert.Equal(x, direction.X, 4);
        Assert.Equal(y, direction.Y, 4);
        Assert.Equal(z, direction.Z, 4);
    }

    [Fact]
    public void EntityTeleportContainsVelocityAndUsesAbsoluteCoordinates()
    {
        var packet = new Obsidian.Net.Packets.Play.Clientbound.TeleportEntityPacket
        {
            EntityId = 156, Position = new VectorF(-17.5f, 64, 31.5f), Delta = new VectorF(0.2f, -0.3f, 0.4f),
            Yaw = 90, Pitch = 45, OnGround = true
        };
        var buffer = new Obsidian.Net.NetworkBuffer();
        packet.Serialize(buffer);
        var reader = new Obsidian.Net.NetworkBuffer(buffer.AsSpan(0, buffer.Size).ToArray());
        Assert.Equal(156, reader.ReadVarInt());
        Assert.Equal(packet.Position.X, reader.ReadDouble());
        Assert.Equal(packet.Position.Y, reader.ReadDouble());
        Assert.Equal(packet.Position.Z, reader.ReadDouble());
        Assert.Equal(packet.Delta.X, reader.ReadDouble());
        Assert.Equal(packet.Delta.Y, reader.ReadDouble());
        Assert.Equal(packet.Delta.Z, reader.ReadDouble());
        Assert.Equal(packet.Yaw.Degrees, reader.ReadSingle());
        Assert.Equal(packet.Pitch.Degrees, reader.ReadSingle());
        Assert.Equal(0, reader.ReadInt());
        Assert.True(reader.ReadBoolean());
        Assert.Equal(reader.Size, reader.Offset);
    }

    [Theory]
    [InlineData(EntityType.Fireball)]
    [InlineData(EntityType.SmallFireball)]
    [InlineData(EntityType.Snowball)]
    public void MobProjectilesRetainTheirBehaviorWhenLoaded(EntityType type)
    {
        var entity = new MobProjectile(new Blaze { Level = null! }, type, new VectorF(2, 65, -4), new VectorF(0, 0, 1));
        var saved = EntityNbt.Save(entity)!;
        var restored = Assert.IsType<MobProjectile>(EntityNbt.Load(saved, null!));
        Assert.Equal(entity.Type, restored.Type);
        Assert.Equal(entity.Motion, restored.Motion);
        Assert.Equal(entity.Position, restored.Position);
        Assert.Equal(entity.NoGravity, restored.NoGravity);
        Assert.True(saved.HasTag("Owner"));
    }

    [Fact]
    public void InkParticlePacketContainsBothVisibilityFlags()
    {
        var packet = new Obsidian.Net.Packets.Play.Clientbound.LevelParticlesPacket
        { AlwaysShow = true, Position = new VectorF(1, 62, 3), Offset = new VectorF(0.3f),
            ParticleCount = 30, Data = new InkParticle() };
        var buffer = new Obsidian.Net.NetworkBuffer();
        packet.Serialize(buffer);
        var reader = new Obsidian.Net.NetworkBuffer(buffer.AsSpan(0, buffer.Size).ToArray());
        Assert.False(reader.ReadBoolean());
        Assert.True(reader.ReadBoolean());
        Assert.Equal(packet.Position.X, reader.ReadDouble());
        Assert.Equal(packet.Position.Y, reader.ReadDouble());
        Assert.Equal(packet.Position.Z, reader.ReadDouble());
        Assert.Equal(packet.Offset.X, reader.ReadSingle());
        Assert.Equal(packet.Offset.Y, reader.ReadSingle());
        Assert.Equal(packet.Offset.Z, reader.ReadSingle());
        Assert.Equal(packet.MaxSpeed, reader.ReadSingle());
        Assert.Equal(30, reader.ReadInt());
        Assert.Equal((int)ParticleType.SquidInk, reader.ReadVarInt());
        Assert.Equal(reader.Size, reader.Offset);
    }

    private sealed class InkParticle : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.SquidInk;
    }

    private static System.Collections.Generic.Dictionary<byte, EntityMetadataType> ReadMetadataTypes(Entity entity)
    {
        var buffer = new Obsidian.Net.NetworkBuffer();
        entity.Write(buffer);
        var reader = new Obsidian.Net.NetworkBuffer(buffer.AsSpan(0, buffer.Size).ToArray());
        var fields = new System.Collections.Generic.Dictionary<byte, EntityMetadataType>();
        while (reader.Offset < reader.Size)
        {
            var index = reader.ReadByte();
            var fieldType = (EntityMetadataType)reader.ReadVarInt();
            Assert.True(fields.TryAdd(index, fieldType), $"Duplicate metadata field {index}");
            switch (fieldType)
            {
                case EntityMetadataType.Byte:
                case EntityMetadataType.Boolean:
                    reader.ReadByte();
                    break;
                case EntityMetadataType.VarInt:
                case EntityMetadataType.Pose:
                case EntityMetadataType.OptionalBlockState:
                    reader.ReadVarInt();
                    break;
                case EntityMetadataType.Float:
                    reader.ReadSingle();
                    break;
                case EntityMetadataType.VarLong:
                    var timer = reader.ReadVarLong();
                    if (entity is Camel camel) Assert.Equal(camel.LastPoseChangeTick, timer);
                    else Assert.Equal(0, timer);
                    break;
                case EntityMetadataType.CatVariant:
                    Assert.Equal(Assert.IsType<Cat>(entity).Variant, reader.ReadVarInt());
                    break;
                case EntityMetadataType.CowVariant:
                    Assert.Equal(Assert.IsType<Cow>(entity).Variant, reader.ReadVarInt());
                    break;
                case EntityMetadataType.ChickenVariant:
                    Assert.Equal(Assert.IsType<Chicken>(entity).Variant, reader.ReadVarInt());
                    break;
                case EntityMetadataType.PigVariant:
                    Assert.Equal(Assert.IsType<Pig>(entity).Variant, reader.ReadVarInt());
                    break;
                case EntityMetadataType.WolfVariant:
                    Assert.Equal(Assert.IsType<Wolf>(entity).Variant, reader.ReadVarInt());
                    break;
                case EntityMetadataType.WolfSoundVariant:
                    Assert.Equal(Assert.IsType<Wolf>(entity).SoundVariant, reader.ReadVarInt());
                    break;
                case EntityMetadataType.OptionalLivingEntityReference:
                    var owner = entity is Cat cat ? cat.Owner : Assert.IsType<Wolf>(entity).Owner;
                    Assert.Equal(owner != System.Guid.Empty, reader.ReadBoolean());
                    if (owner != System.Guid.Empty) Assert.Equal(owner, reader.ReadGuid());
                    break;
                case EntityMetadataType.Particles:
                    Assert.Equal(0, reader.ReadVarInt());
                    break;
                case EntityMetadataType.OptionalTextComponent:
                case EntityMetadataType.OptionalBlockPos:
                    Assert.False(reader.ReadBoolean());
                    break;
                default:
                    throw new System.InvalidOperationException($"Unexpected metadata type {fieldType}");
            }
        }
        return fields;
    }

    private sealed class ProbeGoal(GoalFlags flags) : Goal
    {
        public bool Enabled { get; set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public override GoalFlags Flags => flags;
        public override bool CanUse() => Enabled;
        public override void Start() => Starts++;
        public override void Stop() => Stops++;
    }
}
