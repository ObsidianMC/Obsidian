//     Obsidian/PluginServiceProvider.cs
//     Copyright (C) 2021

using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using Obsidian.API.Plugins.Services.Common;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ILogger = Obsidian.API.Plugins.Services.ILogger;

namespace Obsidian.Plugins.Services
{
    public class PluginServiceProvider : IPluginServiceProvider
    {
        private readonly PluginManager pluginManager;
        private readonly List<IService> services = new();

        public IReadOnlyList<IService> Services => services.ToImmutableList();

        public PluginServiceProvider(PluginManager pluginManager)
        {
            this.pluginManager = pluginManager;
        }

        public void RegisterService<T>(T service) where T : IService
        {
            if (services.Contains(service))
                throw new InvalidOperationException("Service already registered");
            
            services.Add(service);
        }

        public T GetService<T>() where T : IService => (T)services[services.FindIndex(x => x.GetType() == typeof(T))];

        public bool TryGetService<T>(out T service) where T : class, IService
        {
            var maybeService = services.Find(x => x.GetType() == typeof(T));
            if (maybeService is not null)
            {
                service = (T)maybeService;
                return true;
            }

            service = null!;
            return false;
        }

#pragma warning disable CA1822
        public ILogger GetLogger(Plugin plugin) => new LoggerService(plugin, LogLevel.Debug);
#pragma warning restore CA1822

        public IServer GetServer() => pluginManager.server;

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            foreach (var service in services) service.Dispose();
            services.Clear();
        }
    }
}