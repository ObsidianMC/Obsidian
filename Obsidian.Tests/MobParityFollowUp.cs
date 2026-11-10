using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Tests.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobParityFollowUp
{
    [Theory]
    [InlineData(EntityType.Zombie)]
    [InlineData(EntityType.Skeleton)]
    [InlineData(EntityType.ZombieHorse)]
    [InlineData(EntityType.Zoglin)]
    public async Task UndeadRejectPoisonAndRegeneration(EntityType type)
    {
        await using var context = TestServerFactory.CreateServer($"undead-effects-{Guid.NewGuid():N}");
        var mob = EntitySpawner.CreateMob(context.World, type)!;
        mob.InitializeAi(false);

        mob.AddPotionEffect((int)PotionEffect.Regeneration - 1, 100);
        mob.AddPotionEffect((int)PotionEffect.Poison - 1, 100);
        mob.AddPotionEffect((int)PotionEffect.Speed - 1, 100);

        Assert.False(mob.HasPotionEffect((int)PotionEffect.Regeneration - 1));
        Assert.False(mob.HasPotionEffect((int)PotionEffect.Poison - 1));
        Assert.True(mob.HasPotionEffect((int)PotionEffect.Speed - 1));
    }

    [Theory]
    [InlineData(EntityType.Cat, false)]
    [InlineData(EntityType.Ocelot, false)]
    [InlineData(EntityType.IronGolem, false)]
    [InlineData(EntityType.CopperGolem, false)]
    [InlineData(EntityType.Breeze, false)]
    [InlineData(EntityType.Chicken, false)]
    [InlineData(EntityType.Pig, true)]
    public async Task LandingHonorsFallImmunity(EntityType type, bool takesDamage)
    {
        await using var context = TestServerFactory.CreateServer($"mob-fall-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var mob = EntitySpawner.CreateMob(context.World, type)!;
        mob.Position = new VectorD(8, 70, 8);
        mob.InitializeAi(false);
        var saved = EntityNbt.Save(mob)!;
        saved.Set(new NbtTag<float>("FallDistance", 10));
        saved.Set(new NbtTag<bool>("OnGround", true));
        saved.Set(new NbtTag<bool>("NoAI", true));
        mob = Assert.IsAssignableFrom<Mob>(EntityNbt.Load(saved, context.World));
        var health = mob.Health;

        await mob.TickAsync();

        Assert.Equal(takesDamage, mob.Health < health);
    }

    [Fact]
    public async Task SlimeChildrenKeepNoAiAndVisibleNames()
    {
        await using var context = TestServerFactory.CreateServer($"slime-split-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var slime = new Slime
        {
            Level = context.World,
            Position = new VectorD(8, 70, 8),
            MobBitMask = MobBitmask.NoAi,
            CustomName = new ChatMessage { Text = "Still" },
            CustomNameVisible = true
        };
        slime.InitializeAi(false);
        slime.Size = 4;

        await slime.KillAsync(slime);

        var children = context.World.GetEntitiesInRange(slime.Position, 3).OfType<Slime>().ToArray();
        Assert.InRange(children.Length, 2, 4);
        Assert.All(children, child =>
        {
            Assert.True(child.MobBitMask.HasFlag(MobBitmask.NoAi));
            Assert.True(child.CustomNameVisible);
            Assert.Equal("Still", child.CustomName!.Text);
            Assert.Equal(2, child.Size);
        });
    }

    [Theory]
    [InlineData(0, 3, 0x0401)]
    [InlineData(8, 4, 0x0205)]
    public async Task HorseCoatsAndMarkingsAreInheritedIndependently(int coatRoll, int markingsRoll, int expected)
    {
        await using var context = TestServerFactory.CreateServer($"horse-coat-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var first = new BreedingHorse
        {
            Level = context.World,
            Position = new VectorD(8, 70, 8),
            Variant = 0x0001,
            Random = new BreedingRandom(coatRoll == 8 ? [coatRoll, 5, markingsRoll, 2] : [coatRoll, markingsRoll])
        };
        var second = new Horse { Level = context.World, Variant = 0x0406 };

        var child = Assert.IsType<Horse>(first.CreateChild(second));

        Assert.Equal(expected, child.Variant);
    }

    private sealed class BreedingHorse : Horse
    {
        internal IEntity CreateChild(Horse mate) => CreateOffspring(mate);
    }

    private sealed class BreedingRandom(int[] draws) : Random
    {
        private int index;

        public override int Next(int maxValue)
        {
            var value = draws[index++];
            Assert.InRange(value, 0, maxValue - 1);
            return value;
        }
    }
}
