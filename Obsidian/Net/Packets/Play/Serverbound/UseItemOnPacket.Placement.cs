using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;

namespace Obsidian.Net.Packets.Play.Serverbound;

// Vanilla's BlockItem.useOn: what an item places, the last of an item's uses on a block.
public partial class UseItemOnPacket
{
    // The vanilla block classes that implement GameMasterBlock, whose items (GameMasterBlockItem) only creative
    // operators place.
    private static readonly HashSet<string> gameMasterBlocks =
        ["CommandBlock", "StructureBlock", "JigsawBlock", "TestBlock", "TestInstanceBlock"];

    /// <summary>
    /// Places the block the held item places. A refused placement asks for the
    /// player's hand to be sent again, since their client predicted it.
    /// </summary>
    private async ValueTask<UseResult> PlaceAsync(BlockUse use)
    {
        var player = use.Player;
        var placeLevel = use.Level;
        var currentItem = use.Item!;
        var clicked = use.Block;

        if (PlacedBlock(player, currentItem) is not IBlock block)
            return UseResult.Pass;

        // Like vanilla's BlockPlaceContext: a block goes into a clicked block it can replace (tall grass, say, but not
        // a block of its own kind), and otherwise next to it on the clicked face, where there must be room.
        var upperHit = this.Cursor.Y > 0.5;
        var replacesClicked = clicked.CanBeReplaced() && clicked.RegistryId != block.RegistryId
            || VanillaPlacement.SlabCanBeReplaced(clicked, block, true, this.Face, upperHit);
        var position = replacesClicked ? this.Position : this.Position + this.Face.ToVector();

        var target = await placeLevel.GetBlockAsync(position);
        var targetReplaceable = target?.CanBeReplaced() == true
            || target is not null
                && VanillaPlacement.SlabCanBeReplaced(target, block, replacesClicked, this.Face, upperHit);
        if (placeLevel.IsOutsideBuildHeight(position.Y) || !targetReplaceable)
            return Refuse(use);

        var context = new BlockPlaceContext
        {
            Level = placeLevel.BlockUpdates,
            Player = player,
            Hand = this.Hand,
            Item = currentItem,
            Block = block,
            Position = position,
            ClickedPosition = this.Position,
            ClickedFace = this.Face,
            ClickedAt = this.Cursor,
            ReplacesClicked = replacesClicked,
            HorizontalDirection = PlacementDirections.Horizontal(player.Yaw),
            NearestLookDirection = PlacementDirections.Nearest(player.Yaw, player.Pitch)
        };

        // Like the block's other hooks, while the level's block updates wait, so what it reads stays as it read.
        IBlock? state;
        lock (placeLevel.Fluids.TickLock)
            state = GetPlacementState(context, placeLevel);

        if (state is null)
            return Refuse(use);

        block = state;

        // Vanilla's BlockItem.canPlace also requires Level.isUnobstructed for the final state, so a slab that merges
        // into a double slab is checked as the double: the block's collision shape can't overlap a mob or player (say,
        // the placing player's own feet when they look down and place under themselves).
        if (!IsUnobstructed(placeLevel, position, block))
            return Refuse(use);

        var placing = new BlockPlaceEventArgs(use.Server, player, block, position, placeLevel, this.Hand)
        {
            Sequence = this.Sequence
        };

        await use.Server.EventDispatcher.ExecuteEventAsync(placing);
        if (placing.IsCancelled)
            return Refuse(use);

        // What the placed item holds goes to its block entity, like a shulker box's items.
        // A door's upper half or a bed's head goes in the same step as the block, so nothing fills its place between.
        var placed = block;
        var placement = new BlockPlacement(player, use.HandSlot, currentItem)
        {
            Placed = () => VanillaPlacement.OnPlacedByPlayer(placeLevel.BlockUpdates, position, placed)
        };

        // The event's handlers may have filled the target (or a second half's place) or changed the player's hand
        // while it ran: all are checked again in the same step as the block is placed, and the item is only used once
        // it is.
        // Vanilla BlockItem.placeBlock retains UPDATE_ALL_IMMEDIATE; plugin placement keeps its existing flags.
        var flags = currentItem.Holder.Block is BlockType ? BlockUpdateFlags.All : BlockUpdateFlags.AllImmediate;
        var stored = await placeLevel.TrySetBlockAsync(
            position,
            block,
            flags,
            current => use.CanCommit && (current.CanBeReplaced()
                    || VanillaPlacement.SlabCanBeReplaced(current, placed, replacesClicked, this.Face, upperHit))
                && VanillaPlacement.SecondHalfFits(placeLevel.BlockUpdates, position, placed)
                && ReferenceEquals(player.Inventory.GetItem(use.HandSlot), currentItem)
                && currentItem.Count > 0,
            placement);

        if (!stored)
            return Refuse(use);

        await player.FireProgressAsync("placed_block", item: use.UsedItem, block: block, position: position);

        if (currentItem.Holder.Id == ItemIds.PowderSnowBucket)
            BlockLighting.Play(placeLevel, position, "minecraft:item.bucket.empty_powder_snow", except: player.EntityId);
        else
            BlockSoundPlayback.Play(placeLevel, block, position, breaking: false, player.EntityId);

        Signs.OnPlacedByPlayer(placeLevel, position, block, player);
        CommandBlocks.OnPlacedByPlayer(placeLevel, position, block, use.UsedItem);

        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        placeLevel.EmitGameEvent(MobGameEvent.BlockPlace, center, player, affectedBlock: block);

        return UseResult.Success;
    }

