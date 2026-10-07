using Obsidian.API.Commands;
using Obsidian.Integrated;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Commands only an integrated server has, registered when it runs as one (see <see cref="Server.RunAsync"/>).
/// </summary>
public sealed class IntegratedCommandModule(IntegratedServerService integratedServer) : CommandModuleBase
{
    private const string Usage = "/publish [<allowCommands>] [<gamemode>] [<port>]";

    [Command("publish")]
    [CommandInfo("Opens the world to LAN.", Usage)]
    [RequirePermission(op: true, permissions: "obsidian.publish")]
    public Task PublishAsync() => this.PublishAsync(false);

    [CommandOverload]
    public Task PublishAsync(bool allowCommands) => this.PublishAsync(allowCommands, string.Empty);

    [CommandOverload]
    public Task PublishAsync(bool allowCommands, string gamemode) => this.PublishAsync(allowCommands, gamemode, 0);

    /// <summary>
    /// Vanilla's <c>/publish</c>: opens the world to LAN like the client's Open to LAN, by default without commands for
    /// everyone, in the world's game mode and on a free port.
    /// </summary>
    [CommandOverload]
    public async Task PublishAsync(bool allowCommands, string gamemode, int port)
    {
        if (!IntegratedServerService.TryParseGameMode(gamemode, out var gameMode))
        {
            await this.Sender.SendMessageAsync($"{ChatColor.Red}Unknown game mode {gamemode}. Usage: {Usage}");
            return;
        }

        if (((Server)this.Server).Integrated?.IsPublished == true)
        {
            await this.Sender.SendMessageAsync(Translatable("commands.publish.alreadyPublished"));
            return;
        }

        var publishedPort = await integratedServer.PublishAsync(port, gameMode, allowCommands);

        var message = publishedPort is int boundPort
            ? Translatable("commands.publish.started").AddChatComponent(ChatMessage.Simple(boundPort.ToString()))
            : Translatable("commands.publish.failed");

        await this.Sender.SendMessageAsync(message);
    }

    private static ChatMessage Translatable(string key) => new() { Translate = key };
}
