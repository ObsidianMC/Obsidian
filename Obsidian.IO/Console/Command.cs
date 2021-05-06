using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.IO.Console
{
    public delegate ValueTask CommandCallback(string[] words, ReadOnlyMemory<char> fullArgs);

    internal readonly struct Command : IComparable<Command>
    {
        public string Name { get; }
        public ReadOnlyMemory<char>[] Words { get; }
        public CommandCallback Callback { get; }

        public Command(string name, CommandCallback callback)
        {
            Name = name;
            Callback = callback;

            var words = new List<ReadOnlyMemory<char>>();
            int start = 0;
            for (int i = 0; i < name.Length; i++)
            {
                while (i < name.Length && char.IsWhiteSpace(name[i])) i++;

                start = i++;

                while (i < name.Length && !char.IsWhiteSpace(name[i])) i++;

                words.Add(name.AsMemory(start, i - start));
            }

            Words = words.ToArray();
        }

        public int CompareTo(Command other)
        {
            return Name.CompareTo(other.Name);
        }

        public bool TryExecute(string original, string[] input)
        {
            if (input.Length < Words.Length)
                return false;

            for (int i = 0; i < Words.Length; i++)
            {
                if (!MemoryExtensions.Equals(input[i], Words[i].Span, StringComparison.Ordinal))
                    return false;
            }

            int argsStart = 0;
            for (int i = 0; i < Words.Length; i++)
            {
                argsStart += Words[i].Length;
                while (argsStart < original.Length && char.IsWhiteSpace(original[argsStart])) argsStart++;
            }

            CommandCallback func = Callback;
            string[] args = input[Words.Length..];
            ReadOnlyMemory<char> fullArgs = original.AsMemory(argsStart);
            Task.Run(async () => await func(args, fullArgs));

            return true;
        }
    }
}
