using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.WorldData.Portals;

/// <summary>A portal type's claim on one portal block: the type's identifier and the block it placed there.</summary>
internal readonly record struct PortalOwner(string Type, string Block);

/// <summary>
/// Which portal type lit or built each portal block, kept with the block's chunk and saved with it, so several types
/// can share one portal block such as <c>minecraft:nether_portal</c>. A claimed block belongs to its type even while
/// that type isn't registered: it stays inert instead of falling back to another type that uses the same block. An
/// unclaimed block, such as one placed by a command or in a vanilla world, belongs to the first registered type that
/// uses it, so vanilla's portal blocks stay Nether portals.
/// </summary>
internal static class PortalOwnership
{
    /// <summary>The chunk tag the claims are saved in.</summary>
    internal const string NbtKey = "ObsidianPortalTypes";

    /// <summary>Claims the portal block at <paramref name="position"/> for <paramref name="type"/>.</summary>
    internal static void Claim(AbstractLevel level, Vector position, PortalType type, IBlock block)
    {
        if (level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is Chunk chunk)
            chunk.PortalOwners[position] = new PortalOwner(type.Identifier, block.UnlocalizedName);
    }

    /// <summary>
    /// The type the portal block <paramref name="block"/> at <paramref name="position"/> of <paramref name="chunk"/>
    /// belongs to while that type is registered, or null. A claim on a block since replaced is dropped.
    /// </summary>
    internal static PortalType? TypeAt(IChunk? chunk, Vector position, IBlock block)
    {
        string? owner = null;
        if (chunk is Chunk claimed && claimed.PortalOwners.TryGetValue(position, out var claim))
        {
            if (claim.Block == block.UnlocalizedName)
                owner = claim.Type;
            else
                claimed.PortalOwners.TryRemove(position, out _);
        }

        return PortalTypesRegistry.ForBlock(block.RegistryId, owner);
    }

    /// <summary>Drops the claim on a position whose block is no longer the claimed portal block.</summary>
    internal static void Release(IChunk? chunk, Vector position, IBlock? block)
    {
        if (chunk is Chunk claimed
            && claimed.PortalOwners.TryGetValue(position, out var claim)
            && claim.Block != block?.UnlocalizedName)
            claimed.PortalOwners.TryRemove(position, out _);
    }

    internal static void Read(Chunk chunk, NbtCompound compound)
    {
        if (!compound.TryGetTag<NbtList>(NbtKey, out var claims))
            return;

        foreach (var claim in claims.OfType<NbtCompound>())
        {
            if (!claim.TryGetTag<NbtTag<string>>("type", out var type)
                || !claim.TryGetTag<NbtTag<string>>("block", out var block))
                continue;

            var position = new Vector(claim.GetInt("x"), claim.GetInt("y"), claim.GetInt("z"));
            chunk.PortalOwners[position] = new PortalOwner(type.Value!, block.Value!);
        }
    }

    internal static void Write(INbtWriter writer, Chunk chunk)
    {
        var claims = chunk.PortalOwners.ToArray();
        writer.WriteListStart(NbtKey, NbtTagType.Compound, claims.Length);
        foreach (var (position, claim) in claims)
        {
            writer.WriteCompoundStart();
            writer.WriteInt("x", position.X);
            writer.WriteInt("y", position.Y);
            writer.WriteInt("z", position.Z);
            writer.WriteString("type", claim.Type);
            writer.WriteString("block", claim.Block);
            writer.EndCompound();
        }

        writer.EndList();
    }
}
