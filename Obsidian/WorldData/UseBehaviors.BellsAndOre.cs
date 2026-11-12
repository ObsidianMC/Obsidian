using Obsidian.API.Blocks;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior BellBlock { get; } = new BellUse();

    internal static IBlockUseBehavior RedStoneOreBlock { get; } = new RedstoneOreUse();

    /// <summary>
    /// Vanilla's <c>BellBlock.useWithoutItem</c>: a click on a side the bell swings from rings it, whatever the hand
    /// holds; a click elsewhere passes to the item.
    /// </summary>
    private sealed class BellUse : IBlockUseBehavior
    {
        // Vanilla's BellBlock.isProperHit: clicks above this height hit the bell's frame, not the bell.
        private const float RimHeight = 0.8124f;

        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (!IsProperHit(use.Block, use.Face, use.Cursor.Y))
                return ValueTask.FromResult(UseResult.Pass);

            // Without its block entity the bell doesn't ring, but the use still succeeds, as in vanilla.
            if (use.BlockEntity is null)
                return ValueTask.FromResult(UseResult.Success);

            // The block event carries the clicked face as vanilla's 3D data value, which BlockFace's order matches.
            var level = use.Level;
            if (!use.TryCommit(() =>
            {
                level.QueueBlockEvent(use.Position, use.Block.RegistryId, RedstoneBell.RingEvent, (int)use.Face);
                return true;
            }))
            {
                return ValueTask.FromResult(UseResult.Fail);
            }

            BlockLighting.Play(level, use.Position, RedstoneBell.RingSound, RedstoneBell.RingVolume);
            level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);
            use.Award("minecraft:bell_ring");

            return ValueTask.FromResult(UseResult.Success);
        }

        // A floor bell swings along its facing, a wall bell across it and a ceiling bell either way; never from above
        // or below.
        private static bool IsProperHit(IBlock bell, BlockFace face, double clickY)
        {
            if (face is BlockFace.Up or BlockFace.Down || clickY > RimHeight)
                return false;

            var facing = FeatureHelpers.ParseFace(bell.GetProperty("facing"));
            var sameAxis = IsAlongX(facing) == IsAlongX(face);
            return bell.GetProperty("attachment") switch
            {
                "floor" => sameAxis,
                "single_wall" or "double_wall" => !sameAxis,
                _ => true
            };
        }

        private static bool IsAlongX(BlockFace face) => face is BlockFace.East or BlockFace.West;
    }

    /// <summary>
    /// Vanilla's <c>RedStoneOreBlock.useItemOn</c>: any use lights the ore. A held block item that can be placed there
    /// is placed next; anything else ends the click. Its dust particles are drawn by clients alone.
    /// </summary>
    private sealed class RedstoneOreUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (use.Block.GetProperty("lit") == "false")
            {
                var lit = use.Block.WithProperty("lit", true);
                if (!use.TryCommit(() => use.Level.BlockUpdates.SetBlock(use.Position, lit, BlockUpdateFlags.All)))
                    return ValueTask.FromResult(UseResult.Fail);
            }

            var result = UseItemOnPacket.CanPlace(use) ? UseResult.Pass : UseResult.Success;
            return ValueTask.FromResult(result);
        }
    }
}
