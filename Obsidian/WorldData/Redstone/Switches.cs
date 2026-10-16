using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Entities;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A lever or button: attached to the floor, a wall or the ceiling, it powers everything around it while on, and
/// strongly the block it's on. Players and wind charges flip a lever, or press a button, which stays on for its time
/// (and wooden ones while an arrow is in them).
/// </summary>
/// <param name="pressedFor">
/// Game ticks a button stays on: 20 for stone, 30 for wood; null for a lever, which stays as it's flipped.
/// </param>
/// <param name="pressedByArrows">Whether arrows in it press it: wooden buttons.</param>
/// <param name="sound">The sounds' prefix, like <c>stone_button</c>, for buttons.</param>
internal sealed class Switch(int? pressedFor, bool pressedByArrows, string sound) : RedstoneComponent
{
    /// <summary>
    /// The lever.
    /// </summary>
    public static Switch Lever { get; } = new(null, false, "lever");

    /// <summary>
    /// Whether arrows in it press it: wooden buttons, like vanilla's <c>canButtonBeActivatedByArrows</c>.
    /// </summary>
    public bool PressedByArrows => pressedByArrows;

    /// <summary>
    /// The button with the given identifier, by vanilla's block set types: stone and polished blackstone buttons,
    /// and the wooden ones with their wood's sounds.
    /// </summary>
    public static Switch Button(string name) => name switch
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

    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        IsOn(state) ? 15 : 0;

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        IsOn(state) && AwayFromSupport(state) == direction ? 15 : 0;

    // On the first face among the nearest looking directions it can attach to; on the floor or ceiling, it faces the
    // way the player does.
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
        if (IsOn(state))
            UpdateAround(level, position, state);
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor)
    {
        if (AwayFromSupport(state).Opposite() != direction || CanSurvive(level, position, state))
            return state;

        return BlocksRegistry.Air;
    }

    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        if (pressedFor is null)
            this.Turn(level, position, state, !IsOn(state), player: null);
        else if (!IsOn(state))
            this.Turn(level, position, state, true, player);

        return true;
    }

    public override bool Trigger(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (pressedFor is not null && IsOn(state))
            return false;

        this.Turn(level, position, state, !IsOn(state), player: null);
        return true;
    }

    // A pressed button's time is up: it goes off, unless a wooden one has an arrow in it.
    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (pressedFor is not null && IsOn(state))
            this.Recheck(level, position, state);
    }

    public override void EntityInside(LevelBlockUpdates level, Vector position, IBlock state, IEntity entity)
    {
        if (pressedByArrows && !IsOn(state))
            this.Recheck(level, position, state);
    }

    private static bool IsOn(IBlock state) => state.GetProperty("powered") == "true";

    // The side away from the block it's on.
    private static BlockFace AwayFromSupport(IBlock state) => state.GetProperty("face") switch
    {
        "ceiling" => BlockFace.Down,
        "floor" => BlockFace.Up,
        _ => FeatureHelpers.ParseFace(state.GetProperty("facing"))
    };

    // The block it's on needs a full face toward it.
    private static bool CanSurvive(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var away = AwayFromSupport(state);
        return level.GetBlock(position.Offset(away.Opposite())).IsFaceSturdy(away);
    }

    // Its own neighbours hear of it, then those of the block it's on, as vanilla's switches update.
    private static void UpdateAround(LevelBlockUpdates level, Vector position, IBlock state)
    {
        level.UpdateNeighbors(position, state);
        level.UpdateNeighbors(position.Offset(AwayFromSupport(state).Opposite()), state);
    }

    // Turns the switch on or off with its neighbours' updates and its click. A button pressed on stays on for its
    // time; the click of a player's press is the player's client's own.
    private void Turn(LevelBlockUpdates level, Vector position, IBlock state, bool on, IPlayer? player)
    {
        var turned = state.WithProperty("powered", on);
        level.Set(position, turned, BlockUpdateFlags.All);
        UpdateAround(level, position, turned);

        if (pressedFor is not int time)
        {
            level.PlaySound(position, "minecraft:block.lever.click", 0.3f, on ? 0.6f : 0.5f);
            return;
        }

        level.ScheduleTick(position, turned, time);
        level.PlaySound(position, $"minecraft:block.{sound}.click_on", except: player);
    }

    // A wooden button stays on while an arrow is in it, and turns off otherwise, like vanilla's.
    private void Recheck(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var arrowed = pressedByArrows && HasArrow(level, position, state);
        if (arrowed != IsOn(state))
        {
            var updated = state.WithProperty("powered", arrowed);
            level.Set(position, updated, BlockUpdateFlags.All);
            UpdateAround(level, position, updated);
            level.PlaySound(position, $"minecraft:block.{sound}.{(arrowed ? "click_on" : "click_off")}");
        }

        if (arrowed)
            level.ScheduleTick(position, state, pressedFor!.Value);
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
