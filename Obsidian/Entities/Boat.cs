using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// A boat or a bamboo raft, like vanilla's <c>AbstractBoat</c>: it floats on water, slides on land as slippery as
/// the blocks under it, and seats two. Vanilla has an entity type per wood since 1.21.2, which <see cref="Entity.Type"/>
/// holds; they share their size, which is generated from <c>oak_boat</c>.
/// </summary>
/// <remarks>
/// Like vanilla's, a boat a player paddles is moved by the player's client, which sends where it went; the server
/// checks each move (see <see cref="ClientVehicleControl"/>). Without a player at the helm the server moves it:
/// it bobs on water, drifts with the current, sinks when held under, and picks up mobs that bump into it.
/// </remarks>
[MinecraftEntity("minecraft:oak_boat")]
public partial class Boat : VehicleEntity
{
    // Vanilla's limits: a boat under water this long throws off its passengers; a bubble column above takes this long
    // to throw a boat up or pull it down.
    private const int OutOfControlLimit = 60;
    private const int BubbleTime = 60;

    // A paddle turns this far a tick, and splashes once a turn as it passes a quarter of the way round.
    private const float PaddleStep = MathF.PI / 8;
    private const double PaddleSplash = MathF.PI / 4;

    private static readonly Dictionary<EntityType, int> items = new()
    {
        [EntityType.PoplarBoat] = ItemIds.PoplarBoat,
        [EntityType.PoplarChestBoat] = ItemIds.PoplarChestBoat,
        [EntityType.OakBoat] = ItemIds.OakBoat,
        [EntityType.SpruceBoat] = ItemIds.SpruceBoat,
        [EntityType.BirchBoat] = ItemIds.BirchBoat,
        [EntityType.JungleBoat] = ItemIds.JungleBoat,
        [EntityType.AcaciaBoat] = ItemIds.AcaciaBoat,
        [EntityType.CherryBoat] = ItemIds.CherryBoat,
        [EntityType.DarkOakBoat] = ItemIds.DarkOakBoat,
        [EntityType.PaleOakBoat] = ItemIds.PaleOakBoat,
        [EntityType.MangroveBoat] = ItemIds.MangroveBoat,
        [EntityType.BambooRaft] = ItemIds.BambooRaft,
        [EntityType.OakChestBoat] = ItemIds.OakChestBoat,
        [EntityType.SpruceChestBoat] = ItemIds.SpruceChestBoat,
        [EntityType.BirchChestBoat] = ItemIds.BirchChestBoat,
        [EntityType.JungleChestBoat] = ItemIds.JungleChestBoat,
        [EntityType.AcaciaChestBoat] = ItemIds.AcaciaChestBoat,
        [EntityType.CherryChestBoat] = ItemIds.CherryChestBoat,
        [EntityType.DarkOakChestBoat] = ItemIds.DarkOakChestBoat,
        [EntityType.PaleOakChestBoat] = ItemIds.PaleOakChestBoat,
        [EntityType.MangroveChestBoat] = ItemIds.MangroveChestBoat,
        [EntityType.BambooChestRaft] = ItemIds.BambooChestRaft
    };

    private static readonly Dictionary<int, EntityType> types = items.ToDictionary(pair => pair.Value, pair => pair.Key);

    private readonly float[] paddlePositions = new float[2];

    private BoatStatus status;
    private BoatStatus oldStatus;
    private double waterLevel;
    private float landFriction;
    private double lastYd;
    private float outOfControlTicks;
    private float deltaRotation;

    private bool aboveBubbleColumn;
    private bool bubbleColumnDown;
    private int bubbleTime;

    private bool paddleLeft;
    private bool paddleRight;

    // The paddles the controlling player's client last sent, 1 for left and 2 for right, or -1 for none since the
    // last tick (see PaddleBoatPacket).
    private int pendingPaddles = -1;

    public Boat() => this.Type = EntityType.OakBoat;

