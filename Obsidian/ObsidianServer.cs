using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.Data;
using Obsidian.Utilities;

namespace Obsidian
{
    public sealed class ObsidianServer : BackgroundService
    {
        private readonly GlobalConfig _config;
        private readonly ServerRegistry _serverRegistry;
        private readonly ILogger<ObsidianServer> _logger;
        private readonly IServiceProvider _services;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly string _version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion ?? "1.0.0";
        private IServiceScope _scope;

        // Will implement multi-server functionality later
        // private readonly Dictionary<int, Server> servers = new();
        private Server _server;

        public ObsidianServer
        (
            IOptions<GlobalConfig> config, 
            ServerRegistry serverRegistry, 
            ILogger<ObsidianServer> logger,
            IServiceProvider services,
            IHostApplicationLifetime appLifetime
        )
        {
            _config = config.Value;
            _serverRegistry = serverRegistry;
            _logger = logger;
            _services = services;
            _appLifetime = appLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;

            _logger.LogInformation("Starting Core Service...");

            IServiceScope scope = null;
            try
            {
                // Create a new scope for this session
                scope = _services.CreateScope();

                // Register with the cancellation token so we can stop listening to client events if the service
                // is shutting down or being disposed.
                stoppingToken.Register(OnStopping);

                _logger.LogInformation("Running database migrations.");
                await scope.ServiceProvider.GetRequiredService<ServerContext>()
                    .Database.MigrateAsync(stoppingToken);

                // Register MediatR listensers

                // Promote scope to a variable
                _scope = scope;

                _logger.LogInformation("Starting server(s)");
                foreach (Server server in _serverRegistry)
                {
                    await server.StartServerAsync();
                }

                // Block until shutdown.
                stoppingToken.WaitHandle.WaitOne();
            }
            catch
            {
                OnStopping();
                throw;
            }
            
            void OnStopping()
            {
                _logger.LogInformation("Stopping background service.");
                try
                {
                    _appLifetime.StopApplication();
                }
                finally
                {
                    scope?.Dispose();
                    _scope = null;
                }
            }
        }

    }
}
