using Microsoft.Extensions.Logging;
using Obsidian.Nbt;
using System.IO;

namespace Obsidian.WorldData;

/// <summary>
/// Reads and writes a player's data files (a gzipped compound) like vanilla's <c>PlayerDataStorage</c>: a save is written
/// to a temporary file that then replaces the file, keeping the previous one as a backup named like vanilla's
/// (<c>&lt;uuid&gt;.dat_old</c>), and a file that's missing or can't be read is read from that backup.
/// </summary>
/// <remarks>
/// Writes of the same file mustn't overlap; <see cref="Entities.Player.SaveAsync"/> runs a player's saves one at a time.
/// </remarks>
internal static partial class PlayerDataFile
{
    public static string BackupPath(string path) => $"{path}_old";

    /// <summary>
    /// The compound in <paramref name="path"/> or its backup, or <c>null</c> when neither can be read.
    /// </summary>
    public static NbtCompound? Read(string path, ILogger logger) => ReadFile(path, logger) ?? ReadFile(BackupPath(path), logger);

    public static async Task WriteAsync(string path, NbtCompound data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await using var writer = new NbtWriterStream(stream, NbtCompression.GZip, "");

                foreach (var (_, tag) in data)
                    writer.WriteTag(tag);

                writer.EndCompound();
                await writer.TryFinishAsync();
            }

            if (File.Exists(path))
                File.Replace(temporaryPath, path, BackupPath(path));
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static NbtCompound? ReadFile(string path, ILogger logger)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            // Shared for deletion, so a save can replace the file while it's read.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new NbtReader(stream, NbtCompression.GZip).ReadNextTag() as NbtCompound;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException
            or Nbt.Exceptions.NbtException or System.Diagnostics.UnreachableException)
        {
            Log.UnreadablePlayerData(logger, path, ex);
            return null;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Can't read the player data in {Path}")]
        public static partial void UnreadablePlayerData(ILogger logger, string path, Exception exception);
    }
}
