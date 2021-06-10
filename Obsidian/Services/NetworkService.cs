using System.Net.Http;

namespace Obsidian.Services
{
    /// <summary>
    /// Exposes a <see cref="NetworkService"/> which wraps a configured <see cref="HttpClient"/>.
    /// </summary>
    public interface INetworkService
    {

    }
    public sealed class NetworkService : INetworkService
    {
        private readonly HttpClient _httpClient;

        public NetworkService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
    }
}
