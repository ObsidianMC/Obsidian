using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// The states vanilla blocks take when a player places them (their facing, axis, half, a chest's pairing, a door's
/// hinge), and the blocks players open and close by hand: doors, trapdoors and fence gates.
/// </summary>
internal static class VanillaPlacement
{
    /// <summary>
    /// Vanilla <c>getStateForPlacement</c> for a vanilla block that isn't a redstone component.
    /// </summary>
    /// <param name="cursor">Where the player clicked on the clicked block, from 0 to 1 on each axis.</param>
    /// <returns>The state to place, or null when it can't go there.</returns>
    public static IBlock? GetState(LevelBlockUpdates level, IBlock block, Vector position, IPlayer player,
        BlockFace face, VectorD cursor, bool replacesClicked)
    {
        var looking = PlacementDirections.Horizontal(player.Yaw.Degrees);
        var nearest = PlacementDirections.Nearest(player.Yaw.Degrees, player.Pitch.Degrees);
        var kind = block.BlockClass();
        // Vanilla's clicked height, relative to the block placed: above the middle of it places a top half.
        var clickedY = cursor.Y + (ClickedBlockY(position, face, replacesClicked) - position.Y);
        var upperHalf = face != BlockFace.Up && (face == BlockFace.Down || clickedY > 0.5);

        switch (kind)
        {
            case "DoorBlock":
                return DoorState(level, block, position, looking, cursor);
            case "TrapDoorBlock":
                block = !replacesClicked && !face.IsVertical()
                    ? block.WithProperty("facing", Name(face)).WithProperty("half", upperHalf ? "top" : "bottom")
                    : block.WithProperty("facing", Name(looking.Opposite()))
                        .WithProperty("half", face == BlockFace.Up ? "bottom" : "top");
                return Waterlog(level, block, position);
            case "FenceGateBlock":
                return block.WithProperty("facing", Name(looking));
            case "ChestBlock" or "TrappedChestBlock" or "CopperChestBlock" or "WeatheringCopperChestBlock":
                return Waterlog(level, ChestState(level, block, position, looking, face, player.Sneaking), position);
            case "StairBlock":
                return Waterlog(level, block.WithProperty("facing", Name(looking))
                    .WithProperty("half", upperHalf ? "top" : "bottom"), position);
            case "SlabBlock":
                return Waterlog(level, block.WithProperty("type", upperHalf ? "top" : "bottom"), position);
            case "BedBlock":
                // Vanilla's BedBlock.getStateForPlacement: the head goes in front, the way the player looks, so that
                // space must take it.
                if (!level.GetBlock(position + looking.ToVector()).CanBeReplaced())
                    return null;

                return block.WithProperty("facing", Name(looking)).WithProperty("part", "foot").WithProperty("occupied", false);
            case "AnvilBlock":
                return block.WithProperty("facing", Name(looking.ClockWise()));
            case "LadderBlock":
                return face.IsVertical() ? null : Waterlog(level, block.WithProperty("facing", Name(face)), position);
            case "ObserverBlock":
                return block.WithProperty("facing", Name(nearest));
            case "EndRodBlock" or "LightningRodBlock" or "AmethystClusterBlock":
                return Waterlog(level, block.WithProperty("facing", Name(face)), position);
            case "StandingSignBlock" or "CeilingHangingSignBlock":
                return Signs.GetState(level, block, position, player, face);
        }

        if (block.GetProperty("facing") is not null)
        {
            // A block that can face up or down faces away from where the player looks; one that can't, the player.
            var sixWay = block.WithProperty("facing", "up").GetProperty("facing") == "up";
            block = block.WithProperty("facing", Name(sixWay ? nearest.Opposite() : looking.Opposite()));
        }

        if (block.GetProperty("axis") is "x" or "y" or "z")
            block = block.WithProperty("axis", face is BlockFace.Up or BlockFace.Down ? "y"
                : face is BlockFace.North or BlockFace.South ? "z" : "x");

        if (block.GetProperty("rotation") is not null)
            block = block.WithProperty("rotation", (int)Math.Floor((player.Yaw.Degrees + 180) * 16 / 360 + 0.5) & 15);

        return Waterlog(level, block, position);
    }

    // The clicked block's height: the placed block's own when it replaces the clicked one, else the one it's against.
    private static int ClickedBlockY(Vector position, BlockFace face, bool replacesClicked) =>
        replacesClicked ? position.Y : position.Y - face.ToVector().Y;

