using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.Events;

/// <summary>
/// Keeps a player's recipe book up to date with the server's recipes (see <see cref="RecipesRegistry.Current"/>):
/// the whole book as they join, then, when the recipes change, the recipes added and removed since. Every player
/// knows every crafting recipe, as Obsidian has no recipe unlocking.
/// </summary>
/// <remarks>
/// It remembers the recipes the player was last sent, so the changes are sent once and in order, whichever comes
/// first of the join and a change made meanwhile. Nothing is sent before the join's book, as the client has no recipe
/// book until it joins.
/// </remarks>
internal sealed class RecipeBookSync
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private RecipeSet? sent;

    /// <summary>
    /// Sends <paramref name="player"/> the whole recipe book, replacing their client's, as they join.
    /// </summary>
    public async ValueTask SendInitialAsync(IPlayer player)
    {
        await this.gate.WaitAsync();
        try
        {
            var recipes = RecipesRegistry.Current;
            var entries = recipes.Book
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .ToList();

            await player.QueuePacketAsync(new RecipeBookAddPacket(entries, replace: true));
            this.sent = recipes;
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <summary>
    /// Sends <paramref name="player"/> what changed in the recipe book since they were last sent it: the recipes gone
    /// and those added or registered again. Nothing, before the join's book.
    /// </summary>
    public async ValueTask SendChangesAsync(IPlayer player)
    {
        await this.gate.WaitAsync();
        try
        {
            if (this.sent is not RecipeSet previous)
                return;

            var recipes = RecipesRegistry.Current;
            var removed = new List<int>();
            var added = new List<RecipeBookEntry>();

            for (var displayId = 0; displayId < recipes.Book.Count; displayId++)
            {
                var before = previous.GetBookEntry(displayId);
                var now = recipes.Book[displayId];

                if (ReferenceEquals(before?.Recipe, now?.Recipe))
                    continue;

                // A recipe replaced under the same id is added over the old one, as the client keys its book by id.
                if (now is not null)
                    added.Add(now);
                else
                    removed.Add(displayId);
            }

            if (removed.Count > 0)
                await player.QueuePacketAsync(new RecipeBookRemovePacket(removed));

            if (added.Count > 0)
                await player.QueuePacketAsync(new RecipeBookAddPacket(added, replace: false));

            this.sent = recipes;
        }
        finally
        {
            this.gate.Release();
        }
    }
}
