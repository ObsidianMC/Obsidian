using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A minecart with TNT, like vanilla's <c>MinecartTNT</c>: a powered activator rail, fire, an explosion or breaking it
/// while it rolls lights it, and it goes off when its fuse burns down, when it crashes into a block fast, or when it
/// falls three blocks or more. The faster it goes, the bigger the blast; a lit one spares the rails it's on.
/// </summary>
[MinecraftEntity("minecraft:tnt_minecart")]
public sealed partial class TntMinecart : AbstractMinecart
{
    // Vanilla's fuse once lit, and the entity event that tells clients it's lit.
    private const int LitFuse = 80;
    private const byte PrimeEvent = 10;

    private const float DefaultPower = 4;
    private const float DefaultSpeedFactor = 1;

    // Vanilla's: crashing or breaking it this fast (squared horizontal speed) sets it off.
    private const double DangerousSpeedSquared = 0.01;

    public TntMinecart() => this.Type = EntityType.TntMinecart;

    /// <summary>The ticks before it explodes, or -1 while it isn't lit (vanilla's <c>fuse</c>).</summary>
    public int Fuse { get; set; } = -1;

    /// <summary>The blast's power at rest (vanilla's <c>explosion_power</c>).</summary>
    public float ExplosionPower { get; set; } = DefaultPower;

    /// <summary>How much its speed adds to the blast (vanilla's <c>explosion_speed_factor</c>).</summary>
    public float ExplosionSpeedFactor { get; set; } = DefaultSpeedFactor;

    /// <summary>Whether it's lit (vanilla's <c>isPrimed</c>).</summary>
    public bool IsPrimed => this.Fuse > -1;

    // Who lit it, whom the explosion is blamed on.
    private IEntity? igniter;

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.TntMinecart);

    // Vanilla's damageSourceIgnitesTnt: fire, explosions and burning projectiles.
    private protected override bool ShouldSourceDestroy(VehicleDamage source) => Ignites(source);

    private static bool Ignites(VehicleDamage source) =>
        source is VehicleDamage.Fire or VehicleDamage.Explosion or VehicleDamage.BurningProjectile;

    // Vanilla's MinecartTNT.hurtServer: a burning arrow sets it off at once, as hard as the arrow flew.
    private protected override async ValueTask OnHurtAsync(IEntity? attacker, VehicleDamage source, VectorD projectileMotion)
    {
        if (source == VehicleDamage.BurningProjectile)
            await this.ExplodeAsync(attacker, projectileMotion.MagnitudeSquared());
    }

    // Vanilla's MinecartTNT.destroy: what lights TNT, or breaking it while it rolls, lights it with a short random
    // fuse instead of breaking it.
    private protected override async ValueTask DestroyAsync(IEntity? attacker, VehicleDamage source)
    {
        var speed = this.Motion.X * this.Motion.X + this.Motion.Z * this.Motion.Z;
        if (Ignites(source) || speed >= DangerousSpeedSquared)
        {
            if (this.Fuse < 0)
            {
                this.Prime(attacker);
                this.Fuse = Random.Shared.Next(20) + Random.Shared.Next(20);
            }

            return;
        }

        await this.DestroyAsync(this.DropItem);
    }

    // Vanilla's MinecartTNT.activateMinecart: a powered activator rail lights it.
    private protected override ValueTask ActivateAsync(Vector rail, bool powered)
    {
        if (powered && this.Fuse < 0)
            this.Prime(null);

        return default;
    }

    private protected override async ValueTask AfterMoveAsync()
    {
        var speed = this.Motion.X * this.Motion.X + this.Motion.Z * this.Motion.Z;

        // Vanilla's causeFallDamage: a fall of three blocks or more sets it off.
        if (this.LandedFrom >= 3)
        {
            var fall = this.LandedFrom / 10;
            await this.ExplodeAsync(this.igniter, fall * fall);
            if (this.IsRemoved)
                return;
        }

        if (this.Fuse > 0)
            this.Fuse--;
        else if (this.Fuse == 0)
            await this.ExplodeAsync(this.igniter, speed);

        if (!this.IsRemoved && this.HorizontalCollision && speed >= DangerousSpeedSquared)
            await this.ExplodeAsync(this.igniter, speed);
    }

    /// <summary>
    /// Vanilla's <c>primeFuse</c>: lights it with an 80-tick fuse, hissing, where <c>tnt_explodes</c> is on.
    /// </summary>
    internal void Prime(IEntity? source)
    {
        if (this.Level is not AbstractLevel level || !level.LevelData.GetBooleanRule("tnt_explodes"))
            return;

        this.Fuse = LitFuse;
        if (source is not null)
            this.igniter ??= source;

        this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new EntityEventPacket
        {
            EntityId = this.EntityId,
            Event = PrimeEvent
        });

        if (this.Silent)
            return;

        this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new SoundPacket
        {
            SoundLocation = "minecraft:entity.tnt.primed",
            SoundPosition = new SoundPosition(this.Position.X, this.Position.Y, this.Position.Z),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });
    }

    // Vanilla's MinecartTNT.explode: the power grows with the speed, up to five blocks a tick.
    private async ValueTask ExplodeAsync(IEntity? source, double speedSquared)
    {
        if (this.IsRemoved || this.Level is not AbstractLevel level)
            return;

        if (!level.LevelData.GetBooleanRule("tnt_explodes"))
        {
            if (this.IsPrimed)
                await this.DiscardAsync();

            return;
        }

        var speed = Math.Min(Math.Sqrt(speedSquared), 5);
        var power = (float)(this.ExplosionPower + this.ExplosionSpeedFactor * Random.Shared.NextDouble() * 1.5 * speed);
        var primed = this.IsPrimed;
        var center = this.Position;

        await this.DiscardAsync();
        await level.ExplodeAsync(center, power, this, source ?? this.igniter, AbstractLevel.ExplosionInteraction.Tnt,
            (position, block) => primed && (Rails.IsRail(block) || Rails.IsRail(level.BlockUpdates.GetBlock(position + Vector.Up))));
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<int>("fuse", this.Fuse));
        tag.SetOrRemove("explosion_power", this.ExplosionPower == DefaultPower
            ? null
            : new NbtTag<float>("explosion_power", this.ExplosionPower));
        tag.SetOrRemove("explosion_speed_factor", this.ExplosionSpeedFactor == DefaultSpeedFactor
            ? null
            : new NbtTag<float>("explosion_speed_factor", this.ExplosionSpeedFactor));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.Fuse = tag.TryGetNumber<int>("fuse", out var fuse) ? fuse : -1;
        this.ExplosionPower = tag.TryGetNumber<float>("explosion_power", out var power)
            ? Math.Clamp(power, 0, 128)
            : DefaultPower;
        this.ExplosionSpeedFactor = tag.TryGetNumber<float>("explosion_speed_factor", out var factor)
            ? Math.Clamp(factor, 0, 128)
            : DefaultSpeedFactor;
    }
}
