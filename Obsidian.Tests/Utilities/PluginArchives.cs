using Obsidian.Plugins.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.Tests.Utilities;

/// <summary>
/// Writes plugin archives in both formats, as Obsidian.MSBuild packs them, for tests.
/// </summary>
internal static class PluginArchives
{
    /// <summary>
    /// Writes a version 2 archive: a ZIP file of the given files, signed with <paramref name="key"/> if given.
    /// </summary>
    public static byte[] Zip(IReadOnlyDictionary<string, byte[]> files, RSA? key = null)
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

        if (key is null)
            return stream.ToArray();

        byte[] digest;
        using (var unsigned = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read))
            digest = ZipPluginArchive.ComputeDigest(unsigned.Entries);

        var signature = key.SignData(digest, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var entry = zip.CreateEntry(ZipPluginArchive.SignatureFileName).Open();
            entry.Write(signature);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Writes a version 1 archive with the header metadata Obsidian.MSBuild writes and stored (uncompressed) files.
    /// </summary>
    public static byte[] Legacy(string id, string version, string assembly, IReadOnlyDictionary<string, byte[]> files)
    {
        using var data = new MemoryStream();

        using (var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(assembly);
            writer.Write(version);
            writer.Write(id);
            writer.Write(id);
            writer.Write("Tester");
            writer.Write("A test plugin");
            writer.Write("https://example.com");
            writer.Write(0);

            writer.Write(files.Count);
            foreach (var (name, contents) in files)
            {
                writer.Write(name);
                writer.Write(contents.Length);
                writer.Write(contents.Length);
            }

            foreach (var (_, contents) in files)
                writer.Write(contents);
        }

        using var archive = new MemoryStream();

        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("OBBY"u8);
            writer.Write("1.0");
            writer.Write(SHA384.HashData(data.ToArray()));
            writer.Write(false);
            writer.Write((int)data.Length);
            writer.Write(data.ToArray());
        }

        return archive.ToArray();
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
