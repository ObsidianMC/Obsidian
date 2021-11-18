using System;
using System.IO;

namespace Obsidian.IO.Console;

using Console = System.Console;

public static partial class CommandLine
{
    private static TextWriter consoleOut;

    private static partial void TakeControlInternal()
    {
        consoleOut = Console.Out;

        // Prevent Console interception
        Console.SetOut(TextWriter.Null);
        Console.SetIn(TextReader.Null);
        Console.SetError(TextWriter.Null);
    }

    public static partial void ResetColor()
    {
        Console.ResetColor();
    }

    public static partial void Write(string text)
    {
        Write(text.AsSpan());
    }

    public static partial void Write(ReadOnlySpan<char> text)
    {
        consoleOut.Write(text);
    }

    public static partial void WriteLine()
    {
        WriteLine(ReadOnlySpan<char>.Empty);
    }

    public static partial void WriteLine(string text)
    {
        WriteLine(text.AsSpan());
    }

    public static partial void WriteLine(ReadOnlySpan<char> text)
    {
        consoleOut.WriteLine(text);
    }

    private static partial void ChangeCommandPrefixInternal(string value)
    {
    }

    private static partial void SetForegroundColor(ConsoleColor color)
    {
        Console.ForegroundColor = color;
    }

    private static partial void SetBackgroundColor(ConsoleColor color)
    {
        Console.BackgroundColor = color;
    }
}
