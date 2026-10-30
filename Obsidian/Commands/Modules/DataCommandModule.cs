using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Text.Json;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's <c>/data get</c>: shows the NBT of an entity or a block entity, or a value inside it.
/// </summary>
[CommandGroup("data")]
[CommandInfo("Shows entity and block entity data.", "/data get <entity|block> …")]
[RequirePermissionLevel(2)]
public sealed partial class DataCommandModule : CommandModuleBase
{
    [CommandGroup("get")]
    [CommandInfo("Shows entity and block entity data.", "/data get <entity <target>|block <pos>> [<path>]")]
    public sealed class Get : CommandModuleBase
    {
        [Command("storage")]
        public Task<int> StorageAsync(ResourceLocation target) => this.StorageAsync(target, null);

        [CommandOverload]
        public async Task<int> StorageAsync(ResourceLocation target, string? path)
        {
            var tag = CommandStorage.Read(this.Server, target.ToString());
            return await this.ShowAsync(tag, path, $"{target} has the following contents: ");
        }

        [Command("entity")]
        public Task<int> EntityAsync(IEntity target) => this.EntityAsync(target, null);

        [CommandOverload]
        public async Task<int> EntityAsync(IEntity target, string? path)
        {
            if (target is not Entity entity)
            {
                await this.CommandContext.FailAsync("No entity was found");
                return 0;
            }

            var tag = new NbtCompound();
            entity.WriteNbt(tag);

            return await this.ShowAsync(tag, path, $"{NameOf(target)} has the following entity data: ");
        }

        [Command("block")]
        public Task<int> BlockAsync(Vector pos) => this.BlockAsync(pos, null);

        [CommandOverload]
        public async Task<int> BlockAsync(Vector pos, string? path)
        {
            var blockEntity = await LevelOf(this.CommandContext).GetBlockEntityAsync(pos);
            if (blockEntity is null || BlockEntityNbt.Save(blockEntity) is not NbtCompound tag)
            {
                await this.CommandContext.FailAsync("The target block is not a block entity");
                return 0;
            }

            return await this.ShowAsync(tag, path, $"{Format(pos)} has the following block data: ");
        }

        private async Task<int> ShowAsync(NbtCompound tag, string? path, string prefix)
        {
            INbtTag? found = tag;
            if (path is not null)
                found = Find(tag, path);

            if (found is null || Result(found, path is null) is not { } result)
            {
                await this.CommandContext.FailAsync($"Found no elements matching {path}");
                return 0;
            }

            await this.CommandContext.SuccessAsync(prefix + Snbt.Write(found));
            return result;
        }

        /// <summary>
        /// Vanilla's <c>/data get</c> result: 1 for the whole data, otherwise a number floored, a collection's size or a
        /// string's length. Other tags have no result and fail.
        /// </summary>
        internal static int? Result(INbtTag found, bool whole)
        {
            if (whole)
                return 1;

            if (found.TryGetNumber(out double number))
                return ComputeCommandModule.FloorDouble(number);

            return found switch
            {
                NbtCompound compound => compound.Count,
                NbtList list => list.Count,
                NbtArray<int> array => array.Count,
                NbtArray<long> array => array.Count,
                NbtArray<byte> array => array.Count,
                NbtTag<string> text => text.Value.Length,
                _ => null
            };
        }
    }

    /// <summary>
    /// Follows a path of names and list indices, like <c>Inventory[0].id</c>, through NBT.
    /// </summary>
    internal static INbtTag? Find(INbtTag root, string path)
    {
        var current = root;

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = part.IndexOf('[');
            var name = bracket < 0 ? part : part[..bracket];

            if (name.Length > 0)
            {
                if (current is not NbtCompound compound || !compound.TryGetTag(name.Trim('"'), out var child))
                    return null;

                current = child;
            }

            while (bracket >= 0)
            {
                var close = part.IndexOf(']', bracket);
                if (close < 0 || current is not NbtList list || !int.TryParse(part[(bracket + 1)..close], out var index))
                    return null;

                if (index < 0)
                    index += list.Count;

                if (index < 0 || index >= list.Count)
                    return null;

                current = list[index];
                bracket = part.IndexOf('[', close);
            }
        }

        return current;
    }
}

