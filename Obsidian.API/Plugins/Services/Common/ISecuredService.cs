namespace Obsidian.API.Plugins.Services.Common
{
    /// <summary>
    /// Provides the base interface for services that need permission to be used.
    /// </summary>
    public interface ISecuredService : IService
    {
        internal const string securityExceptionMessage = "Service cannot be used because the plugin wasn't given a permission.";
        
        /// <summary>
        /// Gets a value indicating whether the <see cref="ISecuredService"/> has a permission to be used.
        /// </summary>
        public bool IsUsable { get; }
    }
}
