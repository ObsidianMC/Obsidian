//     Obsidian.API/IPluginServiceProvider.cs
//     Copyright (C) 2021

using Obsidian.API.Plugins.Services.Common;
using System;

namespace Obsidian.API.Plugins.Services
{
    /// <summary>
    /// Interface for plugin service provider
    /// </summary>
    public interface IPluginServiceProvider : IDisposable
    {
        /// <summary>
        /// Registers a service to be available for other plugins
        /// </summary>
        /// <param name="service">Service to register</param>
        /// <typeparam name="T">Service type</typeparam>
        public void RegisterService<T>(T service) where T : IService;

        /// <summary>
        /// Gets a service
        /// </summary>
        /// <typeparam name="T">Type of the service</typeparam>
        /// <returns>The service</returns>
        /// <exception cref="InvalidOperationException">Thrown when the specified service is not registered</exception>
        public T GetService<T>() where T : IService;
        
        /// <summary>
        /// Tries to get a service
        /// </summary>
        /// <param name="service">Service instance</param>
        /// <typeparam name="T">Type of the service</typeparam>
        /// <returns>True if service was found</returns>
        public bool TryGetService<T>(out T service) where T : class, IService;

        /// <summary>
        /// Gets a logger for a plugin
        /// </summary>
        /// <param name="plugin">Plugin for which create a service</param>
        /// <returns>Logger instance</returns>
        public ILogger GetLogger(Plugin plugin);
    }
}