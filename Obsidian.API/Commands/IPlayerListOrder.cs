namespace Obsidian.API.Commands;

/// <summary>
/// A server that keeps its online players in vanilla's player list order, which selectors go through.
/// </summary>
/// <remarks>
/// <see cref="IServer.OnlinePlayers"/> has no order of its own; vanilla's <c>@a</c>, and <c>@p</c> or <c>@r</c> among
/// equals, go by the order of <c>PlayerList.players</c>.
/// </remarks>
internal interface IPlayerListOrder
{
    /// <summary>The online players, in vanilla's player list order.</summary>
    IReadOnlyList<IPlayer> PlayersInListOrder { get; }
}
