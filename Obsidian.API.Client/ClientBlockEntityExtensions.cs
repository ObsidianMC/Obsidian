using Obsidian.API.BlockEntities;
using System.Runtime.CompilerServices;

namespace Obsidian.API.Client;

/// <summary>Client world access for block entities, without making their server-side Level a client level.</summary>
public static class ClientBlockEntityExtensions
{
    private static readonly ConditionalWeakTable<BlockEntity, IClientWorld> worlds = new();

    /// <summary>
    /// The client view while this is a live client block entity (including Load and ClientTick), otherwise null.
    /// The view's thread rules still apply. The server never attaches a client world.
    /// </summary>
    public static IClientWorld? GetClientWorld(this BlockEntity blockEntity) =>
        worlds.TryGetValue(blockEntity, out var world) ? world : null;

    internal static void Attach(BlockEntity blockEntity, IClientWorld world) => worlds.Add(blockEntity, world);

    internal static void Detach(BlockEntity blockEntity) => worlds.Remove(blockEntity);
}
