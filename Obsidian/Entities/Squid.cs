using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:squid")]
public partial class Squid : AgeableMob
{
    private VectorF swimming;
    private int changeDirection;
    public Squid() => Type = EntityType.Squid;
    protected override bool UsesAi => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "squid";
    protected virtual ParticleType InkParticleType => ParticleType.SquidInk;
    protected override int GetExperienceReward() => Random.Next(1, 4);
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        if (--changeDirection <= 0 || MovementFlags.HasFlag(MovementFlags.HorizontalCollision))
        {
            changeDirection = Random.Next(20, 70);
            var angle = Random.NextSingle() * MathF.Tau;
            swimming = new VectorF(MathF.Cos(angle) * 0.2f, Random.NextSingle() * 0.2f - 0.1f, MathF.Sin(angle) * 0.2f);
        }
        if (InWater)
        {
            if (Terrain.GetBlock((Vector)(Position + swimming * 4).Floor())?.Material != Material.Water)
                swimming = new VectorF(swimming.X, -0.1f, swimming.Z);
            Motion = swimming;
            if (AiTick % 5 == 0)
                PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityMotionPacket
                { EntityId = EntityId, Velocity = new Velocity(Motion.X, Motion.Y, Motion.Z) });
            Yaw = MathF.Atan2(-Motion.X, Motion.Z) * 180 / MathF.PI;
            Pitch = -MathF.Atan2(Motion.Y, MathF.Sqrt(Motion.X * Motion.X + Motion.Z * Motion.Z)) * 180 / MathF.PI;
        }
    }
    protected override VectorF Travel()
    {
        // Water travel has no sinking acceleration; outside water ordinary gravity applies.
        var gravity = NoGravity;
        NoGravity |= InWater;
        var position = EntityMovement.Move(this, Terrain, VectorF.Zero);
        NoGravity = gravity;
        return position;
    }
    protected override async ValueTask TickAirSupplyAsync()
    {
        if (InWater)
            Air = 300;
        else if (--Air <= -20)
        {
            Air = 0;
            await DamageEnvironmentAsync(2);
        }
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (InWater && !ReferenceEquals(source, this))
        {
            var direction = Position - source.Position;
            if (direction.Magnitude > 0.001f)
                swimming = direction / direction.Magnitude * 0.3f;
            changeDirection = 100;
            PlayMobSound("squirt");
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new LevelParticlesPacket
            { Position = Position, ParticleCount = 30, Offset = new VectorF(0.3f), Data = new InkParticle(InkParticleType) });
        }
        return default;
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby) DropItem(Material.InkSac, Random.Next(1, 4));
        return default;
    }
    private sealed class InkParticle(ParticleType type) : ParticleData
    {
        public override ParticleType ParticleType => type;
    }
}
