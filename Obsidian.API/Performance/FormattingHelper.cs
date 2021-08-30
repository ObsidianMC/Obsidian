using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Obsidian.API.Performance
{
    internal static class FormattingHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsUtf8Formattable<T>()
        {
            // Relies on JIT optimization
            return typeof(T) == typeof(bool)
                || typeof(T) == typeof(byte)
                || typeof(T) == typeof(sbyte)
                || typeof(T) == typeof(short)
                || typeof(T) == typeof(ushort)
                || typeof(T) == typeof(int)
                || typeof(T) == typeof(uint)
                || typeof(T) == typeof(long)
                || typeof(T) == typeof(ulong)
                || typeof(T) == typeof(float)
                || typeof(T) == typeof(double)
                || typeof(T) == typeof(decimal)
                || typeof(T) == typeof(DateTime)
                || typeof(T) == typeof(DateTimeOffset)
                || typeof(T) == typeof(TimeSpan)
                || typeof(T) == typeof(Guid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool WriteUtf8FormattableCore<T>(Span<byte> destination, T primitive, out int bytesWritten, StandardFormat format = default)
        {
            if (primitive is bool @bool)
            {
                return Utf8Formatter.TryFormat(@bool, destination, out bytesWritten, format);
            }
            else if (primitive is byte @byte)
            {
                return Utf8Formatter.TryFormat(@byte, destination, out bytesWritten, format);
            }
            else if (primitive is sbyte @sbyte)
            {
                return Utf8Formatter.TryFormat(@sbyte, destination, out bytesWritten, format);
            }
            else if (primitive is short @short)
            {
                return Utf8Formatter.TryFormat(@short, destination, out bytesWritten, format);
            }
            else if (primitive is ushort @ushort)
            {
                return Utf8Formatter.TryFormat(@ushort, destination, out bytesWritten, format);
            }
            else if (primitive is int @int)
            {
                return Utf8Formatter.TryFormat(@int, destination, out bytesWritten, format);
            }
            else if (primitive is uint @uint)
            {
                return Utf8Formatter.TryFormat(@uint, destination, out bytesWritten, format);
            }
            else if (primitive is long @long)
            {
                return Utf8Formatter.TryFormat(@long, destination, out bytesWritten, format);
            }
            else if (primitive is ulong @ulong)
            {
                return Utf8Formatter.TryFormat(@ulong, destination, out bytesWritten, format);
            }
            else if (primitive is float @float)
            {
                return Utf8Formatter.TryFormat(@float, destination, out bytesWritten, format);
            }
            else if (primitive is double @double)
            {
                return Utf8Formatter.TryFormat(@double, destination, out bytesWritten, format);
            }
            else if (primitive is decimal @decimal)
            {
                return Utf8Formatter.TryFormat(@decimal, destination, out bytesWritten, format);
            }
            else if (primitive is DateTime dateTime)
            {
                return Utf8Formatter.TryFormat(@dateTime, destination, out bytesWritten, format);
            }
            else if (primitive is DateTimeOffset dateTimeOffset)
            {
                return Utf8Formatter.TryFormat(dateTimeOffset, destination, out bytesWritten, format);
            }
            else if (primitive is TimeSpan timeSpan)
            {
                return Utf8Formatter.TryFormat(timeSpan, destination, out bytesWritten, format);
            }
            else if (primitive is Guid guid)
            {
                return Utf8Formatter.TryFormat(guid, destination, out bytesWritten, format);
            }

            throw new NotSupportedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Span<byte> WriteUtf8Formattable<T>(Span<byte> destination, T primitive, StandardFormat format)
        {
            WriteUtf8FormattableCore(destination, primitive, out int bytesWritten, format);
            return destination.UnsafeShrink(bytesWritten);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Span<byte> WriteUtf8FormattableAlignedLeft<T>(Span<byte> destination, T primitive, StandardFormat format, int minLength)
        {
            WriteUtf8FormattableCore(destination, primitive, out int bytesWritten, format);

            Debug.Assert(destination.Length >= minLength);

            if (bytesWritten >= minLength)
            {
                return destination.UnsafeShrink(bytesWritten);
            }

            destination.UnsafeSlice(bytesWritten, minLength - bytesWritten).Fill((byte)' ');
            return destination.UnsafeShrink(minLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Span<byte> WriteUtf8FormattableAlignedRight<T>(Span<byte> destination, T primitive, StandardFormat format, int minLength)
        {
            WriteUtf8FormattableCore(destination, primitive, out int bytesWritten, format);

            Debug.Assert(destination.Length >= minLength);

            if (bytesWritten >= minLength)
            {
                return destination.UnsafeShrink(bytesWritten);
            }

            // Copy to the right
            destination.UnsafeShrink(bytesWritten).CopyTo(destination.UnsafeAdvance(destination.Length - bytesWritten));
            // Fill spaces
            destination.UnsafeSlice(destination.Length - minLength, minLength - bytesWritten).Fill((byte)' ');

            return destination.UnsafeSlice(destination.Length - minLength, minLength);
        }

        // 'format' is expected to be valid JSON
        internal static (byte[][] literals, FormattableArgument[] arguments) ParseFormat(string format, int argumentCount)
        {
            Debug.Assert(format is not null);
            Debug.Assert(argumentCount > 0);

            var literals = new List<byte[]>();
            var arguments = new List<FormattableArgument>();

            int start = 0, index = 0;
            while (true)
            {
                int end = format.IndexOf('"', index);
                if (end == -1)
                    break;
                end = format.IndexOf('"', end);
                if (end == -1)
                    break;

                index = end + 1;
                while (index < format.Length && format[index] != ':') index++;
                index++; // Skip past ':'
                while (index < format.Length && char.IsWhiteSpace(format[index])) index++; // Skip whitespace
                Debug.Assert(index < format.Length); // Should always be true in valid JSON

                if (format[index] == '"') // String value opening
                {
                    index++; // Skip past '"'

                    char c = format[index];
                    while (c != '"')
                    {
                        if (c == '\\')
                        {
                            index++;
                        }
                        else if (c == '}')
                        {
                            if (format[index + 1] == '}')
                                index++;
                            else
                                ThrowFormatError();
                        }
                        else if (c == '{')
                        {
                            if (format[index + 1] == '{')
                                index++;
                            else
                            {
                                end = index;
                                c = format[++index];

                                //
                                // Argument hole
                                //
                                if (c < '0' || c > '9') ThrowFormatError();
                                int identificator = 0;
                                do
                                {
                                    identificator = identificator * 10 + c - '0';
                                    index++;
                                    c = format[index];
                                }
                                while (c >= '0' && c <= '9');
                                if (identificator >= argumentCount) ThrowFormatError();

                                // Parse alignment (optional)
                                while (char.IsWhiteSpace(c)) c = format[++index];
                                int alignment = 0;
                                if (c == ',')
                                {
                                    c = format[++index];
                                    while (char.IsWhiteSpace(c)) c = format[++index];

                                    bool negativeAlignment = false;
                                    if (c == '-')
                                    {
                                        negativeAlignment = true;
                                        c = format[++index];
                                    }

                                    if (c < '0' || c > '9') ThrowFormatError();
                                    do
                                    {
                                        alignment = alignment * 10 + c - '0';
                                        c = format[++index];
                                    }
                                    while (c >= '0' && c <= '9');

                                    if (alignment > 1024) ThrowFormatError();
                                    if (negativeAlignment) alignment *= -1;
                                }

                                // Parse format (optional)
                                while (char.IsWhiteSpace(c)) c = format[++index];
                                int formatStart = -1;
                                if (c == ':')
                                {
                                    c = format[++index];
                                    formatStart = index;

                                    while (c != '}')
                                    {
                                        if (c == '{') ThrowFormatError();
                                        c = format[++index];
                                    }
                                }
                                else if (c != '}')
                                {
                                    ThrowFormatError();
                                }

                                if (formatStart != -1)
                                {
                                    if (index - formatStart == 1)
                                        arguments.Add(new FormattableArgument(identificator, new StandardFormat(format[formatStart]), alignment));
                                    else
                                        arguments.Add(new FormattableArgument(identificator, format[formatStart..index], alignment));
                                }
                                else
                                {
                                    arguments.Add(new FormattableArgument(identificator, null, alignment));
                                }

                                literals.Add(Encoding.UTF8.GetBytes(format, start, end - start));

                                start = index + 1;
                            }
                        }

                        index++;
                        c = format[index];
                    }
                    index++; // Skip past the string closing '"'
                }
            }

            literals.Add(Encoding.UTF8.GetBytes(format, start, format.Length - start));

            return (literals.ToArray(), arguments.ToArray());
        }

        private enum FormatterState
        {
            Object, Identifier, String
        }

        private static void ThrowFormatError()
        {
            throw new FormatException();
        }

        internal static Span<byte> GetUtf8Bytes<T>(this T? value)
        {
            if (value is null)
            {
                return Span<byte>.Empty;
            }

            string? text = value.ToString();
            return text is not null ? Encoding.UTF8.GetBytes(text) : Span<byte>.Empty;
        }

        internal static Span<byte> GetUtf8Bytes<T>(this T? value, string format)
        {
            if (value is null)
            {
                return Span<byte>.Empty;
            }

            var formattable = value as IFormattable;
            string? text = formattable?.ToString(format, null);
            return text is not null ? Encoding.UTF8.GetBytes(text) : Span<byte>.Empty;
        }

        internal static Span<byte> UnsafeAdvance(this Span<byte> span, int start)
        {
            Debug.Assert(start >= 0 && start <= span.Length);
            return MemoryMarshal.CreateSpan(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), start), span.Length - start);
        }

        internal static Span<byte> UnsafeShrink(this Span<byte> span, int length)
        {
            Debug.Assert(length >= 0 && length <= span.Length);
            return MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), length);
        }

        internal static Span<byte> UnsafeSlice(this Span<byte> span, int start, int length)
        {
            Debug.Assert(start >= 0 && start <= span.Length);
            Debug.Assert(start + length <= span.Length);
            return MemoryMarshal.CreateSpan(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), start), length);
        }

        internal readonly struct FormattableArgument
        {
            public readonly int index;
            public readonly int aligment;
            private readonly AlignmentType type;
            public readonly StandardFormat standardFormat;
            private readonly string? textFormat;

            public FormattableArgument(int index, StandardFormat format = default, int aligment = 0)
            {
                this.index = index;
                textFormat = format.Symbol.ToString();
                standardFormat = format;
                if (aligment < 0)
                {
                    type = AlignmentType.AlignLeft;
                    aligment = -aligment;
                }
                else if (aligment > 0)
                {
                    type = AlignmentType.AlignRight;
                }
                else
                {
                    type = AlignmentType.None;
                }
                this.aligment = aligment;
            }

            public FormattableArgument(int index, string? format = null, int aligment = 0)
            {
                this.index = index;
                standardFormat = default;
                textFormat = format;
                if (aligment < 0)
                {
                    type = AlignmentType.AlignLeft;
                    aligment = -aligment;
                }
                else if (aligment > 0)
                {
                    type = AlignmentType.AlignRight;
                }
                else
                {
                    type = AlignmentType.None;
                }
                this.aligment = aligment;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Span<byte> Write<T>(Span<byte> destination, T value)
            {
                if (IsUtf8Formattable<T>())
                {
                    return type switch
                    {
                        AlignmentType.AlignLeft => WriteUtf8FormattableAlignedLeft(destination, value, standardFormat, aligment),
                        AlignmentType.AlignRight => WriteUtf8FormattableAlignedRight(destination, value, standardFormat, aligment),
                        _ /* None */ => WriteUtf8Formattable(destination, value, standardFormat),
                    };
                }

                return textFormat is null ? GetUtf8Bytes(value) : GetUtf8Bytes(value, textFormat);
            }

            private enum AlignmentType
            {
                None, AlignLeft, AlignRight
            }
        }
    }
}
