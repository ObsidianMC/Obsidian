using System.IO;
using System.Text;
using System.Threading;

namespace Obsidian.Integrated;

/// <summary>
/// Writes an integrated server's events to its client, one line each and flushed, so lines never interleave.
/// </summary>
public sealed class IntegratedEventWriter(TextWriter output)
{
    private readonly Lock gate = new();

    /// <summary>
    /// Takes standard output for the events, and sends whatever else writes to the console to standard error instead,
    /// so stray output can't corrupt the control channel.
    /// </summary>
    public static IntegratedEventWriter ForStandardOutput()
    {
        var output = new StreamWriter(System.Console.OpenStandardOutput(), new UTF8Encoding(false));
        System.Console.SetOut(System.Console.Error);

        return new IntegratedEventWriter(output);
    }

    public void Write(IntegratedEvent integratedEvent)
    {
        var line = IntegratedProtocol.FormatEvent(integratedEvent);

        lock (this.gate)
        {
            // A client that went away closed the pipe; the server still has to stop cleanly (end of input stops it).
            try
            {
                output.Write(line);
                output.Write('\n');
                output.Flush();
            }
            catch (IOException)
            {
            }
        }
    }
}
