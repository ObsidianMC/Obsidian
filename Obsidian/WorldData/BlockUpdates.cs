using Obsidian.API;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    internal static class BlockUpdates
    {
        internal static async Task<bool> HandleFallingBlock(BlockUpdate bu)
        {
            if (bu.block is null) { return false; }
            var w = bu.world;
            var worldLoc = bu.position;
            var mat = bu.block.Value.Material;
            if (w.GetBlock(worldLoc + Vector.Down) is Block below && (below.IsAir || below.IsFluid))
            {
                w.SetBlock(worldLoc, new Block(Material.Air));
                foreach (var p in w.Server.PlayersInRange(worldLoc))
                {
                    await w.Server.BroadcastBlockPlacementToPlayerAsync(p, new Block(Material.Air), worldLoc);
                }
                w.SpawnFallingBlock(worldLoc, mat);
                return true;
            }
            return false;
        }

        internal static async Task<bool> HandleLiquidPhysics(BlockUpdate bu)
        {
            if (bu.block is null) { return false; }
            var b = bu.block.Value;
            var w = bu.world;
            var worldLoc = bu.position;
            bool returnval = false;
            int state = b.State;
            Vector bl = worldLoc + Vector.Down;
            if (state >= 8) // Falling water
            {
                if (w.GetBlock(bl) is Block below && below.IsAir)
                {
                    var nb = new Block(b.BaseId, state);
                    w.SetBlock(bl, nb);
                    foreach (var p in w.Server.PlayersInRange(bl))
                    {
                        await w.Server.BroadcastBlockPlacementToPlayerAsync(p, nb, bl);
                    }
                    returnval = true;
                }
                else
                {
                    state -= 8;
                }
            }

            if (state < 7)
            {
                if (w.GetBlock(bl) is Block below)
                {
                    if (below.IsAir)
                    {
                        var nb = new Block(b.BaseId, state + 8);
                        w.SetBlock(bl, nb);
                        foreach (var p in w.Server.PlayersInRange(bl))
                        {
                            await w.Server.BroadcastBlockPlacementToPlayerAsync(p, nb, bl);
                        }
                        return true;
                    }
                    else if (below.IsFluid && below.State > 7)
                    {
                        return false;
                    }
                    
                }
                
                var fw = worldLoc + Vector.Forwards;
                if (w.GetBlock(fw) is Block front && front.IsAir)
                {
                    var nb = new Block(b.BaseId, state + 1);                    
                    w.SetBlock(fw, nb);
                    foreach (var p in w.Server.PlayersInRange(fw))
                    {
                        await w.Server.BroadcastBlockPlacementToPlayerAsync(p, nb, fw);
                    }
                    returnval = true;
                }


                var bw = worldLoc + Vector.Backwards;
                if (w.GetBlock(bw) is Block back && back.IsAir)
                {
                    var nb = new Block(b.BaseId, state + 1);
                    w.SetBlock(bw, nb);
                    foreach (var p in w.Server.PlayersInRange(bw))
                    {
                        await w.Server.BroadcastBlockPlacementToPlayerAsync(p, nb, bw);
                    }
                    returnval = true;
                }


                var lt = worldLoc + Vector.Left;
                if (w.GetBlock(lt) is Block left && left.IsAir)
                {
                    var nb = new Block(b.BaseId, state + 1);
                    w.SetBlock(lt, nb);
                    foreach (var p in w.Server.PlayersInRange(lt))
                    {
                        await w.Server.BroadcastBlockPlacementToPlayerAsync(p, nb, lt);
                    }
                    returnval = true;
                }


                var rt = worldLoc + Vector.Right;
                if (w.GetBlock(rt) is Block right && right.IsAir)
                {
                    var nb = new Block(b.BaseId, state + 1);
                    w.SetBlock(rt, nb);
                    foreach (var p in w.Server.PlayersInRange(rt))
                    {
                        await w.Server.BroadcastBlockPlacementToPlayerAsync(p, nb, rt);
                    }
                    returnval = true;
                }
            }

            return returnval;

        }
    }
}
