using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    /// <summary>
    /// What the join and respawn packets tell the client about the player's level and game mode, like vanilla's
    /// <c>CommonPlayerSpawnInfo</c>.
    /// </summary>
    /// <remarks>
    /// Obsidian doesn't keep the game mode a player had before, so it sends none, as vanilla does for a player whose
    /// game mode never changed.
    /// </remarks>
    internal CommonPlayerSpawnInfo CreateSpawnInfo() => new()
    {
        DimensionType = this.Level.DimensionType.Id,
        DimensionName = this.Level.DimensionName,
        HashedSeed = HashSeed(this.Level.LevelData.RandomSeed),
        GameMode = this.GameMode,
        Flat = (this.Level as AbstractLevel)?.Generator is SuperflatGenerator,
        PortalCooldown = this.portalCooldown,
        SeaLevel = SeaLevelOf(this.Level)
    };

    /// <summary>
    /// The seed clients get, which their biome lookups use: the first 8 bytes of the SHA-256 hash of the seed's
    /// little-endian bytes, read little-endian, like vanilla's <c>BiomeManager.obfuscateSeed</c>, so that biome colors
    /// blend the way vanilla's clients blend them.
    /// </summary>
    internal static long HashSeed(long seed)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, seed);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    // The sea level clients draw the horizon at: the generator's, -63 for a superflat level like vanilla's flat
    // generator, or the overworld's.
    private static int SeaLevelOf(ILevel level) => (level as AbstractLevel)?.Generator switch
    {
        MojangGenerator generator => generator.SeaLevel,
        SuperflatGenerator => -63,
        _ => 63
    };
}
