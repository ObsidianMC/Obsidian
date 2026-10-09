namespace Obsidian.Utilities;

public static class PlayerConnectionExtensions
{
    public static bool TryGetClient(this IPlayer player, out IClient client)
    {
        if (player is IClientPlayer connected)
        {
            client = connected.Client;
            return true;
        }

        client = null!;
        return false;
    }

    public static IClient RequireClient(this IPlayer player, string operation) =>
        player is IClientPlayer connected
            ? connected.Client
            : throw new InvalidOperationException($"Player '{player.Username}' does not have a client connection for {operation}.");
}
