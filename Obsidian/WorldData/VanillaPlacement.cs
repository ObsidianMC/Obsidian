using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Placement;

namespace Obsidian.WorldData;

/// <summary>
/// What vanilla blocks do once a player placed them (a door's upper half, a bed's head), how doors and chests follow
/// their neighbours, and the blocks players open and close by hand: doors, trapdoors and fence gates. The states blocks
/// are placed in are <see cref="BlockPlacements"/>.
/// </summary>
internal static class VanillaPlacement
{
    /// <summary>
    /// Vanilla <c>SlabBlock.canBeReplaced</c>: a slab of the same kind fills the other half of a half slab. When the
    /// placement replaces the clicked block, the click must land on the open side: the top of a bottom slab, or the
    /// lower side of a horizontal face of a top slab, and so on. Otherwise the placement adjoins it and any non-double slab works.
    /// </summary>
    /// <param name="upperHit">Whether the click landed above the middle of the clicked block.</param>
    public static bool SlabCanBeReplaced(IBlock target, IBlock placing, bool replacingClicked, BlockFace face, bool upperHit)
    {
        if (!SameSlab(target, placing) || target.GetProperty("type") == "double")
            return false;

        if (!replacingClicked)
            return true;

        var horizontal = face is BlockFace.North or BlockFace.South or BlockFace.East or BlockFace.West;
        return target.GetProperty("type") == "bottom"
            ? face == BlockFace.Up || upperHit && horizontal
            : face == BlockFace.Down || !upperHit && horizontal;
    }

    private static bool SameSlab(IBlock block, IBlock other) =>
        IsSlab(block) && IsSlab(other) && block.BaseId == other.BaseId;

    private static bool IsSlab(IBlock block) => block.BlockClass() is "SlabBlock" or "WeatheringCopperSlabBlock";

    private static bool IsDoor(IBlock block) => block.BlockClass() is "DoorBlock" or "WeatheringCopperDoorBlock";

    private static bool IsTrapdoor(IBlock block) =>
        block.BlockClass() is "TrapDoorBlock" or "WeatheringCopperTrapDoorBlock";

    private static bool IsChest(IBlock block) =>
        block.BlockClass() is "ChestBlock" or "TrappedChestBlock" or "CopperChestBlock" or "WeatheringCopperChestBlock";

    /// <summary>
    /// Where the second half of a block a player places goes (a door's or tall plant's upper half, a bed's head), or
    /// null for a block of one piece.
    /// </summary>
    public static Vector? SecondHalf(IBlock state, Vector position) => state.BlockClass() switch
    {
        "DoorBlock" or "WeatheringCopperDoorBlock" => position + Vector.Up,
        "DoublePlantBlock" or "TallFlowerBlock" or "TallSeagrassBlock" or "SmallDripleafBlock" => position + Vector.Up,
        "BedBlock" or "StrawBedBlock" => position + FeatureHelpers.ParseFace(state.GetProperty("facing")).ToVector(),
        _ => null
    };

    /// <summary>
    /// Whether the second half of <paramref name="state"/> still has room, as its placement found when it was worked
    /// out: something placed meanwhile (by a plugin's place event, say) refuses the whole placement rather than being
    /// overwritten. So does a second half in a chunk that isn't loaded (it reads as void air), where it couldn't be
    /// written: a bed's foot would be placed, and the item used, without its head.
    /// </summary>
    /// <remarks>Call it under the level's tick lock, with the placement it checks, so the chunk can't go between.</remarks>
    public static bool SecondHalfFits(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (SecondHalf(state, position) is not Vector other)
            return true;

        var there = level.GetBlock(other);
        return !there.Is(BlockIds.VoidAir) && there.CanBeReplaced();
    }

    /// <summary>
    /// Vanilla <c>setPlacedBy</c>: a door's upper half goes on top of the lower half a player placed, a bed's head in
    /// front of its foot, and a tall plant's upper half above it.
    /// </summary>
    public static void OnPlacedByPlayer(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (!level.GetBlock(position).IsSameState(state) || SecondHalf(state, position) is not Vector other)
            return;

        switch (state.BlockClass())
        {
            case "DoorBlock" or "WeatheringCopperDoorBlock":
                level.Set(other, state.WithProperty("half", "upper"), BlockUpdateFlags.All);
                break;
            case "BedBlock" or "StrawBedBlock":
                level.Set(other, state.WithProperty("part", "head"), BlockUpdateFlags.All);
                break;
            case "DoublePlantBlock" or "TallFlowerBlock" or "TallSeagrassBlock":
                // Vanilla DoublePlantBlock.setPlacedBy: the upper half's default state.
                var upper = BlocksRegistry.GetDefault(state.RegistryId).WithProperty("half", "upper");
                level.Set(other, upper, BlockUpdateFlags.All);
                break;
            case "SmallDripleafBlock":
                // Vanilla SmallDripleafBlock.setPlacedBy: turned like the lower half, taking any water above.
                var leaf = BlocksRegistry.GetDefault(state.RegistryId)
                    .WithProperty("half", "upper")
                    .WithProperty("facing", state.GetProperty("facing")!)
                    .WithProperty("waterlogged", IsWater(level.GetBlock(other)));
                level.Set(other, leaf, BlockUpdateFlags.All);
                break;
        }
    }

