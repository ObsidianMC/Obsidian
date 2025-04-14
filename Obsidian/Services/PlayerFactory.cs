using Microsoft.Extensions.DependencyInjection;
using Obsidian.Entities;

namespace Obsidian.Services;
public sealed class PlayerFactory(IServiceProvider provider)
{

  

    public sealed class PlayerBuilder
    {
        public string Username { get; private set; }

        public IWorld World { get; private set; }

        public Guid Uuid { get; private set; }

        public IClient Client { get; private set; }

        public PlayerBuilder WithUsername(string username)
        {
            this.Username = username;

            return this;
        }

        public PlayerBuilder WithWorld(IWorld world)
        {
            this.World = world;
            return this;
        }

        public PlayerBuilder WithUuid(Guid uuid)
        {
            this.Uuid = uuid;
            return this;
        }

        public PlayerBuilder WithClient(IClient client)
        {
            this.Client = client;

            return this;
        }
    }

}
