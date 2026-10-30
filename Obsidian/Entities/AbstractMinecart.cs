using Obsidian.Nbt;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;

namespace Obsidian.Entities;

/// <summary>
/// A minecart, like vanilla's <c>AbstractMinecart</c> moved by its <c>OldMinecartBehavior</c> (the experimental
/// minecart improvements aren't modelled): it follows the rails it's on, rolls down slopes, speeds up on powered rails
/// and brakes on unpowered ones, and on activator rails does what its kind does. Off rails it slides and falls.
/// </summary>
public abstract class AbstractMinecart : VehicleEntity
{
    // Vanilla's OldMinecartBehavior: the most a minecart moves a tick, on land and in water.
    private const double MaxSpeedOnLand = 0.4;
    private const double MaxSpeedInWater = 0.2;

    // Vanilla's slope pull and powered rail boost.
    private const double SlopeAcceleration = 0.0078125;
    private const double PoweredBoost = 0.06;

    // The yaw with the precision vanilla keeps (Yaw is sent at a 256th of a turn).
    private float yaw;
    private bool flipped;
    private bool hasTicked;

    private IBlock? displayBlock;
    private int? displayOffset;

    /// <summary>Whether it's on a rail, as of its last tick (vanilla's <c>onRails</c>).</summary>
    public bool OnRails { get; private set; }

    /// <summary>
    /// The block shown in it instead of its kind's (vanilla's <c>DATA_ID_CUSTOM_DISPLAY_BLOCK</c>), if any.
    /// </summary>
    public IBlock? DisplayBlock
    {
        get => this.displayBlock;
        set
        {
            this.displayBlock = value;
            this.DataChanged = true;
        }
    }

    /// <summary>
    /// How far up the shown block sits, in sixteenths (vanilla's <c>DATA_ID_DISPLAY_OFFSET</c>).
    /// </summary>
    public int DisplayOffset
    {
        get => this.displayOffset ?? this.DefaultDisplayOffset;
        set
        {
            this.displayOffset = value;
            this.DataChanged = true;
        }
    }

    /// <summary>Vanilla's <c>getDefaultDisplayOffset</c>.</summary>
    protected virtual int DefaultDisplayOffset => 6;

    /// <summary>Whether players and mobs can ride it (vanilla's <c>isRideable</c>).</summary>
    internal virtual bool IsRideable => false;

    /// <summary>Whether it's a furnace minecart, which pushes the carts it bumps (vanilla's <c>isFurnace</c>).</summary>
    internal virtual bool IsFurnace => false;

    // Vanilla's getDefaultGravity: slower in water.
    internal override double Gravity => this.NoGravity ? 0 : this.InWater ? 0.005 : 0.04;

    /// <summary>Vanilla's <c>getMaxSpeed</c>.</summary>
    protected virtual double MaxSpeed => this.InWater ? MaxSpeedInWater : MaxSpeedOnLand;

    // Vanilla's minecart passenger attachment, which villagers and wandering traders sit lower than.
    protected override VectorD PassengerAttachment(int index, Entity passenger) =>
        passenger is Villager or WanderingTrader ? VectorD.Zero : new VectorD(0, 0.1875, 0);

    // Vanilla's animateHurt-free activator reaction: what each kind does on an activator rail, powered or not.
    private protected virtual ValueTask ActivateAsync(Vector rail, bool powered) => default;

    public override async ValueTask TickAsync()
    {
        if (!await this.TickVehicleAsync())
            return;

        var oldPosition = this.Position;
        var oldYaw = this.yaw;
        if (!this.hasTicked)
            this.yaw = this.Yaw.Degrees;

        await this.TickBehaviorAsync(oldPosition, oldYaw);
        if (this.IsRemoved)
            return;

        // Vanilla's updateInWaterStateAndDoFluidPushing, after the move.
        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        this.InWater = this.Terrain.ContainsFluid(bounds, water: true);
        if (this.InWater)
            this.Motion += this.Terrain.FluidPush(bounds, true, this.Motion);
        if (this.InLava)
            this.FallDistance *= 0.5;

        this.hasTicked = true;
        this.Yaw = this.yaw;
        await this.AfterMoveAsync();
        if (this.IsRemoved)
            return;

        this.FinishTick(oldPosition);
    }

