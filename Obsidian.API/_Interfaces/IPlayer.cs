using System.Net;

namespace Obsidian.API;

public interface IPlayer : ILiving
{
    public Container Inventory { get; }
    public Container EnderInventory { get; }
    public BaseContainer? OpenedContainer { get; set; }

    public List<SkinProperty> SkinProperties { get; set; }

    public ClientInformation ClientInformation { get; }

    public string Username { get; }

    public bool IsOperator { get; }

    public Vector? LastDeathLocation { get; set; }

    public IPAddress? ClientIP { get; }
    public Gamemode Gamemode { get; set; }

    public PlayerAbility Abilities { get; }

    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }
    public short CurrentSlot { get; }

    public int Ping { get; }
    public int FoodLevel { get; set; }
    public int FoodTickTimer { get; set; }
    public int XpLevel { get; set; }
    public int XpTotal { get; set; }

    public double HeadY { get; }

    public float AdditionalHearts { get; set; }
    public float FallDistance { get; set; }
    public float FoodExhaustionLevel { get; set; }
    public float FoodSaturationLevel { get; set; }

    public Task SendMessageAsync(ChatMessage message);
    public Task SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature);
    public Task SetActionBarTextAsync(ChatMessage message);
    public Task SendSoundAsync(ISoundEffect soundEffect);
    public Task KickAsync(ChatMessage reason);
    public Task KickAsync(string reason);
    public Task OpenInventoryAsync(BaseContainer container);
    public Task DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot position);

    /// <summary>
    /// Sends a title message to the player.
    /// </summary>
    /// <param name="title">The title text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public Task SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends a title and subtitle message to the player.
    /// </summary>
    /// <param name="title">The title text</param>
    /// <param name="subtitle">The subtitle text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public Task SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends a subtitle message to the player.
    /// </summary>
    /// <param name="subtitle">The title text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public Task SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends an action bar text to the player.
    /// </summary>
    /// <param name="text">The text of the action bar.</param>
    public Task SendActionBarAsync(string text);

    /// <summary>
    /// Spawns the given particle at the target coordinates.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="x">The target x-coordination.</param>
    /// <param name="y">The target y-coordination.</param>
    /// <param name="z">The target z-coordination.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float extra = 0);

    /// <summary>
    /// Spawns the given particle at the target coordinates and with the given offset.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="x">The target x-coordination.</param>
    /// <param name="y">The target y-coordination.</param>
    /// <param name="z">The target z-coordination.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="offsetX">The x-offset.</param>
    /// <param name="offsetY">The y-offset.</param>
    /// <param name="offsetZ">The z-offset.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float offsetX,
        float offsetY, float offsetZ, float extra = 0);

    /// <summary>
    /// Spawns the given particle at the target position.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="pos">The target position as <see cref="VectorF"/>.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float extra = 0);

    /// <summary>
    /// Spawns the given particle at the target position.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="pos">The target position as <see cref="VectorF"/>.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="offsetX">The x-offset.</param>
    /// <param name="offsetY">The y-offset.</param>
    /// <param name="offsetZ">The z-offset.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float offsetX, float offsetY,
        float offsetZ, float extra = 0);

    /// <summary>
    /// Spawns the given particle at the target coordinates.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="x">The target x-coordination.</param>
    /// <param name="y">The target y-coordination.</param>
    /// <param name="z">The target z-coordination.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="data">The <see cref="ParticleData"/> of the particle.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, ParticleData data, float extra = 0);

    /// <summary>
    /// Spawns the given particle at the target coordinates and with the given offset.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="x">The target x-coordination.</param>
    /// <param name="y">The target y-coordination.</param>
    /// <param name="z">The target z-coordination.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="offsetX">The x-offset.</param>
    /// <param name="offsetY">The y-offset.</param>
    /// <param name="offsetZ">The z-offset.</param>
    /// <param name="data">The <see cref="ParticleData"/> of the particle.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float offsetX,
        float offsetY, float offsetZ, ParticleData data, float extra = 0);

    /// <summary>
    /// Spawns the given particle at the target position.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="pos">The target position as <see cref="VectorF"/>.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="data">The <see cref="ParticleData"/> of the particle.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, ParticleData data, float extra = 0);

    /// <summary>
    /// Spawns the given particle at the target position.
    /// </summary>
    /// <param name="particle">The <see cref="ParticleType"/> to be spawned.</param>
    /// <param name="pos">The target position as <see cref="VectorF"/>.</param>
    /// <param name="count">The amount of particles to be spawned.</param>
    /// <param name="offsetX">The x-offset.</param>
    /// <param name="offsetY">The y-offset.</param>
    /// <param name="offsetZ">The z-offset.</param>
    /// <param name="data">The <see cref="ParticleData"/> of the particle.</param>
    /// <param name="extra">The extra data of the particle, mostly used for speed.</param>
    public Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float offsetX, float offsetY,
        float offsetZ, ParticleData data, float extra = 0);

    public Task<bool> GrantPermissionAsync(string permission);
    public Task<bool> RevokePermissionAsync(string permission);
    public bool HasPermission(string permission);
    public bool HasAnyPermission(IEnumerable<string> permissions);
    public bool HasAllPermissions(IEnumerable<string> permissions);
    public Task SetGamemodeAsync(Gamemode gamemode);

    public Task UpdateDisplayNameAsync(string newDisplayName);

    public ItemStack? GetHeldItem();
    public ItemStack? GetOffHandItem();
}
