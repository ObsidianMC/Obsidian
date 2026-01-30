using System.Reflection;

namespace Obsidian.GuiConsole;

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

            await using var embeddedFile = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.GuiConsole.config.server.json");

            await embeddedFile!.CopyToAsync(file);
        }

        if (!File.Exists(whitelistJsonFile))
        {
            await using var file = File.Create(whitelistJsonFile);

            await using var embeddedFile = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.GuiConsole.config.whitelist.json");

            await embeddedFile!.CopyToAsync(file);
        }
    }

    public static void DrawLogo()
    {
        const string asciilogo =
            "\n" +
            "      ▄▄▄▄   ▄▄       ▄▄▄▄  ▀   ▄▄▄   ▐ ▄ \n" +
            "      ▐█ ▀█ ▐█ ▀  ██ ██  ██ ██ ▐█ ▀█  █▌▐█\n" +
            " ▄█▀▄ ▐█▀▀█▄▄▀▀▀█▄▐█ ▐█  ▐█▌▐█ ▄█▀▀█ ▐█▐▐▌\n" +
            "▐█▌ ▐▌██▄ ▐█▐█▄ ▐█▐█▌██  ██ ▐█▌▐█  ▐▌██▐█▌\n" +
            " ▀█▄▀  ▀▀▀▀  ▀▀▀▀ ▀▀▀▀▀▀▀▀  ▀▀▀ ▀  ▀ ▀▀ █ \n\n";

        Console.Title = $"Obsidian for {ServerConstants.DefaultProtocol} ({ServerConstants.VERSION})";
        Console.BackgroundColor = ConsoleColor.White;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.CursorVisible = false;
        Console.WriteLine(asciilogo);
        Console.ResetColor();
    }
}
