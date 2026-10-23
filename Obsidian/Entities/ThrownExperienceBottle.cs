using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A thrown bottle o' enchanting, like vanilla's <c>ThrownExperienceBottle</c>: it shatters where it lands, splashing
/// and releasing 3 to 11 experience.
/// </summary>
[MinecraftEntity("minecraft:experience_bottle")]
public sealed partial class ThrownExperienceBottle : Throwable
{
    // Vanilla's: the bottle falls faster than other thrown items, and is thrown upward a little.
    private const double Gravity = 0.07;
    private const float Speed = 0.7f;
    private const float PitchOffset = -20;

    // Vanilla's splash color for the bottle, an argb value.
    private const int SplashColor = -13083194;

    private int age;
    private bool shattered;

    public ThrownExperienceBottle() => this.Type = EntityType.ExperienceBottle;

    internal IEntity? Owner { get; set; }

    /// <summary>The item clients show.</summary>
    public ItemStack Item { get; internal set; } = new(ItemsRegistry.ExperienceBottle, 1);

    /// <summary>
    /// Vanilla's <c>ExperienceBottleItem.use</c>: throws a bottle from <paramref name="player"/>, using one up unless
    /// the player is in creative mode.
    /// </summary>
    internal static async ValueTask<bool> ThrowAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
    {
        if (!player.Alive || player.Respawning || player.GameMode == GameMode.Spectator || player.Level is not AbstractLevel level)
            return false;

        var bottle = new ThrownExperienceBottle
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Owner = player,
            Position = new VectorD(player.Position.X, player.HeadY - 0.1, player.Position.Z),
            Motion = Direction(player.Yaw.Degrees, player.Pitch.Degrees + PitchOffset) * Speed + player.CombatMotion,
            Item = new ItemStack(stack, 1)
        };
        bottle.BoundingBox = bottle.Dimension.CreateBBFromPosition(bottle.Position);

        if (!level.TrySpawnEntity(bottle))
            return false;

        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)player.Position.Floor(), new SoundPacket
        {
            SoundLocation = "minecraft:entity.experience_bottle.throw",
            SoundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = 0.5f,
            Pitch = 0.4f / (Globals.Random.NextSingle() * 0.4f + 0.8f),
            Seed = Globals.Random.NextInt64()
        });

        if (player.GameMode != GameMode.Creative)
        {
            var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
            player.Inventory.RemoveItem(slot, 1);
            await player.SyncInventorySlotAsync(slot);
        }

        player.Statistics.Increment(stack.Holder.UnlocalizedName, type: "minecraft:used");
        return true;
    }

    // Vanilla's shootFromRotation direction, with its usual slight inaccuracy.
    internal static VectorD Direction(float yaw, float pitch)
    {
        const double ToRadians = Math.PI / 180;

        var direction = new VectorD(
            -Math.Sin(yaw * ToRadians) * Math.Cos(pitch * ToRadians),
            -Math.Sin(pitch * ToRadians),
            Math.Cos(yaw * ToRadians) * Math.Cos(pitch * ToRadians));

        direction += new VectorD(
            (Globals.Random.NextDouble() - Globals.Random.NextDouble()) * 0.0172275,
            (Globals.Random.NextDouble() - Globals.Random.NextDouble()) * 0.0172275,
            (Globals.Random.NextDouble() - Globals.Random.NextDouble()) * 0.0172275);

        return direction / direction.Magnitude;
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) =>
        base.SpawnEntity(velocity ?? new Velocity(this.Motion.X, this.Motion.Y, this.Motion.Z), this.Owner?.EntityId ?? additionalData);

    public override async ValueTask TickAsync()
    {
        if (this.shattered || this.Level is not AbstractLevel level || !level.IsMobTicking(this.Position))
            return;

        if (++this.age > 1200 || this.Position.Y < -128)
        {
            await this.RemoveAsync();
            return;
        }

        var terrain = new MobTerrain(level);
        var swept = new BoundingBox(
            VectorD.Min(this.Position, this.Position + this.Motion) - new VectorD(0.3f),
            VectorD.Max(this.Position, this.Position + this.Motion) + new VectorD(0.3f));
        var fraction = 1d;
        var hitEntity = false;

        foreach (var shape in terrain.GetCollisions(swept))
        {
            if (MobTerrain.RayIntersection(shape, this.Position, this.Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        }

        foreach (var candidate in level.GetEntitiesInRange(this.Position, (float)this.Motion.Magnitude + 4))
        {
            if (candidate is not Living { Health: > 0 }
                || ReferenceEquals(candidate, this.Owner) && this.age < 5
                || candidate is IPlayer { GameMode: GameMode.Spectator })
                continue;

            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            var grown = new BoundingBox(bounds.Min - new VectorD(0.3f), bounds.Max + new VectorD(0.3f));
            if (MobTerrain.RayIntersection(grown, this.Position, this.Motion) is double hit && hit < fraction)
            {
                fraction = hit;
                hitEntity = true;
            }
        }

        var next = this.Position + this.Motion * fraction;
        if (!level.TryMoveEntity(this, this.Position, next))
            return;

        this.Position = next;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);

        if (fraction < 1 || hitEntity)
        {
            await this.ShatterAsync(level);
            return;
        }

        var water = terrain.GetBlock((Vector)this.Position.Floor())?.RegistryId == BlockIds.Water;
        this.Motion *= water ? 0.8 : 0.99;
        if (!this.NoGravity)
            this.Motion -= new VectorD(0, Gravity, 0);

        this.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, new TeleportEntityPacket
        {
            EntityId = this.EntityId,
            Position = this.Position,
            Delta = this.Motion,
            Yaw = this.Yaw,
            Pitch = this.Pitch,
            OnGround = false
        });
    }

    // Vanilla's onHit: the splash, then 3 to 11 experience, in orbs.
    private async ValueTask ShatterAsync(AbstractLevel level)
    {
        this.shattered = true;
        level.EmitGameEvent(MobGameEvent.ProjectileLand, this.Position, this, this.Owner);

        var block = (Vector)this.Position.Floor();
        level.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, new LevelEventPacket(2002, block, SplashColor));

        var experience = 3 + Globals.Random.Next(5) + Globals.Random.Next(5);
        level.SpawnExperienceOrbs(this.Position, (short)experience);

        await this.RemoveAsync();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(this.Item.ToNbt("Item"));
        if (this.Owner is { } owner)
            tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(owner.Uuid)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack)
            this.Item = new ItemStack(stack, 1);

        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
    }
}