    /// <summary>
    /// What a minecart's kind does after it moved each tick (vanilla's subclasses' <c>tick</c> after their super's).
    /// </summary>
    private protected virtual ValueTask AfterMoveAsync() => default;

    // Vanilla's OldMinecartBehavior.tick on the server.
    private async ValueTask TickBehaviorAsync(VectorD oldPosition, float oldYaw)
    {
        if (!this.NoGravity)
            this.Motion -= new VectorD(0, this.Gravity, 0);

        var railPosition = this.CurrentBlockPositionOrRailBelow();
        var rail = this.Terrain.GetBlock(railPosition);
        this.OnRails = Rails.IsRail(rail);

        if (this.OnRails)
        {
            this.MoveAlongTrack(railPosition, rail!);
            if (rail!.Is(BlockIds.ActivatorRail))
                await this.ActivateAsync(railPosition, Rails.IsPowered(rail));
        }
        else
        {
            this.ComeOffTrack();
        }

        if (this.IsRemoved)
            return;

        // It faces the way it went, turning round rather than snapping more than 170 degrees.
        this.Pitch = 0;
        var dx = oldPosition.X - this.Position.X;
        var dz = oldPosition.Z - this.Position.Z;
        if (dx * dx + dz * dz > 0.001)
        {
            this.yaw = (float)(Math.Atan2(dz, dx) * 180 / Math.PI);
            if (this.flipped)
                this.yaw += 180;
        }

        var turn = WrapDegrees(this.yaw - oldYaw);
        if (turn is < -170 or >= 170)
        {
            this.yaw += 180;
            this.flipped = !this.flipped;
        }

        this.yaw %= 360;
        this.PushAndPickUpEntities();
        this.PushedByLiving();
    }

    /// <summary>
    /// Vanilla's <c>getCurrentBlockPosOrRailBelow</c>: the block it's in, or the rail under it when it sits on top of
    /// one.
    /// </summary>
    internal Vector CurrentBlockPositionOrRailBelow()
    {
        var position = (Vector)this.Position.Floor();
        return Rails.IsRail(this.Terrain.GetBlock(position + Vector.Down)) ? position + Vector.Down : position;
    }

