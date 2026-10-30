using Obsidian.Nbt;
using Obsidian.WorldData;
using System.IO;

namespace Obsidian.Commands;

/// <summary>Vanilla's per-namespace command storage, committed eagerly so it survives a server restart.</summary>
internal static class CommandStorage
{
    private static readonly object gate = new();

    internal static NbtCompound Read(IServer server, string identifier)
    {
        var id = Parse(identifier);
        lock (gate)
        {
            var payload = SavedDataFile.Read(PathOf(server, id.Namespace));
            if (payload?.TryGetTag<NbtCompound>("contents", out var contents) == true
                && contents.TryGetTag<NbtCompound>(id.Path, out var data))
            {
                return DataBlockEntity.Copy(data);
            }

            return new NbtCompound();
        }
    }

    internal static void Write(IServer server, string identifier, NbtCompound data)
    {
        var id = Parse(identifier);
        lock (gate)
        {
            var path = PathOf(server, id.Namespace);
            var payload = SavedDataFile.Read(path) ?? new NbtCompound();
            if (!payload.TryGetTag<NbtCompound>("contents", out var contents))
            {
                contents = new NbtCompound("contents");
                payload.Add(contents);
            }

            if (data.Count == 0)
            {
                contents.Remove(id.Path);
            }
            else
            {
                var copy = DataBlockEntity.Copy(data);
                copy.Name = id.Path;
                contents.Remove(id.Path);
                contents.Add(copy);
            }

            SavedDataFile.Write(path, payload);
        }
    }

    private static ResourceLocation Parse(string identifier)
    {
        var qualified = CommandInput.Qualify(identifier);
        var parts = qualified.Split(':', 2);
        return new ResourceLocation(parts[0], parts[1]);
    }

    private static string PathOf(IServer server, string name)
    {
        if (server.DefaultWorld is not World world || string.IsNullOrEmpty(world.WorldFolderPath))
            throw new FormatException("Command storage requires a saved world");

        if (name is "." or "..")
            throw new FormatException("This storage namespace cannot be represented on disk");

        return WorldLayout.SavedDataPath(world.WorldFolderPath, $"{name}:command_storage");
    }
}