    private static bool IsWater(IBlock block) => block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater;

    /// <summary>
    /// Vanilla <c>updateShape</c> for chests (pairing up with a chest placed beside, and back to single when it goes).
    /// Doors follow their partner and support through the shared pass's rule (<see cref="ShapeRules.DoorShapeRule"/>).
    /// </summary>
    /// <returns>The new state, or null for a block this doesn't handle.</returns>
    public static IBlock? UpdateShape(IBlock state, BlockFace direction, IBlock neighbor)
    {
        if (!IsChest(state))
            return null;

        if (direction.IsVertical())
            return state;

        var connects = BlockPlacements.ChestsConnect(state, neighbor);
        var updated = state;
        if (connects)
        {
            var type = neighbor.GetProperty("type");
            if (state.GetProperty("type") == "single" && type != "single"
                && state.GetProperty("facing") == neighbor.GetProperty("facing")
                && BlockPlacements.ConnectedDirection(neighbor) == direction.Opposite())
            {
                updated = state.WithProperty("type", type == "left" ? "right" : "left");
            }
        }
        else if (BlockPlacements.ConnectedDirection(state) == direction)
        {
            updated = state.WithProperty("type", "single");
        }

        // Vanilla CopperChestBlock.updateShape: a copper chest paired with another becomes its kind, so the two halves
        // weather (and are waxed) alike.
        var pairedWith = connects
            && BlockPlacements.IsCopperChest(state)
            && updated.GetProperty("type") != "single"
            && BlockPlacements.ConnectedDirection(updated) == direction;
        if (!pairedWith)
            return updated;

        return BlocksRegistry.GetDefault(neighbor.RegistryId)
            .WithProperty("facing", updated.GetProperty("facing")!)
            .WithProperty("type", updated.GetProperty("type")!)
            .WithProperty("waterlogged", updated.GetProperty("waterlogged")!);
    }

    /// <summary>
    /// Vanilla <c>useWithoutItem</c> for doors, trapdoors and fence gates: a player opens or closes one. Iron doors and
    /// trapdoors don't open by hand.
    /// </summary>
    /// <returns>The sound the block makes, or null when it didn't react.</returns>
    public static string? Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player)
    {
        var gate = state.BlockClass() == "FenceGateBlock";
        if (!IsDoor(state) && !IsTrapdoor(state) && !gate
            || state.UnlocalizedName is "minecraft:iron_door" or "minecraft:iron_trapdoor")
        {
            return null;
        }

        var open = state.GetProperty("open") != "true";
        if (gate && open)
        {
            // A gate swings away from the player opening it.
            var looking = PlacementDirections.Horizontal(player.Yaw.Degrees);
            if (state.GetProperty("facing") == FeatureHelpers.FaceName(looking.Opposite()))
                state = state.WithProperty("facing", FeatureHelpers.FaceName(looking));
        }

        level.Set(position, state.WithProperty("open", open), BlockUpdateFlags.All);

        return OpenSound(state, open);
    }

    /// <summary>The sound a door, trapdoor or fence gate makes as it opens or closes (vanilla's block set sounds).</summary>
    public static string OpenSound(IBlock state, bool open)
    {
        var material = SoundMaterial(state);
        string sound;
        if (IsDoor(state))
            sound = $"{material}_door";
        else if (IsTrapdoor(state))
            sound = $"{material}_trapdoor";
        else
            sound = material is "wooden" or "copper" ? "fence_gate" : $"{material}_fence_gate";

        return $"minecraft:block.{sound}.{(open ? "open" : "close")}";
    }

    /// <summary>
    /// The sound material of a block, named as vanilla's block sets name it: iron, copper, stone, copper and
    /// bamboo, cherry and nether wood have their own; other wood shares one.
    /// </summary>
    public static string SoundMaterial(IBlock state)
    {
        var name = state.UnlocalizedName;
        return name.Contains("iron") ? "iron"
            : name.Contains("copper") ? "copper"
            : name.Contains("stone") ? "stone"
            : name.Contains("bamboo") ? "bamboo_wood"
            : name.Contains("cherry") ? "cherry_wood"
            : name.Contains("crimson") || name.Contains("warped") ? "nether_wood"
            : "wooden";
    }
}