    // Vanilla's OldMinecartBehavior.moveAlongTrack.
    private void MoveAlongTrack(Vector railPosition, IBlock rail)
    {
        this.FallDistance = 0;
        var x = this.Position.X;
        var y = (double)railPosition.Y;
        var z = this.Position.Z;
        var before = this.PositionOnRail(this.Position.X, this.Position.Y, this.Position.Z);

        var powered = false;
        var braking = false;
        if (rail.Is(BlockIds.PoweredRail))
        {
            powered = Rails.IsPowered(rail);
            braking = !powered;
        }

        var slope = SlopeAcceleration;
        if (this.InWater)
            slope *= 0.2;

        var shape = Rails.ShapeOf(rail);
        switch (shape)
        {
            case RailShape.AscendingEast:
                this.Motion += new VectorD(-slope, 0, 0);
                y++;
                break;
            case RailShape.AscendingWest:
                this.Motion += new VectorD(slope, 0, 0);
                y++;
                break;
            case RailShape.AscendingNorth:
                this.Motion += new VectorD(0, 0, slope);
                y++;
                break;
            case RailShape.AscendingSouth:
                this.Motion += new VectorD(0, 0, -slope);
                y++;
                break;
        }

        // Its motion turns along the rail, keeping its speed (up to 2).
        var motion = this.Motion;
        var (first, second) = Rails.Exits(shape);
        double alongX = second.X - first.X;
        double alongZ = second.Z - first.Z;
        var length = Math.Sqrt(alongX * alongX + alongZ * alongZ);
        if (motion.X * alongX + motion.Z * alongZ < 0)
        {
            alongX = -alongX;
            alongZ = -alongZ;
        }

        var speed = Math.Min(2, HorizontalLength(motion));
        this.Motion = new VectorD(speed * alongX / length, motion.Y, speed * alongZ / length);

        // A player riding it nudges a cart that's almost stopped the way they walk, braking or not.
        if (this.Passengers.Count > 0 && this.Passengers[0] is IPlayer rider && MoveIntent(rider) is VectorD intent)
        {
            if (HorizontalLengthSquared(this.Motion) < 0.01)
            {
                this.Motion += new VectorD(intent.X * 0.001, 0, intent.Z * 0.001);
                braking = false;
            }
        }

        if (braking)
        {
            this.Motion = HorizontalLength(this.Motion) < 0.03
                ? VectorD.Zero
                : new VectorD(this.Motion.X * 0.5, 0, this.Motion.Z * 0.5);
        }

        // It's set onto the line between the rail's ends.
        var startX = railPosition.X + 0.5 + first.X * 0.5;
        var startZ = railPosition.Z + 0.5 + first.Z * 0.5;
        var endX = railPosition.X + 0.5 + second.X * 0.5;
        var endZ = railPosition.Z + 0.5 + second.Z * 0.5;
        alongX = endX - startX;
        alongZ = endZ - startZ;

        double progress;
        if (alongX == 0)
            progress = z - railPosition.Z;
        else if (alongZ == 0)
            progress = x - railPosition.X;
        else
            progress = ((x - startX) * alongX + (z - startZ) * alongZ) * 2;

        x = startX + alongX * progress;
        z = startZ + alongZ * progress;
        this.SetPosition(new VectorD(x, y, z));

        var factor = this.Passengers.Count > 0 ? 0.75 : 1;
        var maxSpeed = this.MaxSpeed;
        motion = this.Motion;
        this.Move(new VectorD(
            Math.Clamp(factor * motion.X, -maxSpeed, maxSpeed),
            0,
            Math.Clamp(factor * motion.Z, -maxSpeed, maxSpeed)));

        // Reaching a slope's low end, it steps down; its high end, up.
        var floorX = (int)Math.Floor(this.Position.X) - railPosition.X;
        var floorZ = (int)Math.Floor(this.Position.Z) - railPosition.Z;
        if (first.Y != 0 && floorX == first.X && floorZ == first.Z)
            this.SetPosition(this.Position + new VectorD(0, first.Y, 0));
        else if (second.Y != 0 && floorX == second.X && floorZ == second.Z)
            this.SetPosition(this.Position + new VectorD(0, second.Y, 0));

        this.Motion = this.ApplyNaturalSlowdown(this.Motion);

        // Going down speeds it up, going up slows it.
        var after = this.PositionOnRail(this.Position.X, this.Position.Y, this.Position.Z);
        if (after is VectorD end && before is VectorD start)
        {
            var drop = (start.Y - end.Y) * 0.05;
            motion = this.Motion;
            var horizontal = HorizontalLength(motion);
            if (horizontal > 0)
            {
                this.Motion = new VectorD(
                    motion.X * (horizontal + drop) / horizontal,
                    motion.Y,
                    motion.Z * (horizontal + drop) / horizontal);
            }

            this.SetPosition(new VectorD(this.Position.X, end.Y, this.Position.Z));
        }

        // Leaving the rail's block, it heads straight for the next.
        var blockX = (int)Math.Floor(this.Position.X);
        var blockZ = (int)Math.Floor(this.Position.Z);
        if (blockX != railPosition.X || blockZ != railPosition.Z)
        {
            motion = this.Motion;
            var horizontal = HorizontalLength(motion);
            this.Motion = new VectorD(
                horizontal * (blockX - railPosition.X),
                motion.Y,
                horizontal * (blockZ - railPosition.Z));
        }

        if (!powered)
            return;

        motion = this.Motion;
        var current = HorizontalLength(motion);
        if (current > 0.01)
        {
            this.Motion += new VectorD(motion.X / current * PoweredBoost, 0, motion.Z / current * PoweredBoost);
            return;
        }

        // A stopped cart on a powered rail starts off away from a solid block at one end.
        var pushX = motion.X;
        var pushZ = motion.Z;
        if (shape == RailShape.EastWest)
        {
            if (this.IsRedstoneConductor(railPosition + new Vector(-1, 0, 0)))
                pushX = 0.02;
            else if (this.IsRedstoneConductor(railPosition + new Vector(1, 0, 0)))
                pushX = -0.02;
        }
        else if (shape == RailShape.NorthSouth)
        {
            if (this.IsRedstoneConductor(railPosition + new Vector(0, 0, -1)))
                pushZ = 0.02;
            else if (this.IsRedstoneConductor(railPosition + new Vector(0, 0, 1)))
                pushZ = -0.02;
        }
        else
        {
            return;
        }

        this.Motion = new VectorD(pushX, motion.Y, pushZ);
    }

