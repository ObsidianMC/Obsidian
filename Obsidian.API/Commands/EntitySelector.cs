using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Obsidian.API.Commands;

/// <summary>
/// A target selector, like <c>@e[type=zombie,distance=..10,limit=3]</c>, or a player's name or UUID, as vanilla's
/// <c>entity</c> arguments read them.
/// </summary>
/// <remarks>
/// <para>
/// Supports the selectors <c>@p</c>, <c>@a</c>, <c>@r</c>, <c>@s</c>, <c>@e</c> and <c>@n</c>, and the arguments
/// <c>x</c>, <c>y</c>, <c>z</c>, <c>distance</c>, <c>type</c>, <c>name</c>, <c>gamemode</c>, <c>level</c>,
/// <c>limit</c> and <c>sort</c>, where <c>type</c>, <c>name</c> and <c>gamemode</c> may be negated with <c>!</c>.
/// </para>
/// <para>
/// <c>@a</c>, <c>@p</c> and <c>@r</c> look at every online player; <c>@e</c> and <c>@n</c> at the entities of the
/// level the command runs in.
/// </para>
/// </remarks>
public sealed partial class EntitySelector
{
    private readonly List<Func<IEntity, CommandContext, bool>> filters = [];

    private string? playerName;
    private Guid? uuid;

    private char kind;
    private double? x, y, z;
    private (double? Min, double? Max)? distance;
    private int? limit;
    private SelectorSort? sort;
    private bool playersOnly;
    private bool typeSelected;

    private EntitySelector() { }

    /// <summary>Whether the selector can only select players.</summary>
    public bool PlayersOnly => this.playersOnly;

    /// <summary>Whether the selector can select at most one entity.</summary>
    public bool Single => this.kind is '\0' or 'p' or 'r' or 's' or 'n' || this.limit == 1;

    /// <summary>
    /// Reads a selector, or a player's name or UUID.
    /// </summary>
    public static bool TryParse(string input, [NotNullWhen(true)] out EntitySelector? selector)
    {
        selector = null;

        if (string.IsNullOrEmpty(input))
            return false;

        if (input[0] != '@')
        {
            if (Guid.TryParse(input, out var guid))
            {
                selector = new EntitySelector { uuid = guid };
                return true;
            }

            if (!PlayerName().IsMatch(input))
                return false;

            selector = new EntitySelector { playerName = input, playersOnly = true };
            return true;
        }

        if (input.Length < 2 || input[1] is not ('p' or 'a' or 'r' or 's' or 'e' or 'n'))
            return false;

        var result = new EntitySelector
        {
            kind = input[1],
            playersOnly = input[1] is 'p' or 'a' or 'r'
        };

        result.sort = result.kind switch
        {
            'p' or 'n' => SelectorSort.Nearest,
            'r' => SelectorSort.Random,
            _ => null
        };

        if (result.kind is 'p' or 'r' or 'n')
            result.limit = 1;

        var rest = input.AsSpan(2);
        if (!rest.IsEmpty)
        {
            if (rest[0] != '[' || rest[^1] != ']')
                return false;

            foreach (var argument in SplitArguments(rest[1..^1].ToString()))
            {
                var separator = argument.IndexOf('=');
                if (separator <= 0)
                    return false;

                if (!result.TryAddArgument(argument[..separator].Trim(), argument[(separator + 1)..].Trim()))
                    return false;
            }
        }

        selector = result;
        return true;
    }

    /// <summary>
    /// Finds the entities the selector selects for a command run in <paramref name="context"/>.
    /// </summary>
    public IReadOnlyList<IEntity> Select(CommandContext context)
    {
        if (this.playerName is string name)
            return context.Server.GetPlayer(name) is IPlayer player ? [player] : [];

        if (this.uuid is Guid guid)
        {
            if (context.Server.GetPlayer(guid) is IPlayer player)
                return [player];

            var entity = context.Level?.GetEntitiesInRange(context.Origin ?? default, float.MaxValue)
                .FirstOrDefault(entity => entity.Uuid == guid);
            return entity is null ? [] : [entity];
        }

        if (this.kind == 's')
            return context.Executor is IEntity executor && this.Matches(executor, context) ? [executor] : [];

        var origin = this.Origin(context);

        IEnumerable<IEntity> candidates = this.playersOnly
            ? context.Server.OnlinePlayers.Values
            : context.Level?.GetEntitiesInRange(origin, float.MaxValue) ?? [];

        var matches = candidates.Where(entity => this.Matches(entity, context, origin));

        matches = this.sort switch
        {
            SelectorSort.Nearest => matches.OrderBy(entity => (entity.Position - origin).MagnitudeSquared()),
            SelectorSort.Furthest => matches.OrderByDescending(entity => (entity.Position - origin).MagnitudeSquared()),
            SelectorSort.Random => matches.OrderBy(_ => Random.Shared.Next()),
            _ => matches
        };

        if (this.limit is int count)
            matches = matches.Take(count);

        return [.. matches];
    }

    /// <summary>
    /// Finds the players the selector selects, leaving out other entities.
    /// </summary>
    public IReadOnlyList<IPlayer> SelectPlayers(CommandContext context) => [.. this.Select(context).OfType<IPlayer>()];

