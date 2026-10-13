using System.Text;

namespace Obsidian.Porting;

/// <summary>
/// What porting a mod did: the files it converted or copied, those it left out and why, and notes for the author.
/// </summary>
public sealed class PortingReport
{
    /// <summary>
    /// Converted files: the mod's file and the file it became, with how, if worth saying.
    /// </summary>
    public List<string> Converted { get; } = [];

    /// <summary>
    /// The mod's files copied unchanged, whose format Obsidian reads as is or which it doesn't read.
    /// </summary>
    public List<string> Copied { get; } = [];

    /// <summary>
    /// Files, or parts of them, that couldn't be converted, by path, with the reason.
    /// </summary>
    public List<(string Path, string Reason)> NotConverted { get; } = [];

    /// <summary>
    /// What the author has to do or check by hand.
    /// </summary>
    public List<string> Notes { get; } = [];

    internal void Skip(string path, string reason) => this.NotConverted.Add((path, reason));

    /// <summary>
    /// The report as text, for the console and the output folder's <c>porting-report.txt</c>.
    /// </summary>
    public override string ToString()
    {
        var text = new StringBuilder();

        text.AppendLine("Porting report");
        text.AppendLine();

        AppendSection(text, "Converted", this.Converted);

        text.AppendLine($"Copied unchanged: {this.Copied.Count} files.");
        text.AppendLine();

        AppendSection(text, "Not converted", this.NotConverted.Select(entry => $"{entry.Path}: {entry.Reason}"));
        AppendSection(text, "Notes", this.Notes);

        return text.ToString();
    }

    private static void AppendSection(StringBuilder text, string title, IEnumerable<string> lines)
    {
        var entries = lines.ToList();

        text.AppendLine($"{title}: {(entries.Count == 0 ? "none" : entries.Count.ToString())}");

        foreach (var line in entries)
            text.AppendLine($"- {line}");

        text.AppendLine();
    }
}
