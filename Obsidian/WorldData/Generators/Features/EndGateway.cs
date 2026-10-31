using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature EndGateway(JsonElement json)
    {
        Vector? exit = json.TryGetProperty("exit", out var coordinates)
            ? new Vector(coordinates[0].GetInt32(), coordinates[1].GetInt32(), coordinates[2].GetInt32())
            : null;
        var exact = json.GetProperty("exact").GetBoolean();

        return (context, _, origin) =>
        {
            var level = context.Level;
            var extent = new Vector(1, 2, 1);

            foreach (var position in FeatureHelpers.BetweenClosed(origin - extent, origin + extent))
            {
                var centerX = position.X == origin.X;
                var centerZ = position.Z == origin.Z;
                var middle = position.Y == origin.Y;
                var cap = Math.Abs(position.Y - origin.Y) == 2;
                var state = BlocksRegistry.Air;

                if (position == origin)
                {
                    state = BlocksRegistry.EndGateway;
                }
                else if (!middle && (cap && centerX && centerZ || !cap && (centerX || centerZ)))
                {
                    state = BlocksRegistry.Bedrock;
                }

                level.SetBlock(position, state);
            }

            if (level.GetBlockEntity(origin) is not DataBlockEntity gateway)
                return true;

            // Vanilla's gateway saves its age (0 until it ticks) whether or not it has an exit.
            if (!gateway.Data.HasTag("Age"))
            {
                gateway.Set("Age", 0L);
            }

            if (exit is Vector target)
            {
                gateway.Set(new NbtArray<int>("exit_portal", [target.X, target.Y, target.Z]));

                if (exact)
                {
                    gateway.Set("ExactTeleport", true);
                }
            }

            return true;
        };
    }
}
