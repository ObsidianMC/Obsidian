using System.Reflection;

public partial class Program
{
    private static async ValueTask GenerateConfigFiles()
    {
        const string path = "config";

        Directory.CreateDirectory(path);

        var serverJsonFile = Path.Combine(path, "server.json");
        var whitelistJsonFile = Path.Combine(path, "whitelist.json");

        if (!File.Exists(serverJsonFile))
        {
            await using var file = File.Create(serverJsonFile);

            await using var embeddedFile = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.ConsoleApp.config.server.json");

            await embeddedFile!.CopyToAsync(file);
        }

        if (!File.Exists(whitelistJsonFile))
        {
            await using var file = File.Create(whitelistJsonFile);

            await using var embeddedFile = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.ConsoleApp.config.whitelist.json");

            await embeddedFile!.CopyToAsync(file);
        }
    }
}
