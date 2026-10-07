using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Nbt;
using Obsidian.WorldData;
using System;
using System.IO;
using Xunit;

namespace Obsidian.Tests;

public class LevelDataBackup
{
    [Fact(DisplayName = "An unreadable level.dat reads as missing, so loading falls back to its backup")]
    public void TruncatedLevelDataIsUnreadable()
    {
        var path = Path.Join(Path.GetTempPath(), $"obsidian-level-{Guid.NewGuid():N}.dat");
        try
        {
            using (var file = File.Create(path))
            using (var writer = new NbtWriterStream(file, NbtCompression.GZip, ""))
            {
                writer.WriteString("LevelName", "world");
                writer.EndCompound();
                writer.TryFinish();
            }
            Assert.Equal("world", World.ReadLevelData(path, NullLogger.Instance)!.GetString("LevelName"));

            // A save that stopped partway: the first half of the file.
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
            Assert.Null(World.ReadLevelData(path, NullLogger.Instance));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "Level data falls back to vanilla's level.dat_old, then Obsidian's level.dat.old")]
    public void BackupsAreReadInOneOrder()
    {
        var folder = Directory.CreateTempSubdirectory("obsidian-level-").FullName;
        try
        {
            var levelDat = Path.Join(folder, "level.dat");
            Assert.Null(World.ReadLevelDataOrBackup(levelDat, NullLogger.Instance));

            File.WriteAllBytes(levelDat, [1, 2, 3]);
            Assert.Throws<InvalidDataException>(() => World.ReadLevelDataOrBackup(levelDat, NullLogger.Instance));

            WriteLevelData($"{levelDat}.old", "obsidian backup");
            Assert.Equal("obsidian backup", ReadName(levelDat));

            WriteLevelData($"{levelDat}_old", "vanilla backup");
            Assert.Equal("vanilla backup", ReadName(levelDat));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        static string ReadName(string levelDat) =>
            World.ReadLevelDataOrBackup(levelDat, NullLogger.Instance)!.GetString("LevelName");
    }

    private static void WriteLevelData(string path, string levelName)
    {
        using var file = File.Create(path);
        using var writer = new NbtWriterStream(file, NbtCompression.GZip, "");

        writer.WriteString("LevelName", levelName);
        writer.EndCompound();
        writer.TryFinish();
    }
}
