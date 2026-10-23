using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using System.Globalization;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's <c>/execute</c>: runs a command as other entities, from other places, or only when conditions hold.
/// </summary>
/// <remarks>
/// Supports <c>as</c>, <c>at</c>, <c>positioned</c> (and <c>positioned as</c>), <c>rotated</c> (and
/// <c>rotated as</c>), <c>in</c>, <c>align</c>, <c>anchored</c> (which doesn't move the position), <c>if</c> and
/// <c>unless</c> with <c>entity</c>, <c>block</c> and <c>score … matches</c>, and <c>run</c>. Commands run with the
/// permissions of whoever ran <c>/execute</c>.
/// </remarks>
[RequirePermissionLevel(2)]
public sealed class ExecuteCommandModule : CommandModuleBase
{
    private static readonly string[] subcommands = ["align", "anchored", "as", "at", "if", "in", "positioned", "rotated", "run", "unless"];

    [Command("execute")]
    [CommandInfo("Runs a command as other entities, elsewhere, or under conditions.", "/execute <subcommands…> [run <command>]")]
    public async Task ExecuteAsync([Remaining, ExecuteSuggestions] string subcommands)
    {
        // The line as typed, so that run's command keeps its quotes and spacing.
        var message = this.CommandContext.Message;
        var line = message.IndexOf(' ') is var space and >= 0 ? message[(space + 1)..] : subcommands;

        var words = CommandParser.SplitWords(line);
        if (words[^1].Value.Length == 0)
            words.RemoveAt(words.Count - 1);

        List<CommandContext> contexts = [this.CommandContext];
        var index = 0;

        while (index < words.Count)
        {
            var keyword = words[index].Value;
            index++;

            if (keyword == "run")
            {
                if (index >= words.Count)
                {
                    await this.CommandContext.FailAsync("Expected a command after run");
                    return;
                }

                var command = line[words[index].Start..];
                var handler = (CommandHandler)this.Server.CommandHandler;

                foreach (var context in contexts)
                    await handler.ProcessCommand(context.Derive($"{CommandHelpers.DefaultPrefix}{command}", context.Executor,
                        context.Origin, context.Level, context.Rotation));

                return;
            }

            var next = keyword switch
            {
                "as" => Fork(contexts, words, ref index, (context, target) =>
                    context.Derive(context.Message, target, context.Origin, context.Level, context.Rotation)),
                "at" => Fork(contexts, words, ref index, (context, target) =>
                    context.Derive(context.Message, context.Executor, target.Position, target.Level, (target.Yaw, target.Pitch))),
                "positioned" => Positioned(contexts, words, ref index),
                "rotated" => Rotated(contexts, words, ref index),
                "in" => In(contexts, words, ref index),
                "align" => Align(contexts, words, ref index),
                "anchored" => Take(words, ref index) is "eyes" or "feet" ? contexts : null,
                "if" => await this.ConditionAsync(contexts, words, () => index, value => index = value, expected: true),
                "unless" => await this.ConditionAsync(contexts, words, () => index, value => index = value, expected: false),
                _ => null
            };

            if (next is null)
            {
                await this.CommandContext.FailAsync($"Incorrect argument for command at '{keyword}'");
                return;
            }

            contexts = next;
        }

        // Without run, /execute reports whether its conditions held.
        if (contexts.Count == 0)
            await this.CommandContext.FailAsync("Test failed");
        else
            await this.CommandContext.SuccessAsync(contexts.Count == 1 ? "Test passed" : $"Test passed, count: {contexts.Count}");
    }

    // One context for each entity a selector selects in each context.
    private static List<CommandContext>? Fork(List<CommandContext> contexts, List<(int Start, string Value)> words, ref int index,
        Func<CommandContext, IEntity, CommandContext> derive)
    {
        if (!EntitySelector.TryParse(Take(words, ref index) ?? string.Empty, out var selector))
            return null;

        return [.. contexts.SelectMany(context => selector.Select(context).Select(target => derive(context, target)))];
    }

    private static List<CommandContext>? Positioned(List<CommandContext> contexts, List<(int Start, string Value)> words, ref int index)
    {
        if (index < words.Count && words[index].Value == "as")
        {
            index++;
            return Fork(contexts, words, ref index, (context, target) =>
                context.Derive(context.Message, context.Executor, target.Position, context.Level, context.Rotation));
        }

        var text = TakeWords(words, ref index, 3);
        if (text is null)
            return null;

        var moved = new List<CommandContext>(contexts.Count);
        foreach (var context in contexts)
        {
            if (!CommandCoordinates.TryParse(text, context, centerBlocks: true, wholeNumbers: false, out var position))
                return null;

            moved.Add(context.Derive(context.Message, context.Executor, position, context.Level, context.Rotation));
        }

        return moved;
    }

    private static List<CommandContext>? Rotated(List<CommandContext> contexts, List<(int Start, string Value)> words, ref int index)
    {
        if (index < words.Count && words[index].Value == "as")
        {
            index++;
            return Fork(contexts, words, ref index, (context, target) =>
                context.Derive(context.Message, context.Executor, context.Origin, context.Level, (target.Yaw, target.Pitch)));
        }

        var yawText = Take(words, ref index);
        var pitchText = Take(words, ref index);
        if (yawText is null || pitchText is null)
            return null;

        var rotated = new List<CommandContext>(contexts.Count);
        foreach (var context in contexts)
        {
            var (currentYaw, currentPitch) = context.Rotation ?? (default, default);
            if (!TryParseAngle(yawText, currentYaw.Degrees, out var yaw) || !TryParseAngle(pitchText, currentPitch.Degrees, out var pitch))
                return null;

            rotated.Add(context.Derive(context.Message, context.Executor, context.Origin, context.Level, (yaw, pitch)));
        }

        return rotated;
    }

