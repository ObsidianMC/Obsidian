using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class Mob
{
    private int ambientSoundTime;
    protected virtual string? SoundName => null;
    protected virtual SoundCategory MobSoundCategory => SoundCategory.Neutral;

    /// <summary>Vanilla's <c>getSoundSource</c>: the category of the sounds the mob makes.</summary>
    internal SoundCategory SoundSource => MobSoundCategory;
    protected internal float EffectiveDifficulty
    {
        get
        {
            var (x, z) = Position.ToChunkCoord();
            var inhabited = Level is Obsidian.WorldData.AbstractLevel level && level.GetLoadedChunk(x, z) is Obsidian.WorldData.Chunk chunk
                ? chunk.InhabitedTime : 0;
            ReadOnlySpan<float> moon = [1, 0.75f, 0.5f, 0.25f, 0, 0.25f, 0.5f, 0.75f];
            var phase = (int)((Level.LevelData.Time / 24000 % 8 + 8) % 8);
            return CalculateDifficulty(Level.LevelData.Difficulty, Level.LevelData.GameTime, inhabited, moon[phase]);
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
    private double fallDistance;
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
        if (!IsFireImmune && InLava)
        {
            Ignite(15);
            await DamageCombatAsync(this, 4, CombatDamageKind.FireContact);
        }
        await TickAirSupplyAsync();
        if (WaterSensitive && (InWater || Terrain.IsRainingAt((Vector)Position.Floor())))
            await DamageEnvironmentAsync(1);

        var minimumY = Level is Obsidian.WorldData.AbstractLevel level ? level.MinY : -64;
        if (Position.Y < minimumY - 64)
            await DamageCombatAsync(this, 4, CombatDamageKind.Void);
        if (InWater || InLava || HasPotionEffect((int)PotionEffect.Levitation - 1))
        {
            fallDistance = 0;
        }

        var feet = Terrain.GetBlock((Vector)Position.Floor());
        if (feet?.RegistryId is BlockIds.Fire or BlockIds.SoulFire && !IsFireImmune)
        {
            Ignite(8);
            await DamageCombatAsync(this, 1, CombatDamageKind.FireContact);
        }

        await TickBlockHazardsAsync(feet);
    }

    protected virtual async ValueTask TickAirSupplyAsync()
    {
        if (Terrain.IsInFluid(EyePosition, water: true)
            && !TagsRegistry.EntityType.CanBreatheUnderWater.Entries.Contains(TypeId)
            && !HasPotionEffect((int)PotionEffect.WaterBreathing - 1)
            && !HasPotionEffect((int)PotionEffect.BreathOfTheNautilus - 1))
        {
            Air--;
            if (Air == -20)
            {
                Air = 0;
                await DamageEnvironmentAsync(2);
            }
        }
        else
        {
            Air = (short)Math.Min(300, Air + 4);
        }
    }

    protected internal virtual void PlayMobSound(string kind)
    {
        if (Silent || SoundName == null || kind == "ambient" && this is Creeper or Slime)
            return;
        var baby = this is IAgeable { IsBaby: true };
        var blockDamage = IncomingDamageKind
            is CombatDamageKind.Fall or CombatDamageKind.HotFloor or CombatDamageKind.SweetBerryBush
                or CombatDamageKind.Cactus or CombatDamageKind.Campfire;
        if (kind == "hurt" && blockDamage && Level is AbstractLevel world)
        {
            var pitch = (Random.NextSingle() - Random.NextSingle()) * 0.2f + (baby ? 1.5f : 1);
            LevelSounds.Play(world, Position, $"minecraft:entity.{SoundName}.hurt", MobSoundCategory, 1, pitch);
            return;
        }

        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.SoundEntityPacket
        {
            EntityId = EntityId, SoundLocation = $"minecraft:entity.{(this is Wolf && kind == "shake" ? "wolf" : SoundName)}.{kind}{(this is Slime { Size: 1 } && (this is not MagmaCube || kind != "jump") ? "_small" : "")}", Category = MobSoundCategory,
            Volume = 1, Pitch = (Random.NextSingle() - Random.NextSingle()) * 0.2f + (baby ? 1.5f : 1), Seed = Random.NextInt64()
        }, EntityId);
    }
}
