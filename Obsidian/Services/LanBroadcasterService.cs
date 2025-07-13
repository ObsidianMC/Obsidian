using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Obsidian.Services;
public sealed class LanBroadcasterService : BackgroundService
{
    private readonly IDisposable optionsChanged;
    private ServerConfiguration configuration;

    public LanBroadcasterService(IOptionsMonitor<ServerConfiguration> options)
    {
        this.configuration = options.CurrentValue;
        this.optionsChanged = options.OnChange((configuration, _) =>
        {
            this.configuration = configuration;
        });
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.configuration.AllowLan)
            return;

        var udpClient = new UdpClient("224.0.2.60", 4445);
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.5));
        string? lastMotd = null;
        byte[] bytes = []; // Cached motd as utf-8 bytes
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (this.configuration.Motd != lastMotd)
            {
                lastMotd = this.configuration.Motd;
                bytes = Encoding.UTF8.GetBytes($"[MOTD]{this.configuration.Motd.Replace('[', '(').Replace(']', ')')}[/MOTD][AD]{this.configuration.Port}[/AD]");
            }

            await udpClient.SendAsync(bytes, bytes.Length);
        }
    }

    public override void Dispose()
    {
        this.optionsChanged.Dispose();
        base.Dispose();
    }
}