    // The block a held item places, or null for an item that places nothing (or a plugin's player who can't use
    // items).
    private static IBlock? PlacedBlock(IPlayer player, ItemStack item)
    {
        if (item.Holder.Block is BlockType pluginBlock)
            return Plugins.ItemBehaviors.CanUseItems(player) ? pluginBlock.DefaultState : null;

        if (BucketItems.Handles(item))
            return null;

        if (VanillaRedstone.TryGetPlacedBlock(item.Holder, out var placed)
            || BlocksRegistry.TryGetBlock(item.Holder, out placed))
        {
            return placed;
        }

        return null;
    }

    /// <summary>
    /// Vanilla's <c>BlockPlaceContext.canPlace</c> for the held item, which some blocks' uses read to leave the click
    /// to placement: whether the item places a block, and the clicked block or the one beside it on the clicked face
    /// can be replaced by it.
    /// </summary>
    internal static bool CanPlace(BlockUse use)
    {
        if (!use.HasItem || PlacedBlock(use.Player, use.Item!) is not IBlock block)
            return false;

        var upperHit = use.Cursor.Y > 0.5;
        var clicked = use.Block;
        var replacesClicked = clicked.CanBeReplaced() && clicked.RegistryId != block.RegistryId
            || VanillaPlacement.SlabCanBeReplaced(clicked, block, true, use.Face, upperHit);
        if (replacesClicked)
            return true;

        var beside = use.Level.BlockUpdates.GetBlock(use.Position + use.Face.ToVector());
        return beside.CanBeReplaced()
            || VanillaPlacement.SlabCanBeReplaced(beside, block, false, use.Face, upperHit);
    }

    // A placement that didn't happen: the player's client, which predicted it, gets the item back.
    private static UseResult Refuse(BlockUse use)
    {
        use.ResyncHand = true;
        return UseResult.Fail;
    }

    /// <summary>
    /// Vanilla <c>BlockItem.getPlacementState</c> and <c>canPlace</c>: what the item places where the context says,
    /// or null where vanilla refuses it. A plugin's block decides for itself; a vanilla item's own rules come first
    /// (blocks only creative operators place, torches and signs that go on a wall or stand), then the block's class
    /// decides, and the state must survive there.
    /// </summary>
    internal static IBlock? GetPlacementState(BlockPlaceContext context, AbstractLevel level)
    {
        IBlock? GetState(BlockPlaceContext placing) =>
            Plugins.BlockBehaviors.GetStateForPlacement(placing, level.Logger);

        var item = context.Item;
        if (item.Holder.Block is BlockType)
            return GetState(context);

        // Vanilla GameMasterBlockItem: command, structure, jigsaw and test blocks, for creative operators only.
        var gameMaster = gameMasterBlocks.Contains(context.Block.BlockClass());
        if (gameMaster && !CommandBlockMinecart.CanEdit(context.Player))
            return null;

        if (StandingAndWallItems.TryGet(item.Holder, out var attachment))
            return StandingAndWallItems.Place(context, attachment, GetState);

        // Vanilla ScaffoldingBlockItem: scaffolding goes nowhere it would fall straight away, and needn't survive.
        var scaffolding = context.Block.Is(BlockIds.Scaffolding);
        if (scaffolding
            && !context.Level.GetBlock(context.Position).Is(BlockIds.Scaffolding)
            && WorldData.Placement.BlockPlacements.ScaffoldingDistance(context.Level, context.Position) == 7)
        {
            return null;
        }

        var state = GetState(context);
        if (state is null || scaffolding)
            return state;

        return PlacementSurvival.CanSurvive(context.Level, state, context.Position) ? state : null;
    }

    // Vanilla's Level.isUnobstructed: an entity that blocks building can't be inside the block's collision shape.
    private static bool IsUnobstructed(AbstractLevel level, Vector position, IBlock block)
    {
        var shapes = BlockCollisionShapes.Get(level, position, block);
        if (shapes.Count == 0)
            return true;

        var offset = (VectorD)position;
        foreach (var entity in level.GetEntitiesInRange(offset + new VectorD(0.5, 0.5, 0.5), 4))
        {
            if (!BlocksBuilding(entity))
                continue;

            var bounds = entity.Dimension.CreateBBFromPosition(entity.Position);
            foreach (var shape in shapes)
            {
                if (MobTerrain.Overlaps(shape.OffsetBy(offset), bounds))
                    return false;
            }
        }

        return true;
    }

    // Vanilla's Entity.blocksBuilding: living entities (players but spectators, mobs, armor stands), boats and
    // minecarts. Items, projectiles and the like don't keep a block from going where they are.
    private static bool BlocksBuilding(IEntity entity) => entity switch
    {
        IPlayer player => player.GameMode != GameMode.Spectator,
        Living or VehicleEntity => true,
        _ => false
    };
}
