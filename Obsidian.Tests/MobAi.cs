using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Registries;
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
    [InlineData(EntityType.Cow)]
    [InlineData(EntityType.Horse)]
    [InlineData(EntityType.Cat)]
    [InlineData(EntityType.Camel)]
    [InlineData(EntityType.Enderman)]
    [InlineData(EntityType.MagmaCube)]
    [InlineData(EntityType.Wolf)]
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
        if (mob is Horse normalHorse)
            normalHorse.Variant = 0x0304;
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
            enderman.CarriedBlock = BlocksRegistry.GrassBlock.WithProperty("snowy", "true");
            enderman.AngerTicks = 234;
            enderman.AngryAt = System.Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        }
        if (mob is MagmaCube magma)
            magma.Size = 4;
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
}
