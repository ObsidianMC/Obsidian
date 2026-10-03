namespace Obsidian.Entities;

[MinecraftEntity("minecraft:bat")]
public sealed partial class Bat : Ambient
{
    private VectorF? flightTarget;
    public Bat() => Type = EntityType.Bat;
    public bool IsHanging { get; internal set; } = true;
    protected override bool UsesAi => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "bat";
    protected override int GetExperienceReward() => 0;
    protected override VectorF Travel() => AI.VolumeMovement.Travel(this, true);
    protected override ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return default;
        var ceiling = new Vector((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y + Dimension.Height), (int)MathF.Floor(Position.Z));
        var supported = Terrain.GetBlock(ceiling) is { } block && AI.BlockCollisionShapes.Get(block).Count > 0;
        if (IsHanging)
        {
            Motion = VectorF.Zero;
            if (!supported || Level.GetPlayersInRange(Position, 4).Any(player => player.Gamemode != Gamemode.Spectator))
            {
                IsHanging = false;
                SynchronizeMetadata();
                PlayMobSound("takeoff");
            }
        }
        else
        {
            if (flightTarget is not VectorF target || (target - Position).MagnitudeSquared() < 4 ||
                Random.Next(30) == 0 || !Terrain.IsFree(Dimension.CreateBBFromPosition(target)))
                flightTarget = Position + new VectorF(Random.Next(7) - Random.Next(7), Random.Next(-2, 4), Random.Next(7) - Random.Next(7));
            var delta = flightTarget.Value - Position;
            Motion += (new VectorF(Math.Sign(delta.X) * 0.5f, Math.Sign(delta.Y) * 0.7f, Math.Sign(delta.Z) * 0.5f) - Motion) * 0.1f;
            Yaw = MathF.Atan2(-Motion.X, Motion.Z) * 180 / MathF.PI;
            if (supported && Random.Next(100) == 0)
            {
                IsHanging = true;
                Motion = VectorF.Zero;
                SynchronizeMetadata();
            }
        }
        return default;
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        IsHanging = false;
        SynchronizeMetadata();
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte(IsHanging ? (byte)1 : (byte)0);
    }
    protected override void WriteAdditionalSave(Obsidian.Nbt.Interfaces.INbtWriter writer) => writer.WriteByte("BatFlags", IsHanging ? (byte)1 : (byte)0);
    protected override void ReadAdditionalSave(Obsidian.Nbt.NbtCompound tag) => IsHanging = tag.TryGetTagValue<byte>("BatFlags", out var flags) && (flags & 1) != 0;
}