    /// <summary>
    /// Vanilla <c>setPlacedBy</c>: a door's upper half goes on top of the lower half a player placed, and a bed's head
    /// in front of its foot.
    /// </summary>
    public static void OnPlacedByPlayer(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (!level.GetBlock(position).IsSameState(state))
            return;

        switch (state.BlockClass())
        {
            case "DoorBlock":
                level.Set(position + Vector.Up, Waterlog(level, state.WithProperty("half", "upper"), position + Vector.Up),
                    BlockUpdateFlags.All);
                break;
            case "BedBlock":
                var head = position + FeatureHelpers.ParseFace(state.GetProperty("facing")).ToVector();
                level.Set(head, state.WithProperty("part", "head"), BlockUpdateFlags.All);
                break;
        }
    }

    /// <summary>
    /// Vanilla <c>updateShape</c> for doors (each half follows the other, and goes without it or its support) and
    /// chests (pairing up with a chest placed beside, and back to single when it goes).
    /// </summary>
    /// <returns>The new state, or null for a block this doesn't handle.</returns>
    public static IBlock? UpdateShape(IBlock state, BlockFace direction, IBlock neighbor)
    {
        switch (state.BlockClass())
        {
            case "DoorBlock":
            {
                var lower = state.GetProperty("half") == "lower";
                if (direction.IsVertical() && lower == (direction == BlockFace.Up))
                    return neighbor.RegistryId == state.RegistryId && neighbor.GetProperty("half") != state.GetProperty("half")
                        ? neighbor.WithProperty("half", lower ? "lower" : "upper").WithProperty("waterlogged",
                            state.GetProperty("waterlogged") ?? "false")
                        : BlocksRegistry.Air;
                return lower && direction == BlockFace.Down && !neighbor.HasFullTopCollisionFace() ? BlocksRegistry.Air : state;
            }
            case "ChestBlock" or "TrappedChestBlock" or "CopperChestBlock" or "WeatheringCopperChestBlock":
            {
                if (direction.IsVertical())
                    return state;

                if (neighbor.RegistryId == state.RegistryId)
                {
                    var type = neighbor.GetProperty("type");
                    if (state.GetProperty("type") == "single" && type != "single"
                        && state.GetProperty("facing") == neighbor.GetProperty("facing")
                        && ConnectedDirection(neighbor) == direction.Opposite())
                        return state.WithProperty("type", type == "left" ? "right" : "left");
                }
                else if (ConnectedDirection(state) == direction)
                    return state.WithProperty("type", "single");

                return state;
            }
        }

        return null;
    }

