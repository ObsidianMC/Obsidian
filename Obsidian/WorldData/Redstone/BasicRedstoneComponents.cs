using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Placement;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.BlockTicks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// State-driven R4 redstone components. Sources expose their output only through <see cref="IRedstoneBehavior"/> and consumers
/// react through the common neighbor queue; neither performs an ad-hoc block-to-block power query.
/// </summary>
internal static class BasicRedstoneComponents
{
    // Vanilla ButtonBlock ticks: stone-set buttons stay pressed 20 ticks, wooden-set buttons 30. The membership comes
    // from the generated stone_buttons and wooden_buttons tags.
    private static int? ButtonDelay(int blockId) =>
        TagsRegistry.Block.StoneButtons.Entries.Contains(blockId) ? 20
        : TagsRegistry.Block.WoodenButtons.Entries.Contains(blockId) ? 30
        : null;

    private static IEnumerable<int> ButtonIds =>
        [.. TagsRegistry.Block.StoneButtons.Entries, .. TagsRegistry.Block.WoodenButtons.Entries];

    /// <summary>
    /// Registers source output and queued consumer reactions.
    /// </summary>
    public static void Register(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneBehaviors redstone, NeighborBehaviors neighbors, RedstoneEngine engine, Action<Vector>? playNote = null)
    {
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.RedstoneBlock).BlockClass(), new ConstantSource());
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.Lever).BlockClass(), new MountedSource());
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.StoneButton).BlockClass(), new MountedSource());
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.RedstoneTorch).BlockClass(), new TorchSource(wall: false));
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.RedstoneWallTorch).BlockClass(), new TorchSource(wall: true));
        // Redstone ore gives no power in vanilla (its lit state is light and particles only), so it has no registration.
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.DaylightDetector).BlockClass(), new DaylightDetectorSignal());
        neighbors.RegisterPlaced(BlockIds.DaylightDetector, new DaylightDetectorPlacement(level));

        var torch = new RedstoneTorch(level, engine);
        neighbors.Register(BlockIds.RedstoneTorch, torch);
        neighbors.Register(BlockIds.RedstoneWallTorch, torch);
        neighbors.RegisterPlaced(BlockIds.RedstoneTorch, torch);
        neighbors.RegisterPlaced(BlockIds.RedstoneWallTorch, torch);
        var plate = new PlateSource();
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.StonePressurePlate).BlockClass(), plate);
        redstone.Register(BlocksRegistry.GetDefault(BlockIds.LightWeightedPressurePlate).BlockClass(), plate);
        neighbors.RegisterClass(BlocksRegistry.GetDefault(BlockIds.StonePressurePlate).BlockClass(), plate);

        // Vanilla affectNeighborsAfterRemoval: a powered switch or plate leaving updates the blocks around it and its support.
        var mountedRemoval = new MountedRemoval();
        neighbors.RegisterRemoved(BlockIds.Lever, mountedRemoval);
        foreach (var id in ButtonIds)
            neighbors.RegisterRemoved(id, mountedRemoval);

        var plateRemoval = new PlateRemoval();
        foreach (var id in TagsRegistry.Block.PressurePlates.Entries)
            neighbors.RegisterRemoved(id, plateRemoval);

        redstone.RegisterContact(BlocksRegistry.GetDefault(BlockIds.StoneButton).BlockClass(), new ButtonContact());

        var lamp = new EngineRedstoneLamp(level, engine);
        neighbors.Register(BlockIds.RedstoneLamp, lamp);
        foreach (var id in new[]
        {
            BlockIds.CopperBulb, BlockIds.ExposedCopperBulb, BlockIds.WeatheredCopperBulb, BlockIds.OxidizedCopperBulb,
            BlockIds.WaxedCopperBulb, BlockIds.WaxedExposedCopperBulb, BlockIds.WaxedWeatheredCopperBulb, BlockIds.WaxedOxidizedCopperBulb
        })
        {
            var bulb = new CopperBulbConsumer(engine);
            neighbors.Register(id, bulb);
            neighbors.RegisterPlaced(id, bulb);
        }

        RegisterOpenConsumers(neighbors, engine);

        // Vanilla NoteBlock: the powered state follows the signal, and a note plays only on a rising edge.
        neighbors.Register(BlockIds.NoteBlock, new NoteBlockConsumer(engine, playNote ?? (_ => { })));

        RegisterTicks(level, ticks, torch, lamp);
    }

    private static void RegisterOpenConsumers(NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var consumer = new OpenConsumer(engine);
        // Doors, trapdoors and fence gates open on a powered neighbor (vanilla neighborChanged), whatever their material.
        foreach (var id in TagsRegistry.Block.Doors.Entries.Concat(TagsRegistry.Block.Trapdoors.Entries).Concat(TagsRegistry.Block.FenceGates.Entries))
        {
            var blockClass = BlocksRegistry.GetDefault(id).BlockClass();
            if (registered.Add(blockClass))
            {
                neighbors.RegisterClass(blockClass, consumer);
            }
        }
    }

    /// <summary>
    /// Registers the delayed behaviors. Each tick callback enqueues its work on the level's simulation path rather than writing
    /// directly, and reads the block as it is when the tick comes due.
    /// </summary>
    private static void RegisterTicks(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneTorch torch, EngineRedstoneLamp lamp)
    {
        foreach (var id in ButtonIds)
            ticks.Register(id, (position, _) => level.EnqueueSimulation(() => ReleaseButtonAsync(level, position)));

        ticks.Register(BlockIds.DaylightDetector, (position, _) =>
            level.EnqueueSimulation(() => DaylightDetector.RefreshAsync(level, position)));
        ticks.Register(BlockIds.RedstoneTorch, (position, _) => level.EnqueueSimulation(() => torch.TickAsync(position)));
        ticks.Register(BlockIds.RedstoneWallTorch, (position, _) => level.EnqueueSimulation(() => torch.TickAsync(position)));
        ticks.Register(BlockIds.RedstoneLamp, (position, _) => level.EnqueueSimulation(() => lamp.TickAsync(position)));
    }

    /// <summary>
    /// Whether <paramref name="block"/> is a redstone lamp. Its lit state is derived at placement, not by a placement notification.
    /// </summary>
    public static bool IsLamp(IBlock block) => block.Is(BlockIds.RedstoneLamp);

    /// <summary>
    /// A lamp placed by a player is lit when it has a neighbor signal (vanilla <c>RedstoneLampBlock.getStateForPlacement</c>).
    /// A lamp placed with <c>/setblock</c> keeps the state it was given, so this runs only on the player's placement path.
    /// </summary>
    public static IBlock DeriveLampPlacement(IBlock state, bool hasNeighborSignal) =>
        state.WithProperty("lit", hasNeighborSignal ? "true" : "false");

    /// <summary>
    /// Uses a lever or button before generic item placement. Returns false for all other blocks.
    /// </summary>
    public static async ValueTask<bool> TryUseAsync(IRedstoneLevel level, Vector position, IBlock state, IPlayer player)
    {
        if (state.BlockClass() == "LeverBlock")
        {
            var on = state.GetProperty("powered") != "true";
            await SetMountedStateAsync(level, position, state.WithProperty("powered", on));
            level.PlaySound(position, "minecraft:block.lever.click", 0.3f, on ? 0.6f : 0.5f, player);
            return true;
        }

        if (state.BlockClass() == DaylightDetector.BlockClassName)
        {
            await DaylightDetector.ToggleAsync(level, position, state);
            return true;
        }

        if (state.Is(RedstoneRepeater.BlockId))
        {
            await RedstoneRepeater.CycleDelayAsync(level, position, state);
            return true;
        }

        if (RedstoneComparator.IsComparator(state))
        {
            await RedstoneComparator.ToggleModeAsync(level, position, state, player);
            return true;
        }

        if (state.BlockClass() == "ButtonBlock" && state.GetProperty("powered") != "true")
        {
            await PressButtonAsync(level, position, state, player);
            return true;
        }

        return state.BlockClass() == "ButtonBlock";
    }

    /// <summary>
    /// Vanilla projectile contact (a wind charge's burst) with a lever or button: a lever flips and a button presses, as a player's use
    /// would, without a player to exempt from the sound. Returns whether the block reacted.
    /// </summary>
    public static async ValueTask<bool> TriggerAsync(IRedstoneLevel level, Vector position, IBlock state)
    {
        if (state.BlockClass() == "LeverBlock")
        {
            var on = state.GetProperty("powered") != "true";
            await SetMountedStateAsync(level, position, state.WithProperty("powered", on));
            level.PlaySound(position, "minecraft:block.lever.click", 0.3f, on ? 0.6f : 0.5f);
            return true;
        }

        if (state.BlockClass() == "ButtonBlock" && state.GetProperty("powered") != "true")
        {
            await PressButtonAsync(level, position, state, player: null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The direction from a mounted lever or button to the block it's attached to (its support). Vanilla's
    /// <c>getConnectedDirection</c>, which is also the direction its direct power is sent in.
    /// </summary>
    internal static BlockFace AttachedDirection(IBlock state) => state.GetProperty("face") switch
    {
        "floor" => BlockFace.Down,
        "ceiling" => BlockFace.Up,
        _ => FeatureHelpers.ParseFace(state.GetProperty("facing")).Opposite()
    };

    /// <summary>
    /// Commits a mounted component's new state, then notifies the neighbors of its support. Vanilla does the same, so the blocks next to
    /// the support (for example a door against a powered wall) see the new power.
    /// </summary>
    public static async ValueTask SetMountedStateAsync(IBlockChangeWorld world, Vector position, IBlock state)
    {
        await world.SetBlockAsync(position, state);
        await world.NotifyNeighborsAsync(position.Offset(AttachedDirection(state)));
    }

    // A redstone block gives weak power only: oracle-checked, a stone beside it doesn't pass the power on to a lamp above.
    private sealed class ConstantSource : IRedstoneBehavior
    {
        public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => RedstoneEngine.MaxSignal;
        public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => 0;
    }

    private sealed class MountedSource : IRedstoneBehavior
    {
        public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            state.GetProperty("powered") == "true" ? RedstoneEngine.MaxSignal : 0;

        public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            state.GetProperty("powered") == "true" && direction == AttachedDirection(state).Opposite() ? RedstoneEngine.MaxSignal : 0;

        // Direct power is asked for in the direction from the receiver to the source. The mounted block is the receiver's neighbour
        // in AttachedDirection, so the receiver asks with its opposite (oracle: a powered floor lever powers the block below it).
        // A wall mount's facing points away from the wall.
        private static BlockFace AttachedDirection(IBlock state) => BasicRedstoneComponents.AttachedDirection(state);
    }

    private sealed class TorchSource(bool wall) : IRedstoneBehavior
    {
        public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal)
        {
            if (state.GetProperty("lit") != "true")
                return 0;

            // SignalGetter direction is receiver -> source. A floor torch does not power its support below it (Up here);
            // a wall torch does not power the wall it is mounted on (its facing here).
            return !wall ? direction != BlockFace.Up ? RedstoneEngine.MaxSignal : 0
                : direction != FeatureHelpers.ParseFace(state.GetProperty("facing")) ? RedstoneEngine.MaxSignal : 0;
        }

        // Oracle-verified for a standing torch, and inherited by a wall torch (vanilla's wall torch extends the standing one and
        // overrides only its weak signal): a lit torch strongly powers the block above it, and not the support it stands on.
        public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            state.GetProperty("lit") == "true" && direction == BlockFace.Down ? RedstoneEngine.MaxSignal : 0;
    }

    /// <summary>
    /// Pressure plates output their pressed signal weakly everywhere, and directly to the block below them (oracle-verified: a pressed stone
    /// plate powers its support, and not the block above).
    /// </summary>
    private sealed class PlateSource : IRedstoneBehavior, INeighborChangedBehavior
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
        {
            // BasePressurePlateBlock.updateShape only rechecks support when the block below changes.
            if (change.Source != change.Position + Vector.Down)
                return ValueTask.CompletedTask;

            if (world.GetLoadedBlock(change.Position + Vector.Down) is not IBlock support
                || !support.IsFaceSturdy(BlockFace.Up, SupportType.Rigid))
            {
                world.DropAndRemove(change.Position, change.State, playEffect: true);
            }

            return ValueTask.CompletedTask;
        }

        public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => PressurePlates.Signal(state);

        public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            direction == BlockFace.Up ? PressurePlates.Signal(state) : 0;
    }

    /// <summary>A button's click sound, from its block set (vanilla <c>ButtonBlock.playSound</c>).</summary>
    internal static string ButtonSound(IBlock state, bool on) =>
        $"minecraft:block.{VanillaPlacement.SoundMaterial(state)}_button.{(on ? "click_on" : "click_off")}";

    /// <summary>
    /// Vanilla <c>DiodeBlock.updateNeighborsInFront</c>: the block in front of a diode's output hears of the change, and so do the
    /// blocks around it, except the one toward the diode.
    /// </summary>
    internal static async ValueTask NotifyOutputFrontAsync(IRedstoneLevel level, Vector position, BlockFace output)
    {
        var front = position.Offset(output);
        await level.NotifyNeighborAsync(front, position);
        foreach (var face in RedstoneEngine.Directions)
        {
            if (face != output.Opposite())
                await level.NotifyNeighborAsync(front.Offset(face), front);
        }
    }

    /// <summary>
    /// Whether a button is pressed by arrows: the wooden set (vanilla <c>BlockSetType.canButtonBeActivatedByArrows</c>, which the
    /// generated wooden_buttons tag lists).
    /// </summary>
    private static bool PressedByArrows(IBlock state) => TagsRegistry.Block.WoodenButtons.Entries.Contains(state.RegistryId);

    /// <summary>
    /// Whether an arrow overlaps the button's outline (vanilla <c>checkPressed</c>, which looks for arrows in the shape's bounds).
    /// </summary>
    private static bool ArrowInside(IRedstoneLevel level, Vector position, IBlock state)
    {
        var (offsetX, offsetY, offsetZ) = BlockPhysics.ShapeOffset(state.GetHashCode(), position.X, position.Z);
        foreach (var box in BlockPhysics.OutlineBoxes(state.GetHashCode()))
        {
            var min = (VectorD)position + new VectorD(box[0] + offsetX, box[1] + offsetY, box[2] + offsetZ);
            var max = (VectorD)position + new VectorD(box[3] + offsetX, box[4] + offsetY, box[5] + offsetZ);
            if (level.EntitiesIn(min, max).Any(entity => entity is Arrow))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Presses a button: it powers its support and its time starts (vanilla <c>ButtonBlock.press</c>).
    /// </summary>
    private static async ValueTask PressButtonAsync(IRedstoneLevel level, Vector position, IBlock state, IPlayer? player)
    {
        var powered = state.WithProperty("powered", true);
        await SetMountedStateAsync(level, position, powered);
        level.PlaySound(position, ButtonSound(state, on: true), except: player);
        if (ButtonDelay(state.RegistryId) is { } delay)
            level.ScheduleBlockTick(position, powered, delay);
    }

    /// <summary>
    /// Vanilla <c>ButtonBlock.tick</c> (checkPressed): a pressed button goes off once its time is up, unless a wooden one still has an
    /// arrow in it, when it waits another period. Reads the live block, so a button already released is left alone.
    /// </summary>
    private static async ValueTask ReleaseButtonAsync(IRedstoneLevel level, Vector position)
    {
        if (level.GetLoadedBlock(position) is not IBlock state || state.BlockClass() != "ButtonBlock" || state.GetProperty("powered") != "true")
            return;

        if (PressedByArrows(state) && ArrowInside(level, position, state))
        {
            level.ScheduleBlockTick(position, state, ButtonDelay(state.RegistryId) ?? 30);
            return;
        }

        await SetMountedStateAsync(level, position, state.WithProperty("powered", false));
        level.PlaySound(position, ButtonSound(state, on: false));
    }

    /// <summary>
    /// Vanilla <c>ButtonBlock.entityInside</c>: an unpressed wooden button is pressed while an arrow is in it. Stone buttons ignore arrows.
    /// </summary>
    private sealed class ButtonContact : IRedstoneContact
    {
        public async ValueTask EntityInsideAsync(IRedstoneLevel level, Vector position, IBlock state)
        {
            if (!PressedByArrows(state) || state.GetProperty("powered") == "true" || !ArrowInside(level, position, state))
                return;

            await PressButtonAsync(level, position, state, player: null);
        }
    }

    /// <summary>
    /// Vanilla <c>affectNeighborsAfterRemoval</c> of a powered lever or button (<c>updateNeighbours</c>): its neighbours, and those of its support, hear of it going.
    /// </summary>
    private sealed class MountedRemoval : IRemovalBehavior
    {
        public async ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world)
        {
            if (change.Old.GetProperty("powered") != "true")
                return;

            await world.NotifyNeighborsAsync(change.Position);
            await world.NotifyNeighborsAsync(change.Position.Offset(AttachedDirection(change.Old)));
        }
    }

    /// <summary>
    /// Vanilla <c>BasePressurePlateBlock.affectNeighborsAfterRemoval</c>: a pressed plate leaving updates its neighbours and those of the block below.
    /// </summary>
    private sealed class PlateRemoval : IRemovalBehavior
    {
        public async ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world)
        {
            if (PressurePlates.Signal(change.Old) <= 0)
                return;

            await world.NotifyNeighborsAsync(change.Position);
            await world.NotifyNeighborsAsync(change.Position + Vector.Down);
        }
    }

    private sealed class OpenConsumer(RedstoneEngine engine) : INeighborChangedBehavior
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => this.UpdateAsync(change.Position, change.State, world);

        // Vanilla DoorBlock.setOpen: a door, trapdoor or gate that changes plays its open or close sound once, at its lower half.
        private async ValueTask UpdateAsync(Vector position, IBlock state, IBlockChangeWorld world)
        {
            var powered = engine.HasNeighborSignal(position);
            if (state.GetProperty("half") is "lower" or "upper")
            {
                var lower = state.GetProperty("half") == "lower";
                var otherPosition = position.Offset(lower ? BlockFace.Up : BlockFace.Down);
                var other = world.GetLoadedBlock(otherPosition);
                powered |= other is not null && engine.HasNeighborSignal(otherPosition);
                var changed = await UpdateAsync(position, state, powered, world);
                if (other is not null && other.BlockClass() == state.BlockClass())
                    changed |= await UpdateAsync(otherPosition, other, powered, world);

                if (changed)
                    world.PlaySound(lower ? position : otherPosition, VanillaPlacement.OpenSound(state, powered));
                return;
            }

            if (await UpdateAsync(position, state, powered, world))
                world.PlaySound(position, VanillaPlacement.OpenSound(state, powered));
        }

        // Returns whether the open state changed: only that plays the sound, like vanilla's setOpen. Vanilla acts only on a change
        // of the powered state, so a manual open or close survives neighbour updates that leave the power as it was.
        private static async ValueTask<bool> UpdateAsync(Vector position, IBlock state, bool powered, IBlockChangeWorld world)
        {
            if ((state.GetProperty("powered") == "true") == powered)
                return false;

            var open = state.GetProperty("open") == "true";

            await world.SetBlockAsync(position, state.WithProperty("powered", powered).WithProperty("open", powered));
            return open != powered;
        }
    }

    private sealed class NoteBlockConsumer(RedstoneEngine engine, Action<Vector> playNote) : INeighborChangedBehavior
    {
        public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
        {
            var powered = engine.HasNeighborSignal(change.Position);
            if ((change.State.GetProperty("powered") == "true") == powered)
                return;

            await world.SetBlockAsync(change.Position, change.State.WithProperty("powered", powered));
            if (powered)
                playNote(change.Position);
        }
    }

    private sealed class CopperBulbConsumer(RedstoneEngine engine) : INeighborChangedBehavior, IPlacementBehavior
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => this.UpdateAsync(change.Position, change.State, world);

        public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.UpdateAsync(change.Position, change.New, world);

        private async ValueTask UpdateAsync(Vector position, IBlock state, IBlockChangeWorld world)
        {
            var powered = engine.HasNeighborSignal(position);
            var wasPowered = state.GetProperty("powered") == "true";
            if (powered == wasPowered)
                return;

            var next = state.WithProperty("powered", powered);
            if (powered)
                next = next.WithProperty("lit", next.GetProperty("lit") != "true");
            await world.SetBlockAsync(position, next);
        }
    }
}
