using Obsidian.Porting;

// Ports a NeoForge mod's data and metadata to Obsidian's formats:
//
//   dotnet run --project Obsidian.Porting -- <mod folder or jar> <output folder>
//
// The output folder gets obsidian.mod.json, the mod's data/ and assets/ (NeoForge's biome modifiers converted to
// Obsidian's) and porting-report.txt, which lists what couldn't be ported. See docs/porting-from-neoforge.md.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Obsidian.Porting <mod folder or jar> <output folder>");
    return 1;
}

var (source, destination) = (args[0], args[1]);

if (!File.Exists(source) && !Directory.Exists(source))
{
    Console.Error.WriteLine($"{source} isn't a file or folder.");
    return 1;
}

if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
{
    Console.Error.WriteLine($"{destination} isn't empty; give a new or empty folder.");
    return 1;
}

var (output, report) = ModPorter.Port(ModPorter.ReadMod(source));
var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

foreach (var (path, data) in output)
{
    var file = Path.GetFullPath(Path.Join(root, path));

    // A jar's entry names are untrusted: none may write outside the output folder.
    if (!file.StartsWith(root, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"Left out {path}: it points outside the output folder.");
        continue;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllBytes(file, data);
}

Console.Write(report);

return 0;
