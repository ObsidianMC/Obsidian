using Microsoft.Extensions.Options;
using System.Globalization;
using System.Threading;

namespace Obsidian.Console;

/// <summary>Serializes log output and command editing on the terminal's current line.</summary>
public sealed partial class ConsoleTerminal(IConsoleDevice device, IOptions<ConsoleCommandOptions> options)
{
    private const string EraseLine = "\r\u001b[2K";

    private readonly Lock gate = new();

    private string text = string.Empty;

    private int cursor;

    private bool reading;

    public bool IsInteractive => device.IsInteractive;

    public void WriteLog(string message, bool standardError = false)
    {
        lock (this.gate)
        {
            bool redraw = this.reading && (!standardError || !device.IsErrorRedirected);

            if (redraw)
                device.Write(EraseLine);

            device.Write(message, standardError);

            if (redraw)
                this.Render();
        }
    }

    public async Task<string?> ReadLineAsync(CancellationToken token,
        Func<string, int, CancellationToken, ValueTask<ConsoleCompletion?>>? complete = null)
    {
        lock (this.gate)
        {
            this.text = string.Empty;
            this.cursor = 0;
            this.reading = true;
            this.Render();
        }

        try
        {
            int width = device.Width;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                (string Text, int Cursor)? completionInput = null;

                lock (this.gate)
                {
                    if (device.KeyAvailable)
                    {
                        ConsoleKeyInfo key = device.ReadKey();

                        if (key.Key == ConsoleKey.Enter)
                            return this.text;


                        if (this.text.Length == 0 && key.Modifiers.HasFlag(ConsoleModifiers.Control)
                            && key.Key is ConsoleKey.D or ConsoleKey.Z)
                            return null;


                        if (key.Key == ConsoleKey.Tab && complete is not null)
                            completionInput = (this.text, this.cursor);
                        else
                        {
                            this.Edit(key);
                            this.Render();

                            continue;
                        }
                    }

                    if (width != device.Width)
                    {
                        width = device.Width;
                        this.Render();
                    }
                }

                // Permission checks and plugin providers may await work; never hold the output lock while they run.
                if (completionInput is { } input && complete is not null)
                {
                    var completion = await complete(input.Text, input.Cursor, token).AsTask().WaitAsync(token).ConfigureAwait(false);

                    lock (this.gate)
                    {
                        if (this.text == input.Text && this.cursor == input.Cursor && completion is not null)
                            this.ApplyCompletion(completion);

                        this.Render();
                    }

                    continue;
                }

                // Polling avoids an uncancellable ReadKey and leaves Ctrl+C with ConsoleLifetime.
                await Task.Delay(20, token).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (this.gate)
            {
                device.Write(EraseLine);
                this.reading = false;
            }
        }
    }

    public void Clear()
    {
        lock (this.gate)
        {
            if (!device.IsInteractive)
                return;

            device.Write("\u001b[2J\u001b[H");

            if (this.reading)
                this.Render();
        }
    }

    private void Edit(ConsoleKeyInfo key)
    {
        int[] boundaries = StringInfo.ParseCombiningCharacters(this.text).Append(this.text.Length).ToArray();
        int previous = boundaries.LastOrDefault(index => index < this.cursor);
        int next = boundaries.FirstOrDefault(index => index > this.cursor, this.text.Length);

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                this.cursor = previous;

                break;
            case ConsoleKey.RightArrow:
                this.cursor = next;

                break;
            case ConsoleKey.Home:
                this.cursor = 0;

                break;
            case ConsoleKey.End:
                this.cursor = this.text.Length;

                break;
            case ConsoleKey.Backspace when this.cursor > 0:
                this.text = this.text.Remove(previous, this.cursor - previous);
                this.cursor = previous;

                break;
            case ConsoleKey.Delete when this.cursor < this.text.Length:
                this.text = this.text.Remove(this.cursor, next - this.cursor);

                break;
            default:
                if (!char.IsControl(key.KeyChar))
                {
                    this.text = this.text.Insert(this.cursor, key.KeyChar.ToString());
                    this.cursor++;
                }

                break;
        }
    }

    private void Render()
    {
        int available = Math.Max(0, device.Width - 1);
        string prompt = new(options.Value.Prompt.Where(c => !char.IsControl(c)).ToArray());
        string prefix = Fit(prompt, Math.Max(0, available - 1));

        available -= DisplayWidth(prefix);

        int start = this.cursor;
        int[] boundaries = StringInfo.ParseCombiningCharacters(this.text);

        foreach (int index in boundaries.Reverse())
        {
            if (index >= this.cursor)
                continue;

            if (DisplayWidth(this.text[index..this.cursor]) >= available)
                break;

            start = index;
        }

        string visible = Fit(this.text[start..], available);
        int column = DisplayWidth(prefix) + DisplayWidth(this.text[start..this.cursor]);

        device.Write($"{EraseLine}{prefix}{visible}\r\u001b[{column + 1}G");
    }

    private static string Fit(string text, int width)
    {
        int end = 0;

        foreach (string element in Elements(text))
        {
            width -= ElementWidth(element);

            if (width < 0)
                break;

            end += element.Length;
        }

        return text[..end];
    }

    private static int DisplayWidth(string text) => Elements(text).Sum(ElementWidth);

    private static IEnumerable<string> Elements(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);

        while (elements.MoveNext())
            yield return elements.GetTextElement();
    }

    private static int ElementWidth(string element)
    {
        int value = System.Text.Rune.TryGetRuneAt(element, 0, out var rune) ? rune.Value : element[0];

        // Common full-width CJK characters and emoji occupy two terminal cells. Combining
        // characters stay with their base through StringInfo, including joined emoji.
        return value is >= 0x1100 and <= 0x115f or >= 0x2329 and <= 0x232a
            or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3
            or >= 0xf900 and <= 0xfaff or >= 0xfe10 and <= 0xfe6f
            or >= 0xff01 and <= 0xff60 or >= 0xffe0 and <= 0xffe6
            or >= 0x1f000 and <= 0x1ffff or >= 0x20000 and <= 0x3ffff
            || element.Contains('\ufe0f') ? 2 : 1;
    }
}
