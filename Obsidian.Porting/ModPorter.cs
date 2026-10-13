using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.Porting;

/// <summary>
/// Ports a NeoForge mod's data and metadata to Obsidian's formats where they differ, copies the rest of its data and
/// assets as they are, and reports what it couldn't port.
/// </summary>
public static class ModPorter
{
    /// <summary>
    /// The report's file name in the output folder.
    /// </summary>
    public const string ReportFile = "porting-report.txt";

    private static readonly JsonSerializerOptions writeOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Reads a mod: a folder laid out like its jar, or the jar (a zip file).
    /// </summary>
    /// <param name="path">The folder or jar.</param>
    /// <returns>The mod's files by path, with <c>/</c> separators.</returns>
    public static SortedDictionary<string, byte[]> ReadMod(string path)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                files[Path.GetRelativePath(path, file).Replace('\\', '/')] = File.ReadAllBytes(file);

            return files;
        }

        using var archive = ZipFile.OpenRead(path);

        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/')))
        {
            using var stream = entry.Open();
            using var contents = new MemoryStream();

            stream.CopyTo(contents);
            files[entry.FullName.Replace('\\', '/')] = contents.ToArray();
        }

        return files;
    }

    /// <summary>
    /// Ports a mod's files.
    /// </summary>
    /// <param name="files">The mod's files by path, as <see cref="ReadMod"/> reads them.</param>
    /// <returns>The output's files by path, the report among them, and the report.</returns>
    public static (SortedDictionary<string, byte[]> Output, PortingReport Report) Port(
        IReadOnlyDictionary<string, byte[]> files)
    {
        var output = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var report = new PortingReport();
        var biomeModifiers = new List<KeyValuePair<string, byte[]>>();
        var classes = 0;

        foreach (var (path, data) in files)
        {
            if (path.EndsWith(".class", StringComparison.Ordinal))
            {
                classes++;
                continue;
            }

            if (path is "META-INF/neoforge.mods.toml" or "META-INF/mods.toml")
            {
                PortMetadata(path, data, files, output, report);
                continue;
            }

            if (IsBiomeModifier(path))
            {
                biomeModifiers.Add(KeyValuePair.Create(path, data));
                continue;
            }

            if (NeoForgeOnly(path) is string reason)
            {
                report.Skip(path, reason);
                continue;
            }

            if (!path.StartsWith("data/", StringComparison.Ordinal)
                && !path.StartsWith("assets/", StringComparison.Ordinal))
            {
                // The jar's manifest, pack.mcmeta, mixin configs, access transformers, logos and the like.
                if (!path.StartsWith("META-INF/MANIFEST.MF", StringComparison.Ordinal))
                    report.Skip(path, "it isn't data or assets, which is all an Obsidian plugin's archive holds");

                continue;
            }

            if (HasConditions(path, data))
            {
                report.Skip(path, "it has NeoForge load conditions, which Obsidian doesn't read");
                continue;
            }

            output[path] = data;
            report.Copied.Add(path);
        }

        BiomeModifiers.Port(biomeModifiers, output, report);

        if (classes > 0)
        {
            report.Notes.Add(
                $"The mod's Java code can't be ported ({classes} .class files): write its blocks, items, entities, "
                + "events and commands again as an Obsidian plugin in C# (see docs/porting-from-neoforge.md).");
        }

        output[ReportFile] = Encoding.UTF8.GetBytes(report.ToString());

        return (output, report);
    }

    private static void PortMetadata(
        string path,
        byte[] data,
        IReadOnlyDictionary<string, byte[]> files,
        SortedDictionary<string, byte[]> output,
        PortingReport report)
    {
        var jarVersion = files.TryGetValue("META-INF/MANIFEST.MF", out var manifest)
            ? ManifestVersion(Encoding.UTF8.GetString(manifest))
            : null;

        if (ModMetadata.Convert(Encoding.UTF8.GetString(data), jarVersion, path, report) is not JsonObject metadata)
            return;

        output["obsidian.mod.json"] = JsonSerializer.SerializeToUtf8Bytes(metadata, writeOptions);
        report.Converted.Add($"{path} -> obsidian.mod.json");
    }

    // The Implementation-Version of a jar's manifest, which NeoForge's ${file.jarVersion} stands for.
    private static string? ManifestVersion(string manifest) => manifest
        .Split('\n')
        .Select(line => line.TrimEnd('\r'))
        .Where(line => line.StartsWith("Implementation-Version:", StringComparison.Ordinal))
        .Select(line => line["Implementation-Version:".Length..].Trim())
        .FirstOrDefault();

    // data/<ns>/neoforge/biome_modifier/<path>.json
    private static bool IsBiomeModifier(string path)
    {
        var parts = path.Split('/', 3);

        return parts is ["data", _, var rest]
            && rest.StartsWith(BiomeModifiers.NeoForgeFolder, StringComparison.Ordinal)
            && path.EndsWith(".json", StringComparison.Ordinal);
    }

    // Why a NeoForge-only data file can't be ported, or null for one in vanilla's formats.
    private static string? NeoForgeOnly(string path)
    {
        if (path.Split('/') is not ["data", var ns, var folder, var subfolder, ..])
            return null;

        if (ns == "neoforge" || folder == "loot_modifiers")
        {
            return folder switch
            {
                "data_maps" => "NeoForge's data maps have no Obsidian equivalent",
                "loot_modifiers" => "NeoForge's global loot modifiers have no Obsidian equivalent",
                _ => "NeoForge's own data has no Obsidian equivalent"
            };
        }

        if (folder == "data_maps")
            return "NeoForge's data maps have no Obsidian equivalent";

        if (folder != "neoforge")
            return null;

        return subfolder == "structure_modifier"
            ? "NeoForge's structure modifiers have no Obsidian equivalent"
            : "NeoForge's own data has no Obsidian equivalent";
    }

    // Whether a JSON file has NeoForge's load conditions, which decide whether NeoForge loads it.
    private static bool HasConditions(string path, byte[] data)
    {
        if (!path.EndsWith(".json", StringComparison.Ordinal))
            return false;

        try
        {
            return JsonNode.Parse(Utf8(data), documentOptions: documentOptions) is JsonObject root
                && root.ContainsKey("neoforge:conditions");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the output of <see cref="Port"/> to a folder, leaving out any file whose path points outside it.
    /// </summary>
    /// <param name="output">The output's files by path.</param>
    /// <param name="destination">The folder, with or without a trailing separator.</param>
    /// <param name="errors">Gets a line for each file left out.</param>
    /// <returns>How many files were written.</returns>
    public static int Write(IReadOnlyDictionary<string, byte[]> output, string destination, TextWriter errors)
    {
        // One separator at the end, so that the check below only accepts paths inside the folder.
        var folder = Path.GetFullPath(destination);
        var root = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        var written = 0;

        foreach (var (path, data) in output)
        {
            var file = Path.GetFullPath(Path.Join(root, path));

            // A jar's entry names are untrusted: none may write outside the output folder.
            if (!file.StartsWith(root, StringComparison.Ordinal))
            {
                errors.WriteLine($"Left out {path}: it points outside the output folder.");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, data);
            written++;
        }

        return written;
    }

    /// <summary>
    /// A file's bytes without a UTF-8 byte order mark, which JSON readers refuse.
    /// </summary>
    internal static ReadOnlySpan<byte> Utf8(byte[] data) =>
        data.AsSpan().StartsWith("﻿"u8) ? data.AsSpan(3) : data;
}
