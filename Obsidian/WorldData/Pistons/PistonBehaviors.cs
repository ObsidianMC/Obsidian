using Obsidian.API.BlockEntities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData.Pistons;

/// <summary>
/// The piston base, head and movement (vanilla <c>PistonBaseBlock</c>, <c>PistonHeadBlock</c>, and the moves that
/// <c>PistonBaseBlock.moveBlocks</c> applies). The base reacts to redstone by queuing an event. The event runs on the
/// simulation path, like vanilla's block events, and it plans and applies the move with <see cref="PistonStructureResolver"/>.
/// </summary>
internal static class PistonBehaviors
{
    // Vanilla blockEvent types of PistonBaseBlock.triggerEvent.
    private const int EventExtend = 0;
    private const int EventRetract = 1;
    private const int EventDrop = 2;

    /// <summary>Vanilla <c>PistonBaseBlock.getStateForPlacement</c>: faces away from the placer, retracted.</summary>
    public static IBlock DerivePlacement(IBlock state, BlockFace nearestLook) =>
        state.WithProperty("facing", nearestLook.Opposite().ToString().ToLowerInvariant()).WithProperty("extended", "false");

    public static void Register(IRedstoneLevel level, RedstoneEngine engine, NeighborBehaviors neighbors)
    {
        var mover = new Mover(level, engine);
        var piston = new PistonBase(level, mover);
        var head = new PistonHead(level);
        foreach (var id in new[] { BlockIds.Piston, BlockIds.StickyPiston })
        {
            neighbors.Register(id, piston);
            neighbors.RegisterPlaced(id, piston);
        }

        neighbors.Register(BlockIds.PistonHead, head);
    }

    /// <summary>The piston base's logic: deciding when to extend or retract, and the events that do it.</summary>
    private sealed class PistonBase(IRedstoneLevel level, Mover mover) : INeighborChangedBehavior, IPlacementBehavior
    {
        // Vanilla onPlace and setPlacedBy: a new piston checks its power at once.
        public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world)
        {
            this.CheckIfExtend(change.Position, change.New);
            return ValueTask.CompletedTask;
        }

        // Vanilla neighborChanged: any change around the piston may power or unpower it.
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
        {
            this.CheckIfExtend(change.Position, change.State);
            return ValueTask.CompletedTask;
        }

        // Vanilla checkIfExtend: starts extending an unextended piston that gets power, and retracts an extended one that
        // loses it. A retracting piston whose head is still moving toward it drops the head (type 2).
        private void CheckIfExtend(Vector position, IBlock state)
        {
            var facing = PistonRules.Facing(state);
            var extended = PistonRules.IsExtended(state);
            var powered = Mover.NeighborSignal(mover.Engine, position, facing);
            if (powered && !extended)
            {
                if (new PistonStructureResolver(level, position, facing, extending: true).Resolve())
                    this.QueueEvent(position, EventExtend, facing);
            }
            else if (!powered && extended)
            {
                var type = EventRetract;
                var headPosition = PistonRules.Step(position, facing, 2);
                var headState = level.GetLoadedBlock(headPosition);
                if (headState is not null && PistonRules.IsMoving(headState)
                    && PistonRules.Facing(headState) == facing
                    && level.GetBlockEntityAt(headPosition) is MovingPistonBlockEntity { IsSource: true })
                {
                    type = EventDrop;
                }

                this.QueueEvent(position, type, facing);
            }
        }

        // Vanilla blockEvent: the event runs later on the simulation path, against the state at that time.
        private void QueueEvent(Vector position, int type, BlockFace facing) =>
            level.EnqueueSimulation(() => this.TriggerAsync(position, type, facing));

        // Vanilla PistonBaseBlock.triggerEvent.
        private async ValueTask TriggerAsync(Vector position, int type, BlockFace facing)
        {
            var state = level.GetLoadedBlock(position);
            if (state is null || !PistonRules.IsBase(state))
                return;

            var powered = Mover.NeighborSignal(mover.Engine, position, facing);
            if (powered && type != EventExtend)
            {
                await level.WriteAsync(position, state.WithProperty("extended", "true"), notifyNeighbors: false);
                return;
            }

            if (!powered && type == EventExtend)
                return;

            if (type == EventExtend)
            {
                if (!await mover.MoveBlocksAsync(position, facing, extending: true, PistonRules.IsStickyPiston(state)))
                    return;

                await level.WriteAsync(position, state.WithProperty("extended", "true"), notifyNeighbors: true);
                level.PlaySound(position, "minecraft:block.piston.extend");
                return;
            }

            await this.RetractAsync(position, state, type, facing);
        }

