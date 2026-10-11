using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Obsidian.Services;
public sealed partial class LanBroadcasterService : BackgroundService
{
    private readonly IDisposable optionsChanged;
    private readonly ILogger<LanBroadcasterService> logger;
    private readonly IServer server;
    private ServerConfiguration configuration;

    public LanBroadcasterService(IOptionsMonitor<ServerConfiguration> options, ILogger<LanBroadcasterService> logger, IServer server)
    {
        this.configuration = options.CurrentValue;
        this.optionsChanged = options.OnChange((configuration, _) =>
        {
            this.configuration = configuration;
        });
        this.logger = logger;
        this.server = server;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.configuration.AllowLan)
            return Task.CompletedTask;

        // The server's port is the bound one once it listens, which matters when the configured port is 0.
        return BroadcastAsync(() => (this.configuration.Motd, this.server.Port), this.logger, stoppingToken);
    }

    /// <summary>
    /// Advertises a server to the LAN until <paramref name="stoppingToken"/> is cancelled, like vanilla's
    /// <c>LanServerPinger</c>: a <c>[MOTD]...[/MOTD][AD]port[/AD]</c> datagram to 224.0.2.60:4445 every 1.5 seconds.
    /// </summary>
    /// <param name="advertisement">The MOTD and port to advertise, read before each datagram.</param>
    internal static async Task BroadcastAsync(Func<(string Motd, int Port)> advertisement, ILogger logger, CancellationToken stoppingToken)
    {
        using var udpClient = new UdpClient("224.0.2.60", 4445);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.5));
        (string Motd, int Port) last = default;
        byte[] bytes = []; // Cached advertisement as utf-8 bytes

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var current = advertisement();
                if (current != last)
                {
                    last = current;
                    bytes = Encoding.UTF8.GetBytes($"[MOTD]{current.Motd.Replace('[', '(').Replace(']', ')')}[/MOTD][AD]{current.Port}[/AD]");
                }

                await udpClient.SendAsync(bytes, bytes.Length);
            }
        }
        catch (OperationCanceledException)
        {
            // The server is stopping.
        }
        catch (Exception ex)
        {
            Log.BroadcastFailed(logger, ex);
        }
    }

    public override void Dispose()
    {
        this.optionsChanged.Dispose();
        base.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Broadcasting to the LAN failed")]
        public static partial void BroadcastFailed(ILogger logger, Exception exception);
    }
}