    private bool IsRedstoneConductor(Vector position) =>
        this.Terrain.GetBlock(position)?.IsRedstoneConductor() == true;

    // Vanilla's AbstractMinecart.comeOffTrack.
    private void ComeOffTrack()
    {
        var maxSpeed = this.MaxSpeed;
        var motion = this.Motion;
        this.Motion = new VectorD(
            Math.Clamp(motion.X, -maxSpeed, maxSpeed),
            motion.Y,
            Math.Clamp(motion.Z, -maxSpeed, maxSpeed));

        if (this.MovementFlags.HasFlag(MovementFlags.OnGround))
            this.Motion *= 0.5;

        this.Move(this.Motion);

        if (!this.MovementFlags.HasFlag(MovementFlags.OnGround))
            this.Motion *= 0.95;
    }

    /// <summary>
    /// Vanilla's <c>applyNaturalSlowdown</c>: rolling friction, less with a passenger, more in water. It stops any
    /// vertical motion.
    /// </summary>
    private protected virtual VectorD ApplyNaturalSlowdown(VectorD motion)
    {
        var factor = this.Passengers.Count > 0 ? 0.997 : 0.96;
        var slowed = new VectorD(motion.X * factor, 0, motion.Z * factor);
        return this.InWater ? slowed * 0.95 : slowed;
    }

    /// <summary>
    /// Vanilla's <c>OldMinecartBehavior.getPos</c>: where on the rail at a point a cart would be, or null off rails.
    /// </summary>
    private VectorD? PositionOnRail(double x, double y, double z)
    {
        var blockX = (int)Math.Floor(x);
        var blockY = (int)Math.Floor(y);
        var blockZ = (int)Math.Floor(z);
        if (Rails.IsRail(this.Terrain.GetBlock(new Vector(blockX, blockY - 1, blockZ))))
            blockY--;

        var rail = this.Terrain.GetBlock(new Vector(blockX, blockY, blockZ));
        if (!Rails.IsRail(rail))
            return null;

        var (first, second) = Rails.Exits(Rails.ShapeOf(rail!));
        var startX = blockX + 0.5 + first.X * 0.5;
        var startY = blockY + 0.0625 + first.Y * 0.5;
        var startZ = blockZ + 0.5 + first.Z * 0.5;
        var endX = blockX + 0.5 + second.X * 0.5;
        var endY = blockY + 0.0625 + second.Y * 0.5;
        var endZ = blockZ + 0.5 + second.Z * 0.5;

        var alongX = endX - startX;
        var alongY = (endY - startY) * 2;
        var alongZ = endZ - startZ;

        double progress;
        if (alongX == 0)
            progress = z - blockZ;
        else if (alongZ == 0)
            progress = x - blockX;
        else
            progress = ((x - startX) * alongX + (z - startZ) * alongZ) * 2;

        x = startX + alongX * progress;
        y = startY + alongY * progress;
        z = startZ + alongZ * progress;
        if (alongY < 0)
            y++;
        else if (alongY > 0)
            y += 0.5;

        return new VectorD(x, y, z);
    }

