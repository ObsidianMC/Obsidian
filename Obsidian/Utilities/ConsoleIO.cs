using System.Threading;

namespace Obsidian.Utilities;

/// <summary>
/// Allows simultaneous console input and output without breaking user input
/// (Without having this annoying behaviour : User inp[Some Console output]ut)
/// Provide some fancy features such as formatted output, text pasting and tab-completion.
/// By ORelio - (c) 2012-2018 - Available under the CDDL-1.0 license
/// source https://github.com/ORelio/Minecraft-Console-Client/blob/master/MinecraftClient/ConsoleIO.cs
/// 
/// Modified by GasInfinity -> Fix Linux Console Blocking - 18/05/2022
/// </summary>
public static class ConsoleIO
{
    private static IAutoComplete autocomplete_engine;
    private static LinkedList<string> autocomplete_words = new LinkedList<string>();
    private static LinkedList<string> previous = new LinkedList<string>();
    private static readonly object io_lock = new object();
    private static bool reading = false;
    private static string buffer = "";
    private static string buffer2 = "";

    /// <summary>
    /// Reset the IO mechanism and clear all buffers
    /// </summary>
    public static void Reset()
    {
        lock (io_lock)
        {
            if (reading)
            {
                ClearLineAndBuffer();
                reading = false;
                Console.Write("\b \b");
            }
        }
    }

    /// <summary>
    /// Set an auto-completion engine for TAB autocompletion.
    /// </summary>
    /// <param name="engine">Engine implementing the IAutoComplete interface</param>
    public static void SetAutoCompleteEngine(IAutoComplete engine)
    {
        autocomplete_engine = engine;
    }

    /// <summary>
    /// Determines whether to use interactive IO or basic IO.
    /// Set to true to disable interactive command prompt and use the default Console.Read|Write() methods.
    /// Color codes are printed as is when BasicIO is enabled.
    /// </summary>
    public static bool BasicIO = false;

    /// <summary>
    /// Determines whether not to print color codes in BasicIO mode.
    /// </summary>
    public static bool BasicIO_NoColor = false;



    /// <summary>
    /// Read a line from the standard input
    /// </summary>
    public static string ReadLine()
    {
        if (BasicIO || Console.IsInputRedirected)
        {
            return Console.ReadLine();
        }

        ConsoleKeyInfo k = new ConsoleKeyInfo();

        lock (io_lock)
        {
            Console.Write('>');
            reading = true;
            buffer = "";
            buffer2 = "";
        }

        while (k.Key != ConsoleKey.Enter)
        {
            while(!Console.KeyAvailable) 
            {
                // This doesn't introduce noticeable input lag. Maybe in the future we'll come with a better solution without any input lag?
                Thread.Sleep(50);
            }

            k = Console.ReadKey(true);

            lock (io_lock)
            {
                switch (k.Key)
                {
                    case ConsoleKey.Escape:
                        ClearLineAndBuffer();
                        break;
                    case ConsoleKey.Backspace:
                        RemoveOneChar();
                        break;
                    case ConsoleKey.Enter:
                        Console.Write('\n');
                        break;
                    case ConsoleKey.LeftArrow:
                        GoLeft();
                        break;
                    case ConsoleKey.RightArrow:
                        GoRight();
                        break;
                    case ConsoleKey.Home:
                        while (buffer.Length > 0) { GoLeft(); }
                        break;
                    case ConsoleKey.End:
                        while (buffer2.Length > 0) { GoRight(); }
                        break;
                    case ConsoleKey.Delete:
                        if (buffer2.Length > 0)
                        {
                            GoRight();
                            RemoveOneChar();
                        }
                        break;
                    case ConsoleKey.DownArrow:
                        if (previous.Count > 0)
                        {
                            ClearLineAndBuffer();
                            buffer = previous.First.Value;
                            previous.AddLast(buffer);
                            previous.RemoveFirst();
                            Console.Write(buffer);
                        }
                        break;
                    case ConsoleKey.UpArrow:
                        if (previous.Count > 0)
                        {
                            ClearLineAndBuffer();
                            buffer = previous.Last.Value;
                            previous.AddFirst(buffer);
                            previous.RemoveLast();
                            Console.Write(buffer);
                        }
                        break;
                    case ConsoleKey.Tab:
                        if (autocomplete_words.Count == 0 && autocomplete_engine != null && buffer.Length > 0)
                            foreach (string result in autocomplete_engine.AutoComplete(buffer))
                                autocomplete_words.AddLast(result);
                        string word_autocomplete = null;
                        if (autocomplete_words.Count > 0)
                        {
                            word_autocomplete = autocomplete_words.First.Value;
                            autocomplete_words.RemoveFirst();
                            autocomplete_words.AddLast(word_autocomplete);
                        }
                        if (!String.IsNullOrEmpty(word_autocomplete) && word_autocomplete != buffer)
                        {
                            while (buffer.Length > 0 && buffer[buffer.Length - 1] != ' ') { RemoveOneChar(); }
                            foreach (char c in word_autocomplete) { AddChar(c); }
                        }
                        break;
                    default:
                        if (k.KeyChar != 0)
                            AddChar(k.KeyChar);
                        break;
                }

                if (k.Key != ConsoleKey.Tab)
                    autocomplete_words.Clear();
            }
        }

        lock (io_lock)
        {
            reading = false;
            previous.AddLast(buffer + buffer2);
            return buffer + buffer2;
        }
    }

    /// <summary>
    /// Debug routine: print all keys pressed in the console
    /// </summary>
    public static void DebugReadInput()
    {
        ConsoleKeyInfo k = new ConsoleKeyInfo();
        while (true)
        {
            k = Console.ReadKey(true);
            Console.WriteLine("Key: {0}\tChar: {1}\tModifiers: {2}", k.Key, k.KeyChar, k.Modifiers);
        }
    }

