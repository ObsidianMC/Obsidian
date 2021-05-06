using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.IO.Console
{
    using Console = System.Console;

    public static partial class CommandLine
    {
        public static string CommandPrefix { get; set; }
        public static ConsoleColor ForegroundColor { get => Console.ForegroundColor; set => Console.ForegroundColor = value; }
        public static ConsoleColor BackgroundColor { get => Console.BackgroundColor; set => Console.BackgroundColor = value; }

        static CommandLine()
        {
            Console.CancelKeyPress += (sender, args) => args.Cancel = CancelKeyPress?.Invoke();
        }

        public static void TakeControlInternal()
        {

        }

        public static void ResetColor()
        {
            Console.ResetColor();
        }
    }
}
