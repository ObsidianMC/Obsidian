using Microsoft.Extensions.DependencyInjection;

namespace Obsidian.Utilities
{
    public static partial class Extensions
    {
        public static IServiceCollection AddServers(this IServiceCollection services, int serverCount)
        {
            return services;
        }
    }
}