/// <summary>
/// Vanilla's <c>/tellraw</c> and <c>/title</c>, which show text components to players.
/// </summary>
[RequirePermissionLevel(2)]
public sealed class TextCommandModule : CommandModuleBase
{
    // Vanilla's default title times, in ticks.
    private const int FadeIn = 10, Stay = 70, FadeOut = 20;

    [Command("tellraw")]
    [CommandInfo("Sends a text component to players.", "/tellraw <targets> <message>")]
    public async Task TellRawAsync(IPlayer[] targets, [Remaining] string message)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        var component = ParseComponent(message);
        foreach (var player in targets)
            await player.SendMessageAsync(component);
    }

    [CommandGroup("title")]
    [CommandInfo("Shows titles to players.", "/title <targets> <clear|reset|title|subtitle|actionbar|times>")]
    public sealed class Title : CommandModuleBase
    {
        [GroupCommand]
        public async Task TitleAsync(IPlayer[] targets, [Suggestions("clear", "reset")] string action)
        {
            if (await this.CommandContext.NoneFoundAsync(targets))
                return;

            if (action is not ("clear" or "reset"))
            {
                await this.CommandContext.FailAsync("Expected clear, reset, title, subtitle, actionbar or times");
                return;
            }

            foreach (var player in targets)
                await player.QueuePacketAsync(new ClearTitlesPacket { Reset = action == "reset" });

            await this.CommandContext.SuccessAsync(action == "clear"
                ? $"Cleared titles for {Describe(targets, "players")}"
                : $"Reset title options for {Describe(targets, "players")}");
        }

        [GroupCommand]
        public async Task TitleAsync(IPlayer[] targets, [Suggestions("title", "subtitle", "actionbar", "times")] string action, [Remaining] string title)
        {
            if (await this.CommandContext.NoneFoundAsync(targets))
                return;

            var component = ParseComponent(title);
            foreach (var player in targets)
            {
                switch (action)
                {
                    case "title":
                        await player.QueuePacketAsync(new SetTitleTextPacket { Text = component });
                        break;
                    case "subtitle":
                        await player.QueuePacketAsync(new SetSubtitleTextPacket { Text = component });
                        break;
                    case "actionbar":
                        await player.SetActionBarTextAsync(component);
                        break;
                    case "times" when TryParseTimes(title, out var times):
                        await player.QueuePacketAsync(new SetTitlesAnimationPacket { FadeIn = times.In, Stay = times.Stay, FadeOut = times.Out });
                        break;
                    default:
                        await this.CommandContext.FailAsync("Expected clear, reset, title, subtitle, actionbar or times");
                        return;
                }
            }

            await this.CommandContext.SuccessAsync($"Showing new {action} for {Describe(targets, "players")}");
        }

        // The fade in, stay and fade out ticks of /title times, which also takes vanilla's s, t and d suffixes.
        private static bool TryParseTimes(string text, out (int In, int Stay, int Out) times)
        {
            times = (FadeIn, Stay, FadeOut);

            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;

            Span<int> ticks = stackalloc int[3];
            for (var i = 0; i < 3; i++)
            {
                var part = parts[i];
                var scale = part[^1] switch { 's' => 20, 'd' => 24000, 't' => 1, _ => 0 };
                if (!int.TryParse(scale == 0 ? part : part[..^1], out var value) || value < 0)
                    return false;

                ticks[i] = value * Math.Max(scale, 1);
            }

            times = (ticks[0], ticks[1], ticks[2]);
            return true;
        }
    }

    /// <summary>
    /// Reads a text component written as JSON, like <c>{"text":"Hi","color":"gold"}</c> or <c>"Hi"</c>, or else takes the
    /// text as it is.
    /// </summary>
    internal static ChatMessage ParseComponent(string text)
    {
        try
        {
            if (text.StartsWith('{'))
                return JsonSerializer.Deserialize<ChatMessage>(text, Globals.JsonOptions) ?? ChatMessage.Simple(text);

            if (text.StartsWith('"'))
                return ChatMessage.Simple(JsonSerializer.Deserialize<string>(text) ?? text);

            if (text.StartsWith('['))
            {
                var parts = JsonSerializer.Deserialize<ChatMessage[]>(text, Globals.JsonOptions) ?? [];
                var message = ChatMessage.Empty;
                foreach (var part in parts)
                    message.AddExtra(part);

                return message;
            }
        }
        catch (JsonException)
        {
            // Not JSON after all; shown as written.
        }

        return ChatMessage.Simple(text);
    }
}
