namespace Obsidian.Console;

public sealed partial class ConsoleTerminal
{
    private void ApplyCompletion(ConsoleCompletion completion)
    {
        if (completion.Start < 0 || completion.Length < 0 || completion.Start > this.cursor
            || completion.Length < this.cursor - completion.Start
            || completion.Length > this.text.Length - completion.Start || completion.Candidates.Count == 0)
            return;

        var candidates = completion.Candidates.Select((value, index) => (Value: value, Index: index))
            .Where(candidate => !candidate.Value.Any(char.IsControl)).ToArray();

        if (candidates.Length == 0)
            return;

        var common = candidates[0].Value;

        for (var index = 1; index < candidates.Length; index++)
        {
            var length = 0;

            while (length < common.Length && length < candidates[index].Value.Length
                && char.ToUpperInvariant(common[length]) == char.ToUpperInvariant(candidates[index].Value[length]))
                length++;

            if (length > 0 && char.IsHighSurrogate(common[length - 1]))
                length--;

            common = common[..length];
        }

        if (candidates.Length == 1 || common.Length > this.cursor - completion.Start)
        {
            var suffix = this.text[(completion.Start + completion.Length)..];

            this.text = this.text[..completion.Start] + common + suffix;
            this.cursor = completion.Start + common.Length;

            var continuation = completion.Continuations is { } flags && candidates[0].Index < flags.Count && flags[candidates[0].Index];

            if (candidates.Length == 1 && suffix.Length == 0 && !continuation)
            {
                this.text += " ";
                this.cursor++;
            }
        }
        else
            device.Write(EraseLine + string.Join("  ", candidates.Select(candidate => candidate.Value)) + Environment.NewLine);
    }
}