    /// <summary>
    /// The boat type an item places, like vanilla's <c>BoatItem</c>, or null for other items.
    /// </summary>
    internal static EntityType? TypeFor(Item item) => types.TryGetValue(item.Id, out var type) ? type : null;

    /// <summary>
    /// Creates the boat of <paramref name="type"/>: a chest boat for chest boats and rafts.
    /// </summary>
    internal static Boat Create(EntityType type, ILevel level) => IsChestBoat(type)
        ? new ChestBoat { Level = level, Type = type }
        : new Boat { Level = level, Type = type };

    /// <summary>Whether a boat type carries a chest.</summary>
    internal static bool IsChestBoat(EntityType type) =>
        type is EntityType.BambooChestRaft || type.ToString().EndsWith("ChestBoat", StringComparison.Ordinal);

    /// <summary>Whether an entity type is a boat or raft.</summary>
    internal static bool IsBoat(EntityType type) => items.ContainsKey(type);

    /// <summary>Whether it's a bamboo raft, which rides higher.</summary>
    public bool IsRaft => this.Type is EntityType.BambooRaft or EntityType.BambooChestRaft;

    /// <summary>Whether the left paddle turns (vanilla's <c>DATA_ID_PADDLE_LEFT</c>).</summary>
    public bool PaddleLeft => this.paddleLeft;

    /// <summary>Whether the right paddle turns (vanilla's <c>DATA_ID_PADDLE_RIGHT</c>).</summary>
    public bool PaddleRight => this.paddleRight;

    internal override double Gravity => this.NoGravity ? 0 : 0.04;

    protected override Item DropItem =>
        ItemsRegistry.Get(items.TryGetValue(this.Type ?? EntityType.OakBoat, out var item) ? item : ItemIds.OakBoat);

    protected override int MaximumPassengers => 2;

    // Vanilla's getSinglePassengerXOffset: a chest boat seats its one passenger in front of the chest.
    private protected virtual float SinglePassengerOffset => 0;

    // Vanilla's getPassengerAttachmentPoint: two passengers sit front and back, animals a little further forward, at
    // a third of the boat's height (most of a raft's).
    protected override VectorD PassengerAttachment(int index, Entity passenger)
    {
        var forward = this.SinglePassengerOffset;
        if (this.Passengers.Count > 1)
        {
            forward = index == 0 ? 0.2f : -0.6f;
            if (passenger is Animal)
                forward += 0.2f;
        }

        var height = this.IsRaft ? this.Dimension.Height * 0.8888889f : this.Dimension.Height / 3;
        return this.Rotated(height, forward);
    }

    // Vanilla's canAddPassenger: room, and its eyes not under water.
    protected override bool CanAddPassenger(Entity passenger) =>
        base.CanAddPassenger(passenger) && !this.Terrain.IsInFluid(this.Position + new VectorD(0, 0.5625, 0), water: true);

    /// <summary>
    /// Takes the paddle state the controlling player's client sent, applied on the boat's next tick.
    /// </summary>
    internal void QueuePaddles(bool left, bool right) =>
        Interlocked.Exchange(ref this.pendingPaddles, (left ? 1 : 0) | (right ? 2 : 0));