    // Vanilla's OldMinecartBehavior.pushAndPickupEntities: a rideable cart moving fast enough picks up the mobs it
    // runs into while it's empty, and pushes away what it doesn't; otherwise it only pushes other minecarts.
    private void PushAndPickUpEntities()
    {
        var box = this.Dimension.CreateBBFromPosition(this.Position);
        var bounds = new BoundingBox(box.Min - new VectorD(0.2, 0, 0.2), box.Max + new VectorD(0.2, 0, 0.2));

        if (this.IsRideable && HorizontalLengthSquared(this.Motion) >= 0.01)
        {
            foreach (var entity in this.PushableEntitiesIn(bounds).ToArray())
            {
                if (entity is IPlayer or IronGolem or AbstractMinecart
                    || this.Passengers.Count > 0
                    || entity.Vehicle is not null)
                {
                    PushedBy(entity, this);
                }
                else
                {
                    this.Board(entity);
                }
            }

            return;
        }

        foreach (var cart in this.PushableEntitiesIn(bounds).OfType<AbstractMinecart>().ToArray())
        {
            if (cart.Vehicle is null)
                cart.Push(this);
        }
    }

    // Vanilla's LivingEntity.pushEntities for the living things overlapping the cart: each pushes it.
    private void PushedByLiving()
    {
        var box = this.Dimension.CreateBBFromPosition(this.Position);
        foreach (var living in this.PushableEntitiesIn(box).OfType<Living>().ToArray())
            this.Push(living);
    }

    // Entity.push(Entity) on whatever the cart ran into: a minecart's own rules, or the plain push apart.
    private static void PushedBy(Entity entity, AbstractMinecart cart)
    {
        if (entity is AbstractMinecart other)
            other.Push(cart);
        else
            PushApart(entity, cart);
    }

    /// <summary>
    /// Vanilla's <c>AbstractMinecart.push(Entity)</c>: the cart and what touches it push apart, minecarts by their own
    /// rules.
    /// </summary>
    private void Push(Entity entity)
    {
        if (entity.Vehicle == this)
            return;

        var dx = entity.Position.X - this.Position.X;
        var dz = entity.Position.Z - this.Position.Z;
        var distance = dx * dx + dz * dz;
        if (distance < 1.0E-4)
            return;

        distance = Math.Sqrt(distance);
        dx /= distance;
        dz /= distance;
        var scale = Math.Min(1, 1 / distance);
        dx *= scale * 0.1 * 0.5;
        dz *= scale * 0.1 * 0.5;

        if (entity is AbstractMinecart other)
        {
            this.PushOtherMinecart(other, dx, dz);
            return;
        }

        this.Motion += new VectorD(-dx, 0, -dz);
        if (entity is not IPlayer)
            entity.Motion += new VectorD(dx / 4, 0, dz / 4);
    }

    // Vanilla's OldMinecartBehavior pushOtherMinecart: carts in line share their speed; a furnace minecart drives
    // the cart it bumps.
    private void PushOtherMinecart(AbstractMinecart other, double dx, double dz)
    {
        var towardX = other.Position.X - this.Position.X;
        var towardZ = other.Position.Z - this.Position.Z;
        var toward = Normalize(new VectorD(towardX, 0, towardZ));
        var radians = this.yaw * Math.PI / 180;
        var facing = Normalize(new VectorD(Math.Cos(radians), 0, Math.Sin(radians)));
        if (Math.Abs(toward.X * facing.X + toward.Z * facing.Z) < 0.8)
            return;

        var own = this.Motion;
        var theirs = other.Motion;
        if (other.IsFurnace && !this.IsFurnace)
        {
            this.Motion = new VectorD(own.X * 0.2, own.Y, own.Z * 0.2) + new VectorD(theirs.X - dx, 0, theirs.Z - dz);
            other.Motion = new VectorD(theirs.X * 0.95, theirs.Y, theirs.Z * 0.95);
        }
        else if (!other.IsFurnace && this.IsFurnace)
        {
            other.Motion = new VectorD(theirs.X * 0.2, theirs.Y, theirs.Z * 0.2) + new VectorD(own.X + dx, 0, own.Z + dz);
            this.Motion = new VectorD(own.X * 0.95, own.Y, own.Z * 0.95);
        }
        else
        {
            var averageX = (theirs.X + own.X) / 2;
            var averageZ = (theirs.Z + own.Z) / 2;
            this.Motion = new VectorD(own.X * 0.2, own.Y, own.Z * 0.2) + new VectorD(averageX - dx, 0, averageZ - dz);
            other.Motion = new VectorD(theirs.X * 0.2, theirs.Y, theirs.Z * 0.2) + new VectorD(averageX + dx, 0, averageZ + dz);
        }
    }

