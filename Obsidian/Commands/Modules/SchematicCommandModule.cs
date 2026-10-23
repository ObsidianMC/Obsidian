using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Nbt;
using Obsidian.Registries;
using System.IO;
using System.Net.Http;
using System.Threading;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// <c>/schematic</c>, which downloads a schematic and places its blocks: a Sponge schematic (<c>.schem</c>, versions 1
/// to 3) or a vanilla structure (<c>.nbt</c>). Block entities and entities aren't placed.
/// </summary>
[RequirePermissionLevel(4)]
public sealed class SchematicCommandModule : CommandModuleBase
{
    // The most it downloads, and the most blocks it places, so a link can't exhaust the server.
    private const int MaxDownloadBytes = 16 * 1024 * 1024;
    private const int MaxBlocks = 1 << 21;
    private static readonly TimeSpan downloadTimeout = TimeSpan.FromSeconds(30);

    [Command("schematic")]
    [CommandInfo("Places a schematic downloaded from a URL, its lowest corner at a position.", "/schematic <pos> <url>")]
    public async Task PlaceAsync(Vector pos, [Remaining] string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            await this.CommandContext.FailAsync("That is not an http or https URL");
            return;
        }

        Schematic schematic;
        try
        {
            schematic = Read(await DownloadAsync(uri));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException
            or InvalidOperationException or InvalidCastException or KeyNotFoundException or EndOfStreamException)
        {
            await this.CommandContext.FailAsync($"Could not load the schematic: {ex.Message}");
            return;
        }

        var max = pos + schematic.Size - new Vector(1, 1, 1);
        var level = LevelOf(this.CommandContext);
        for (var x = pos.X >> 4; x <= max.X >> 4; x++)
        for (var z = pos.Z >> 4; z <= max.Z >> 4; z++)
        {
            if (await level.GetChunkAsync(x, z, scheduleGeneration: false) is null)
            {
                await this.CommandContext.FailAsync("That area is not loaded");
                return;
            }
        }

        // Like a WorldEdit paste, without block updates, so sand doesn't fall and redstone doesn't fire while it's built.
        foreach (var (offset, block) in schematic.Blocks)
            await level.SetBlockAsync(pos + offset, block, doBlockUpdate: false);

