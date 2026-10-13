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
var written = ModPorter.Write(output, destination, Console.Error);

Console.Write(report);

if (written == 0)
{
    Console.Error.WriteLine($"Nothing could be written to {destination}.");
    return 1;
}

return 0;
