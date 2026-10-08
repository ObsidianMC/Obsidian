using Obsidian.Entities.AI;
using Obsidian.WorldData.Structures;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:silverfish")]
public sealed partial class Silverfish : PathfinderMob
{
    private int wakeTicks;
    private static readonly Vector[] directions = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
    public Silverfish() => Type = EntityType.Silverfish;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "silverfish";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new FloatGoal(this));
        actions.AddGoal(4, new MeleeAttackGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 1));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => entity is IPlayer));
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (wakeTicks == 0 && !ReferenceEquals(source, this))
            wakeTicks = 20;
        return default;
    }
    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        var origin = (Vector)Position.Floor();
        if (wakeTicks > 0 && --wakeTicks == 0)
        {
            for (var y = 0; y <= 5; y++)
            for (var x = -10; x <= 10; x++)
            for (var z = -10; z <= 10; z++)
            for (var sign = -1; sign <= 1; sign += 2)
            {
                if (y == 0 && sign == 1) continue;
                var point = new Vector(origin.X + x, origin.Y + y * sign, origin.Z + z);
                if (Terrain.GetBlock(point) is not { } block || !block.UnlocalizedName.StartsWith("minecraft:infested_", StringComparison.Ordinal))
                    continue;
                await Level.SetBlockAsync(point, BlocksRegistry.Air, true);
                SpawnFromBlock(Level, point);
                if (Random.Next(2) == 0) return;
            }
        }
        if (AttackTarget == null && AiTick - LastHurtTick > 100 && Random.Next(10) == 0)
        {
            var offset = directions[Random.Next(directions.Length)];
            var point = new Vector(origin.X + offset.X, origin.Y + offset.Y, origin.Z + offset.Z);
            var block = Terrain.GetBlock(point);
            if (block != null && BlockStateParser.TryParse("minecraft:infested_" + block.UnlocalizedName[10..]) is { } infested)
            {
                foreach (var property in BlockStateProperties.GetProperties(block))
                    infested = infested.WithProperty(property.Key, property.Value);
                await Level.SetBlockAsync(point, infested, true);
                await RemoveAsync();
            }
        }
    }
    internal static void SpawnFromBlock(ILevel level, Vector point)
    {
        if (level.LevelData.Difficulty != Difficulty.Peaceful)
            level.SpawnEntity(new Silverfish { Level = level, EntityId = Server.GetNextEntityId(), Position = (VectorD)point + 0.5f });
    }
}