    private List<CommandContext>? In(List<CommandContext> contexts, List<(int Start, string Value)> words, ref int index)
    {
        if (!ResourceLocation.TryParse(Take(words, ref index), out var dimension))
            return null;

        var name = dimension.Value.ToString();
        if (this.Server.DefaultWorld.GetLevel(name) is not ILevel level)
            return null;

        return [.. contexts.Select(context => context.Derive(context.Message, context.Executor, context.Origin, level, context.Rotation))];
    }

    private static List<CommandContext>? Align(List<CommandContext> contexts, List<(int Start, string Value)> words, ref int index)
    {
        var axes = Take(words, ref index);
        if (axes is null || axes.Length == 0 || axes.Any(axis => axis is not ('x' or 'y' or 'z')) || axes.Distinct().Count() != axes.Length)
            return null;

        return [.. contexts.Select(context =>
        {
            var origin = OriginOf(context);
            var aligned = new VectorD(
                axes.Contains('x') ? Math.Floor(origin.X) : origin.X,
                axes.Contains('y') ? Math.Floor(origin.Y) : origin.Y,
                axes.Contains('z') ? Math.Floor(origin.Z) : origin.Z);

            return context.Derive(context.Message, context.Executor, aligned, context.Level, context.Rotation);
        })];
    }

    // Keeps the contexts in which a condition holds, or doesn't with unless.
    private async Task<List<CommandContext>?> ConditionAsync(List<CommandContext> contexts, List<(int Start, string Value)> words,
        Func<int> getIndex, Action<int> setIndex, bool expected)
    {
        var index = getIndex();
        var kind = Take(words, ref index);
        var kept = new List<CommandContext>();

        switch (kind)
        {
            case "entity":
            {
                if (!EntitySelector.TryParse(Take(words, ref index) ?? string.Empty, out var selector))
                    return null;

                kept.AddRange(contexts.Where(context => selector.Select(context).Count > 0 == expected));
                break;
            }
            case "block":
            {
                var position = TakeWords(words, ref index, 3);
                var blockText = Take(words, ref index);
                if (position is null || blockText is null)
                    return null;

                var handler = this.Server.CommandHandler;
                if (!handler.GetArgumentParser(typeof(BlockStateArgument)).TryParseArgument(blockText, this.CommandContext, out var parsed)
                    || parsed is not BlockStateArgument block)
                    return null;

                foreach (var context in contexts)
                {
                    if (!CommandCoordinates.TryParse(position, context, centerBlocks: false, wholeNumbers: true, out var at))
                        return null;

                    var found = await LevelOf(context).GetBlockAsync((Vector)at.Floor());
                    var matches = found is not null && found.UnlocalizedName == block.Name
                        && block.Properties.All(property => found.GetProperty(property.Key) == property.Value);

                    if (matches == expected)
                        kept.Add(context);
                }
                break;
            }
            case "score":
            {
                var holderText = Take(words, ref index);
                var objectiveName = Take(words, ref index);
                if (holderText is null || objectiveName is null || Take(words, ref index) != "matches"
                    || !EntitySelector.TryParseRange(Take(words, ref index) ?? string.Empty, out var range))
                    return null;

                var scoreboard = ((Server)this.Server).CommandScoreboard;
                if (scoreboard.Find(objectiveName) is not { } objective)
                    return null;

                foreach (var context in contexts)
                {
                    var holder = holderText.StartsWith('@')
                        ? EntitySelector.TryParse(holderText, out var selector) ? selector.Select(context).Select(ScoreHolderArgument.NameOf).FirstOrDefault() : null
                        : holderText;

                    var score = holder is null ? null : scoreboard.GetScore(holder, objective);
                    var matches = score is int value && (range.Min is not double min || value >= min) && (range.Max is not double max || value <= max);

                    if (matches == expected)
                        kept.Add(context);
                }
                break;
            }
            default:
                return null;
        }

        setIndex(index);
        return kept;
    }

    private static string? Take(List<(int Start, string Value)> words, ref int index) =>
        index < words.Count ? words[index++].Value : null;

    private static string? TakeWords(List<(int Start, string Value)> words, ref int index, int count)
    {
        if (index + count > words.Count)
            return null;

        var text = string.Join(' ', words.Skip(index).Take(count).Select(word => word.Value));
        index += count;
        return text;
    }

    private static bool TryParseAngle(string text, float current, out float angle)
    {
        var relative = text.StartsWith('~');
        var number = relative ? text[1..] : text;

        if (number.Length == 0 && relative)
        {
            angle = current;
            return true;
        }

        if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out angle) || !float.IsFinite(angle))
            return false;

        if (relative)
            angle += current;

        return true;
    }

    /// <summary>
    /// Suggests the subcommand keywords after the words typed so far, and command names after <c>run</c>.
    /// </summary>
    private sealed class ExecuteSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context)
        {
            var message = context.Message;
            var start = message.IndexOf(' ');
            if (start < 0)
                return ValueTask.FromResult<IEnumerable<CommandSuggestion>>([]);

            var typed = message[(start + 1)..];
            var completed = typed[..(typed.LastIndexOf(' ') + 1)];
            var words = completed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            IEnumerable<string> next = words.Length > 0 && words[^1] == "run"
                ? context.Server.CommandHandler.GetAllCommands().Where(x => x.Parent is null).Select(x => x.Name)
                : words.Length > 0 && words[^1] is "if" or "unless"
                    ? ["block", "entity", "score"]
                    : subcommands;

            // Keywords are offered after every word, since telling arguments from subcommands would mean parsing them.
            return ValueTask.FromResult(next.Select(word => new CommandSuggestion(completed + word)));
        }
    }
}
