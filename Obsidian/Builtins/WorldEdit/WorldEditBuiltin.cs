using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.Builtins.WorldEdit;

internal sealed class WorldEditBuiltin() : Builtin(new PluginInfo
{
    Id = "worldedit",
    Name = "WorldEdit",
    AssemblyName = typeof(WorldEditBuiltin).Assembly.GetName().Name!,
    Version = new Version(1, 0, 0),
    Side = PluginSide.Server,
    Authors = ["Obsidian Team"]
})
{
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly Dictionary<Guid, EditSession> sessions = [];
    private bool stopping;

    public override void ConfigureRegistry(IPluginRegistry registry)
    {
        registry.MapCommands();
        registry.MapEvent<BlockAttackEventArgs>(this.AttackAsync);
        registry.MapEvent<PlayerInteractEventArgs>(this.InteractAsync);
        registry.MapEvent<BlockBreakEventArgs>(this.BreakAsync);
        registry.MapEvent<PlayerLeaveEventArgs>(this.LeaveAsync);
    }

    internal async Task ExecuteAsync(CommandContext context, string command, string arguments)
    {
        if (context.Player is not { } player)
        {
            await context.Sender.SendMessageAsync(ChatMessage.Simple("WorldEdit commands require a player."));
            return;
        }
        if (!context.Server.Operators.IsOperator(context.Sender.Player ?? player))
        {
            await context.Sender.SendMessageAsync(ChatMessage.Simple("WorldEdit requires operator permission."));
            return;
        }

        await this.operations.WaitAsync();
        try
        {
            if (this.stopping)
                return;
            var result = await WorldEditCommands.DispatchAsync(this.Session(player), player, command, arguments);
            await context.Sender.SendMessageAsync(ChatMessage.Simple(result));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            await context.Sender.SendMessageAsync(ChatMessage.Simple($"WorldEdit: {ex.Message}"));
        }
        finally
        {
            this.operations.Release();
        }
    }

    private EditSession Session(IPlayer player)
    {
        if (!this.sessions.TryGetValue(player.Uuid, out var session))
            this.sessions.Add(player.Uuid, session = new EditSession());
        return session;
    }

    private bool CanUseTool(IPlayer player, IServer server) => !this.stopping && server.Operators.IsOperator(player);

    private async ValueTask AttackAsync(BlockAttackEventArgs args)
    {
        if (args.IsCancelled || !this.CanUseTool(args.Player, args.Server) || !WorldEditTools.IsWand(args.Player.GetHeldItem()))
            return;
        args.Cancel();
        await this.SelectAsync(args.Player, args.Location, first: true);
    }

    private async ValueTask InteractAsync(PlayerInteractEventArgs args)
    {
        if (args.IsCancelled || !this.CanUseTool(args.Player, args.Server) || !WorldEditTools.IsWand(args.Item))
            return;
        args.Cancel();
        if (args.BlockLocation is { } position)
            await this.SelectAsync(args.Player, position, first: false);
    }

    private ValueTask BreakAsync(BlockBreakEventArgs args)
    {
        if (this.CanUseTool(args.Player, args.Server) && WorldEditTools.IsWand(args.Player.GetHeldItem()))
            args.Cancel();
        return ValueTask.CompletedTask;
    }

    private async ValueTask SelectAsync(IPlayer player, Vector position, bool first)
    {
        await this.operations.WaitAsync();
        try
        {
            if (!this.stopping)
                await player.SendMessageAsync(ChatMessage.Simple(this.Session(player).Select(player.Level, position, first)));
        }
        finally
        {
            this.operations.Release();
        }
    }

    private async ValueTask LeaveAsync(PlayerLeaveEventArgs args)
    {
        await this.operations.WaitAsync();
        try { this.sessions.Remove(args.Player.Uuid); }
        finally { this.operations.Release(); }
    }

    public override async ValueTask OnUnloadingAsync()
    {
        await this.operations.WaitAsync();
        try
        {
            this.stopping = true;
            this.sessions.Clear();
        }
        finally { this.operations.Release(); }
    }
}
