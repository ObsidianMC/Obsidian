using System.Text.Json;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.API.Commands;
using Obsidian.API.Data;
using Obsidian.WorldData;

namespace Obsidian.Commands;

/// <summary>
/// The generated vanilla grammar supplies wire parsers and suggestions. Execution remains in our command modules;
/// plugin commands retain their own grammar even when they use a vanilla command's name.
/// </summary>
internal static class VanillaCommandTree
{
    internal static JsonElement Reference { get; } = Load();

    internal static bool Owns(Obsidian.API.Commands.Command command,
        IEnumerable<Obsidian.API.Commands.Command> commands) => command.PluginContainer is null
        && command.Overloads.Concat(commands.Where(child => child.Parent == command).SelectMany(child => child.Overloads))
        .OfType<CommandExecutor>().Any(executor => executor.ModuleType == (command.Name switch
        {
            "time" => typeof(TimeCommandModule),
            "compute" => typeof(ComputeCommandModule),
            "swing" => typeof(SwingCommandModule),
            "posteffect" => typeof(PostEffectCommandModule),
            "item" => typeof(ItemCommandModule),
            "execute" => typeof(ExecuteCommandModule),
            "place" => typeof(PlaceCommandModule),
            "data" => typeof(DataCommandModule),
            "publish" or "unpublish" => typeof(IntegratedCommandModule),
            _ => null
        }));

