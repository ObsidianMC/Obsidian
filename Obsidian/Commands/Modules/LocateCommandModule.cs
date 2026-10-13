using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's <c>/locate structure</c>: finds the nearest structure of a type or tag, vanilla's or a plugin's, in the
/// sender's dimension.
/// </summary>
[CommandGroup("locate")]
[CommandInfo("Finds the nearest structure of a type or tag.", "/locate structure <structure|#tag>")]
[RequirePermissionLevel(2)]
public sealed class LocateCommandModule : CommandModuleBase
{
    /// <summary>
    /// How many rings of placement regions vanilla's <c>/locate</c> and eyes of ender search for spread structures.
    /// </summary>
    internal const int SearchRadius = 100;

    [Command("structure")]
    [CommandInfo("Finds the nearest structure of a type or tag.", "/locate structure <structure|#tag>")]
    public async Task StructureAsync([Remaining, StructureSuggestions] string structure)
    {
        // Like vanilla's, the search starts at the sender's block; the console's is the default world's spawn.
        var level = this.Player?.Level ?? this.Server.DefaultWorld;
        var position = this.Player?.Position ?? level.LevelData.SpawnPosition;
        var origin = (Vector)position.Floor();
        var generator = (level as AbstractLevel)?.Generator as MojangGenerator;

        // Searching computes structure starts, which takes a while for distant ones.
        var message = await Task.Run(() => LocateStructure(
            structure.Trim(),
            origin,
            PluginStructures.Current.Structures,
            targets => generator?.LocateStructure(
                locator => locator.Locate(targets, origin, SearchRadius, skipKnownStructures: false))));

        await this.Sender.SendMessageAsync(message);
    }

    /// <summary>
    /// What <c>/locate structure</c> says: where the nearest structure of <paramref name="argument"/> is, or why
    /// there's none, with vanilla's translation keys and arguments.
    /// </summary>
    /// <param name="argument">A structure's identifier, or a structure tag's after a <c>#</c>.</param>
    /// <param name="origin">The block the search starts at.</param>
    /// <param name="structures">Every structure, by identifier, which an identifier must name.</param>
    /// <param name="search">Finds the nearest of a set of structures, by identifier.</param>
    internal static ChatMessage LocateStructure(
        string argument,
        Vector origin,
        IReadOnlyDictionary<string, Structure> structures,
        Func<IReadOnlySet<string>, LocatedStructure?> search)
    {
        var isTag = argument.StartsWith('#');
        var identifier = Qualify(isTag ? argument[1..] : argument);
        var printable = isTag ? $"#{identifier}" : identifier;

        var targets = isTag
            ? StructureLocator.TagStructures(identifier)
            : Single(identifier, structures);

        if (targets is null)
            return Failure("commands.locate.structure.invalid", printable);

        if (search(targets) is not LocatedStructure found)
            return Failure("commands.locate.structure.not_found", printable);

        // Vanilla names the structure found when it searched a tag.
        var name = isTag ? $"{printable} ({found.Structure.Identifier})" : printable;
        return Success(name, origin, found.Position);
    }

    // Vanilla's showLocateResult for structures: the position's height isn't known, so it's "~", and the distance is
    // horizontal. The distance matches vanilla's: a float square root of an int sum, which wraps like Java's.
    private static ChatMessage Success(string name, Vector origin, Vector found)
    {
        var dx = found.X - origin.X;
        var dz = found.Z - origin.Z;
        var root = MathF.Sqrt(unchecked(dx * dx + dz * dz));
        var distance = float.IsNaN(root) ? 0 : (int)MathF.Floor(root);

        var coordinates = new ChatMessage { Translate = "chat.coordinates" };
        coordinates.AddChatComponent([found.X.ToString(), "~", found.Z.ToString()]);

        var tooltip = new ChatMessage { Translate = "chat.coordinates.tooltip" };
        var link = new ChatMessage
        {
            Translate = "chat.square_brackets",
            Color = HexColor.Green,
            ClickEvent = new ClickComponent
            {
                Action = ClickAction.SuggestCommand,
                Value = $"/tp @s {found.X} ~ {found.Z}"
            },
            HoverEvent = new HoverComponent
            {
                Action = HoverAction.ShowText,
                Contents = new HoverChatContent { ChatMessage = tooltip }
            }
        };

        link.AddChatComponent(coordinates);

        var message = new ChatMessage { Translate = "commands.locate.structure.success" };
        message.AddChatComponent([name, link, distance.ToString()]);
        return message;
    }

    // Vanilla's command failures are red.
    private static ChatMessage Failure(string key, string printable)
    {
        var message = new ChatMessage
        {
            Translate = key,
            Color = HexColor.Red
        };

        return message.AddChatComponent(printable);
    }

    // The structure with the identifier, when there's one.
    private static IReadOnlySet<string>? Single(string identifier, IReadOnlyDictionary<string, Structure> structures) =>
        structures.ContainsKey(identifier) ? new HashSet<string> { identifier } : null;

    // An identifier without a namespace is vanilla's.
    private static string Qualify(string identifier) =>
        identifier.Contains(':') ? identifier : $"minecraft:{identifier}";

    /// <summary>
    /// Suggests every structure's identifier and vanilla's structure tags.
    /// </summary>
    private sealed class StructureSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context)
        {
            var suggestions = PluginStructures.Current.Structures.Keys
                .Order(StringComparer.Ordinal)
                .Concat(StructureTags.All.Keys.Order(StringComparer.Ordinal).Select(tag => $"#{tag}"))
                .Select(text => new CommandSuggestion(text));

            return ValueTask.FromResult(suggestions);
        }
    }
}
