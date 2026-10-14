using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Entities;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// What levers and buttons share, like vanilla's <c>FaceAttachedHorizontalDirectionalBlock</c>: attached to the floor,
/// a wall or the ceiling, they power everything around them while on, and strongly the block they're on.
/// </summary>
internal abstract class FaceAttachedSwitch : RedstoneComponent
{
    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        IsPowered(state) ? 15 : 0;

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        IsPowered(state) && ConnectedDirection(state) == direction ? 15 : 0;

    // Vanilla FaceAttachedHorizontalDirectionalBlock.getStateForPlacement: on the first face among the nearest looking
    // directions it can attach to, the floor and ceiling facing the way the player does.
    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        foreach (var direction in placement.NearestLookingDirections)
        {
            var state = direction.IsVertical()
                ? placement.Block
                    .WithProperty("face", direction == BlockFace.Up ? "ceiling" : "floor")
                    .WithProperty("facing", placement.HorizontalDirection.PropertyName())
                : placement.Block
                    .WithProperty("face", "wall")
                    .WithProperty("facing", direction.Opposite().PropertyName());

            if (CanSurvive(placement.Level, placement.Position, state))
                return state;
        }

        return null;
    }

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (IsPowered(state))
            UpdateNeighbours(level, position, state);
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor)
    {
        if (ConnectedDirection(state).Opposite() != direction || CanSurvive(level, position, state))
            return state;

        return BlocksRegistry.Air;
    }

    protected static bool IsPowered(IBlock state) => state.GetProperty("powered") == "true";

    /// <summary>
    /// Vanilla <c>updateNeighbours</c>: its own neighbours, then those of the block it's on.
    /// </summary>
    protected static void UpdateNeighbours(LevelBlockUpdates level, Vector position, IBlock state)
    {
        level.UpdateNeighbors(position, state);
        level.UpdateNeighbors(position.Offset(ConnectedDirection(state).Opposite()), state);
    }

    // Vanilla getConnectedDirection: the side away from the block it's on.
    private static BlockFace ConnectedDirection(IBlock state) => state.GetProperty("face") switch
    {
        "ceiling" => BlockFace.Down,
        "floor" => BlockFace.Up,
        _ => FeatureHelpers.ParseFace(state.GetProperty("facing"))
    };

    // Vanilla canAttach: the block it's on has a full face toward it.
    private static bool CanSurvive(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var connected = ConnectedDirection(state);
        return level.GetBlock(position.Offset(connected.Opposite())).IsFaceSturdy(connected);
    }
}

/// <summary>
/// The lever, like vanilla's <c>LeverBlock</c>: players and wind charges flip it.
/// </summary>
internal sealed class Lever : FaceAttachedSwitch
{
    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        Pull(level, position, state);
        return true;
    }

    public override bool Trigger(LevelBlockUpdates level, Vector position, IBlock state)
    {
        Pull(level, position, state);
        return true;
    }

    // Vanilla's pull: the lever flips, its neighbours hear of it, and it clicks.
    private static void Pull(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var pulled = state.WithProperty("powered", !IsPowered(state));
        level.Set(position, pulled, BlockUpdateFlags.All);
        UpdateNeighbours(level, position, pulled);
        level.PlaySound(position, "minecraft:block.lever.click", 0.3f, IsPowered(pulled) ? 0.6f : 0.5f);
    }
}

/// <summary>
/// A button, like vanilla's <c>ButtonBlock</c>: pressed by players and wind charges, and wooden ones by arrows in
/// them, it stays on for 20 game ticks (stone) or 30 (wood).
/// </summary>
internal sealed class Button(int ticksToStayPressed, bool pressedByArrows, string sound) : FaceAttachedSwitch
{
    /// <summary>
    /// Vanilla <c>BlockSetType.canButtonBeActivatedByArrows</c>: wooden buttons.
    /// </summary>
    public bool PressedByArrows => pressedByArrows;

    /// <summary>
    /// The button with the given identifier, by vanilla's block set types: stone and polished blackstone buttons,
    /// and the wooden ones with their wood's sounds.
    /// </summary>
    public static Button For(string name) => name switch
    {
        "minecraft:stone_button" or "minecraft:polished_blackstone_button" => new(20, false, "stone_button"),
        _ => new(30, true, WoodSound(name, "button"))
    };

    /// <summary>
    /// The sounds' prefix of a wooden button or pressure plate, like vanilla's block set types': cherry, bamboo and
    /// nether wood have their own, the others wood's.
    /// </summary>
    public static string WoodSound(string name, string kind) => name switch
    {
        _ when name.StartsWith("minecraft:cherry_", StringComparison.Ordinal) => $"cherry_wood_{kind}",
        _ when name.StartsWith("minecraft:bamboo_", StringComparison.Ordinal) => $"bamboo_wood_{kind}",
        _ when name.StartsWith("minecraft:crimson_", StringComparison.Ordinal) => $"nether_wood_{kind}",
        _ when name.StartsWith("minecraft:warped_", StringComparison.Ordinal) => $"nether_wood_{kind}",
        _ => $"wooden_{kind}"
    };

    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        if (!IsPowered(state))
            this.Press(level, position, state, player);

        return true;
    }

    public override bool Trigger(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (IsPowered(state))
            return false;

        this.Press(level, position, state, null);
        return true;
    }

    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (IsPowered(state))
            this.CheckPressed(level, position, state);
    }

    public override void EntityInside(LevelBlockUpdates level, Vector position, IBlock state, IEntity entity)
    {
        if (pressedByArrows && !IsPowered(state))
            this.CheckPressed(level, position, state);
    }

    // Vanilla's press: on, its neighbours hear of it, and it stays on its time.
    private void Press(LevelBlockUpdates level, Vector position, IBlock state, IPlayer? player)
    {
        var pressed = state.WithProperty("powered", true);
        level.Set(position, pressed, BlockUpdateFlags.All);
        UpdateNeighbours(level, position, pressed);
        level.ScheduleTick(position, pressed, ticksToStayPressed);
        level.PlaySound(position, $"minecraft:block.{sound}.click_on", except: player);
    }

    // Vanilla checkPressed: a wooden button stays on while an arrow is in it, and turns off otherwise.
    private void CheckPressed(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var arrowed = pressedByArrows && HasArrow(level, position, state);
        if (arrowed != IsPowered(state))
        {
            var updated = state.WithProperty("powered", arrowed);
            level.Set(position, updated, BlockUpdateFlags.All);
            UpdateNeighbours(level, position, updated);
            level.PlaySound(position, $"minecraft:block.{sound}.{(arrowed ? "click_on" : "click_off")}");
        }

        if (arrowed)
            level.ScheduleTick(position, state, ticksToStayPressed);
    }

    // An arrow in the bounds of the button's shape, like vanilla's.
    private static bool HasArrow(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var boxes = BlockPhysics.OutlineBoxes(state.GetHashCode());
        if (boxes.Count == 0)
            return false;

        var min = new VectorD(boxes.Min(box => box[0]), boxes.Min(box => box[1]), boxes.Min(box => box[2]));
        var max = new VectorD(boxes.Max(box => box[3]), boxes.Max(box => box[4]), boxes.Max(box => box[5]));
        var corner = (VectorD)position;

        return level.EntitiesIn(corner + min, corner + max).Any(entity => entity is Arrow);
    }
}
