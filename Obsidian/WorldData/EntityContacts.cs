using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Plugins;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// What a block's contact hooks get from the level an entity touches it in: its blocks, as block hooks read and change
/// them, and the redstone blocks whose contact presses or trips them.
/// </summary>
internal interface IContactLevel
{
    /// <summary>The level's blocks, as block hooks read and change them.</summary>
    IBlockLevel Blocks { get; }

    /// <summary>The level, for plugin blocks' hooks.</summary>
    ILevel Level { get; }

    /// <summary>The level's pressure plates, tripwires, detector rails and buttons.</summary>
    RedstoneContacts Redstone { get; }

    /// <summary>Where a plugin block's failing hook is logged.</summary>
    ILogger Logger { get; }

    int MinY { get; }

    int Height { get; }

    /// <summary>The chunk at a chunk position, or null when it isn't loaded.</summary>
    Chunk? GetContactChunk(int chunkX, int chunkZ);
}

/// <summary>
/// Which hooks a block state has for entities touching it, read before anything else for each cell an entity touches.
/// </summary>
/// <remarks>
/// A vanilla state's flags come from its generated owners (<c>BlocksRegistry.GetEntityInsideBehavior</c>,
/// <c>GetStepOnBehavior</c> and <c>GetFallOnBehavior</c>), so a state none of them binds costs one array read. A
/// plugin block, whose ids follow vanilla's, has its behaviour's inside and stepped-on hooks while its plugin has one.
/// </remarks>
internal static class ContactFlags
{
    public const byte Inside = 1;

    public const byte SteppedOn = 2;

    public const byte FallenOn = 4;

    private static readonly byte[] byState = Build();

    /// <summary>The state's flags.</summary>
    public static byte Of(IBlock state)
    {
        var stateId = state.GetHashCode();
        if ((uint)stateId < (uint)byState.Length)
            return byState[stateId];

        return BlockBehaviors.HasBehavior(state) ? (byte)(Inside | SteppedOn) : (byte)0;
    }

    private static byte[] Build()
    {
        var flags = new byte[BlocksRegistry.StateToNumeric.Length];
        for (var stateId = 0; stateId < flags.Length; stateId++)
        {
            var registryId = BlocksRegistry.StateToNumeric[stateId];
            var inside = BlocksRegistry.GetEntityInsideBehavior(registryId) is null ? 0 : Inside;
            var steppedOn = BlocksRegistry.GetStepOnBehavior(registryId) is null ? 0 : SteppedOn;
            var fallenOn = BlocksRegistry.GetFallOnBehavior(registryId) is null ? 0 : FallenOn;
            // Beds inherit the base fall hook in 26.3, with a distance-reduction property.
            if (BlocksRegistry.GetDefault(registryId).BlockClass() == "BedBlock")
                fallenOn = FallenOn;

            flags[stateId] = (byte)(inside | steppedOn | fallenOn);
        }

        return flags;
    }
}

/// <summary>
/// Runs the hooks of the blocks an entity touches, once for each entity straight after its own tick, as vanilla's
/// entity movement does: the block it landed on (fallen on, with the distance it fell), then the block under its feet
/// if it stands on the ground (stepped on), then each block its box is inside, in vanilla's order (x, then y, then z).
/// </summary>
/// <remarks>
/// <para>
/// Each touched cell is read once and its <see cref="ContactFlags"/> checked first; only a cell with a hook goes on to
/// the behaviour bound to its block, and only a behaviour that doesn't finish at once makes the pass asynchronous. So
/// an entity touching nothing that reacts takes no lock, makes no asynchronous call and allocates nothing.
/// </para>
/// <para>
/// Like vanilla, entities that don't move through the world themselves never touch blocks: markers, displays,
/// interactions, area effect clouds and hanging entities among them, and those without physics (spectators, armor
/// stands without gravity or marked, vexes, the ender dragon). A dead entity still steps on blocks but is no longer
/// inside any. Vanilla also finds the blocks between where an entity was and where it moved to in one tick; this pass
/// reads the box where the entity is, so a fast entity can pass through a block between ticks without touching it.
/// </para>
/// </remarks>
internal sealed class EntityContactPass(IContactLevel level)
{
    // Vanilla's checkInsideBlocks shrinks the box by this much, so that only touching a block's face isn't being in it.
    private const double Inset = 1.0E-5;

    // Vanilla's getOnPosLegacy: the block stepped and fallen on is this far below the feet.
    private const double StepDepth = 0.2;

    // Vanilla's checkSupportingBlock: the slab under the box that a block holding the entity up reaches into.
    private const double SupportDepth = 1.0E-6;

