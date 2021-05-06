using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Obsidian.IO.Console
{
    using ColoredChar = NativeMethods.ColoredChar;
    using Console = System.Console;
    using Coordinate = NativeMethods.Coordinate;
    using HandlerRoutine = NativeMethods.HandlerRoutine;
    using Rect = NativeMethods.Rect;

    public static partial class CommandLine
    {
        // Windows-only implementation of CommandLine

        public static ConsoleColor ForegroundColor
        {
            get
            {
                return (ConsoleColor)(color & 0b1111);
            }

            set
            {
                color = (short)((color & 0b11110000) | (int)value);
            }
        }

        public static ConsoleColor BackgroundColor
        {
            get
            {
                return (ConsoleColor)(color >> 4);
            }

            set
            {
                color = (short)((color & 0b1111) | ((int)value << 4));
            }
        }

        private static short color = (short)ConsoleColor.Gray;

        private static SafeFileHandle handle;
        private static HandlerRoutine ctrlHandler; // Keep managed reference to prevent garbage collection

        private static readonly int maxCommandMemory = 50;
        private static readonly LinkedList<string> commandMemory = new();
        private static LinkedListNode<string>? selectedMemorizedCommand;

        private static readonly string newLine = "\n";

        /*
         * Messages are kept in a circular buffer, that can be scrolled vertically.
         * When buffer is full, old messages get rewritten. The reason for keeping
         * messages in a fixed area is to simply implement separated, independent
         * input in its own area. Input buffer can be wider than window width and
         * can be scrolled horizontally.
         * 
         * Head and tail specify start and end of content in a circular buffer.
         * 
         * messageHead and messageTail specify start and end of content shown in the
         * output area. messagesTop and messagesHeight specify the output area.
         * 
         * To show if whether there are messages preceding or following the shown area,
         * lines with arrows are drawn. arrowXDrawn variables make sure not to overwrite
         * this line unless necessary.
        */

        private static ColoredChar[] buffer;
        private static Coordinate bufferSize;
        private static int bufferIndex;
        private static int width;
        private static int height;
        private static int head;
        private static int tail;

        private static ColoredChar[] inputBuffer;
        private static Coordinate inputBufferSize;
        private static int inputLength;
        private static int cursorPosition;
        private static int inputLeft;
        private static Rect inputRect;

        private static int messageHead;
        private static int messageTail;
        private static int messagesTop;
        private static int messagesHeight;

        private static ColoredChar[] arrowUp;
        private static ColoredChar[] arrowDown;
        private static bool arrowUpDrawn;
        private static bool arrowDownDrawn;

#pragma warning disable CS8618 // Initialization happens inside TakeControlInternal()
        static CommandLine()
        {
            ctrlHandler = HandleCtrlKeyPress;
            NativeMethods.SetConsoleCtrlHandler(ctrlHandler, true);
        }
#pragma warning restore CS8618

        private static void TakeControlInternal()
        {
            // Prevent Console interception
            Console.SetOut(TextWriter.Null);
            Console.SetIn(TextReader.Null);
            Console.SetError(TextWriter.Null);

            width = Console.BufferWidth;
            height = Console.BufferHeight;

            messagesTop = 0;
            messagesHeight = Console.WindowHeight - 1;

            Debug.Assert(Console.BufferWidth == Console.WindowWidth);
            Debug.Assert(height > messagesHeight);

            bufferIndex = 0;
            head = 0;
            tail = messagesHeight;
            messageHead = 0;
            messageTail = tail;

            // Setup input
            inputBuffer = new ColoredChar[160];
            inputBufferSize = new Coordinate(inputBuffer.Length, 1);
            inputRect = new Rect(0, Console.WindowHeight - 1, width, Console.WindowHeight - 1);
            inputLength = 0;
            inputLeft = 0;
            cursorPosition = 0;

            // Setup arrows
            arrowUp = new ColoredChar[width];
            arrowDown = new ColoredChar[width];
            for (int i = 0; i < width; i++)
            {
                arrowUp[i] = ColoredChar.Empty;
                arrowDown[i] = ColoredChar.Empty;
            }
            arrowUp[width / 2] = new ColoredChar('↑', (short)ConsoleColor.Cyan);
            arrowDown[width / 2] = new ColoredChar('↓', (short)ConsoleColor.Cyan);
            arrowUpDrawn = false;
            arrowDownDrawn = false;

            // Setup buffer
            buffer = new ColoredChar[width * height];
            bufferSize = new Coordinate(width, height);
            handle = NativeMethods.CreateFile("CONOUT$", 0x40000000, 2, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

            RedrawMessages();
            StartReadingInput();
        }

        public static void ResetColor()
        {
            color = (short)ConsoleColor.Gray;
        }

        #region Writing output
        public static void Write(string? text)
        {
            if (text is null)
                return;

            WriteInternal(text);
            RedrawMessages();
        }

        public static void Write(ReadOnlySpan<char> text)
        {
            WriteInternal(text);
            RedrawMessages();
        }

        public static void WriteLine()
        {
            WriteInternal(newLine);
            RedrawMessages();
        }

        public static void WriteLine(string? text)
        {
            if (text is null)
                return;

            WriteInternal(text);
            WriteInternal(newLine);
            RedrawMessages();
        }

        public static void WriteLine(ReadOnlySpan<char> text)
        {
            WriteInternal(text);
            WriteInternal(newLine);
            RedrawMessages();
        }

        private static void WriteInternal(ReadOnlySpan<char> text)
        {
            lock (buffer)
            {
                bool scrollHead = head == messageHead;
                bool scrollTail = tail == messageTail;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '\n')
                    {
                        GoToNextLine();
                        continue;
                    }

                    if (char.IsWhiteSpace(c))
                        c = ' ';

                    buffer[bufferIndex++] = new ColoredChar(c, color);

                    if (bufferIndex % width == 0)
                        GoToNextLine();
                }

                void GoToNextLine()
                {
                    tail++;
                    if (tail == height) // Wrap around circular buffer
                        tail = 0;
                    if (tail == head)   // Tail pushes head
                        head++;
                    if (head == height) // Wrap around circular buffer
                        head = 0;

                    ClearBufferLine(tail);
                    bufferIndex = tail * width;

                    // Autoscroll as new lines are added
                    if (scrollTail)
                    {
                        messageTail = tail;
                        messageHead = messageTail - messagesHeight;
                        if (messageHead < 0)
                            messageHead = height + messageHead;
                    }
                    else if (scrollHead)
                    {
                        messageHead = head;
                        messageTail = messageHead + messagesHeight;
                        if (messageTail >= height)
                            messageTail -= height;
                    }
                }
            }
        }

        private static void ClearBufferLine(int line)
        {
            var empty = ColoredChar.Empty;
            int index = width * line;
            int end = index + width;
            for (; index < end; index++)
            {
                buffer[index] = empty;
            }
        }

        private static void RedrawMessages()
        {
            int sourceHead = messageHead;
            int targetHead = messagesTop;
            int length = messagesHeight;

            if (head != messageHead) // Draw arrow up
            {
                sourceHead++;
                targetHead++;
                length--;

                if (!arrowUpDrawn)
                {
                    DrawLine(arrowUp, 0);
                    arrowUpDrawn = true;
                }
            }
            else
            {
                arrowUpDrawn = false;
            }

            if (tail != messageTail) // Draw arrow down
            {
                length--;

                if (!arrowDownDrawn)
                {
                    DrawLine(arrowDown, inputRect.Top - 1);
                    arrowDownDrawn = true;
                }
            }
            else
            {
                arrowDownDrawn = false;
            }

            if (sourceHead >= height)
                sourceHead -= height;

            if (sourceHead + length > height)
            {
                int endLinesCount = height - sourceHead;
                RedrawMessageLines(sourceLine: sourceHead, targetLine: targetHead, linesCount: endLinesCount); // Lines left until the end of the buffer
                RedrawMessageLines(sourceLine: 0, targetLine: targetHead + endLinesCount, linesCount: length - endLinesCount - 1);
            }
            else
            {
                RedrawMessageLines(sourceLine: sourceHead, targetLine: targetHead, linesCount: length - 1);
            }
        }

        private static void RedrawMessageLines(int sourceLine, int targetLine, int linesCount)
        {
            if (linesCount < 1)
                return;

            var coordinate = new Coordinate(0, sourceLine);
            var rect = new Rect(0, targetLine, width, targetLine + linesCount);
            if (!NativeMethods.WriteConsoleOutput(handle, buffer, bufferSize, coordinate, ref rect))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static void DrawLine(ColoredChar[] line, int y)
        {
            var size = new Coordinate(line.Length, 1);
            var rect = new Rect(0, y, line.Length, y);
            if (!NativeMethods.WriteConsoleOutput(handle, line, size, Coordinate.Zero, ref rect))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        #endregion

        #region Reading input
        private static void StartReadingInput()
        {
            short color = (short)inputColor;

            for (int i = 0; i < defaultCommandPrefix.Length; i++)
            {
                inputBuffer[i] = new ColoredChar(defaultCommandPrefix[i], color);
            }

            Console.SetCursorPosition(defaultCommandPrefix.Length, inputRect.Top);
            RedrawInput();

            cursorPosition = commandPrefix.Length;

            Task.Run(() =>
            {
                while (true)
                {
                    var input = Console.ReadKey(true);

                    if (exitSemaphore is not null)
                    {
                        exitSemaphore.Release();
                        continue;
                    }

                    if (input.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        HandleInputCtrlKey(input);
                    }
                    else
                    {
                        HandleInputKey(input);
                    }
                }
            });
        }

        private static void HandleInputKey(ConsoleKeyInfo input)
        {
            switch (input.Key)
            {
                case ConsoleKey.RightArrow:
                    MoveCursorRight();
                    break;

                case ConsoleKey.LeftArrow:
                    MoveCursorLeft();
                    break;

                case ConsoleKey.Backspace:
                    RemoveInputCharAt(inputLeft + cursorPosition - 1);
                    MoveCursorLeft();
                    break;

                case ConsoleKey.Delete:
                    RemoveInputCharAt(inputLeft + cursorPosition);
                    break;

                case ConsoleKey.Home:
                    inputLeft = 0;
                    cursorPosition = commandPrefix.Length;
                    Console.SetCursorPosition(cursorPosition, inputRect.Top);
                    RedrawInput();
                    break;

                case ConsoleKey.End:
                    MoveCursorToEnd();
                    break;

                case ConsoleKey.Escape:
                    ClearInput();
                    break;

                case ConsoleKey.Enter:
                    HandleInput();
                    break;

                case ConsoleKey.UpArrow:
                    if (TryGetUpperCommandMemory(out string? upperCommand))
                    {
                        SetInput(upperCommand);
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (TryGetLowerCommandMemory(out string? lowerCommand))
                    {
                        SetInput(lowerCommand);
                    }
                    break;

                case ConsoleKey.PageUp:
                    MoveMessagesUp();
                    break;

                case ConsoleKey.PageDown:
                    MoveMesagesDown();
                    break;

                default:
                    if (input.KeyChar != 0)
                        WriteInputChar(input.KeyChar);
                    break;
            }
        }

        private static void HandleInputCtrlKey(ConsoleKeyInfo input)
        {
            int cursorIndex = inputLeft + cursorPosition;
            switch (input.Key)
            {
                case ConsoleKey.UpArrow:
                    MoveMessagesUp();
                    break;

                case ConsoleKey.DownArrow:
                    MoveMesagesDown();
                    break;

                case ConsoleKey.LeftArrow:
                    MoveCursorTo(GetNextWordIndex(cursorIndex, -1));
                    break;

                case ConsoleKey.RightArrow:
                    MoveCursorTo(GetNextWordIndex(cursorIndex, 1));
                    break;

                case ConsoleKey.Backspace:
                    int wordStart = GetNextWordIndex(cursorIndex, -1);
                    RemoveInput(wordStart, cursorIndex - wordStart);
                    MoveCursorTo(wordStart);
                    break;

                case ConsoleKey.Delete:
                    int wordEnd = GetNextWordIndex(cursorIndex, 1);
                    RemoveInput(cursorIndex, wordEnd - cursorIndex);
                    break;

                case ConsoleKey.Home:
                    MoveMessagesToTop();
                    break;

                case ConsoleKey.End:
                    MoveMessageToBottom();
                    break;

                case ConsoleKey.PageUp:
                    MoveMessagesToTop();
                    break;

                case ConsoleKey.PageDown:
                    MoveMessageToBottom();
                    break;
            }
        }

        private static void WriteInputChar(char c)
        {
            if (commandPrefix.Length + inputLength + 1 == inputBuffer.Length) // Don't write if the buffer is full
                return;
            inputLength++;

            int targetIndex = inputLeft + cursorPosition;       // Where the char should be written to
            int inputEnd = commandPrefix.Length + inputLength;  // Last input char index
            for (; inputEnd >= targetIndex; inputEnd--)         // Move text after to the right
            {
                inputBuffer[inputEnd] = inputBuffer[inputEnd - 1];
            }

            inputBuffer[targetIndex] = new ColoredChar(c, (short)inputColor);   // Write char
            MoveCursorRight();

            RedrawInput();
        }

        private static void MoveCursorRight()
        {
            if (inputLeft + cursorPosition - commandPrefix.Length == inputLength) // End of the input
            {
                return;
            }

            if (cursorPosition + 1 == width) // End of the window, but more content is to the right
            {
                inputLeft++;
            }
            else
            {
                cursorPosition++;
                Console.SetCursorPosition(cursorPosition, inputRect.Top);
            }

            RedrawInput();
        }

        private static void MoveCursorLeft()
        {
            if (inputLeft == 0 && cursorPosition == commandPrefix.Length) // All the way to the left
            {
                return;
            }

            if (cursorPosition == 0) // Left side of the window
            {
                inputLeft--;
                if (inputLeft == commandPrefix.Length) // At the first input char -> jump all the way to the left to show command prefix
                {
                    inputLeft = 0;
                    cursorPosition = commandPrefix.Length;
                    Console.SetCursorPosition(cursorPosition, inputRect.Top);
                }
            }
            else
            {
                cursorPosition--;
                Console.SetCursorPosition(cursorPosition, inputRect.Top);
            }

            RedrawInput();
        }

        private static void RemoveInputCharAt(int index)
        {
            if (index < commandPrefix.Length || index >= commandPrefix.Length + inputLength) // Make sure that index is not out of bounds
            {
                return;
            }

            inputBuffer[index] = ColoredChar.Empty; // Remove char

            for (index += 1; index < commandPrefix.Length + inputLength; index++) // Move following text back
            {
                inputBuffer[index - 1] = inputBuffer[index];
            }

            inputLength--;

            inputBuffer[commandPrefix.Length + inputLength] = ColoredChar.Empty; // Remove last char (leftover after moving text back)

            RedrawInput();
        }

        private static void RemoveInput(int index, int length)
        {
            if (length < 1 || index < commandPrefix.Length || index + length > commandPrefix.Length + inputLength) // Make sure that range is not out of bounds
            {
                return;
            }

            for (int i = index; i < index + length; i++) // Clear range
            {
                inputBuffer[i] = ColoredChar.Empty;
            }

            for (int i = index + length; i < commandPrefix.Length + inputLength; i++) // Move following text back
            {
                inputBuffer[i - length] = inputBuffer[i];
            }

            inputLength -= length;

            for (int i = commandPrefix.Length + inputLength; i < commandPrefix.Length + inputLength + length; i++) // Remove last -length- chars (leftover after moving text back)
            {
                inputBuffer[i] = ColoredChar.Empty;
            }

            RedrawInput();
        }

        private static void RedrawInput()
        {
            var coordinates = new Coordinate(inputLeft, 0);
            if (!NativeMethods.WriteConsoleOutput(handle, inputBuffer, inputBufferSize, coordinates, ref inputRect))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static void ClearInput()
        {
            for (int i = commandPrefix.Length; i < inputBuffer.Length; i++)
            {
                inputBuffer[i] = ColoredChar.Empty;
            }
            inputLeft = 0;
            inputLength = 0;
            cursorPosition = commandPrefix.Length;
            Console.SetCursorPosition(cursorPosition, inputRect.Top);
            RedrawInput();
        }

        private static void HandleInput()
        {
            Span<char> input = stackalloc char[inputLength];
            for (int i = 0; i < inputLength; i++)
            {
                input[i] = inputBuffer[i + commandPrefix.Length].character;
            }

            ClearInput();

            string command = input.Trim().ToString();
            MemorizeCommand(command);

            ExecuteCommand(command);
        }

        // Overwrites current input with specified text
        private static void SetInput(string text)
        {
            for (int i = commandPrefix.Length; i < inputBuffer.Length; i++)
            {
                inputBuffer[i] = ColoredChar.Empty;
            }
            for (int i = 0; i < text.Length; i++)
            {
                inputBuffer[i + commandPrefix.Length] = new ColoredChar(text[i], (short)inputColor);
            }

            inputLength = text.Length;

            MoveCursorToEnd();
        }

        private static void MoveCursorTo(int index)
        {
            if (index >= inputLeft + width)
            {
                cursorPosition = width - 1;
                inputLeft += (index - inputLeft - width + 1);
            }
            else if (index < inputLeft)
            {
                inputLeft = index;
                if (inputLeft == commandPrefix.Length)
                {
                    inputLeft = 0;
                    cursorPosition = commandPrefix.Length;
                }
                else
                {
                    cursorPosition = 0;
                }
            }
            else
            {
                cursorPosition = index - inputLeft;
            }

            Console.SetCursorPosition(cursorPosition, inputRect.Top);
            RedrawInput();
        }

        private static void MoveCursorToEnd()
        {
            inputLeft = Math.Max(0, commandPrefix.Length + inputLength - width + 1);
            cursorPosition = commandPrefix.Length + inputLength - inputLeft;
            Console.SetCursorPosition(cursorPosition, inputRect.Top);
            RedrawInput();
        }

        // Finds the next other word from specific position, moving in direction specified by step
        private static int GetNextWordIndex(int index, int step)
        {
            index += step;
            bool insideWord = true;
            for (; index >= commandPrefix.Length && index < commandPrefix.Length + inputLength; index += step)
            {
                char c = inputBuffer[index].character;
                bool isDelimiter = char.IsWhiteSpace(c) || c == '@';

                if (isDelimiter && insideWord)
                {
                    insideWord = false;
                }
                else if (!isDelimiter && !insideWord)
                {
                    return Math.Clamp(index - step, commandPrefix.Length, commandPrefix.Length + inputLength);
                }
            }
            return Math.Clamp(index, commandPrefix.Length, commandPrefix.Length + inputLength);
        }

        // Note: clears input
        private static void ChangeCommandPrefixInternal(string value)
        {
            value ??= string.Empty;

            commandPrefix = value;
            for (int i = 0; i < value.Length; i++)
            {
                inputBuffer[i] = new ColoredChar(value[i], (short)inputColor);
            }

            ClearInput();
        }
        #endregion

        #region Moving messages
        private static void MoveMessagesUp()
        {
            lock (buffer)
            {
                MoveMessages(-1);
            }
        }

        private static void MoveMesagesDown()
        {
            lock (buffer)
            {
                MoveMessages(1);
            }
        }

        private static void MoveMessagesToTop()
        {
            // lock can't be moved to MoveMessages because of these calculations
            lock (buffer)
            {
                MoveMessages(head <= messageHead ? head - messageHead : -head - (height - messageHead));
            }
        }

        private static void MoveMessageToBottom()
        {
            // lock can't be moved to MoveMessages because of these calculations
            lock (buffer)
            {
                MoveMessages(tail >= messageTail ? tail - messageTail : (height - messageTail) + tail);
            }
        }

        private static void MoveMessages(int by)
        {
            if (by == 0)
                return;

            if (by < 0) // Move messages head
            {
                int diff = head.CompareTo(messageHead);
                if (diff == 0) // Already at the top
                    return;

                messageHead += by;
                if (messageHead < 0) // Wrap around circular buffer
                {
                    messageHead += height;
                    diff = -diff;
                }
                if (diff != head.CompareTo(messageHead)) // Stepped over head -> clamp
                    messageHead = head;

                messageTail = messageHead + messagesHeight;
                if (messageTail >= height) // Wrap around circular buffer
                    messageTail -= tail;
            }
            else // Move messages tail
            {
                int diff = tail.CompareTo(messageTail);
                if (diff == 0) // Already at the bottom
                    return;

                messageTail += by;
                if (messageTail >= height) // Wrap around circular buffer
                {
                    messageTail -= height;
                    diff = -diff;
                }
                if (diff != tail.CompareTo(messageTail)) // Stepped over tail -> clamp
                    messageTail = tail;

                messageHead = messageTail - messagesHeight;
                if (messageHead < 0) // Wrap around circular buffer
                    messageHead += height;
            }

            RedrawMessages();
        }
        #endregion

        private static bool HandleCtrlKeyPress(NativeMethods.CtrlType ctrlType)
        {
            return CancelKeyPress?.Invoke() ?? false;
        }

        #region Commands memory
        private static void MemorizeCommand(string? command)
        {
            selectedMemorizedCommand = null;

            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            if (command.Equals(commandMemory.First?.Value))
            {
                return;
            }

            commandMemory.AddFirst(command);

            if (commandMemory.Count > maxCommandMemory)
                commandMemory.RemoveLast();
        }

        private static bool TryGetUpperCommandMemory([NotNullWhen(true)] out string? command)
        {
            if (selectedMemorizedCommand is not null)
            {
                if (selectedMemorizedCommand.Next is null)
                {
                    command = null;
                    return false;
                }

                selectedMemorizedCommand = selectedMemorizedCommand.Next;
                command = selectedMemorizedCommand.Value;
                return true;
            }

            selectedMemorizedCommand = commandMemory.First;
            if (selectedMemorizedCommand is not null)
            {
                command = selectedMemorizedCommand.Value;
                return true;
            }

            command = null;
            return false;
        }

        private static bool TryGetLowerCommandMemory([NotNullWhen(true)] out string? command)
        {
            if (selectedMemorizedCommand is null || selectedMemorizedCommand.Previous is null)
            {
                command = null;
                return false;
            }

            selectedMemorizedCommand = selectedMemorizedCommand.Previous;
            command = selectedMemorizedCommand.Value;
            return true;
        }
        #endregion
    }
}
