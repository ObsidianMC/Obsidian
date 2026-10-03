namespace Obsidian.Entities;

public partial class Mob
{
    private int ambientSoundTime;
    protected virtual string? SoundName => null;
    protected virtual SoundCategory MobSoundCategory => SoundCategory.Neutral;
    protected float EffectiveDifficulty
    {
        get
        {
            var (x, z) = Position.ToChunkCoord();
            var inhabited = Level is Obsidian.WorldData.AbstractLevel level && level.GetLoadedChunk(x, z) is Obsidian.WorldData.Chunk chunk
                ? chunk.InhabitedTime : 0;
            ReadOnlySpan<float> moon = [1, 0.75f, 0.5f, 0.25f, 0, 0.25f, 0.5f, 0.75f];
            var phase = (int)((Level.LevelData.Time / 24000 % 8 + 8) % 8);
            return CalculateDifficulty(Level.LevelData.Difficulty, Level.LevelData.Time, inhabited, moon[phase]);
        }
    }
    protected float SpecialDifficulty => Math.Clamp((EffectiveDifficulty - 2) / 2, 0, 1);

    internal static float CalculateDifficulty(Difficulty difficulty, long time, long inhabitedTime, float moonBrightness)
    {
        if (difficulty == Difficulty.Peaceful)
            return 0;
        var elapsed = Math.Clamp((time - 72000) / 1440000f, 0, 1) * 0.25f;
        var local = Math.Clamp(inhabitedTime / 3600000f, 0, 1) * (difficulty == Difficulty.Hard ? 1 : 0.75f) +
            Math.Clamp(moonBrightness * 0.25f, 0, elapsed);
        if (difficulty == Difficulty.Easy)
            local *= 0.5f;
        return (int)difficulty * (0.75f + elapsed + local);
    }
    private float fallDistance;
    private int jumpDelay;

    internal bool TryJump()
    {
        if (jumpDelay > 0)
            return false;
        jumpDelay = 10;
        return true;
    }

    private async ValueTask TickEnvironmentAsync()
    {
        if (Random.Next(1000) < ambientSoundTime++)
        {
            ambientSoundTime = -120;
            PlayMobSound("ambient");
        }
        if (jumpDelay > 0)
            jumpDelay--;
        if (InLava && !IsFireImmune)
        {
            Ignite(15);
            await DamageEnvironmentAsync(4);
        }
        await TickAirSupplyAsync();
        if (WaterSensitive && (InWater || Terrain.IsRainingAt((Vector)Position.Floor())))
            await DamageEnvironmentAsync(1);

        if (Position.Y < -128)
            await DamageEnvironmentAsync(4);
        if (InWater || InLava)
            fallDistance = 0;
        else if (MovementFlags.HasFlag(MovementFlags.OnGround))
        {
            var damage = MathF.Ceiling(fallDistance - 3);
            if (this is Creeper creeper)
                creeper.FuseTicks = Math.Max(0, Math.Min(creeper.Fuse - 5, creeper.FuseTicks + (int)(fallDistance * 1.5f)));
            fallDistance = 0;
            if (damage > 0 && TakesFallDamage && this is not Chicken)
                await DamageEnvironmentAsync(damage);
        }
        else if (LastPosition.Y > Position.Y)
            fallDistance += LastPosition.Y - Position.Y;

        var feet = Terrain.GetBlock((Vector)Position.Floor());
        var floor = Terrain.GetBlock((Vector)(Position - new VectorF(0, 0.01f, 0)).Floor());
        if (feet?.Material is Material.Fire or Material.SoulFire && !IsFireImmune)
        {
            Ignite(8);
            await ApplyDamageAsync(this, 1, true);
        }
        if (floor?.Material == Material.MagmaBlock && !IsFireImmune && !Sneaking)
            await ApplyDamageAsync(this, 1, true);
        if (feet?.Material == Material.SweetBerryBush && !IsRemoved &&
            (LastPosition - Position).MagnitudeSquared() > 0.000025f)
            await ApplyDamageAsync(this, 1, true);
    }

    protected virtual async ValueTask TickAirSupplyAsync()
    {
        if (Terrain.GetBlock((Vector)EyePosition.Floor())?.Material == Material.Water &&
            !TagsRegistry.EntityType.CanBreatheUnderWater.Entries.Contains((int)Type))
        {
            Air--;
            if (Air == -20)
            {
                Air = 0;
                await DamageEnvironmentAsync(2);
            }
        }
        else
            Air = (short)Math.Min(300, Air + 4);

    }

    protected internal void PlayMobSound(string kind)
    {
        if (Silent || SoundName == null || kind == "ambient" && this is Creeper or Slime)
            return;
        var baby = this is AgeableMob { IsBaby: true } or Zombie { IsBaby: true };
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.SoundEntityPacket
        {
            EntityId = EntityId, SoundLocation = $"minecraft:entity.{(this is Wolf && kind == "shake" ? "wolf" : SoundName)}.{kind}{(this is Slime { Size: 1 } && (this is not MagmaCube || kind != "jump") ? "_small" : "")}", Category = MobSoundCategory,
            Volume = 1, Pitch = (Random.NextSingle() - Random.NextSingle()) * 0.2f + (baby ? 1.5f : 1), Seed = Random.NextInt64()
        }, EntityId);
    }
}