    /// <summary>
    /// Runs the contact hooks of the blocks <paramref name="entity"/> touches. Call straight after its tick.
    /// </summary>
    public ValueTask TouchAsync(Entity entity)
    {
        if (IsRemoved(entity) || !TouchesBlocks(entity))
            return default;

        if (entity.TakeLanding() is BlockLanding fall)
        {
            var beforeHook = entity.Position;
            var pending = this.FallenOnAsync(entity, fall);
            if (!pending.IsCompletedSuccessfully)
                return this.AfterFallAsync(pending, entity, fall, beforeHook);

            pending.GetAwaiter().GetResult();
            this.LandingEvents(entity, fall, beforeHook);
        }
        else if (entity.ContactBounced)
        {
            // Restitution also runs for a downward push while already resting on the surface.
            this.BounceEvent(entity);
        }

        return this.StepAndInsideAsync(entity);
    }

    /// <summary>
    /// Whether <paramref name="entity"/> touches blocks at all, like vanilla's entities that apply block effects as they
    /// move (living entities, items, experience orbs, falling blocks, TNT, projectiles, boats, minecarts and end
    /// crystals) while they have physics (vanilla's <c>isAffectedByBlocks</c>). Vexes fly through blocks but still touch
    /// them, as vanilla's do, and a plugin's projectiles and objects touch blocks like vanilla's.
    /// </summary>
    internal static bool TouchesBlocks(Entity entity) => entity switch
    {
        PlayerBase player => player.GameMode != GameMode.Spectator,
        ArmorStand stand => !stand.NoGravity && !stand.Marker,
        EnderDragon => false,
        Living => true,
        ItemEntity or ExperienceOrb or FallingBlock or PrimedTnt or FireworkRocket or FishingHook => true,
        PluginEntity => true,
        Arrow or Throwable or VehicleEntity or EndCrystal or DragonFireball or BreezeWindCharge => true,
        MobProjectile => entity.Type != EntityType.LlamaSpit,
        _ => entity.Type == EntityType.WindCharge,
    };

    // An entity its own tick removed, which vanilla's isAffectedByBlocks leaves out. The level also skips any entity
    // its tick took out of the level (see AbstractLevel's entity loop); these flags are for the ones that mark
    // themselves removed.
    private static bool IsRemoved(Entity entity) =>
        entity is ItemEntity { Removed: true } or Mob { IsRemoved: true } or VehicleEntity { IsRemoved: true };

    /// <summary>
    /// Whether pressure plates and tripwires leave <paramref name="entity"/> out when they look for entities on them,
    /// like vanilla's <c>isIgnoringBlockTriggers</c> (and its selector leaving spectators out). Bats ignore them too.
    /// </summary>
    internal static bool IgnoresBlockTriggers(IEntity entity) => entity switch
    {
        IPlayer player => player.GameMode == GameMode.Spectator,
        ArmorStand stand => stand.Marker,
        DisplayEntity or TextDisplay or Interaction or OminousItemSpawner or Bat => true,
        _ => entity.Type == EntityType.Marker,
    };

    private async ValueTask AfterFallAsync(
        ValueTask pending, Entity entity, BlockLanding fall, VectorD beforeHook)
    {
        await pending;
        this.LandingEvents(entity, fall, beforeHook);
        await this.StepAndInsideAsync(entity);
    }

    private void LandingEvents(Entity entity, BlockLanding fall, VectorD beforeHook)
    {
        if (level.Level is AbstractLevel world && !IsRemoved(entity))
        {
            // Keep a lift caused by the hook, without substituting movement that preceded dispatch.
            var impact = fall.Impact + (entity.Position - beforeHook);
            world.EmitGameEvent(MobGameEvent.Land, impact, entity);
        }

        this.BounceEvent(entity);
    }

    private void BounceEvent(Entity entity)
    {
        if (entity.ContactBounced && level.Level is AbstractLevel world && !IsRemoved(entity))
            world.EmitGameEvent(MobGameEvent.Bounce, entity.Position, entity);

        entity.ContactBounced = false;
    }

    private ValueTask StepAndInsideAsync(Entity entity)
    {
        if (IsRemoved(entity))
            return default;

        if ((entity.MovementFlags & MovementFlags.OnGround) != 0)
        {
            var pending = this.SteppedOnAsync(entity);
            if (!pending.IsCompletedSuccessfully)
                return this.AfterStepAsync(pending, entity);

            pending.GetAwaiter().GetResult();
        }

        return this.InsideAsync(entity);
    }

    private async ValueTask AfterStepAsync(ValueTask pending, Entity entity)
    {
        await pending;
        await this.InsideAsync(entity);
    }