    private VectorD Origin(CommandContext context)
    {
        var origin = context.Origin ?? context.Level?.LevelData.SpawnPosition ?? default;

        return new VectorD(this.x ?? origin.X, this.y ?? origin.Y, this.z ?? origin.Z);
    }

    private bool Matches(IEntity entity, CommandContext context) => this.Matches(entity, context, this.Origin(context));

    private bool Matches(IEntity entity, CommandContext context, VectorD origin)
    {
        if (this.playersOnly && entity is not IPlayer)
            return false;

        // Like vanilla's, selectors skip dead players.
        if (entity is IPlayer { Alive: false })
            return false;

        if (this.distance is var (min, max))
        {
            if (entity.Level != context.Level)
                return false;

            var squared = (entity.Position - origin).MagnitudeSquared();
            if (min is double low && squared < low * low)
                return false;

            if (max is double high && squared > high * high)
                return false;
        }

        foreach (var filter in this.filters)
        {
            if (!filter(entity, context))
                return false;
        }

        return true;
    }

    private bool TryAddArgument(string key, string value)
    {
        var negated = value.StartsWith('!');
        var operand = negated ? value[1..].Trim() : value;

        switch (key)
        {
            case "x" when TryParseDouble(value, out var number):
                this.x = number;
                return true;
            case "y" when TryParseDouble(value, out var number):
                this.y = number;
                return true;
            case "z" when TryParseDouble(value, out var number):
                this.z = number;
                return true;
            case "distance" when TryParseRange(value, out var range) && range.Min is not < 0:
                this.distance = range;
                return true;
            case "limit" when int.TryParse(value, CultureInfo.InvariantCulture, out var count) && count > 0 && this.kind is not 's':
                this.limit = count;
                return true;
            case "sort" when this.kind is 'a' or 'e':
                this.sort = value switch
                {
                    "nearest" => SelectorSort.Nearest,
                    "furthest" => SelectorSort.Furthest,
                    "random" => SelectorSort.Random,
                    "arbitrary" => null,
                    _ => SelectorSort.Invalid
                };
                return this.sort is not SelectorSort.Invalid;
            case "type":
                if (!negated && this.typeSelected)
                    return false;

                var typeName = RemoveUnderscores(operand.StartsWith("minecraft:", StringComparison.Ordinal) ? operand[10..] : operand);
                if (!Enum.GetNames<EntityType>().Any(name => name.Equals(typeName, StringComparison.OrdinalIgnoreCase)))
                    return false;

                if (!negated)
                {
                    this.typeSelected = true;
                    this.playersOnly |= typeName.Equals(nameof(EntityType.Player), StringComparison.OrdinalIgnoreCase);
                }

                this.filters.Add((entity, _) =>
                    (entity.Type?.ToString().Equals(typeName, StringComparison.OrdinalIgnoreCase) ?? false) != negated);
                return true;
            case "name":
                var expected = operand.Trim('"');
                this.filters.Add((entity, _) => string.Equals(NameOf(entity), expected, StringComparison.Ordinal) != negated);
                return true;
            case "gamemode":
                var mode = operand switch
                {
                    "survival" => GameMode.Survival,
                    "creative" => GameMode.Creative,
                    "adventure" => GameMode.Adventure,
                    "spectator" => GameMode.Spectator,
                    _ => (GameMode?)null
                };
                if (mode is null)
                    return false;

                this.playersOnly = true;
                this.filters.Add((entity, _) => entity is IPlayer player && (player.GameMode == mode) != negated);
                return true;
            case "level" when TryParseRange(value, out var levels):
                this.playersOnly = true;
                this.filters.Add((entity, _) => entity is IPlayer player
                    && (levels.Min is not double low || player.XpLevel >= low)
                    && (levels.Max is not double high || player.XpLevel <= high));
                return true;
            default:
                return false;
        }
    }

    /// <summary>The name a selector's <c>name</c> argument compares: a player's username, else the custom name.</summary>
    private static string? NameOf(IEntity entity) =>
        entity is IPlayer player ? player.Username : entity.CustomName?.Text;

    // Splits arguments at commas outside of quotes.
    private static IEnumerable<string> SplitArguments(string text)
    {
        var start = 0;
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                quoted = !quoted;
            }
            else if (text[i] == ',' && !quoted)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        if (text.Length > 0)
            yield return text[start..];
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>Reads a range like <c>5</c>, <c>..5</c>, <c>5..</c> or <c>1..5</c>.</summary>
    internal static bool TryParseRange(string text, out (double? Min, double? Max) range)
    {
        range = default;

        var separator = text.IndexOf("..", StringComparison.Ordinal);
        if (separator < 0)
        {
            if (!TryParseDouble(text, out var exact))
                return false;

            range = (exact, exact);
            return true;
        }

        double? min = null, max = null;
        var low = text[..separator];
        var high = text[(separator + 2)..];

        if (low.Length > 0)
        {
            if (!TryParseDouble(low, out var value))
                return false;
            min = value;
        }

        if (high.Length > 0)
        {
            if (!TryParseDouble(high, out var value))
                return false;
            max = value;
        }

        if (min is null && max is null || min > max)
            return false;

        range = (min, max);
        return true;
    }

    private static string RemoveUnderscores(string name) => name.Replace("_", string.Empty);

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex PlayerName();

    private enum SelectorSort
    {
        Nearest,
        Furthest,
        Random,
        Invalid
    }
}