    /// <summary>
    /// Vanilla <c>useWithoutItem</c> for doors, trapdoors and fence gates: a player opens or closes one. Iron doors and
    /// trapdoors don't open by hand.
    /// </summary>
    /// <returns>The sound the block makes, or null when it didn't react.</returns>
    public static string? Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player)
    {
        var kind = state.BlockClass();
        if (kind is not ("DoorBlock" or "TrapDoorBlock" or "FenceGateBlock")
            || state.UnlocalizedName is "minecraft:iron_door" or "minecraft:iron_trapdoor")
            return null;

        var open = state.GetProperty("open") != "true";
        if (kind == "FenceGateBlock" && open)
        {
            // A gate swings away from the player opening it.
            var looking = PlacementDirections.Horizontal(player.Yaw.Degrees);
            if (state.GetProperty("facing") == Name(looking.Opposite()))
                state = state.WithProperty("facing", Name(looking));
        }

        level.Set(position, state.WithProperty("open", open), BlockUpdateFlags.All);

        var name = state.UnlocalizedName;
        // Vanilla's block set sounds: copper, bamboo, cherry and nether wood have their own, other wood shares one.
        var material = name.Contains("copper") ? "copper" : name.Contains("bamboo") ? "bamboo_wood"
            : name.Contains("cherry") ? "cherry_wood" : name.Contains("crimson") || name.Contains("warped") ? "nether_wood"
            : "wooden";
        var sound = kind switch
        {
            "DoorBlock" => $"{material}_door",
            "TrapDoorBlock" => $"{material}_trapdoor",
            _ => material is "wooden" or "copper" ? "fence_gate" : $"{material}_fence_gate"
        };
        return $"minecraft:block.{sound}.{(open ? "open" : "close")}";
    }

    private static IBlock? DoorState(LevelBlockUpdates level, IBlock block, Vector position, BlockFace looking,
        VectorD cursor)
    {
        if (!level.GetBlock(position + Vector.Up).CanBeReplaced() || !level.GetBlock(position + Vector.Down).HasFullTopCollisionFace())
            return null;

        return Waterlog(level, block.WithProperty("facing", Name(looking)).WithProperty("half", "lower")
            .WithProperty("hinge", Hinge(level, block, position, looking, cursor)), position);
    }

    // Vanilla DoorBlock.getHinge: against the wall beside it, or beside a door so the two open as a pair, or where
    // the player clicked.
    private static string Hinge(LevelBlockUpdates level, IBlock door, Vector position, BlockFace facing, VectorD cursor)
    {
        var left = position.Offset(facing.CounterClockWise());
        var right = position.Offset(facing.ClockWise());
        var leftState = level.GetBlock(left);
        var rightState = level.GetBlock(right);
        var walls = (Full(leftState) ? -1 : 0) + (Full(level.GetBlock(left + Vector.Up)) ? -1 : 0)
            + (Full(rightState) ? 1 : 0) + (Full(level.GetBlock(right + Vector.Up)) ? 1 : 0);
        var doorLeft = leftState.RegistryId == door.RegistryId && leftState.GetProperty("half") == "lower";
        var doorRight = rightState.RegistryId == door.RegistryId && rightState.GetProperty("half") == "lower";

        if (doorLeft && !doorRight || walls > 0)
            return "right";
        if (doorRight && !doorLeft || walls < 0)
            return "left";

        var step = facing.ToVector();
        return step.X < 0 && cursor.Z < 0.5 || step.X > 0 && cursor.Z > 0.5
            || step.Z < 0 && cursor.X > 0.5 || step.Z > 0 && cursor.X < 0.5 ? "right" : "left";

        static bool Full(IBlock block) => block.HasFullTopCollisionFace();
    }

    // Vanilla ChestBlock.getStateForPlacement: a chest pairs with a single chest beside it facing the same way, or,
    // sneaking, with the one clicked.
    private static IBlock ChestState(LevelBlockUpdates level, IBlock block, Vector position, BlockFace looking,
        BlockFace face, bool sneaking)
    {
        var facing = looking.Opposite();
        var type = "single";
        if (!face.IsVertical() && sneaking)
        {
            if (PartnerFacing(level, block, position, face.Opposite()) is BlockFace partner
                && partner != face && partner != face.Opposite())
            {
                facing = partner;
                type = partner.CounterClockWise() == face.Opposite() ? "right" : "left";
            }
        }

        if (type == "single" && !sneaking)
        {
            if (PartnerFacing(level, block, position, facing.ClockWise()) == facing)
                type = "left";
            else if (PartnerFacing(level, block, position, facing.CounterClockWise()) == facing)
                type = "right";
        }

        return block.WithProperty("facing", Name(facing)).WithProperty("type", type);
    }

    // The facing of a single chest of the same kind in that direction, if there is one.
    private static BlockFace? PartnerFacing(LevelBlockUpdates level, IBlock block, Vector position, BlockFace direction)
    {
        var other = level.GetBlock(position.Offset(direction));
        return other.RegistryId == block.RegistryId && other.GetProperty("type") == "single"
            ? Face(other.GetProperty("facing"))
            : null;
    }

    // Vanilla ChestBlock.getConnectedDirection: where the other half of a double chest is.
    private static BlockFace? ConnectedDirection(IBlock chest)
    {
        if (Face(chest.GetProperty("facing")) is not BlockFace facing)
            return null;

        return chest.GetProperty("type") switch
        {
            "left" => facing.ClockWise(),
            "right" => facing.CounterClockWise(),
            _ => null
        };
    }

    // A block placed in a water source is waterlogged, if it can be.
    internal static IBlock Waterlog(LevelBlockUpdates level, IBlock block, Vector position)
    {
        if (block.GetProperty("waterlogged") is null)
            return block;

        var there = level.GetBlock(position);
        return block.WithProperty("waterlogged", there.Is(BlockIds.Water) && there.GetProperty("level") is null or "0");
    }

    private static string Name(BlockFace face) => face.ToString().ToLowerInvariant();

    private static BlockFace? Face(string? name) => name switch
    {
        "north" => BlockFace.North,
        "south" => BlockFace.South,
        "west" => BlockFace.West,
        "east" => BlockFace.East,
        "up" => BlockFace.Up,
        "down" => BlockFace.Down,
        _ => null
    };
}