    private ValueTask FallenOnAsync(Entity entity, BlockLanding fall)
    {
        var state = fall.State;
        if (state is null || (ContactFlags.Of(state) & ContactFlags.FallenOn) == 0)
        {
            return entity.CauseContactFallAsync(fall.Distance, impact: fall.Impact);
        }

        var owner = BlocksRegistry.GetFallOnBehavior(state.RegistryId) ?? FallOnBehaviors.BedBlock;
        return owner.FallenOnAsync(level, fall.BlockPosition, state, entity, fall.Distance, fall.Impact);
    }

    private ValueTask SteppedOnAsync(Entity entity)
    {
        if (this.SupportCell(entity, ContactFlags.SteppedOn) is not (Vector position, IBlock state))
            return default;

        if (BlocksRegistry.GetStepOnBehavior(state.RegistryId) is IStepOnBehavior owner)
            return owner.SteppedOnAsync(level, position, state, entity);

        return BlockBehaviors.SteppedOnAsync(this.PluginContext(entity, position, state), level.Logger);
    }

    private ValueTask InsideAsync(Entity entity)
    {
        // Vanilla stops looking for blocks an entity is inside once it's dead, while it still steps on them.
        if (IsRemoved(entity) || entity is Living { Alive: false })
            return default;

        var box = entity.Dimension.CreateBBFromPosition(entity.Position);
        var cells = CellBox.Of(
            new VectorD(box.Min.X + Inset, box.Min.Y + Inset, box.Min.Z + Inset),
            new VectorD(box.Max.X - Inset, box.Max.Y - Inset, box.Max.Z - Inset));
        return this.InsideFromAsync(entity, cells, 0);
    }

    // The cells from index on; a behaviour that doesn't finish at once hands the rest to InsideAfterAsync.
    private ValueTask InsideFromAsync(Entity entity, CellBox cells, int index)
    {
        var blocks = new ChunkCursor(level);
        for (; index < cells.Count; index++)
        {
            var cell = cells.At(index);
            if (blocks.Read(cell) is not IBlock state || (ContactFlags.Of(state) & ContactFlags.Inside) == 0)
                continue;

            // A block the entity was inside can kill it, and vanilla goes no further then.
            if (IsRemoved(entity) || entity is Living { Alive: false })
                return default;

            var pending = this.EntityInsideAsync(entity, cell, state);
            if (!pending.IsCompletedSuccessfully)
                return this.InsideAfterAsync(pending, entity, cells, index + 1);

            pending.GetAwaiter().GetResult();
        }

        return default;
    }

    private async ValueTask InsideAfterAsync(ValueTask pending, Entity entity, CellBox cells, int next)
    {
        await pending;
        await this.InsideFromAsync(entity, cells, next);
    }

    private ValueTask EntityInsideAsync(Entity entity, Vector position, IBlock state)
    {
        if (BlocksRegistry.GetEntityInsideBehavior(state.RegistryId) is IEntityInsideBehavior owner)
            return owner.EntityInsideAsync(level, position, state, entity);

        return BlockBehaviors.EntityInsideAsync(this.PluginContext(entity, position, state), level.Logger);
    }

    private EntityBlockContext PluginContext(Entity entity, Vector position, IBlock state) => new()
    {
        Entity = entity,
        Level = level.Level,
        Position = position,
        State = state,
    };

    /// <summary>
    /// The block an entity on the ground stands on, like vanilla's <c>getOnPosLegacy</c>, when it has a hook
    /// <paramref name="flag"/> names; null otherwise.
    /// </summary>
    /// <remarks>
    /// The cells 0.2 below the feet across the box are read first, and with none of them having the hook the answer is
    /// null without more work. Otherwise the block holding the entity up decides, as vanilla's
    /// <c>mainSupportingBlockPos</c> does: of the blocks whose collision reaches just under the feet, the one whose
    /// centre is nearest the entity; the block stood on is the one in its column 0.2 below the feet, or the support
    /// itself for a fence, wall or fence gate. Standing on a carpet, that's the block under the carpet.
    /// </remarks>
    private (Vector, IBlock)? SupportCell(Entity entity, byte flag)
    {
        var position = entity.Position;
        var box = entity.Dimension.CreateBBFromPosition(position);
        var stepY = (int)Math.Floor(position.Y - StepDepth);
        var blocks = new ChunkCursor(level);
        if (!AnyFlagged(ref blocks, box, stepY, flag))
            return null;

        var cell = SupportCellOf(ref blocks, entity, box, position, stepY);

        if (blocks.Read(cell) is not IBlock state || (ContactFlags.Of(state) & flag) == 0)
            return null;

        return (cell, state);
    }

