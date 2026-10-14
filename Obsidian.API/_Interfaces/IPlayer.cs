using Obsidian.API.Inventory;
using Obsidian.API.Utilities.Concurrency;
using System.ComponentModel;

namespace Obsidian.API;

public interface IPlayer : ILiving
{
    public Progress.IStatistics Statistics => throw new NotSupportedException("Player statistics are unavailable.");
    public Advancements.IAdvancements Advancements =>
        throw new NotSupportedException("Player advancements are unavailable.");

    public ValueTask ShowDialogAsync(Registry.Codecs.Dialogs.DialogElement dialog);
    public ValueTask ClearDialogAsync();

    public byte CurrentContainerId { get; }
    public IScoreboard? CurrentScoreboard { get; set; }

    public PlayerInput Input { get; set; }

    /// <summary>
    /// The mob the player rides, or null.
    /// </summary>
    public IEntity? Vehicle => null;

    /// <summary>
    /// Mounts the player on a mob, like vanilla's <c>startRiding</c>: in its next free seat, if it's alive, in the
    /// player's level and has one. A plugin's mob carries as many players as its type has passenger attachments, at
    /// least one, and its first passenger steers it.
    /// </summary>
    /// <returns>Whether the player mounted it.</returns>
    public bool StartRiding(IEntity vehicle) => false;

    /// <summary>
    /// Dismounts the player from what it rides, if anything.
    /// </summary>
    public void StopRiding()
    {
    }

    public ConcurrentHashSet<long> LoadedChunks { get; }

    public Container Inventory { get; }
    public Container EnderInventory { get; }
    public BaseContainer? OpenedContainer { get; set; }

    /// <summary>
    /// The item currently being held by the player. Not null if they're in a container screen and carrying the item.
    /// </summary>
    public ItemStack? CarriedItem { get; set; }

    public List<short> DraggedSlots { get; }
    public List<SkinProperty> SkinProperties { get; set; }

    public ClientInformation ClientInformation { get; set; }

    public string Username { get; }

    public Vector? LastDeathLocation { get; set; }

    public string? ClientIP { get; }
    public GameMode GameMode { get; set; }

    public PlayerAbility Abilities { get; set; }

    /// <summary>
    /// What the player's movement grants add up to (see <see cref="SetMovementAbilities"/>).
    /// </summary>
    public MovementAbilities MovementAbilities => default;

    /// <summary>
    /// Grants the player movement abilities under <paramref name="id"/>, replacing an earlier grant with the same id;
    /// grants with other ids add up (see <see cref="MovementAbilities.Combine"/>). Grants aren't saved, and a plugin's
    /// are taken back when it unloads.
    /// </summary>
    /// <param name="id">The grant's identifier, like <c>ruby:cloud_boots</c>.</param>
    /// <param name="abilities">The abilities; negative extra jumps count as none.</param>
    public void SetMovementAbilities(string id, MovementAbilities abilities)
    {
    }

    /// <summary>
    /// Takes back the grant <see cref="SetMovementAbilities"/> made under <paramref name="id"/>.
    /// </summary>
    /// <returns>Whether there was one.</returns>
    public bool RemoveMovementAbilities(string id) => false;

    public bool IsDragging { get; set; }