    /// <summary>
    /// Vanilla's <c>ServerPlayer.getLastClientMoveIntent</c>: the way the player's keys point, turned by their yaw,
    /// or null when they press none.
    /// </summary>
    private static VectorD? MoveIntent(IPlayer player)
    {
        var input = player.Input;
        var strafe = input.HasFlag(PlayerInput.Left) == input.HasFlag(PlayerInput.Right)
            ? 0
            : input.HasFlag(PlayerInput.Left) ? 1 : -1;
        var forward = input.HasFlag(PlayerInput.Forward) == input.HasFlag(PlayerInput.Backward)
            ? 0
            : input.HasFlag(PlayerInput.Forward) ? 1 : -1;

        var length = strafe * strafe + forward * forward;
        if (length < 1.0E-7)
            return null;

        double x = strafe;
        double z = forward;
        if (length > 1)
        {
            var root = Math.Sqrt(length);
            x /= root;
            z /= root;
        }

        var radians = player.Yaw.Degrees * Math.PI / 180;
        var sin = Math.Sin(radians);
        var cos = Math.Cos(radians);
        return new VectorD(x * cos - z * sin, 0, z * cos + x * sin);
    }

    // Vanilla's Mth.wrapDegrees.
    private static float WrapDegrees(float degrees)
    {
        var wrapped = degrees % 360;
        if (wrapped >= 180)
            wrapped -= 360;
        if (wrapped < -180)
            wrapped += 360;

        return wrapped;
    }

    private static double HorizontalLength(VectorD vector) => Math.Sqrt(HorizontalLengthSquared(vector));

    private static double HorizontalLengthSquared(VectorD vector) => vector.X * vector.X + vector.Z * vector.Z;

    private static VectorD Normalize(VectorD vector)
    {
        var length = vector.Magnitude;
        return length < 1.0E-4 ? VectorD.Zero : vector / length;
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Remove("DisplayState");
        if (this.displayBlock is IBlock shown)
            tag.Add(Obsidian.WorldData.BlockStateNbt.Write(shown, "DisplayState"));

        tag.SetOrRemove("DisplayOffset", this.DisplayOffset == this.DefaultDisplayOffset
            ? null
            : new NbtTag<int>("DisplayOffset", this.DisplayOffset));
        tag.Set(new NbtTag<bool>("FlippedRotation", this.flipped));
        tag.Set(new NbtTag<bool>("HasTicked", this.hasTicked));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag("DisplayState", out var state))
            this.displayBlock = Obsidian.WorldData.BlockStateNbt.Read(state);

        this.displayOffset = tag.TryGetTagValue<int>("DisplayOffset", out var offset) ? offset : null;
        this.flipped = tag.TryGetBool("FlippedRotation", out var flipped) && flipped;
        this.hasTicked = tag.TryGetBool("HasTicked", out var ticked) && ticked;
        this.yaw = this.Yaw.Degrees;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        // Vanilla's optional block state: the state's id, with 0 (air) for none, which shows its kind's block.
        writer.WriteEntityMetadataType(11, EntityMetadataType.OptionalBlockState);
        writer.WriteVarInt(this.displayBlock?.GetHashCode() ?? 0);

        writer.WriteEntityMetadataType(12, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.DisplayOffset);

        this.MetadataIndex = 13;
    }
}
