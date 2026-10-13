using Obsidian.API.Inventory;
using Obsidian.Commands.Modules;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang.Structures;

namespace Obsidian.Entities;

/// <summary>
/// A thrown eye of ender, like vanilla's: it flies toward the nearest structure of
/// <c>#minecraft:eye_of_ender_located</c> (strongholds, or a plugin's structures added to the tag) for four seconds,
/// then drops as an item or breaks.
/// </summary>
/// <remarks>
/// Eyes aren't saved: they live four seconds, and where they head isn't kept, so one loaded again couldn't fly on.
/// </remarks>
[MinecraftEntity("minecraft:eye_of_ender")]
public sealed partial class EyeOfEnder : Entity
{
    /// <summary>
    /// The structures eyes find.
    /// </summary>
    internal const string LocatedTag = "minecraft:eye_of_ender_located";

    // Vanilla's: an eye flies 80 ticks, at most 12 blocks toward a farther structure, rising 8 blocks meanwhile, and
    // drops as an item four times in five.
    private const int Lifetime = 80;

    private const double MaxFlight = 12.0;

    private const double Rise = 8.0;

    private VectorD target;

    private int life;

    private bool surviveAfterDeath;

    public EyeOfEnder() => this.Type = EntityType.EyeOfEnder;

    /// <summary>
    /// The eye item it drops, which clients show.
    /// </summary>
    public ItemStack? Item { get; internal set; }

    /// <summary>
    /// Vanilla <c>EyeOfEnder.use</c>: throws an eye from <paramref name="player"/> toward the nearest structure eyes
    /// find, using up the item unless the player is in creative. Nothing happens, and the item is kept, when there's
    /// no such structure within vanilla's search, or the player looks at an end portal frame, which takes the eye
    /// instead.
    /// </summary>
    /// <returns>Whether an eye was thrown.</returns>
    internal static async ValueTask<bool> ThrowAsync(Player player, InteractionHand hand, ItemStack stack)
    {
        if (player.GameMode == GameMode.Spectator
            || player.Level is not AbstractLevel level
            || level.Generator is not MojangGenerator generator
            || await LooksAtPortalFrameAsync(player, level))
        {
            return false;
        }

        // Vanilla searches from the player's block, like /locate.
        var origin = (Vector)player.Position.Floor();
        var found = await Task.Run(() => generator.LocateStructure(
            locator => locator.Locate(LocatedTag, origin, LocateCommandModule.SearchRadius, false)));

        if (found is not LocatedStructure structure)
            return false;

        var eye = new EyeOfEnder
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = player.Position + new VectorD(0, player.Dimension.Height * 0.5, 0),
            Item = new ItemStack(stack, 1)
        };

        eye.SignalTo((VectorD)structure.Position);

        if (!level.TryAddEntity(eye))
            return false;

        eye.SpawnEntity();
        level.EmitGameEvent(MobGameEvent.ProjectileShoot, eye.Position, player);

        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)player.Position.Floor(), new SoundPacket
        {
            SoundLocation = "minecraft:entity.ender_eye.launch",
            SoundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = 1,
            Pitch = 0.33f + Globals.Random.NextSingle() * (0.5f - 0.33f),
            Seed = Globals.Random.NextInt64()
        });

        if (player.GameMode != GameMode.Creative)
        {
            var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
            player.Inventory.RemoveItem(slot, 1);
            await player.SyncInventorySlotAsync(slot);
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>signalTo</c>: heads for <paramref name="destination"/>, or, when it's more than 12 blocks away, for a
    /// point 12 blocks toward it and 8 blocks up.
    /// </summary>
    internal void SignalTo(VectorD destination)
    {
        this.target = FlightTarget(this.Position, destination);
        this.life = 0;
        this.surviveAfterDeath = Globals.Random.Next(5) > 0;
    }

    /// <summary>
    /// Where an eye thrown from <paramref name="from"/> heads to signal <paramref name="destination"/>.
    /// </summary>
    internal static VectorD FlightTarget(VectorD from, VectorD destination)
    {
        var dx = destination.X - from.X;
        var dz = destination.Z - from.Z;
        var distance = Math.Sqrt(dx * dx + dz * dz);

        return distance > MaxFlight
            ? from + new VectorD(dx / distance * MaxFlight, Rise, dz / distance * MaxFlight)
            : destination;
    }

    public override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level || !level.IsMobTicking(this.Position))
            return;

        var next = this.Position + this.Motion;
        this.Motion = Steer(this.Motion, next, this.target);

        if (!level.TryMoveEntity(this, this.Position, next))
            return;

        this.Position = next;

        if (++this.life > Lifetime)
        {
            await this.EndAsync(level);
            return;
        }

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

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);
    }

    // Vanilla's steering, which decides the path clients see: the horizontal speed eases toward the distance left,
    // slowing within a block of the target, while the eye climbs or sinks toward its height.
    private static VectorD Steer(VectorD motion, VectorD position, VectorD target)
    {
        var toTarget = new VectorD(target.X - position.X, 0, target.Z - position.Z);
        var distance = toTarget.Magnitude;
        var speed = Math.Sqrt(motion.X * motion.X + motion.Z * motion.Z);
        var horizontal = speed + 0.0025 * (distance - speed);
        var vertical = motion.Y;

        if (distance < 1.0)
        {
            horizontal *= 0.8;
            vertical *= 0.8;
        }

        var climb = position.Y - motion.Y < target.Y ? 1.0 : -1.0;
        var across = distance > 0 ? toTarget * (horizontal / distance) : VectorD.Zero;
        return across + new VectorD(0, vertical + (climb - vertical) * 0.015, 0);
    }

    // Vanilla's end of the flight: the eye drops as an item, or breaks with level event 2003 (its particles and
    // sound).
    private async ValueTask EndAsync(AbstractLevel level)
    {
        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)this.Position.Floor(), new SoundPacket
        {
            SoundLocation = "minecraft:entity.ender_eye.death",
            SoundPosition = new SoundPosition(this.Position.X, this.Position.Y, this.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = 1,
            Pitch = 1,
            Seed = Globals.Random.NextInt64()
        });

        await this.RemoveAsync();

        if (this.surviveAfterDeath && this.Item is not null)
        {
            level.SpawnEntity(new ItemEntity
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = this.Position,
                Item = new ItemStack(this.Item, this.Item.Count)
            });

            return;
        }

        var breaking = new LevelEventPacket(2003, (Vector)this.Position.Floor(), 0);
        this.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, breaking);
    }

    // Vanilla leaves an eye used on an end portal frame to the frame: the first block the player looks at within
    // reach, fluids aside, stepping along the look in tenths of a block.
    private static async ValueTask<bool> LooksAtPortalFrameAsync(Player player, AbstractLevel level)
    {
        var reach = player.GameMode == GameMode.Creative ? 5.0 : 4.5;
        var look = player.GetLookDirection();
        var eye = new VectorD(player.Position.X, player.HeadY, player.Position.Z);
        Vector? last = null;

        for (var step = 0.0; step <= reach; step += 0.1)
        {
            var position = (Vector)(eye + new VectorD(look.X * step, look.Y * step, look.Z * step)).Floor();
            if (position == last)
                continue;

            last = position;

            if (await level.GetBlockAsync(position) is not IBlock block || block.IsAir || block.IsLiquid)
                continue;

            return block.Is(BlockIds.EndPortalFrame);
        }

        return false;
    }
}
