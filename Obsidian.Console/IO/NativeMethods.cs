using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Obsidian.ConsoleApp.IO;

internal static class NativeMethods
{
    internal delegate bool HandlerRoutine(CtrlType dwCtrlType);

    [DllImport("kernel32.dll")]
    internal static extern bool SetConsoleCtrlHandler(HandlerRoutine handlerRoutine, bool add);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WriteConsoleOutputW", CharSet = CharSet.Unicode)]
    internal static extern bool WriteConsoleOutput(SafeFileHandle hConsoleOutput, ColoredChar[] lpBuffer, Coordinate dwBufferSize, Coordinate dwBufferCoord, ref Rect lpWriteRegion);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetConsoleCursorPosition(SafeFileHandle hConsoleOutput, Coordinate cursorPosition);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFile(
    string fileName,
    [MarshalAs(UnmanagedType.U4)] uint fileAccess,
    [MarshalAs(UnmanagedType.U4)] uint fileShare,
    IntPtr securityAttributes,
    [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
    [MarshalAs(UnmanagedType.U4)] int flags,
    IntPtr template);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    internal readonly struct ColoredChar
    {
        public static readonly ColoredChar Empty = new(' ', 0);

        public readonly char character;
        public readonly short color;

        public ColoredChar(char character, short color)
        {
            this.character = character;
            this.color = color;
        }

        private string GetDebuggerDisplay()
        {
            return $"'{character}'";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Coordinate
    {
        public static Coordinate Zero => new Coordinate(0, 0);

        public readonly short X;
        public readonly short Y;

        public Coordinate(int x, int y)
        {
            X = (short)x;
            Y = (short)y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Rect
    {
        public readonly short Left;
        public readonly short Top;
        public readonly short Right;
        public readonly short Bottom;

        public Rect(int left, int top, int right, int bottom)
        {
            Left = (short)left;
            Top = (short)top;
            Right = (short)right;
            Bottom = (short)bottom;
        }
    }

    internal enum CtrlType
    {
        CtrlCEvent = 0,
        CtrlBreakEvent = 1,
        CtrlCloseEvent = 2,
        CtrlLogOffEvent = 5,
        CtrlShutdownEvent = 6
    }
}