    /// <summary>The same legacy support selection for impact recording, restitution and contact dispatch.</summary>
    internal static (Vector, IBlock)? SupportAt(IContactLevel level, Entity entity, VectorD position)
    {
        var blocks = new ChunkCursor(level);
        var box = entity.Dimension.CreateBBFromPosition(position);
        var stepY = (int)Math.Floor(position.Y - StepDepth);
        var cell = SupportCellOf(ref blocks, entity, box, position, stepY);
        return blocks.Read(cell) is IBlock state ? (cell, state) : null;
    }

    /// <summary>
    /// The cell of the block below an entity on the ground that affects how it moves, like vanilla's
    /// <c>getBlockPosBelowThatAffectsMyMovement</c>: the block holding it up, in the row <paramref name="belowY"/>
    /// half a block under its feet, or the support itself for a wall or fence gate (not a fence, unlike the block
    /// stepped on).
    /// </summary>
    internal static Vector MovementCellAt(IContactLevel level, Entity entity, VectorD position, int belowY)
    {
        var blocks = new ChunkCursor(level);
        var box = entity.Dimension.CreateBBFromPosition(position);
        if (SupportOf(ref blocks, entity, box, position) is not Vector support)
            return new Vector((int)Math.Floor(position.X), belowY, (int)Math.Floor(position.Z));

        var state = blocks.Read(support);
        var keepsSupport = state is not null
            && (TagsRegistry.Block.Walls.Entries.Contains(state.RegistryId) || state.BlockClass() == "FenceGateBlock");
        return keepsSupport ? support : new Vector(support.X, belowY, support.Z);
    }

    private static Vector SupportCellOf(
        ref ChunkCursor blocks, Entity entity, BoundingBox box, VectorD position, int stepY) =>
        SupportOf(ref blocks, entity, box, position) is Vector support
            ? SteppedCell(ref blocks, support, stepY)
            : new Vector((int)Math.Floor(position.X), stepY, (int)Math.Floor(position.Z));

    // Whether a cell of the box's footprint at y has the hook.
    private static bool AnyFlagged(ref ChunkCursor blocks, BoundingBox box, int y, byte flag)
    {
        var minX = (int)Math.Floor(box.Min.X);
        var maxX = (int)Math.Floor(box.Max.X);
        var minZ = (int)Math.Floor(box.Min.Z);
        var maxZ = (int)Math.Floor(box.Max.Z);
        for (var z = minZ; z <= maxZ; z++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (blocks.Read(new Vector(x, y, z)) is IBlock state && (ContactFlags.Of(state) & flag) != 0)
                    return true;
            }
        }

