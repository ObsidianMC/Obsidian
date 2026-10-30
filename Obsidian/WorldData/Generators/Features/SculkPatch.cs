using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature SculkPatch(JsonElement json)
    {
        var charges = json.GetProperty("charge_count").GetInt32();
        var amount = json.GetProperty("amount_per_charge").GetInt32();
        var attempts = json.GetProperty("spread_attempts").GetInt32();
        var growth = json.GetProperty("growth_rounds").GetInt32();
        var spread = json.GetProperty("spread_rounds").GetInt32();
        var replaceable = this.BlockTag("minecraft:sculk_replaceable_world_gen");
        var substrate = this.BlockTag("minecraft:sculk_replaceable");

        return (context, random, origin) =>
        {
            var level = context.Level;
            var state = level.GetBlock(origin);
            var canStart = SculkSpreader.IsSculkBehaviour(state);

            if (!canStart && (state.IsAir || state.Is(BlockIds.Water) && state.IsFluidSource()))
            {
                canStart = FeatureHelpers.Directions.Any(face =>
                    level.GetBlock(origin.Offset(face)).IsCollisionShapeFullBlock());
            }

            if (!canStart)
            {
                return false;
            }

            var cursors = new SculkSpreader(replaceable, substrate);

            for (var round = 0; round < spread + growth; round++)
            {
                for (var charge = 0; charge < charges; charge++)
                {
                    cursors.AddCursors(origin, amount);
                }

                for (var attempt = 0; attempt < attempts; attempt++)
                {
                    cursors.UpdateCursors(level, origin, random, round < spread);
                }

                cursors.Clear();
            }

            // 26.3 moved catalyst and rare growth placement into composed features. No extra draws belong here.
            return true;
        };
    }
}
