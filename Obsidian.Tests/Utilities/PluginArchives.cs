using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Obsidian.Tests.Utilities;

/// <summary>
/// Writes plugin archives in both formats, as Obsidian.MSBuild packs them, for tests.
/// </summary>
internal static class PluginArchives
{
    /// <summary>
    /// Writes a version 2 archive: a ZIP file of the given files.
    /// </summary>
    public static byte[] Zip(IReadOnlyDictionary<string, byte[]> files)
    {
        using var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in files)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(contents);
            }
        }

        return stream.ToArray();
    }

    public static byte[] Metadata(string id, string version, string assembly, string dependencies = "[]") =>
        Encoding.UTF8.GetBytes($$"""
            {
              "modId": "{{id}}",
              "displayName": "{{id}} display",
              "version": "{{version}}",
              "assembly": "{{assembly}}",
              "side": "both",
              "dependencies": {{dependencies}}
            }
            """);
}
