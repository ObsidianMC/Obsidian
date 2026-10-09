using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:frog")]
public sealed partial class Frog : FarmAnimal
{
    private int jumpCooldown;
    public Frog() => Type = EntityType.Frog;
    public int Variant { get; internal set; } = 1;
    public bool Pregnant { get; internal set; }
    internal override bool SwimmingNavigation => true;
    internal override float MovementSpeed => base.MovementSpeed * 0.2f;
    protected override bool UsesFloatGoal => false;
    protected override bool TakesFallDamage => false;
    protected override float DimensionScale => 1;
    protected override string? SoundName => "frog";
    protected override bool CanEat(ItemStack? item) => !Pregnant && item is { Count: > 0, Type: Material.SlimeBall };
    protected override void FinalizeSpawn() => SelectVariant();
    internal void SelectVariant()
    {
        var (x, z) = Position.ToChunkCoord();
        if (Level is Obsidian.WorldData.AbstractLevel level && level.GetLoadedChunk(x, z) is { } chunk)
        {
            var biome = chunk.GetBiome((int)Position.X, (int)Position.Y, (int)Position.Z);
            Variant = TagsRegistry.Worldgen.Biome.SpawnsColdVariantFrogs.Entries.Contains(biome.Id) ? 0 :
                TagsRegistry.Worldgen.Biome.SpawnsWarmVariantFrogs.Entries.Contains(biome.Id) ? 2 : 1;
        }
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(1, new MeleeAttackGoal(this, 1.2f));
        actions.AddGoal(5, new SeekWaterGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, target => target is Slime { Size: 1 }));
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        Pregnant = true;
        return this;
    }
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        if (target is not Slime { Size: 1 } prey)
            return;
        Pose = Pose.UsingTongue;
        SynchronizeMetadata();
        await prey.RemoveAsync();
        if (prey is MagmaCube)
            DropItem(Variant switch { 0 => Material.VerdantFroglight, 2 => Material.PearlescentFroglight, _ => Material.OchreFroglight });
        else DropItem(Material.SlimeBall);
    }
    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        if (Pose == Pose.UsingTongue && AiTick % 10 == 0) { Pose = Pose.Standing; SynchronizeMetadata(); }
        if (--jumpCooldown <= 0 && !InWater && Navigator is Navigator { IsNavigating: true })
        {
            JumpControl.Jump();
            jumpCooldown = Random.Next(100, 141);
            PlayMobSound("long_jump");
        }
        if (Pregnant && Level is Obsidian.WorldData.AbstractLevel level && AiTick % 20 == 0)
        {
            var origin = (Vector)Position.Floor();
            for (var x = -1; x <= 1; x++)
            for (var z = -1; z <= 1; z++)
            {
                var water = new Vector(origin.X + x, origin.Y - 1, origin.Z + z);
                var above = new Vector(water.X, water.Y + 1, water.Z);
                if (Terrain.GetBlock(water)?.Material != Material.Water || Terrain.GetBlock(above)?.IsAir != true)
                    continue;
                await level.SetBlockAsync(above, BlocksRegistry.Get(Material.Frogspawn), true);
                level.ScheduleFrogspawn(above, Random.Next(3600, 12001));
                Pregnant = false;
                PlayMobSound("lay_spawn");
                return;
            }
        }
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.FrogVariant);
        writer.WriteVarInt(Variant);
        writer.WriteEntityMetadataType(18, EntityMetadataType.OptionalUnsignedVarInt);
        writer.WriteVarInt(AttackTarget == null ? 0 : AttackTarget.EntityId + 1);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteString("variant", Variant switch { 0 => "minecraft:cold", 2 => "minecraft:warm", _ => "minecraft:temperate" });
        writer.WriteBool("ObsidianPregnant", Pregnant);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Variant = (tag.TryGetTagValue<string>("variant", out var savedvariant) ? savedvariant : null) switch { "minecraft:cold" => 0, "minecraft:warm" => 2, _ => 1 };
        Pregnant = tag.TryGetBool("ObsidianPregnant", out var pregnant) && pregnant;
    }
}
