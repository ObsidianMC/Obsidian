using Obsidian.API.Plugins;

namespace Obsidian.API;

public sealed class CommandContext(string message, ICommandSender commandSender, IPlayer? player, IServer server)
{
    public IPlayer? Player { get; } = player;

    /// <summary>
    /// The server the command runs on. A plugin's command gets the plugin's own <see cref="IServer"/>, whose additions
    /// are the plugin's (see <see cref="Events.BaseMinecraftEventArgs.Server"/>).
    /// </summary>
    public IServer Server => ServerScope.Resolve(server);

    public ICommandSender Sender { get; } = commandSender;
    public bool IsPlayer => Player is not null;

    public PluginBase? Plugin { get; internal set; }
    internal string Message { get; } = message;

    /// <summary>
    /// The entity the command runs as, which <c>@s</c> selects: the player who sent it, or the entity
    /// <c>/execute as</c> chose.
    /// </summary>
    public IEntity? Executor { get; init; } = player;

    /// <summary>
    /// Where the command runs, which relative coordinates and selector distances are measured from: the executor's
    /// position unless <c>/execute at</c> or <c>positioned</c> moved it.
    /// </summary>
    public VectorD? Origin
    {
        get => this.origin ?? this.Executor?.Position;
        init => this.origin = value;
    }

    /// <summary>
    /// The level the command runs in: the executor's unless <c>/execute at</c> or <c>in</c> changed it, else the
    /// default world.
    /// </summary>
    public ILevel? Level
    {
        get => this.level ?? this.Executor?.Level ?? this.Server?.DefaultWorld;
        init => this.level = value;
    }

    /// <summary>
    /// The rotation local (<c>^</c>) coordinates are measured along: the executor's unless <c>/execute rotated</c>
    /// or <c>at</c> changed it.
    /// </summary>
    public (Angle Yaw, Angle Pitch)? Rotation
    {
        get => this.rotation ?? (this.Executor is { } executor ? (executor.Yaw, executor.Pitch) : null);
        init => this.rotation = value;
    }

    private readonly VectorD? origin;
    private readonly ILevel? level;
    private readonly (Angle Yaw, Angle Pitch)? rotation;

    /// <summary>
    /// A context for running <paramref name="message"/> from this one, as <c>/execute run</c> does: the same sender
    /// and plugin, run as <paramref name="executor"/>, which is also its player if it's one, from the given place.
    /// </summary>
    internal CommandContext Derive(string message, IEntity? executor, VectorD? origin, ILevel? level, (Angle Yaw, Angle Pitch)? rotation) =>
        new(message, this.Sender, executor as IPlayer, server)
        {
            Executor = executor,
            Origin = origin,
            Level = level,
            Rotation = rotation,
            Plugin = this.Plugin
        };
}
