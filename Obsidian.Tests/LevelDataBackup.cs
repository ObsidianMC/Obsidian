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
}
