using Obsidian.API;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    internal static class BlockUpdates
    {
        internal static async Task HandleFallingBlock(World w, Vector worldLoc, Material mat)
        {
            if (w.GetBlock(worldLoc + Vector.Down) is Block below && (below.IsAir || below.IsFluid))
            {
                await Task.Delay(40);
                w.SetBlock(worldLoc, new Block(Material.Air));
                foreach (var p in w.Server.PlayersInRange(worldLoc))
                {
                    await w.Server.BroadcastBlockPlacementToPlayerAsync(p, new Block(Material.Air), worldLoc);
                }
                w.SpawnFallingBlock(worldLoc, mat);
            }
        }

        internal static async Task HandleLiquidPhysics(World w, Vector worldLoc, Block b)
        {
            int state = b.State;
            if (state < 7)
            {
                var fw = worldLoc + Vector.Forwards;
                if (w.GetBlock(fw) is Block front && front.IsAir)
                {
                    var nb = new Block((int)b.BaseId, state + 1);                    
                    w.SetBlock(fw, nb);
                    foreach (var p in w.Server.PlayersInRange(fw))
                    {
                        await w.Server.BroadcastBlockPlacementToPlayerAsync(p, nb, fw);
                    }
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
                }


            }

            if (w.GetBlock(worldLoc + Vector.Down) is Block below && below.IsAir)
            {

            }

        }
    }
}
