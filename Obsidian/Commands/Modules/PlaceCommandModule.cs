using Obsidian.Commands.Framework;
using Obsidian.API.Commands;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using System.Text.Json;

namespace Obsidian.Commands.Modules;

[RequirePermissionLevel(2)]
public sealed class PlaceCommandModule : CommandModuleBase
{
    [Command("place")]
    [CommandInfo("Places a feature", "/place feature <feature> [pos]")]
    public async Task<int> PlaceAsync([Remaining] string arguments)
    {
        try
        {
            var input = CommandInput.Arguments(this.CommandContext);
            input.Expect("feature");
            var definition = CommandInput.Data(input.Read());
            if (definition.ValueKind == JsonValueKind.String)
                definition = JsonSerializer.SerializeToElement(CommandInput.Qualify(definition.GetString()!));
            else
                definition = CommandInput.QualifyTypes(definition);

            var origin = CommandFeedback.OriginOf(this.CommandContext);
            var positioned = input.End
                || CommandCoordinates.TryParse(input.Position(), this.CommandContext, false, true, out origin);
            if (!positioned)
                throw new FormatException("Invalid feature position");

            input.ExpectEnd();
            if (CommandFeedback.LevelOf(this.CommandContext) is not AbstractLevel level)
                throw new FormatException("This level cannot place features");

            // Compile before taking the tick lock: malformed or unsupported definitions must not change the level.
            var registry = new FeatureRegistry(WorldgenData.Vanilla);
            var feature = registry.Feature(definition);
            var position = (Vector)origin.Floor();
            var minChunkX = (position.X - 16).ToChunkCoord();
            var maxChunkX = (position.X + 16).ToChunkCoord();
            var minChunkZ = (position.Z - 16).ToChunkCoord();
            var maxChunkZ = (position.Z + 16).ToChunkCoord();
            for (var x = minChunkX; x <= maxChunkX; x++)
            {
                for (var z = minChunkZ; z <= maxChunkZ; z++)
                {
                    if (level.GetLoadedChunk(x, z) is not { IsGenerated: true })
                        throw new FormatException("The feature area is not loaded");
                }
            }

            lock (level.Fluids.TickLock)
            {
                var view = new LiveFeatureLevel(level);
                var context = new DecorationContext(registry, view, at => view.GetBiome(at).Name, level.MinY,
                    level.Height);
                try
                {
                    if (!feature(context, view.Random, position) || view.ReachedUnloaded)
                        throw new FormatException("The feature could not be placed in the loaded area");

                    view.Commit();
                }
                catch
                {
                    view.Rollback();
                    throw;
                }
            }

            await this.CommandContext.SuccessAsync($"Placed feature at {CommandFeedback.Format(position)}");
            return 1;
        }
        catch (Exception exception) when (ComputeCommandModule.IsInputError(exception)
            || exception is NotSupportedException)
        {
            await this.CommandContext.FailAsync(exception.Message);
            return 0;
        }
    }
}