    /// <summary>
    /// Write a string to the topmost line of the console.
    /// </summary>
    /// <param name="text">Status text.</param>
    internal static void UpdateStatusLine(string text)
    {
        if (!BasicIO)
        {
            try
            {
                lock (io_lock)
                {
                    // Update server stats on console
                    var oldPos = Console.GetCursorPosition();
                    var curWidth = Console.WindowWidth;
                    var curHeight = Console.WindowHeight;
                    var topLine = Math.Max(oldPos.Top - curHeight, 0) + 1;
                    var lineHorzOffset = curWidth - text.Length - 1;

                    Console.SetCursorPosition(lineHorzOffset, topLine);
                    Console.Write(new string(' ', text.Length)); // clear the line

                    Console.SetCursorPosition(lineHorzOffset, topLine);
                    Console.Write(text);

                    Console.SetCursorPosition(oldPos.Left, oldPos.Top);
                }
            }
            catch (Exception)
            {
                // disable status line
                BasicIO = true;
            }
        }
    }

    /// <summary>
    /// Write a string to the standard output, without newline character
    /// </summary>
    public static void Write(string text)
    {
        if (!BasicIO && !Console.IsOutputRedirected)
        {
            lock (io_lock)
            {
                if (reading)
                {
                    try
                    {
                        string buf = buffer;
                        string buf2 = buffer2;
                        ClearLineAndBuffer();
                        if (Console.CursorLeft == 0)
                        {
                            Console.CursorLeft = Console.BufferWidth - 1;
                            Console.CursorTop--;
                            Console.Write(' ');
                            Console.CursorLeft = Console.BufferWidth - 1;
                            Console.CursorTop--;
                        }
                        else Console.Write("\b \b");
                        Console.Write(text);
                        buffer = buf;
                        buffer2 = buf2;
                        Console.Write(">" + buffer);
                        if (buffer2.Length > 0)
                        {
                            Console.Write(buffer2 + " \b");
                            for (int i = 0; i < buffer2.Length; i++) { GoBack(); }
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        //Console resized: Try again
                        Console.Write('\n');
                        Write(text);
                    }
                }
                else Console.Write(text);
            }
        }
        else Console.Write(text);
    }

    /// <summary>
    /// Write a string to the standard output with a trailing newline
    /// </summary>
    public static void WriteLine(string line)
    {
        Write(line + '\n');
    }

    /// <summary>
    /// Write a single character to the standard output
    /// </summary>
    public static void Write(char c)
    {
        Write("" + c);
    }

    #region Subfunctions

    /// <summary>
    /// Clear all text inside the input prompt
    /// </summary>
    private static void ClearLineAndBuffer()
    {
        while (buffer2.Length > 0)
        {
            GoRight();
        }
        while (buffer.Length > 0)
        {
            RemoveOneChar();
        }
    }

    /// <summary>
    /// Remove one character on the left of the cursor in input prompt
    /// </summary>
    private static void RemoveOneChar()
    {
        if (buffer.Length > 0)
        {
            try
            {
                GoBack();
                Console.Write(' ');
                GoBack();
            }
            catch (ArgumentOutOfRangeException) { /* Console was resized!? */ }
            buffer = buffer.Substring(0, buffer.Length - 1);

            if (buffer2.Length > 0)
            {
                Console.Write(buffer2);
                Console.Write(' ');
                GoBack();
                for (int i = 0; i < buffer2.Length; i++)
                {
                    GoBack();
                }
            }
        }
    }

    /// <summary>
    /// Move the cursor one character to the left inside the console, regardless of input prompt state
    /// </summary>
    private static void GoBack()
    {
        try
        {
            if (Console.CursorLeft == 0)
            {
                Console.CursorLeft = Console.BufferWidth - 1;
                if (Console.CursorTop > 0)
                    Console.CursorTop--;
            }
            else
            {
                Console.CursorLeft = Console.CursorLeft - 1;
            }
        }
        catch (ArgumentOutOfRangeException) { /* Console was resized!? */ }
    }

    /// <summary>
    /// Move the cursor one character to the left in input prompt, adjusting buffers accordingly
    /// </summary>
    private static void GoLeft()
    {
        if (buffer.Length > 0)
        {
            buffer2 = "" + buffer[buffer.Length - 1] + buffer2;
            buffer = buffer.Substring(0, buffer.Length - 1);
            GoBack();
        }
    }

    /// <summary>
    /// Move the cursor one character to the right in input prompt, adjusting buffers accordingly
    /// </summary>
    private static void GoRight()
    {
        if (buffer2.Length > 0)
        {
            buffer = buffer + buffer2[0];
            Console.Write(buffer2[0]);
            buffer2 = buffer2.Substring(1);
        }
    }

    /// <summary>
    /// Insert a new character in the input prompt
    /// </summary>
    /// <param name="c">New character</param>
    private static void AddChar(char c)
    {
        Console.Write(c);
        buffer += c;
        Console.Write(buffer2);
        for (int i = 0; i < buffer2.Length; i++)
        {
            GoBack();
        }
    }

    #endregion
}


/// <summary>
/// Interface for TAB autocompletion
/// Allows to use any object which has an AutoComplete() method using the IAutocomplete interface
/// </summary>
public interface IAutoComplete
{
    /// <summary>
    /// Provide a list of auto-complete strings based on the provided input behing the cursor
    /// </summary>
    /// <param name="behindCursor">Text behind the cursor, e.g. "my input comm"</param>
    /// <returns>List of auto-complete words, e.g. ["command", "comment"]</returns>
    IEnumerable<string> AutoComplete(string behindCursor);
}

