namespace Obsidian.ConsoleApp.IO;

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
        var start = 0;
        for (var i = 0; i < name.Length; i++)
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

        for (var i = 0; i < Words.Length; i++)
            if (!MemoryExtensions.Equals(input[i], Words[i].Span, StringComparison.Ordinal))
                return false;

        var argsStart = 0;
        for (var i = 0; i < Words.Length; i++)
        {
            argsStart += Words[i].Length;
            while (argsStart < original.Length && char.IsWhiteSpace(original[argsStart])) argsStart++;
        }

        var func = Callback;
        var args = input[Words.Length..];
        var fullArgs = original.AsMemory(argsStart);
        Task.Run(async () => await func(args, fullArgs));

        return true;
    }
}
