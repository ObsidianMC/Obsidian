using System.Collections.Immutable;

namespace Obsidian.API;

/// <summary>
/// The server's operators, saved to <c>ops.json</c>. Changes are sent to the affected player's client.
/// </summary>
public interface IOperatorList
{
    /// <summary>
    /// Makes <paramref name="player"/> an operator.
    /// </summary>
    /// <param name="player">The player to add.</param>
    /// <param name="level">The operator's permission level, from 1 to 4.</param>
    /// <param name="bypassesPlayerLimit">Whether the player may join when the server is full.</param>
    public void AddOperator(IPlayer player, int level = 4, bool bypassesPlayerLimit = false);
    /// <summary>
    /// Starts a request by <paramref name="player"/> to become an operator, whose code is written to the server log.
    /// </summary>
    /// <param name="player">The player asking.</param>
    /// <returns>
    /// <see langword="false"/> if operator requests are disabled in the configuration or the player already has one
    /// pending; otherwise, <see langword="true"/>.
    /// </returns>
    public bool CreateRequest(IPlayer player);
    /// <summary>
    /// Completes the request with <paramref name="code"/> and makes <paramref name="player"/> an operator at level 4.
    /// </summary>
    /// <param name="player">The player to make an operator.</param>
    /// <param name="code">The request's code, from the server log.</param>
    /// <returns><see langword="true"/> if the code matched a pending request; otherwise, <see langword="false"/>.</returns>
    public bool ProcessRequest(IPlayer player, string code);
    /// <summary>
    /// Takes away <paramref name="player"/>'s operator status.
    /// </summary>
    /// <param name="player">The player to remove.</param>
    public void RemoveOperator(IPlayer player);
    /// <summary>
    /// Returns whether <paramref name="p"/> has an operator permission level above 0.
    /// </summary>
    /// <param name="p">The player to check.</param>
    public bool IsOperator(IPlayer p);
    /// <summary>
    /// Gets the online players who are operators.
    /// </summary>
    public ImmutableList<IPlayer> GetOnlineOperators();
}