    // Vanilla's AbstractBoat.interact: a player not sneaking gets in while the boat isn't sinking.
    public override ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand) =>
        ValueTask.FromResult(!player.Sneaking
            && this.outOfControlTicks < OutOfControlLimit
            && this.Board(player));

    public override async ValueTask TickAsync()
    {
        if (!await this.TickVehicleAsync())
            return;

        var oldPosition = this.Position;

        this.oldStatus = this.status;
        this.status = this.GetStatus();
        this.outOfControlTicks = this.status is BoatStatus.UnderWater or BoatStatus.UnderFlowingWater
            ? this.outOfControlTicks + 1
            : 0;
        if (this.outOfControlTicks >= OutOfControlLimit)
            this.EjectPassengers();

        var controller = this.Passengers.Count > 0 ? this.Passengers[0] as Living : null;
        var rider = controller as IPlayer;
        this.ClientControl.Tick(this, this.Terrain, rider, flying: false);

        var paddles = Interlocked.Exchange(ref this.pendingPaddles, -1);
        var clientControlled = this.ClientControl.ControlledBy(rider);
        if (clientControlled)
        {
            if (paddles >= 0)
                this.SetPaddleState((paddles & 1) != 0, (paddles & 2) != 0);

            if (this.ClientControl.FloatingTooLong)
            {
                await rider!.KickAsync(new ChatMessage { Translate = "multiplayer.disconnect.flying" });
                return;
            }

            if (this.ClientControl.Accepted is VehicleMove move)
            {
                this.Yaw = move.Yaw;
                this.Pitch = move.Pitch;
                this.MovementFlags = move.OnGround ? MovementFlags.OnGround : MovementFlags.None;
                this.SetPosition(move.Position);
            }

            // Like vanilla's server, which doesn't simulate a boat its controller's client moves.
            this.Motion = VectorD.Zero;
        }
        else
        {
            if (rider is null)
                this.SetPaddleState(false, false);

            this.FloatBoat();

            // Flowing water carries it along, as vanilla's updateInWaterStateAndDoFluidPushing does.
            if (this.InWater)
                this.Motion += this.Terrain.FluidPush(this.Dimension.CreateBBFromPosition(this.Position), true, this.Motion);

            this.Move(this.Motion);
            if (this.MovementFlags.HasFlag(MovementFlags.OnGround))
                this.FallDistance = 0;
        }

        this.lastYd = this.Motion.Y;
        this.TickBubbleColumn();
        this.TickPaddles();

        if (!clientControlled)
            this.PushAndPickUp(controller);

        this.FinishTick(oldPosition);
    }

    private void SetPaddleState(bool left, bool right)
    {
        if (left == this.paddleLeft && right == this.paddleRight)
            return;

        this.paddleLeft = left;
        this.paddleRight = right;
        this.DataChanged = true;
    }

    // Vanilla's getPaddleState: a paddle only turns with someone at the helm.
    private bool PaddleTurns(int side) => (side == 0 ? this.paddleLeft : this.paddleRight) && this.Passengers.Count > 0;

    // Vanilla's paddle splashes, sounded once a turn of each paddle that turns.
    private void TickPaddles()
    {
        for (var side = 0; side <= 1; side++)
        {
            if (!this.PaddleTurns(side))
            {
                this.paddlePositions[side] = 0;
                continue;
            }

            var position = this.paddlePositions[side];
            if (!this.Silent
                && position % (MathF.PI * 2) <= PaddleSplash
                && (position + PaddleStep) % (MathF.PI * 2) >= PaddleSplash
                && this.PaddleSound() is string sound)
            {
                var view = this.GetLookDirection();
                var x = side == 1 ? -view.Z : view.Z;
                var z = side == 1 ? view.X : -view.X;
                this.PlaySound(sound, this.Position + new VectorD(x, 0, z), 0.8f + 0.4f * Random.Shared.NextSingle());
            }

            this.paddlePositions[side] += PaddleStep;
        }
    }

    // Vanilla's getPaddleSound: water while in or under it, land on land, none in the air.
    private string? PaddleSound() => this.GetStatus() switch
    {
        BoatStatus.InWater or BoatStatus.UnderWater or BoatStatus.UnderFlowingWater => "minecraft:entity.boat.paddle_water",
        BoatStatus.OnLand => "minecraft:entity.boat.paddle_land",
        _ => null
    };

    private void PlaySound(string sound, VectorD position, float pitch) =>
        this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X, position.Y, position.Z),
            Category = SoundCategory.Neutral,
            Volume = 1,
            Pitch = pitch,
            Seed = Random.Shared.NextInt64()
        });

    // Vanilla's getStatus.
    private BoatStatus GetStatus()
    {
        if (this.UnderwaterStatus() is BoatStatus under)
        {
            this.waterLevel = this.Dimension.CreateBBFromPosition(this.Position).Max.Y;
            return under;
        }

        if (this.CheckInWater())
            return BoatStatus.InWater;

        var friction = this.GroundFriction();
        if (friction > 0)
        {
            this.landFriction = friction;
            return BoatStatus.OnLand;
        }

        return BoatStatus.InAir;
    }

    // Vanilla's isUnderwater: water over the top of the box; flowing water there pushes it out of control at once.
    private BoatStatus? UnderwaterStatus()
    {
        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        var top = bounds.Max.Y + 0.001;
        var underwater = false;

        for (var x = (int)Math.Floor(bounds.Min.X); x < (int)Math.Ceiling(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Max.Y); y < (int)Math.Ceiling(top); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z < (int)Math.Ceiling(bounds.Max.Z); z++)
        {
            var position = new Vector(x, y, z);
            if (this.WaterAt(position) is not (FluidState fluid, float height) || top >= y + height)
                continue;

            if (!fluid.IsSource)
                return BoatStatus.UnderFlowingWater;

            underwater = true;
        }

        return underwater ? BoatStatus.UnderWater : null;
    }

    // Vanilla's checkInWater: the water's surface over the bottom of the box, and how high it goes.
    private bool CheckInWater()
    {
        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        var inWater = false;
        this.waterLevel = double.MinValue;

        for (var x = (int)Math.Floor(bounds.Min.X); x < (int)Math.Ceiling(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y); y < (int)Math.Ceiling(bounds.Min.Y + 0.001); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z < (int)Math.Ceiling(bounds.Max.Z); z++)
        {
            if (this.WaterAt(new Vector(x, y, z)) is not (_, float height))
                continue;

            var surface = y + height;
            this.waterLevel = Math.Max(surface, this.waterLevel);
            inWater |= bounds.Min.Y < surface;
        }

        return inWater;
    }

    // Vanilla's getWaterLevelAbove: the surface of the water the boat surfaces into.
    private float WaterLevelAbove()
    {
        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        var minX = (int)Math.Floor(bounds.Min.X);
        var maxX = (int)Math.Ceiling(bounds.Max.X);
        var minY = (int)Math.Floor(bounds.Max.Y);
        var maxY = (int)Math.Ceiling(bounds.Max.Y - this.lastYd);
        var minZ = (int)Math.Floor(bounds.Min.Z);
        var maxZ = (int)Math.Ceiling(bounds.Max.Z);

        for (var y = minY; y < maxY; y++)
        {
            var height = 0f;
            for (var x = minX; x < maxX; x++)
            {
                for (var z = minZ; z < maxZ; z++)
                {
                    if (this.WaterAt(new Vector(x, y, z)) is (_, float water))
                        height = Math.Max(height, water);

                    if (height >= 1)
                        goto NextLayer;
                }
            }

            if (height < 1)
                return y + height;

            NextLayer:;
        }

        return maxY + 1;
    }

    // Vanilla's getGroundFriction: the average slipperiness of the blocks whose collision touches the bottom of the
    // box, lily pads aside.
    private float GroundFriction()
    {
        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        var sole = new BoundingBox(
            new VectorD(bounds.Min.X, bounds.Min.Y - 0.001, bounds.Min.Z),
            new VectorD(bounds.Max.X, bounds.Min.Y, bounds.Max.Z));

        var minX = (int)Math.Floor(sole.Min.X) - 1;
        var maxX = (int)Math.Ceiling(sole.Max.X) + 1;
        var minY = (int)Math.Floor(sole.Min.Y) - 1;
        var maxY = (int)Math.Ceiling(sole.Max.Y) + 1;
        var minZ = (int)Math.Floor(sole.Min.Z) - 1;
        var maxZ = (int)Math.Ceiling(sole.Max.Z) + 1;

        var friction = 0f;
        var count = 0;
        for (var x = minX; x < maxX; x++)
        {
            for (var z = minZ; z < maxZ; z++)
            {
                var edges = (x == minX || x == maxX - 1 ? 1 : 0) + (z == minZ || z == maxZ - 1 ? 1 : 0);
                if (edges == 2)
                    continue;

                for (var y = minY; y < maxY; y++)
                {
                    if (edges > 0 && (y == minY || y == maxY - 1))
                        continue;

                    var position = new Vector(x, y, z);
                    if (this.Terrain.GetBlock(position) is not IBlock block || block.Is(BlockIds.LilyPad))
                        continue;

                    var touches = BlockCollisionShapes.Get(this.Level, position, block)
                        .Any(shape => MobTerrain.Overlaps(shape.OffsetBy((VectorD)position), sole));
                    if (!touches)
                        continue;

                    friction += BlockFriction(block);
                    count++;
                }
            }
        }

        return friction / count;
    }

    /// <summary>
    /// Vanilla's <c>Block.getFriction</c>: how little a block slows what slides on it.
    /// </summary>
    internal static float BlockFriction(IBlock block) => block.RegistryId switch
    {
        BlockIds.Ice or BlockIds.PackedIce or BlockIds.FrostedIce => 0.98f,
        BlockIds.BlueIce => 0.989f,
        BlockIds.SlimeBlock => 0.8f,
        _ => 0.6f
    };

    // The water in a block, with the height of its surface within it, like vanilla's FluidState.getHeight.
    private (FluidState Fluid, float Height)? WaterAt(Vector position)
    {
        if (this.Terrain.GetBlock(position) is not IBlock block)
            return null;

        var fluid = FluidState.Of(block);
        if (!fluid.IsWater)
            return null;

        var above = this.Terrain.GetBlock(position + Vector.Up);
        var height = above is not null && FluidState.Of(above).IsWater ? 1 : fluid.OwnHeight;
        return (fluid, height);
    }

    // Vanilla's floatBoat: gravity, buoyancy and drag by where the boat is.
    private void FloatBoat()
    {
        var gravity = -this.Gravity;
        var buoyancy = 0.0;
        var drag = 0.05f;
        var height = this.Dimension.Height;

        if (this.oldStatus == BoatStatus.InAir && this.status is not (BoatStatus.InAir or BoatStatus.OnLand))
        {
            // Landing in water, it surfaces at once if there's room.
            this.waterLevel = this.Position.Y + height;
            var surface = this.WaterLevelAbove() - height + 0.101;
            var bounds = this.Dimension.CreateBBFromPosition(this.Position);
            var lifted = new BoundingBox(
                bounds.Min + new VectorD(0, surface - this.Position.Y, 0),
                bounds.Max + new VectorD(0, surface - this.Position.Y, 0));
            if (this.Terrain.IsFree(lifted))
            {
                this.SetPosition(new VectorD(this.Position.X, surface, this.Position.Z));
                this.Motion = new VectorD(this.Motion.X, 0, this.Motion.Z);
                this.lastYd = 0;
            }

            this.status = BoatStatus.InWater;
            return;
        }

        switch (this.status)
        {
            case BoatStatus.InWater:
                buoyancy = (this.waterLevel - this.Position.Y) / height;
                drag = 0.9f;
                break;
            case BoatStatus.UnderFlowingWater:
                gravity = -7.0E-4;
                drag = 0.9f;
                break;
            case BoatStatus.UnderWater:
                buoyancy = 0.01f;
                drag = 0.45f;
                break;
            case BoatStatus.InAir:
                drag = 0.9f;
                break;
            case BoatStatus.OnLand:
                drag = this.landFriction;
                if (this.Passengers.Count > 0 && this.Passengers[0] is IPlayer)
                    this.landFriction /= 2;
                break;
        }

        var motion = this.Motion;
        this.Motion = new VectorD(motion.X * drag, motion.Y + gravity, motion.Z * drag);
        this.deltaRotation *= drag;

        if (buoyancy > 0)
        {
            motion = this.Motion;
            this.Motion = new VectorD(motion.X, (motion.Y + buoyancy * (0.04 / 0.65)) * 0.75, motion.Z);
        }
    }

    // Vanilla's AbstractBoat.onAboveBubbleColumn: over a column with open water above, the boat rides its top rather
    // than taking the entity push. Inside a column it's pushed like any entity (see Entity.OnInsideBubbleColumn).
    internal override void OnAboveBubbleColumn(bool downwards, Vector position)
    {
        this.aboveBubbleColumn = true;
        this.bubbleColumnDown = downwards;
        if (this.bubbleTime == 0)
            this.SetBubbleTime(BubbleTime);
    }

    // Vanilla's tickBubbleColumn: a column under the boat counts down, then pulls it under, throwing off its
    // passengers, or throws it up, higher with a player aboard.
    private void TickBubbleColumn()
    {
        if (!this.aboveBubbleColumn)
            this.SetBubbleTime(0);

        var time = this.bubbleTime;
        if (time > 0)
        {
            time--;
            this.SetBubbleTime(time);
            if (BubbleTime - time - 1 > 0 && time == 0)
            {
                this.SetBubbleTime(0);
                var motion = this.Motion;
                if (this.bubbleColumnDown)
                {
                    this.Motion = motion + new VectorD(0, -0.7, 0);
                    this.EjectPassengers();
                }
                else
                {
                    var up = this.Passengers.Any(passenger => passenger is IPlayer) ? 2.7 : 0.6;
                    this.Motion = new VectorD(motion.X, up, motion.Z);
                }
            }

            this.aboveBubbleColumn = false;
        }
    }

    private void SetBubbleTime(int time)
    {
        if (this.bubbleTime == time)
            return;

        this.bubbleTime = time;
        this.DataChanged = true;
    }

    // Vanilla's AbstractBoat.tick: what bumps into the boat is pushed away or, with no player at the helm, small
    // enough mobs climb aboard.
    private void PushAndPickUp(Living? controller)
    {
        var box = this.Dimension.CreateBBFromPosition(this.Position);
        var bounds = new BoundingBox(box.Min - new VectorD(0.2, -0.01, 0.2), box.Max + new VectorD(0.2, -0.01, 0.2));
        var pickUp = controller is not IPlayer;

        foreach (var entity in this.PushableEntitiesIn(bounds).ToArray())
        {
            if (pickUp
                && this.Passengers.Count < this.MaximumPassengers
                && entity.Vehicle is null
                && entity.Dimension.Width < this.Dimension.Width
                && entity is Living { Alive: true }
                && !IsIn(entity, TagsRegistry.EntityType.CannotBePushedOntoBoats)
                && this.Board(entity))
            {
                continue;
            }

            this.Push(entity);
        }
    }

    // Vanilla's AbstractBoat.push: another boat pushes only from below this one's top, anything else from its
    // bottom down.
    private void Push(Entity entity)
    {
        var other = entity.Dimension.CreateBBFromPosition(entity.Position);
        var own = this.Dimension.CreateBBFromPosition(this.Position);
        if (entity is Boat ? other.Min.Y < own.Max.Y : other.Min.Y <= own.Min.Y)
            PushApart(this, entity);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(11, EntityMetadataType.Boolean);
        writer.WriteBoolean(this.paddleLeft);

        writer.WriteEntityMetadataType(12, EntityMetadataType.Boolean);
        writer.WriteBoolean(this.paddleRight);

        writer.WriteEntityMetadataType(13, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.bubbleTime);

        this.MetadataIndex = 14;
    }

    // Vanilla's AbstractBoat$Status.
    private enum BoatStatus
    {
        InWater,
        UnderWater,
        UnderFlowingWater,
        OnLand,
        InAir
    }
}