    private static JsonElement Load()
    {
        var assembly = typeof(VanillaCommandTree).Assembly;
        var name = $"Obsidian.Assets.commands-{ServerConstants.ProtocolDescription}.json";
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing generated command grammar");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    internal static CommandNode? Create(string name, ref int index)
    {
        if (!Reference.GetProperty("tree").TryGetProperty(name, out var definition))
            return null;

        var paths = new Dictionary<string, CommandNode>();
        var redirects = new List<(CommandNode Node, string Path)>();
        var result = Read(name, definition, name, paths, redirects, ref index);
        foreach (var (node, path) in redirects)
        {
            // /execute run redirects to the packet root; other redirects stay inside /execute.
            node.RedirectIndex = path.Length == 0 ? 0 : paths[path].Index;
        }

        return result;
    }

    internal static CommandCompletion Complete(CommandContext context, string name, int offset)
    {
        var text = context.Message[offset..];
        List<(int Start, string Value)> words;
        try
        {
            words = CommandInput.Words(text);
        }
        catch (FormatException)
        {
            return new CommandCompletion(context.Message.Length, 0, []);
        }

        if (text.EndsWith(' '))
            words.Add((text.Length, string.Empty));

        var completed = words.Count - 1;
        var partial = words[^1];
        var found = new HashSet<string>(StringComparer.Ordinal);
        var definition = Reference.GetProperty("tree").GetProperty(name);
        Search(definition, 1, new Dictionary<string, string>());
        var choices = found.Where(value => value != partial.Value
                && (value.StartsWith(partial.Value, StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("minecraft:" + partial.Value, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(1000)
            .Select(value => new CommandSuggestion(value)).ToArray();
        return new CommandCompletion(offset + partial.Start, partial.Value.Length, choices);

        void Search(JsonElement node, int index, Dictionary<string, string> arguments)
        {
            if (node.TryGetProperty("redirect", out var redirect))
            {
                var path = redirect.EnumerateArray().Select(part => part.GetString()!).ToArray();
                if (path.Length > 0)
                {
                    var target = Reference.GetProperty("tree").GetProperty(path[0]);
                    foreach (var part in path.Skip(1))
                        target = target.GetProperty("children").GetProperty(part);

                    Search(target, index, arguments);
                }
            }

            if (!node.TryGetProperty("children", out var children))
                return;

            foreach (var child in children.EnumerateObject())
            {
                var literal = child.Value.GetProperty("type").GetString() == "literal";
                if (index == completed)
                {
                    if (literal)
                        found.Add(child.Name);
                    else
                        found.UnionWith(Suggestions(context, child.Name, child.Value, arguments));

                    continue;
                }

                var count = literal ? 1 : child.Value.GetProperty("parser").GetString() switch
                {
                    "minecraft:block_pos" or "minecraft:vec3" => 3,
                    "minecraft:rotation" or "minecraft:column_pos" or "minecraft:vec2" => 2,
                    _ => 1
                };
                if (index + count > completed || literal && words[index].Value != child.Name)
                    continue;

                var next = new Dictionary<string, string>(arguments);
                if (!literal)
                    next[child.Name] = string.Join(' ', words.Skip(index).Take(count).Select(word => word.Value));

                Search(child.Value, index + count, next);
            }
        }
    }

    private static IEnumerable<string> Suggestions(CommandContext context, string name, JsonElement argument,
        Dictionary<string, string> arguments)
    {
        var parser = argument.GetProperty("parser").GetString();
        var clock = WorldTimelines.DefaultClock(CommandFeedback.LevelOf(context));
        if (arguments.TryGetValue("clock", out var selected))
        {
            if (!ResourceLocation.TryParse(selected, out var identifier))
                return [];

            clock = identifier.Value.ToString();
        }
        if (name == "timemarker")
        {
            return WorldTimelines.Markers.Where(marker => marker.Value.Clock == clock && marker.Value.ShowInCommands)
                .Select(marker => marker.Key);
        }

        if (parser == "minecraft:resource" && argument.TryGetProperty("properties", out var properties))
        {
            return properties.GetProperty("registry").GetString() switch
            {
                "minecraft:world_clock" => CommandFeedback.LevelOf(context).LevelData.WorldClocks.States.Keys,
                "minecraft:timeline" => ItemUseOnBehaviors.Load("timeline.json")
                    .Where(entry => entry.Value.GetProperty("clock").GetString() == clock).Select(entry => entry.Key),
                _ => []
            };
        }

        return parser switch
        {
            "minecraft:slot_source" => Reference.GetProperty("slot_ranges").EnumerateObject().Select(range => range.Name),
            "minecraft:context_int_provider" => ContextNumberProviders.Ints.Keys,
            "minecraft:context_float_provider" => ContextNumberProviders.Floats.Keys,
            "minecraft:entity" => context.Server.OnlinePlayers.Values.Select(player => player.Username)
                .Concat(["@a", "@e", "@n", "@p", "@r", "@s"]),
            "brigadier:bool" => ["false", "true"],
            "minecraft:swing_animation" => ["none", "stab", "whack"],
            _ => []
        };
    }

    private static CommandNode Read(string name, JsonElement definition, string path,
        Dictionary<string, CommandNode> paths, List<(CommandNode Node, string Path)> redirects, ref int index)
    {
        var node = new CommandNode
        {
            Name = name,
            Index = ++index,
            Type = definition.GetProperty("type").GetString() == "argument"
                ? CommandNodeType.Argument
                : CommandNodeType.Literal
        };
        paths.Add(path, node);
        if (definition.TryGetProperty("executable", out var executable) && executable.GetBoolean())
            node.Type |= CommandNodeType.IsExecutable;

        if (definition.GetProperty("restricted").GetBoolean())
            node.Type |= CommandNodeType.IsRestricted;

        if (definition.TryGetProperty("parser", out var parser))
        {
            var wire = Convert.FromHexString(definition.GetProperty("wire").GetString()!);
            node.Parser = new WireParser(parser.GetString()!, wire);
        }

        if (definition.TryGetProperty("suggestions", out var suggestions))
        {
            node.Type |= CommandNodeType.HasSuggestions;
            node.SuggestionType = suggestions.GetString();
        }

        if (definition.TryGetProperty("redirect", out var redirect))
        {
            node.Type |= CommandNodeType.HasRedirect;
            redirects.Add((node, string.Join(' ', redirect.EnumerateArray().Select(part => part.GetString()))));
        }

        if (definition.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateObject())
                node.Children.Add(Read(child.Name, child.Value, path + " " + child.Name, paths, redirects, ref index));
        }

        return node;
    }

    private sealed class WireParser(string identifier, byte[] wire) : BaseArgumentParser<string>
    {
        public override int Id => wire[0];

        public override string Identifier => identifier;

        public override bool TryParseArgument(string input, CommandContext ctx, out string result)
        {
            result = input;
            return false;
        }

        public override void Write(INetStreamWriter writer)
        {
            foreach (var value in wire)
                writer.WriteByte(unchecked((sbyte)value));
        }
    }
}
