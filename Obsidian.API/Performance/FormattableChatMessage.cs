using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Argument = Obsidian.API.Performance.FormattingHelper.FormattableArgument;

namespace Obsidian.API.Performance
{
    public abstract class FormattableChatMessage
    {
        private protected readonly byte[][] literals;
        private protected readonly Argument[] arguments;

        private protected FormattableChatMessage(string formattableMessage, int argumentCount)
            : this(ChatMessage.Simple(formattableMessage ?? throw new ArgumentNullException(nameof(formattableMessage))), argumentCount)
        {
        }

        private protected FormattableChatMessage(ChatMessage formattableChatMessage, int argumentCount)
        {
            // TODO: use JSON settings
            string json = formattableChatMessage.ToString(null!);

            (literals, arguments) = FormattingHelper.ParseFormat(json, argumentCount);
        }
    }

    public sealed class FormattableChatMessage<T1> : FormattableChatMessage
    {
        public FormattableChatMessage(string formattableMessage) : base(formattableMessage, 1)
        {
        }

        public FormattableChatMessage(ChatMessage formattableChatMessage) : base(formattableChatMessage, 1)
        {
        }

        [SkipLocalsInit]
        public Utf8Message Format(T1 arg1)
        {
            var bytes = new byte[literals.Length + arguments.Length][];

            Span<byte> argumentSpan = stackalloc byte[512];

            ref Argument argument = ref arguments[0];
            ref byte[] literal = ref literals[0];

            bytes[0] = literal;

            for (int i = 1; i < bytes.Length; i += 2)
            {
                bytes[i] = (argument.index switch
                {
                    0 => argument.Write(argumentSpan, arg1)
                }).ToArray();

                argument = ref Unsafe.Add(ref argument, 1);
                literal = ref Unsafe.Add(ref literal, 1);

                bytes[i + 1] = literal;
            }

            return new Utf8Message(bytes);
        }
    }

    public sealed class FormattableChatMessage<T1, T2> : FormattableChatMessage
    {
        public FormattableChatMessage(string formattableMessage) : base(formattableMessage, 2)
        {
        }

        public FormattableChatMessage(ChatMessage formattableChatMessage) : base(formattableChatMessage, 2)
        {
        }

        [SkipLocalsInit]
        public Utf8Message Format(T1 arg1, T2 arg2)
        {
            var bytes = new byte[literals.Length + arguments.Length][];

            Span<byte> argumentSpan = stackalloc byte[512];
            ref Argument argument = ref arguments[0];

            for (int i = 0; i < literals.Length - 1; i++)
            {
                bytes[i * 2] = literals[i];

                Span<byte> arg = argument.index switch
                {
                    0 => argument.Write(argumentSpan, arg1),
                    _ => argument.Write(argumentSpan, arg2)
                };
                bytes[i * 2 + 1] = arg.ToArray();
                argument = ref Unsafe.Add(ref argument, 1);
            }
            bytes[^1] = literals[^1];

            return new Utf8Message(bytes);
        }
    }

    public sealed class FormattableChatMessage<T1, T2, T3> : FormattableChatMessage
    {
        public FormattableChatMessage(string formattableMessage) : base(formattableMessage, 3)
        {
        }

        public FormattableChatMessage(ChatMessage formattableChatMessage) : base(formattableChatMessage, 3)
        {
        }

        [SkipLocalsInit]
        public Utf8Message Format(T1 arg1, T2 arg2, T3 arg3)
        {
            var bytes = new byte[literals.Length + arguments.Length][];

            Span<byte> argumentSpan = stackalloc byte[512];
            ref Argument argument = ref arguments[0];

            for (int i = 0; i < literals.Length - 1; i++)
            {
                bytes[i * 2] = literals[i];

                Span<byte> arg = argument.index switch
                {
                    0 => argument.Write(argumentSpan, arg1),
                    1 => argument.Write(argumentSpan, arg2),
                    _ => argument.Write(argumentSpan, arg3)
                };
                bytes[i * 2 + 1] = arg.ToArray();
                argument = ref Unsafe.Add(ref argument, 1);
            }
            bytes[^1] = literals[^1];

            return new Utf8Message(bytes);
        }
    }

    public sealed class FormattableChatMessage<T1, T2, T3, T4> : FormattableChatMessage
    {
        public FormattableChatMessage(string formattableMessage) : base(formattableMessage, 4)
        {
        }

        public FormattableChatMessage(ChatMessage formattableChatMessage) : base(formattableChatMessage, 4)
        {
        }

        [SkipLocalsInit]
        public Utf8Message Format(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            var bytes = new byte[literals.Length + arguments.Length][];

            Span<byte> argumentSpan = stackalloc byte[512];
            ref Argument argument = ref arguments[0];

            for (int i = 0; i < literals.Length - 1; i++)
            {
                bytes[i * 2] = literals[i];

                Span<byte> arg = argument.index switch
                {
                    0 => argument.Write(argumentSpan, arg1),
                    1 => argument.Write(argumentSpan, arg2),
                    2 => argument.Write(argumentSpan, arg3),
                    _ => argument.Write(argumentSpan, arg4)
                };
                bytes[i * 2 + 1] = arg.ToArray();
                argument = ref Unsafe.Add(ref argument, 1);
            }
            bytes[^1] = literals[^1];

            return new Utf8Message(bytes);
        }
    }

    public sealed class FormattableChatMessage<T1, T2, T3, T4, T5> : FormattableChatMessage
    {
        public FormattableChatMessage(string formattableMessage) : base(formattableMessage, 5)
        {
        }

        public FormattableChatMessage(ChatMessage formattableChatMessage) : base(formattableChatMessage, 5)
        {
        }

        [SkipLocalsInit]
        public Utf8Message Format(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            var bytes = new byte[literals.Length + arguments.Length][];

            Span<byte> argumentSpan = stackalloc byte[1024];

            ref Argument argument = ref arguments[0];
            ref byte[] literal = ref literals[0];

            bytes[0] = literal;

            for (int i = 1; i < bytes.Length; i += 2)
            {
                bytes[i] = (argument.index switch
                {
                    0 => argument.Write(argumentSpan, arg1),
                    1 => argument.Write(argumentSpan, arg2),
                    2 => argument.Write(argumentSpan, arg3),
                    3 => argument.Write(argumentSpan, arg4),
                    _ => argument.Write(argumentSpan, arg5)
                }).ToArray();

                argument = ref Unsafe.Add(ref argument, 1);
                literal = ref Unsafe.Add(ref literal, 1);

                bytes[i + 1] = literal;
            }

            return new Utf8Message(bytes);
        }
    }
}