    /// <summary>
    /// How the player started dragging items across slots, while <see cref="IsDragging"/>:
    /// <see cref="DraggingState.StartLeft"/> splits the carried stack evenly, <see cref="DraggingState.StartRight"/>
    /// places one item per slot and <see cref="DraggingState.StartMiddle"/> (creative only) a full stack.
    /// </summary>
    public DraggingState DragStart { get; set; }

    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }
    public short CurrentHeldItemSlot { get; set; }

    public int TeleportId { get; set; }
    public int Ping { get; }
    public int FoodLevel { get; set; }
    public int FoodTickTimer { get; set; }
    public int XpLevel { get; set; }
    public int XpTotal { get; set; }

    public double HeadY { get; }

    public float Absorption { get; set; }
    public float FallDistance { get; set; }
    public float FoodExhaustionLevel { get; set; }
    public float FoodSaturationLevel { get; set; }

    public Task SaveAsync();
    public Task LoadAsync(bool loadFromPersistentWorld = true);

    /// <summary>
    /// Sends a packet to the player's client immediately. Players without a client ignore it.
    /// </summary>
    /// <returns>Whether the packet was sent.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public bool SendPacket(IClientboundPacket packet);

    /// <summary>
    /// Queues a packet for the player's client. Players without a client ignore it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ValueTask QueuePacketAsync(IClientboundPacket packet);

    // Server hooks for what a client has been shown. The defaults suit players without a client, which have nothing to
    // keep in sync; the server's network player overrides them.
    internal ValueTask SynchronizeTrackedEntitiesAsync() => default;
    internal void TrackEntity(IEntity entity) { }
    internal void ForgetVisiblePlayer(IPlayer player) { }

    // Runs after every level has ticked, like vanilla's per-player inventory tick.
    internal ValueTask TickAfterLevelsAsync() => default;

    public ValueTask DisconnectAsync(ChatMessage reason);
    public ValueTask SendMessageAsync(ChatMessage message);
    public ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature);
    public ValueTask SetActionBarTextAsync(ChatMessage message);
    public ValueTask SendSoundAsync(ISoundEffect soundEffect);
    public ValueTask KickAsync(ChatMessage reason);
    public ValueTask KickAsync(string reason);
    public ValueTask OpenInventoryAsync(BaseContainer container);
    /// <summary>
    /// Shows <paramref name="scoreboard"/>'s objective and scores to the player in <paramref name="position"/>. The
    /// player must have been added to the scoreboard first.
    /// </summary>
    /// <remarks>
    /// The packets are queued with the scoreboard's other updates and removals, which the player gets in order, and
    /// sent shortly after this returns. A packet sent to the player directly afterwards may arrive first, so change
    /// the scoreboard through <see cref="IScoreboard"/> rather than with packets of your own.
    /// </remarks>
    public ValueTask DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot position);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ValueTask UpdatePlayerInfoAsync();

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ValueTask SendPlayerInfoAsync();

    public Task<bool> UpdateChunksAsync(bool unloadAll = false, int distance = 0);
    public Task RespawnAsync(DataKept dataKept = DataKept.Metadata);

    /// <summary>
    /// Sends the player to the spawn of a dimension of their world, by its name: the world's own (like
    /// <c>minecraft:overworld</c>), vanilla's other dimensions, or a plugin's, like <c>ruby:crystal_world</c>.
    /// </summary>
    /// <returns>Whether the player was sent: not when their world has no such dimension, or they're dead.</returns>
    public Task<bool> TeleportToDimensionAsync(string dimension);

    /// <summary>
    /// Sends a title message to the player.
    /// </summary>
    /// <param name="title">The title text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public ValueTask SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends a title and subtitle message to the player.
    /// </summary>
    /// <param name="title">The title text</param>
    /// <param name="subtitle">The subtitle text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public ValueTask SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends a subtitle message to the player.
    /// </summary>
    /// <param name="subtitle">The title text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public ValueTask SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends an action bar text to the player.
    /// </summary>
    /// <param name="text">The text of the action bar.</param>
    public ValueTask SendActionBarAsync(string text);

    public ValueTask SpawnParticleAsync(ParticleData data);

    public Task<bool> GrantPermissionAsync(string permission);
    public Task<bool> RevokePermissionAsync(string permission);
    public bool HasPermission(string permission);
    public bool HasAnyPermission(IEnumerable<string> permissions);
    public bool HasAllPermissions(IEnumerable<string> permissions);
    public ValueTask SetGamemodeAsync(GameMode gamemode);

    public ValueTask UpdateDisplayNameAsync(string newDisplayName);

    public ItemStack? GetHeldItem();
    public ItemStack? GetOffHandItem();
}
