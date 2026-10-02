using System.Runtime.InteropServices;

namespace Obsidian.Console;

/// <summary>Uses the terminal's normal screen buffer; never enables an alternate screen.</summary>
public sealed partial class ConsoleDevice : IConsoleDevice, IDisposable
{
    private const uint VirtualTerminalProcessing = 0x0004;

    private readonly nint outputHandle;

    private readonly bool restoreOutputMode;

    public ConsoleDevice()
    {
        this.IsInteractive = !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected
            && Environment.GetEnvironmentVariable("TERM") != "dumb";

        if (OperatingSystem.IsWindows() && !System.Console.IsOutputRedirected)
        {
            this.outputHandle = GetStdHandle(-11);

            if (GetConsoleMode(this.outputHandle, out uint mode) != 0)
            {
                this.restoreOutputMode = (mode & VirtualTerminalProcessing) == 0
                    && SetConsoleMode(this.outputHandle, mode | VirtualTerminalProcessing) != 0;
                this.IsInteractive &= (mode & VirtualTerminalProcessing) != 0 || this.restoreOutputMode;
            }
            else
                this.IsInteractive = false;
        }
    }

    public bool IsInteractive { get; }

    public bool IsErrorRedirected => System.Console.IsErrorRedirected;

    public int Width => Math.Max(2, System.Console.WindowWidth);

    public bool KeyAvailable => System.Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey() => System.Console.ReadKey(intercept: true);

    public void Write(string text, bool standardError = false) =>
        (standardError ? System.Console.Error : System.Console.Out).Write(text);

    public void Dispose()
    {
        if (this.restoreOutputMode && GetConsoleMode(this.outputHandle, out uint mode) != 0)
            _ = SetConsoleMode(this.outputHandle, mode & ~VirtualTerminalProcessing);
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll")]
    private static partial int GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll")]
    private static partial int SetConsoleMode(nint handle, uint mode);
}
