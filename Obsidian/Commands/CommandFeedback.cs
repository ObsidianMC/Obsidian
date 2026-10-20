using Obsidian.API.Commands;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Commands;

/// <summary>
/// What vanilla's commands say back, and helpers they share: how targets are named and where a command runs.
/// </summary>
internal static class CommandFeedback
{
    /// <summary>Tells the sender a command succeeded.</summary>
    public static Task SuccessAsync(this CommandContext context, string message) =>
        context.Sender.SendMessageAsync(ChatMessage.Simple(message));

    /// <summary>Tells the sender a command failed, in red like vanilla's errors.</summary>
    public static Task FailAsync(this CommandContext context, string message) =>
        context.Sender.SendMessageAsync(ChatMessage.Simple(message, ChatColor.Red));

    /// <summary>
    /// Says that a selector found nobody, as vanilla does, and returns whether it did, so a command can stop.
    /// </summary>
    public static async Task<bool> NoneFoundAsync<TEntity>(this CommandContext context, IReadOnlyCollection<TEntity> targets)
        where TEntity : IEntity
    {
        if (targets.Count > 0)
            return false;

        await context.FailAsync(typeof(TEntity) == typeof(IPlayer) ? "No player was found" : "No entity was found");
        return true;
    }

    /// <summary>
    /// Names the only target, or counts several ("3 targets"), as vanilla words its feedback.
    /// </summary>
    public static string Describe<TEntity>(IReadOnlyCollection<TEntity> targets, string plural = "targets") where TEntity : IEntity =>
        targets.Count == 1 ? NameOf(targets.First()) : $"{targets.Count} {plural}";

    /// <summary>The name a command shows for an entity: a player's username, its custom name, or its type.</summary>
    public static string NameOf(IEntity entity) => entity switch
    {
        IPlayer player => player.Username,
        { CustomName.Text: { Length: > 0 } name } => name,
        _ => entity.Type?.ToString().ToSnakeCase().Replace('_', ' ') is { } type
            ? char.ToUpperInvariant(type[0]) + type[1..]
            : "Entity"
    };

    /// <summary>The position a command runs from: its origin, or the world spawn for the console.</summary>
    public static VectorD OriginOf(CommandContext context) =>
        context.Origin ?? context.Level?.LevelData.SpawnPosition ?? default;

    /// <summary>
    /// The level a command runs in, or the default world's.
    /// </summary>
    public static ILevel LevelOf(CommandContext context) => context.Level ?? context.Server.DefaultWorld;

    /// <summary>Formats coordinates as vanilla's feedback does.</summary>
    public static string Format(VectorD position) =>
        FormattableString.Invariant($"{position.X:0.##}, {position.Y:0.##}, {position.Z:0.##}");

    /// <inheritdoc cref="Format(VectorD)"/>
    public static string Format(Vector position) => $"{position.X}, {position.Y}, {position.Z}";

    /// <summary>Resends a player's whole inventory, after a command changed several slots.</summary>
    public static ValueTask SyncInventoryAsync(IPlayer player) =>
        player.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList())
        {
            StateId = player.Inventory.StateId++,
            CarriedItem = player.CarriedItem
        });

    /// <summary>Resends a player's health and food, after a command changed them.</summary>
    public static ValueTask SyncHealthAsync(IPlayer player) =>
        player.QueuePacketAsync(new SetHealthPacket(player.Health, player.FoodLevel, player.FoodSaturationLevel));
}