        await this.CommandContext.SuccessAsync(
            $"Placed {schematic.Blocks.Count} block(s) of a {schematic.Size.X}x{schematic.Size.Y}x{schematic.Size.Z} schematic");
    }

    private static async Task<byte[]> DownloadAsync(Uri uri)
    {
        using var timeout = new CancellationTokenSource(downloadTimeout);
        using var response = await Globals.HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            throw new InvalidDataException("the file is larger than 16 MiB");

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > MaxDownloadBytes)
                throw new InvalidDataException("the file is larger than 16 MiB");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    internal static Schematic Read(byte[] data)
    {
        // Schematics are usually gzipped, but an uncompressed one reads as well.
        var compression = data is [0x1F, 0x8B, ..] ? NbtCompression.GZip : NbtCompression.None;
        var root = new NbtReader(new MemoryStream(data), compression).ReadNextTag() as NbtCompound
            ?? throw new InvalidDataException("it is not NBT");

        // Sponge's version 3 nests everything in a Schematic compound.
        if (root.TryGetTag<NbtCompound>("Schematic", out var nested))
            root = nested;

        if (root.TryGetTag<NbtTag<short>>("Width", out _))
            return ReadSponge(root);
        if (root.TryGetTag<NbtList>("blocks", out _))
            return ReadStructure(root);

        throw new InvalidDataException("it is neither a Sponge schematic nor a structure");
    }

    // Sponge's format: a palette of state strings, and a varint index into it for every block, X fastest, then Z, then Y.
    private static Schematic ReadSponge(NbtCompound root)
    {
        var size = new Vector((ushort)root.GetShort("Width"), (ushort)root.GetShort("Height"), (ushort)root.GetShort("Length"));
        CheckSize(size);

        var container = root.TryGetTag<NbtCompound>("Blocks", out var blocks) ? blocks : root;
        var paletteTag = container.TryGetTag<NbtCompound>("Palette", out var palette)
            ? palette
            : throw new InvalidDataException("it has no palette");
        var dataTag = container.TryGetTag("Data", out var data) || container.TryGetTag("BlockData", out data)
            ? data
            : throw new InvalidDataException("it has no block data");

        var states = new Dictionary<int, IBlock>();
        foreach (var (name, index) in paletteTag)
            states[((NbtTag<int>)index).Value] = ParseState(name);

        var bytes = dataTag switch
        {
            NbtArray<byte> unsigned => unsigned.GetArray(),
            NbtArray<sbyte> signed => signed.GetArray().Select(value => (byte)value).ToArray(),
            _ => throw new InvalidDataException("its block data is not a byte array")
        };

        var placed = new List<(Vector, IBlock)>();
        var position = 0;
        for (var i = 0; i < size.X * size.Y * size.Z; i++)
        {
            var value = 0;
            var shift = 0;
            byte current;
            do
            {
                if (position >= bytes.Length)
                    throw new InvalidDataException("its block data ends early");
                current = bytes[position++];
                value |= (current & 0x7F) << shift;
                shift += 7;
                if (shift > 35)
                    throw new InvalidDataException("its block data has a malformed varint");
            }
            while ((current & 0x80) != 0);

            if (!states.TryGetValue(value, out var state))
                throw new InvalidDataException($"its block data names palette entry {value}, which it doesn't have");

            var x = i % size.X;
            var z = i / size.X % size.Z;
            var y = i / (size.X * size.Z);
            placed.Add((new Vector(x, y, z), state));
        }

        return new Schematic(size, placed);
    }

    // Vanilla's structure format: a palette of named states with properties, and each block's position and state.
    private static Schematic ReadStructure(NbtCompound root)
    {
        var sizeList = root.TryGetTag<NbtList>("size", out var list) ? list : throw new InvalidDataException("it has no size");
        var size = ReadVector(sizeList);
        CheckSize(size);

        var paletteList = root.TryGetTag<NbtList>("palette", out var palette) ? palette
            : root.TryGetTag<NbtList>("palettes", out var palettes) && palettes.Count > 0 ? (NbtList)palettes[0]
            : throw new InvalidDataException("it has no palette");

        var states = new List<IBlock>(paletteList.Count);
        foreach (NbtCompound entry in paletteList)
        {
            var properties = entry.TryGetTag<NbtCompound>("Properties", out var tag)
                ? tag.ToDictionary(property => property.Key, property => ((NbtTag<string>)property.Value).Value!)
                : null;
            states.Add(BlockState(entry.GetString("Name")!, properties));
        }

        var placed = new List<(Vector, IBlock)>();
        foreach (NbtCompound entry in (NbtList)root["blocks"])
        {
            var state = entry.GetInt("state");
            if (state < 0 || state >= states.Count)
                throw new InvalidDataException($"a block names palette entry {state}, which it doesn't have");

            var position = ReadVector((NbtList)entry["pos"]);
            if (position.X < 0 || position.Y < 0 || position.Z < 0 || position.X >= size.X || position.Y >= size.Y || position.Z >= size.Z)
                throw new InvalidDataException("a block lies outside its size");

            placed.Add((position, states[state]));
        }

        return new Schematic(size, placed);
    }

    private static Vector ReadVector(NbtList list) => new(
        ((NbtTag<int>)list[0]).Value, ((NbtTag<int>)list[1]).Value, ((NbtTag<int>)list[2]).Value);

    private static void CheckSize(Vector size)
    {
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw new InvalidDataException("it is empty");
        if ((long)size.X * size.Y * size.Z > MaxBlocks)
            throw new InvalidDataException($"it is larger than {MaxBlocks} blocks");
    }

    // A state as Sponge writes it, like minecraft:oak_stairs[facing=north,half=bottom].
    private static IBlock ParseState(string state)
    {
        var bracket = state.IndexOf('[');
        if (bracket < 0)
            return BlockState(state, null);

        if (!state.EndsWith(']'))
            throw new InvalidDataException($"{state} is not a block state");

        var properties = new Dictionary<string, string>();
        foreach (var property in state[(bracket + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = property.IndexOf('=');
            if (equals < 0)
                throw new InvalidDataException($"{state} is not a block state");
            properties[property[..equals].Trim()] = property[(equals + 1)..].Trim();
        }

        return BlockState(state[..bracket], properties);
    }

    private static IBlock BlockState(string name, IReadOnlyDictionary<string, string>? properties) =>
        BlockStateProperties.TryGetState(name.Contains(':') ? name : $"minecraft:{name}", properties, out var state)
            ? state
            : throw new InvalidDataException($"{name} is not a block");

    internal sealed record Schematic(Vector Size, List<(Vector Offset, IBlock Block)> Blocks);
}
