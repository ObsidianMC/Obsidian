using System.Diagnostics.Metrics;

namespace Obsidian.Services;
public sealed class ServerMetrics
{
    public const string MeterName = "Obsidian.Server";

    private readonly Counter<long> bytesSent;
    private readonly Counter<long> bytesReceived;

    public ServerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        this.bytesSent = meter.CreateCounter<long>($"{MeterName}.bytes_sent.count");
        this.bytesReceived = meter.CreateCounter<long>($"{MeterName}.bytes_received.count");
    }

    public void AddBytesReceived(int count = 1) => this.bytesReceived.Add(count);

    public void AddBytesSent(int count = 1) => this.bytesSent.Add(count);
}
