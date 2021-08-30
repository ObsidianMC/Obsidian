using Obsidian.API;
using System;
using System.Collections.Generic;
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
            if (w.GetBlock(worldLoc + Vector.Down) is Block below && (Block.Replaceable.Contains(below.Material) || below.IsFluid))
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
            int state = b.State;
            Vector belowPos = worldLoc + Vector.Down;
            
            // Handle the initial search for closet path downwards.
            // Just going to do a crappy pathfind for now. We can do
            // proper pathfinding some other time.
            if (state == 0)
            {
                var validPaths = new List<Vector>();
                var paths = new List<Vector>()
                {
                    {worldLoc + Vector.Forwards},
                    {worldLoc + Vector.Backwards},
                    {worldLoc + Vector.Left},
                    {worldLoc + Vector.Right}
                };

                foreach (var pathLoc in paths)
                {
                    if (w.GetBlock(pathLoc) is Block pathSide && (Block.Replaceable.Contains(pathSide.Material) || pathSide.IsFluid))
                    {
                        var pathBelow = w.GetBlock(pathLoc + Vector.Down);
                        if (pathBelow is Block pBelow && (Block.Replaceable.Contains(pBelow.Material) || pBelow.IsFluid))
                        {
                            validPaths.Add(pathLoc);
                        }
                    }
                }

                // if all directions are valid, or none are, use normal liquid spread physics instead
                if (validPaths.Count != 4 && validPaths.Count != 0)
                {
                    var path = validPaths[0];
                    var newBlock = new Block(b.BaseId, state + 1);
                    w.SetBlock(path, newBlock);
                    w.Server.PlayersInRange(path).ForEach(async p => await w.Server.BroadcastBlockPlacementToPlayerAsync(p, newBlock, path));
                    var neighborUpdate = new BlockUpdate(w, path, newBlock);
                    w.ScheduleBlockUpdate(neighborUpdate);
                    return false;
                }
            }

            if (state >= 8) // Falling water
            {
                // If above me is no longer water, than I should disappear too
                if (w.GetBlock(worldLoc + Vector.Up) is Block up && !up.IsFluid)
                {
                    var newBlock = new Block(Material.Air);
                    w.SetBlock(worldLoc, newBlock);
                    w.Server.PlayersInRange(worldLoc).ForEach(async p => await w.Server.BroadcastBlockPlacementToPlayerAsync(p, newBlock, worldLoc));
                    w.ScheduleBlockUpdate(new BlockUpdate(w, belowPos));
                    return false;
                }

                // Keep falling
                if (w.GetBlock(belowPos) is Block below && Block.Replaceable.Contains(below.Material))
                {
                    var newBlock = new Block(b.BaseId, state);
                    w.SetBlock(belowPos, newBlock);
                    w.Server.PlayersInRange(belowPos).ForEach(async p => await w.Server.BroadcastBlockPlacementToPlayerAsync(p, newBlock, belowPos));
                    w.ScheduleBlockUpdate(new BlockUpdate(w, belowPos, newBlock));
                    return false;
                }
                else
                {
                    // Falling water has hit something solid. Change state to spread.
                    state = 1;
                    w.SetBlock(worldLoc, new Block(b.BaseId, state));
                }
            }

            if (state < 8)
            {
                var horizontalNeighbors = new Dictionary<Vector, Block?>()
                {
                    {worldLoc + Vector.Forwards, w.GetBlock(worldLoc + Vector.Forwards) },
                    {worldLoc + Vector.Backwards, w.GetBlock(worldLoc + Vector.Backwards) },
                    {worldLoc + Vector.Left, w.GetBlock(worldLoc + Vector.Left) },
                    {worldLoc + Vector.Right, w.GetBlock(worldLoc + Vector.Right) }
                };

                // Check infinite source blocks
                if (state == 1 && b.Material == Material.Water)
                {
                    // if 2 neighbors are source blocks (state = 0), then become source block
                    int sourceNeighborCount = 0;
                    foreach (var (loc, neighborBlock) in horizontalNeighbors)
                    {
                        if (neighborBlock is Block neighbor && neighbor.Material == b.Material && neighbor.State == 0)
                        {
                            sourceNeighborCount++;
                        }
                    }
                    if (sourceNeighborCount > 1)
                    {
                        var newBlock = new Block(Material.Water);
                        w.SetBlock(worldLoc, newBlock);
                        w.Server.PlayersInRange(worldLoc).ForEach(async p => await w.Server.BroadcastBlockPlacementToPlayerAsync(p, newBlock, worldLoc));
                        return true;
                    }
                }

                if (state > 0)
                {
                    // On some side of the block, there should be another water block with a lower state.
                    int lowestState = state;
                    foreach (var (loc, neighborBlock) in horizontalNeighbors)
                    {
                        if (neighborBlock.Value.Material == b.Material)
                            lowestState = Math.Min(lowestState, neighborBlock.Value.State);
                    }

                    // If not, turn to air and update neighbors.
                    if (lowestState >= state && w.GetBlock(worldLoc + Vector.Up).Value.Material != b.Material)
                    {
                        var newBlock = new Block(Material.Air);
                        w.SetBlock(worldLoc, newBlock);
                        w.Server.PlayersInRange(worldLoc).ForEach(async p => await w.Server.BroadcastBlockPlacementToPlayerAsync(p, newBlock, worldLoc));
                        return true;
                    }
                }

                if (w.GetBlock(belowPos) is Block below)
                {
                    if (below.Material == b.Material) { return false; }
                    if (Block.Replaceable.Contains(below.Material))
                    {
                        var newBlock = new Block(b.BaseId, state + 8);
                        w.SetBlock(belowPos, newBlock);
                        w.Server.PlayersInRange(belowPos).ForEach(async p => await w.Server.BroadcastBlockPlacementToPlayerAsync(p, newBlock, belowPos));
                        var neighborUpdate = new BlockUpdate(w, belowPos, newBlock);
                        w.ScheduleBlockUpdate(neighborUpdate);
                        return false;
                    }
                }

                // the lowest level of water can only go down, so bail now.
                if (state == 7) { return false; }


                foreach (var (loc, neighborBlock) in horizontalNeighbors)
                {
                    if (neighborBlock is null) { continue; }
                    var neighbor = neighborBlock.Value;
                    if (Block.Replaceable.Contains(neighbor.Material) || (neighbor.Material == b.Material && neighbor.State > state + 1))
                    {
                        var newBlock = new Block(b.BaseId, state + 1);
                        w.SetBlock(loc, newBlock);
                        w.Server.PlayersInRange(loc).ForEach(async p => await w.Server.BroadcastBlockPlacementToPlayerAsync(p, newBlock, loc));
                        var neighborUpdate = new BlockUpdate(w, loc, newBlock);
                        w.ScheduleBlockUpdate(neighborUpdate);
                    }
                }
            }

            return false;
        }
    }
}
