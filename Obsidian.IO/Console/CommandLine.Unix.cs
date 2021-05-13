using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.IO.Console
{
    using Console = System.Console;

    public static partial class CommandLine
    {
        public static ConsoleColor ForegroundColor { get => Console.ForegroundColor; set => Console.ForegroundColor = value; }
        public static ConsoleColor BackgroundColor { get => Console.BackgroundColor; set => Console.BackgroundColor = value; }

        private static TextWriter consoleOut;

        static CommandLine()
        {
            Console.CancelKeyPress += (sender, args) => args.Cancel = CancelKeyPress?.Invoke();
        }

        public static void TakeControlInternal()
        {
            consoleOut = Console.Out;

            // Prevent Console interception
            Console.SetOut(TextWriter.Null);
            Console.SetIn(TextReader.Null);
            Console.SetError(TextWriter.Null);
        }

        public static void ResetColor()
        {
            Console.ResetColor();
        }

        public static void Write(string text)
        {
            Write(text.AsSpan());
        }

        public static void Write(ReadOnlySpan<char> text)
        {
            consoleOut.Write(text);
        }

        public static void WriteLine()
        {
            WriteLine(ReadOnlySpan<char>.Empty);
        }

        public static void WriteLine(string text)
        {
            WriteLine(text.AsSpan());
        }

        public static void WriteLine(ReadOnlySpan<char> text)
        {
            consoleOut.WriteLine(text);
        }

        private static void ChangeCommandPrefixInternal(string value)
        {

        }
    }
}