        return false;
    }

    private static Vector SteppedCell(ref ChunkCursor blocks, Vector support, int stepY)
    {
        // Vanilla keeps the support itself when it's a fence, wall or gate, which an entity stands 1.5 blocks above.
        var state = blocks.Read(support);
        var tall = state is not null
            && (TagsRegistry.Block.Fences.Entries.Contains(state.RegistryId)
                || TagsRegistry.Block.Walls.Entries.Contains(state.RegistryId)
                || state.BlockClass() == "FenceGateBlock");
        return tall ? support : new Vector(support.X, stepY, support.Z);
    }

    // Vanilla's findSupportingBlock: of the blocks whose collision meets the slab just under the box, the nearest by
    // the distance from its centre to the entity's position, ties going to the greatest by y, then z, then x.
    private static Vector? SupportOf(ref ChunkCursor blocks, Entity entity, BoundingBox box, VectorD position)
    {
        var slabMin = new VectorD(box.Min.X, box.Min.Y - SupportDepth, box.Min.Z);
        var slabMax = new VectorD(box.Max.X, box.Min.Y, box.Max.Z);
        Vector? best = null;
        var bestDistance = double.MaxValue;
        for (var y = (int)Math.Floor(slabMin.Y) - 1; y <= (int)Math.Floor(slabMax.Y); y++)
        {
            for (var z = (int)Math.Floor(slabMin.Z) - 1; z <= (int)Math.Floor(slabMax.Z) + 1; z++)
            {
                for (var x = (int)Math.Floor(slabMin.X) - 1; x <= (int)Math.Floor(slabMax.X) + 1; x++)
                {
                    var cell = new Vector(x, y, z);
                    if (blocks.Read(cell) is not IBlock state || !Collides(state, entity, cell, slabMin, slabMax))
                        continue;

                    var dx = x + 0.5 - position.X;
                    var dy = y + 0.5 - position.Y;
                    var dz = z + 0.5 - position.Z;
                    var distance = dx * dx + dy * dy + dz * dz;
                    if (distance < bestDistance || distance == bestDistance && IsGreater(cell, best!.Value))
                    {
                        best = cell;
                        bestDistance = distance;
                    }
                }
            }
        }

        return best;
    }

    // Vanilla's Vec3i ordering: by y, then z, then x.
    private static bool IsGreater(Vector cell, Vector than)
    {
        if (cell.Y != than.Y)
            return cell.Y > than.Y;

        return cell.Z != than.Z ? cell.Z > than.Z : cell.X > than.X;
    }

    // Whether the block's collision for the entity meets the box from min to max.
    private static bool Collides(IBlock state, Entity entity, Vector cell, VectorD min, VectorD max)
    {
        if (Entities.AI.EntityCollisionShapes.DependsOnEntity(state))
            return CollidesForEntity(state, entity, cell, min, max);

        var stateId = state.GetHashCode();
        var offset = BlockPhysics.ShapeOffset(stateId, cell.X, cell.Z);
        foreach (var shape in BlockPhysics.ShapeBoxes(stateId))
        {
            var meets = cell.X + offset.X + shape[0] < max.X && cell.X + offset.X + shape[3] > min.X
                && cell.Y + offset.Y + shape[1] < max.Y && cell.Y + offset.Y + shape[4] > min.Y
                && cell.Z + offset.Z + shape[2] < max.Z && cell.Z + offset.Z + shape[5] > min.Z;
            if (meets)
                return true;
        }

        return false;
    }

    // The box's bottom is where the entity's feet are now, which vanilla reads for the block holding it up.
    private static bool CollidesForEntity(IBlock state, Entity entity, Vector cell, VectorD min, VectorD max)
    {
        var shapes = Entities.AI.EntityCollisionShapes.Get(cell, state, entity, max.Y);
        for (var index = 0; index < shapes.Count; index++)
        {
            var shape = shapes[index];
            var meets = cell.X + shape.Min.X < max.X && cell.X + shape.Max.X > min.X
                && cell.Y + shape.Min.Y < max.Y && cell.Y + shape.Max.Y > min.Y
                && cell.Z + shape.Min.Z < max.Z && cell.Z + shape.Max.Z > min.Z;
            if (meets)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads cells for one look around an entity, keeping the chunk it read last: an entity's cells are nearly always in
    /// one chunk. Not kept between entities, since a chunk can unload in between.
    /// </summary>
    private struct ChunkCursor(IContactLevel level)
    {
        private Chunk? chunk;
        private int chunkX = int.MinValue;
        private int chunkZ = int.MinValue;

        /// <summary>The block in the cell, or null outside the build height or an unloaded chunk.</summary>
        public IBlock? Read(Vector cell)
        {
            if (cell.Y < level.MinY || cell.Y >= level.MinY + level.Height)
                return null;

            if (cell.X >> 4 != this.chunkX || cell.Z >> 4 != this.chunkZ)
            {
                this.chunkX = cell.X >> 4;
                this.chunkZ = cell.Z >> 4;
                this.chunk = level.GetContactChunk(this.chunkX, this.chunkZ);
            }

            return this.chunk?.GetBlock(cell.X, cell.Y, cell.Z);
        }
    }

    /// <summary>
    /// The cells from a box's minimum to its maximum corner, numbered as vanilla's <c>BlockPos.betweenClosed</c> walks
    /// them: x fastest, then y, then z.
    /// </summary>
    private readonly record struct CellBox(int MinX, int MinY, int MinZ, int SizeX, int SizeY, int SizeZ)
    {
        public int Count => this.SizeX * this.SizeY * this.SizeZ;

        public static CellBox Of(VectorD min, VectorD max)
        {
            var minX = (int)Math.Floor(min.X);
            var minY = (int)Math.Floor(min.Y);
            var minZ = (int)Math.Floor(min.Z);
            var sizeX = (int)Math.Floor(max.X) - minX + 1;
            var sizeY = (int)Math.Floor(max.Y) - minY + 1;
            var sizeZ = (int)Math.Floor(max.Z) - minZ + 1;
            return new CellBox(minX, minY, minZ, Math.Max(0, sizeX), Math.Max(0, sizeY), Math.Max(0, sizeZ));
        }

        public Vector At(int index)
        {
            var x = this.MinX + index % this.SizeX;
            var y = this.MinY + index / this.SizeX % this.SizeY;
            var z = this.MinZ + index / (this.SizeX * this.SizeY);
            return new Vector(x, y, z);
        }
    }
}
