using Obsidian.Commands.Framework;
using Obsidian.API.Commands;
using Obsidian.API.Data;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using System.IO;
using System.Text.Json;

namespace Obsidian.Commands.Modules;

[RequirePermissionLevel(2)]
public sealed class ComputeCommandModule : CommandModuleBase
{
    [Command("compute")]
    [CommandInfo("Evaluates a context number provider",
        "/compute <default|block <pos>|entity <target>> <integer|float> <provider> [scale]")]
    public async Task<int> ComputeAsync([Remaining] string arguments)
    {
        try
        {
            var input = CommandInput.Arguments(this.CommandContext);
            var numbers = await CreateContextAsync(this.CommandContext, input);
            var kind = input.Read();
            var provider = Provider(input.Read());
            var hasScale = !input.End;
            var scale = input.End ? 1 : CommandInput.Float(input.Read());
            input.ExpectEnd();
            if (!float.IsFinite(scale))
                throw new FormatException("Scale must be finite");

            int result;
            if (kind == "integer")
            {
                if (hasScale)
                    throw new FormatException("Integer providers do not take a scale");

                result = ContextNumberProviders.ReadInt(provider).GetIntOrThrow(numbers);
                await this.CommandContext.SuccessAsync($"The result is {result}");
            }
            else if (kind == "float")
            {
                var value = ContextNumberProviders.ReadFloat(provider).GetFloatOrThrow(numbers);
                result = Floor(value * scale);
                await this.CommandContext.SuccessAsync(
                    FormattableString.Invariant($"The result is {value} (command result {result})"));
            }
            else
            {
                throw new FormatException("Expected integer or float");
            }

            return result;
        }
        catch (Exception exception) when (IsInputError(exception))
        {
            await this.CommandContext.FailAsync(exception.Message);
            return 0;
        }
    }

    internal static bool IsInputError(Exception exception) => exception is
        FormatException or JsonException or InvalidDataException or ArithmeticException
        or KeyNotFoundException or ArgumentException or InvalidOperationException;

    internal static JsonElement Provider(string text) => text[0] is '{' or '[' or '\'' or '"'
        ? CommandInput.Data(text)
        : JsonSerializer.SerializeToElement(CommandInput.Qualify(text));

    internal static int Floor(float value)
    {
        // Java's narrowing conversion saturates, then Mth.floor corrects a negative fractional result.
        var truncated = float.IsNaN(value) ? 0 : (int)Math.Clamp((double)value, int.MinValue, int.MaxValue);
        return value < truncated ? unchecked(truncated - 1) : truncated;
    }

    internal static async Task<NumberContext> CreateContextAsync(CommandContext context, CommandInput input)
    {
        var source = input.Read();
        var level = CommandFeedback.LevelOf(context);
        IBlock? block = null;

        // Vanilla keeps the executor as this_entity and passes the selected entity separately as target_entity.
        IEntity? target = null;
        var origin = CommandFeedback.OriginOf(context);
        switch (source)
        {
            case "block":
                if (!CommandCoordinates.TryParse(input.Position(), context, false, true, out var position))
                    throw new FormatException("Invalid block position");

                block = await level.GetBlockAsync((Vector)position);
                if (block is null)
                    throw new FormatException("That position is not loaded");

                break;

            case "entity":
                if (!EntitySelector.TryParse(input.Read(), out var selector) || !selector.Single)
                    throw new FormatException("Expected a single entity");

                target = selector.Select(context).SingleOrDefault()
                    ?? throw new FormatException("No entity was found");
                break;

            case "default":
                break;

            default:
                throw new FormatException("Expected default, block or entity context");
        }

        var loot = new LootContext
        {
            Random = new SystemRandomSource(Globals.Random),
            Origin = origin,
            ThisEntity = context.Executor,
            TargetEntity = target,
            BlockState = block,
            BlockProperties = block?.GetProperties() ?? ImmutableDictionary<string, string>.Empty
        };

        return new NumberContext
        {
            Random = loot.Random,
            TestCondition = condition => LootTableJson.ReadCondition(condition).Test(loot),
            IntEnvironmentAttribute = _ => throw new FormatException("Numeric environment sampling is not implemented"),
            FloatEnvironmentAttribute = _ => throw new FormatException("Numeric environment sampling is not implemented"),
            IntStorage = (id, path) => StorageInt(context.Server, id, path),
            FloatStorage = (id, path) => StorageFloat(context.Server, id, path),
            Score = (holder, objective) => Score(context, loot, holder, objective)
        };
    }

    private static int? StorageInt(IServer server, string id, string path)
    {
        var tag = DataCommandModule.Find(CommandStorage.Read(server, id), path);
        return tag switch
        {
            NbtTag<int> value => value.Value,
            NbtTag<long> value => unchecked((int)value.Value),
            NbtTag<float> value => Floor(value.Value),
            NbtTag<double> value => FloorDouble(value.Value),
            NbtTag<byte> value => unchecked((sbyte)value.Value),
            NbtTag<short> value => value.Value,
            _ => null
        };
    }

    private static float? StorageFloat(IServer server, string id, string path)
    {
        var tag = DataCommandModule.Find(CommandStorage.Read(server, id), path);
        return tag switch
        {
            NbtTag<int> value => value.Value,
            NbtTag<long> value => value.Value,
            NbtTag<float> value => value.Value,
            NbtTag<double> value => (float)value.Value,
            NbtTag<byte> value => unchecked((sbyte)value.Value),
            NbtTag<short> value => value.Value,
            _ => null
        };
    }

    internal static int FloorDouble(double value)
    {
        var truncated = double.IsNaN(value) ? 0 : (int)Math.Clamp(value, int.MinValue, int.MaxValue);
        return value < truncated ? unchecked(truncated - 1) : truncated;
    }

    private static int? Score(CommandContext context, LootContext loot, JsonElement target, string objectiveName)
    {
        if (context.Server is not Server server)
            return null;

        var holder = ScoreHolder(loot, target);
        var objective = server.CommandScoreboard.Find(objectiveName);
        if (holder is null || objective is null)
            return null;

        return server.CommandScoreboard.GetScore(holder, objective);
    }

    // Vanilla's score name providers: a context entity target, inline or as {type:context}, or a fixed name.
    private static string? ScoreHolder(LootContext loot, JsonElement target)
    {
        string? entityTarget = null;
        if (target.ValueKind == JsonValueKind.String)
        {
            entityTarget = target.GetString();
        }
        else if (target.ValueKind == JsonValueKind.Object)
        {
            if (target.TryGetProperty("name", out var name))
                return name.GetString();

            if (target.TryGetProperty("target", out var contextTarget))
                entityTarget = contextTarget.GetString();
        }

        var entity = entityTarget switch
        {
            "this" => loot.ThisEntity,
            "target_entity" => loot.TargetEntity,
            _ => null
        };
        return entity is null ? null : ScoreHolderArgument.NameOf(entity);
    }
}