        private async ValueTask RetractAsync(Vector position, IBlock state, int type, BlockFace facing)
        {
            var sticky = PistonRules.IsStickyPiston(state);
            var headPosition = PistonRules.Step(position, facing, 1);

            // The head's moving entity is finished first (vanilla: the old head's finalTick).
            if (level.GetBlockEntityAt(headPosition) is MovingPistonBlockEntity oldHead)
                await oldHead.FinishAsync(level);

            // The base's moving entity carries the retracted base back to its place, as vanilla does.
            var moving = BlocksRegistry.GetDefault(BlockIds.MovingPiston)
                .WithProperty("facing", facing.ToString().ToLowerInvariant())
                .WithProperty("type", sticky ? "sticky" : "normal");
            await level.WriteAsync(position, moving, notifyNeighbors: false);
            var retracted = BlocksRegistry.GetDefault(sticky ? BlockIds.StickyPiston : BlockIds.Piston)
                .WithProperty("facing", facing.ToString().ToLowerInvariant())
                .WithProperty("extended", "false");
            mover.PlaceMoving(position, retracted, facing, extending: false, source: true);

            // Vanilla: only a sticky piston pulls. A normal piston, or a dropping one, just removes its head.
            if (!sticky)
            {
                await level.WriteAsync(headPosition, BlocksRegistry.GetDefault(BlockIds.Air), notifyNeighbors: true);
                return;
            }

            var pulled = PistonRules.Step(position, facing, 2);
            var pulledState = level.GetLoadedBlock(pulled) ?? BlocksRegistry.VoidAir;
            if (PistonRules.IsMoving(pulledState) && PistonRules.Facing(pulledState) == facing
                && level.GetBlockEntityAt(pulled) is MovingPistonBlockEntity { IsSource: true } pulledHead)
            {
                await pulledHead.FinishAsync(level);
                return;
            }

            // Vanilla: a dropping piston, or one with nothing it may pull, removes its head. Otherwise the line is pulled.
            var canPull = type == EventRetract
                && !pulledState.IsAir
                && PistonRules.IsPushable(pulledState, level, pulled, facing.Opposite(), false, facing)
                && (PistonRules.PushReaction(pulledState) == "NORMAL" || PistonRules.IsBase(pulledState));
            if (!canPull)
            {
                await level.WriteAsync(headPosition, BlocksRegistry.GetDefault(BlockIds.Air), notifyNeighbors: true);
                return;
            }

            await mover.MoveBlocksAsync(position, facing, extending: false, sticky);
        }
    }

    /// <summary>The piston head (vanilla <c>PistonHeadBlock</c>): it's removed when the base it belongs to is gone.</summary>
    private sealed class PistonHead(IRedstoneLevel level) : INeighborChangedBehavior
    {
        // Vanilla updateShape: a head with no base in front of it becomes air, and a surviving head tells its base.
        public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
        {
            var facing = PistonRules.Facing(change.State);
            var basePosition = PistonRules.Step(change.Position, facing, -1);
            if (!Survives(change.State, basePosition))
            {
                if (change.Source == basePosition)
                    await level.WriteAsync(change.Position, BlocksRegistry.GetDefault(BlockIds.Air), notifyNeighbors: true);

                return;
            }

            await level.NotifyNeighborAsync(basePosition, change.Position);
        }

        private bool Survives(IBlock head, Vector basePosition)
        {
            var facing = PistonRules.Facing(head);
            var neighbor = level.GetLoadedBlock(basePosition);
            if (neighbor is null)
                return false;

            var baseBlock = PistonRules.IsSticky(head) ? BlockIds.StickyPiston : BlockIds.Piston;
            if (neighbor.Is(baseBlock) && PistonRules.IsExtended(neighbor) && PistonRules.Facing(neighbor) == facing)
                return true;

            return PistonRules.IsMoving(neighbor) && PistonRules.Facing(neighbor) == facing;
        }
    }

    /// <summary>Moves blocks for a piston, and places the moving blocks and their entities (vanilla <c>moveBlocks</c>).</summary>
    private sealed class Mover(IRedstoneLevel level, RedstoneEngine engine)
    {
        public RedstoneEngine Engine => engine;

        // Vanilla getNeighborSignal: any powered side except the front, or the back and the sides above it.
        public static bool NeighborSignal(RedstoneEngine engine, Vector position, BlockFace facing)
        {
            foreach (var face in Enum.GetValues<BlockFace>())
            {
                if (face != facing && engine.GetSignal(PistonRules.Step(position, face, 1), face) > 0)
                    return true;
            }

            if (engine.GetSignal(position, BlockFace.Down) > 0)
                return true;

            var above = PistonRules.Step(position, BlockFace.Up, 1);
            foreach (var face in Enum.GetValues<BlockFace>())
            {
                if (face != BlockFace.Down && engine.GetSignal(PistonRules.Step(above, face, 1), face) > 0)
                    return true;
            }

            return false;
        }

        // Vanilla moveBlocks: the planned blocks become moving blocks one step along the move, and the piston head is placed
        // when extending. Vacated positions become air.
        public async ValueTask<bool> MoveBlocksAsync(Vector piston, BlockFace facing, bool extending, bool sticky)
        {
            var headPosition = PistonRules.Step(piston, facing, 1);
            var headState = level.GetLoadedBlock(headPosition);
            if (!extending && headState is not null && PistonRules.IsHead(headState))
                await level.WriteAsync(headPosition, BlocksRegistry.GetDefault(BlockIds.Air), notifyNeighbors: false);

            var resolver = new PistonStructureResolver(level, piston, facing, extending);
            if (!resolver.Resolve())
                return false;

            var moveDirection = resolver.MoveDirection;
            var toPush = resolver.ToPush.ToList();
            var originals = toPush.Select(position => level.GetLoadedBlock(position) ?? BlocksRegistry.VoidAir).ToList();
            var vacated = new HashSet<Vector>(toPush);

            // Breaks the blocks whose push reaction is DESTROY, furthest first.
            foreach (var position in resolver.ToDestroy.Reverse())
            {
                var state = level.GetLoadedBlock(position) ?? BlocksRegistry.VoidAir;
                level.DropAndRemove(position, state, playEffect: true);
            }

            var pieceType = sticky ? "sticky" : "normal";
            for (var i = toPush.Count - 1; i >= 0; i--)
            {
                var target = PistonRules.Step(toPush[i], moveDirection, 1);
                vacated.Remove(target);
                // Vanilla leaves a pushed block's moving piston at its default type; only the head carries stickiness.
                var moving = BlocksRegistry.GetDefault(BlockIds.MovingPiston)
                    .WithProperty("facing", facing.ToString().ToLowerInvariant());
                await level.WriteAsync(target, moving, notifyNeighbors: false);
                this.PlaceMoving(target, originals[i], facing, extending, source: false);
            }

            if (extending)
            {
                var head = BlocksRegistry.GetDefault(BlockIds.PistonHead)
                    .WithProperty("facing", facing.ToString().ToLowerInvariant())
                    .WithProperty("type", pieceType)
                    .WithProperty("short", "false");
                var movingHead = BlocksRegistry.GetDefault(BlockIds.MovingPiston)
                    .WithProperty("facing", facing.ToString().ToLowerInvariant())
                    .WithProperty("type", pieceType);
                vacated.Remove(headPosition);
                await level.WriteAsync(headPosition, movingHead, notifyNeighbors: false);
                this.PlaceMoving(headPosition, head, facing, extending: true, source: true);
            }

            foreach (var position in vacated)
                await level.WriteAsync(position, BlocksRegistry.GetDefault(BlockIds.Air), notifyNeighbors: false);

            return true;
        }

        // Places the block entity that carries a moving block (vanilla newMovingBlockEntity + setBlockEntity).
        public void PlaceMoving(Vector position, IBlock moved, BlockFace facing, bool extending, bool source)
        {
            var entity = new MovingPistonBlockEntity();
            entity.Configure(level, moved, facing, extending, source);
            entity.BlockPosition = position;
            level.SetBlockEntityAt(position, entity);
        }
    }
}
