using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.Events;

/// <summary>
/// Sends the initial book and later differences in order. Remembered ids and registration revisions contain no
/// recipe objects, so a player's book cannot keep an unloaded plugin alive.
/// </summary>
internal sealed class RecipeBookSync
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Dictionary<int, long>? sent;

    /// <summary>Sends the full book that this client's configured content can safely display.</summary>
    public async ValueTask SendInitialAsync(IPlayer player)
    {
        await this.gate.WaitAsync();
        try
        {
            var recipes = MainEventHandler.PlayerRecipes(player);

            // A plugin may have added items since this client's handshake started. Its next configuration join
            // delivers those recipes; vanilla's book is safe in the meantime.
            if (!CanSend(player, recipes))
                recipes = RecipesRegistry.Current;

            var entries = Entries(recipes);
            await player.QueuePacketAsync(new RecipeBookAddPacket(entries, replace: true));
            this.sent = Identities(entries);
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <summary>Sends the difference since the last completed book delivery, once the initial book was sent.</summary>
    /// <returns>Whether the client's content is ready for crafting results from the selected recipe set.</returns>
    public async ValueTask<bool> SendChangesAsync(IPlayer player)
    {
        await this.gate.WaitAsync();
        try
        {
            // Select and validate inside the same per-player gate. Never validate content before waiting here.
            var recipes = MainEventHandler.PlayerRecipes(player);
            if (!CanSend(player, recipes))
                return false;

            if (this.sent is null)
                return true;

            var entries = Entries(recipes);
            var identities = Identities(entries);
            var removed = this.sent.Keys.Where(id => !identities.ContainsKey(id)).ToList();
            var added = entries
                .Where(entry => !this.sent.TryGetValue(entry.DisplayId, out var revision)
                    || revision != entry.Revision)
                .ToList();

            if (removed.Count > 0)
                await player.QueuePacketAsync(new RecipeBookRemovePacket(removed));

            if (added.Count > 0)
                await player.QueuePacketAsync(new RecipeBookAddPacket(added, replace: false));

            // Only acknowledge the complete delivery. A failure is retried on the next recipe change.
            this.sent = identities;

            return true;
        }
        finally
        {
            this.gate.Release();
        }
    }

    internal static bool CanSend(IPlayer player, RecipeSet recipes)
    {
        if (player is not Player { Client: Client client, Server: Server server })
            return true;

        if (client.AwaitingReconfiguration || client.ConfiguredContent is null)
            return false;

        // The stamp belongs to this immutable set. A client may also have configured newer content since it
        // was published; reading the live version after selecting the set safely recognizes that case.
        return recipes.RequiredContent is not ContentVersion required
            || !client.NeedsReconfiguration(required)
            || !client.NeedsReconfiguration(server.GetContentVersion());
    }

    private static List<RecipeBookEntry> Entries(RecipeSet recipes) => recipes.Book
        .Where(entry => entry is not null)
        .Select(entry => entry!)
        .ToList();

    private static Dictionary<int, long> Identities(IEnumerable<RecipeBookEntry> entries) =>
        entries.ToDictionary(entry => entry.DisplayId, entry => entry.Revision);
}
