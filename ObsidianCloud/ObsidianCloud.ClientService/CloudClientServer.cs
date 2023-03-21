using Obsidian.API;
using Obsidian;
using Obsidian.API.Boss;
using Obsidian.API.Crafting;
using System.Net.Sockets;
using System.Net;
using Obsidian.Utilities;
using System;
using System.Reflection;
using Obsidian.Hosting;
using Obsidian.Net.Rcon;

namespace ObsidianCloud.ClientService;

public class CloudClientServer : Server
{
    public CloudClientServer(IHostApplicationLifetime lifetime, IServerEnvironment environment, ILogger<Server> logger, RconServer rconServer) : base(lifetime, environment, logger, rconServer)
    {

    }

    public override void CreateDirectories()
    {
        // We don't do this on the cloud.
    }
}
