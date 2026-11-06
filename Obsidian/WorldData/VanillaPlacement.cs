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
    /// Vanilla <c>setPlacedBy</c>: a door's upper half goes on top of the lower half a player placed, a bed's head in
    /// front of its foot, and a tall plant's upper half above it.
    /// </summary>
    public static void OnPlacedByPlayer(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (!level.GetBlock(position).IsSameState(state))
            return;

        var above = position + Vector.Up;
        switch (state.BlockClass())
        {
            case "DoorBlock" or "WeatheringCopperDoorBlock":
                level.Set(above, state.WithProperty("half", "upper"), BlockUpdateFlags.All);
                break;
            case "BedBlock" or "StrawBedBlock":
                var head = position + FeatureHelpers.ParseFace(state.GetProperty("facing")).ToVector();
                level.Set(head, state.WithProperty("part", "head"), BlockUpdateFlags.All);
                break;
            case "DoublePlantBlock" or "TallFlowerBlock" or "TallSeagrassBlock":
                // Vanilla DoublePlantBlock.setPlacedBy: the upper half's default state.
                var upper = BlocksRegistry.GetDefault(state.RegistryId).WithProperty("half", "upper");
                level.Set(above, upper, BlockUpdateFlags.All);
                break;
            case "SmallDripleafBlock":
                // Vanilla SmallDripleafBlock.setPlacedBy: turned like the lower half, taking any water above.
                var leaf = BlocksRegistry.GetDefault(state.RegistryId)
                    .WithProperty("half", "upper")
                    .WithProperty("facing", state.GetProperty("facing")!)
                    .WithProperty("waterlogged", IsWater(level.GetBlock(above)));
                level.Set(above, leaf, BlockUpdateFlags.All);
                break;
        }
    }

    private static bool IsWater(IBlock block) => block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater;

    /// <summary>
    /// Vanilla <c>updateShape</c> for doors (each half follows the other, and goes without it or its support) and
    /// chests (pairing up with a chest placed beside, and back to single when it goes).
    /// </summary>
    /// <returns>The new state, or null for a block this doesn't handle.</returns>
    public static IBlock? UpdateShape(IBlock state, BlockFace direction, IBlock neighbor)
    {
        if (IsDoor(state))
        {
            var lower = state.GetProperty("half") == "lower";
            if (direction.IsVertical() && lower == (direction == BlockFace.Up))
            {
                var otherHalf = neighbor.RegistryId == state.RegistryId
                    && neighbor.GetProperty("half") != state.GetProperty("half");
                return otherHalf ? neighbor.WithProperty("half", lower ? "lower" : "upper") : BlocksRegistry.Air;
            }

            var unsupported = lower && direction == BlockFace.Down && !neighbor.HasFullTopCollisionFace();
            return unsupported ? BlocksRegistry.Air : state;
        }

        if (!IsChest(state))
            return null;

        if (direction.IsVertical())
            return state;

        if (neighbor.RegistryId == state.RegistryId)
        {
            var type = neighbor.GetProperty("type");
            if (state.GetProperty("type") == "single" && type != "single"
                && state.GetProperty("facing") == neighbor.GetProperty("facing")
                && BlockPlacements.ConnectedDirection(neighbor) == direction.Opposite())
            {
                return state.WithProperty("type", type == "left" ? "right" : "left");
            }
        }
        else if (BlockPlacements.ConnectedDirection(state) == direction)
        {
            return state.WithProperty("type", "single");
        }

        return state;
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
