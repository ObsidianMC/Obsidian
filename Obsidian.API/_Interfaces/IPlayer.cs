using Microsoft.Extensions.Logging;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities.Concurrency;
using System.ComponentModel;
using System.Text.Json;

namespace Obsidian.API;

public interface IPlayer : ILiving
{
    /// <summary>
    /// The player's statistics. A player that keeps none, like another engine's by default, reads zero for each and
    /// drops increments, so that gameplay counting them works for any player.
    /// </summary>
    public Progress.IStatistics Statistics => Progress.NoStatistics.Instance;
    public Advancements.IAdvancements Advancements =>
        throw new NotSupportedException("Player advancements are unavailable.");

    public IServer Server { get; }
    public ILogger Logger { get; }

    public ValueTask ShowDialogAsync(Registry.Codecs.Dialogs.DialogElement dialog);
    public ValueTask ClearDialogAsync();

    public byte CurrentContainerId { get; }
    public IScoreboard? CurrentScoreboard { get; set; }

    public PlayerInput Input { get; set; }

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

    /// <summary>
    /// How far the player is through their current level, from 0 to 1.
    /// </summary>
    public float XpP { get; set; }

    /// <summary>
    /// Whether the player is between dying and coming back in a level. They can't act meanwhile.
    /// </summary>
    public bool Respawning { get; }

    public double HeadY { get; }

    public float Absorption { get; set; }
    public float FallDistance { get; set; }
    public float FoodExhaustionLevel { get; set; }
    public float FoodSaturationLevel { get; set; }

    public Task SaveAsync();
    public Task LoadAsync(bool loadFromPersistentWorld = true);

    /// <summary>
    /// Sends a packet to the player's client without packet handlers: immediately, or right after the packets queued
    /// before it that are still on their way. Players without a client ignore it.
    /// </summary>
    /// <returns>Whether the packet was sent or is on its way.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public bool SendPacket(IClientboundPacket packet);

    /// <summary>
    /// Queues a packet for the player's client. Packet handlers see it in the client's outgoing pipeline, after the
    /// call returned; the call never waits for them. Players without a client ignore it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ValueTask QueuePacketAsync(IClientboundPacket packet);

    // What a client makes its player do, for plugins driving a player without one, like a server-side player. Each
    // runs as the client's packets do; a player moves and looks through UpdateAsync.

    /// <summary>
    /// Attacks <paramref name="target"/> with the held item, like a client's attack: only in reach, and through
    /// <see cref="Events.PlayerAttackEntityEventArgs"/>, which plugins can cancel. The hit lands on the target's
    /// level tick.
    /// </summary>
    /// <returns>Whether the target was in reach and the attack's event wasn't cancelled.</returns>
    public ValueTask<bool> AttackAsync(IEntity target, bool sneaking = false);

    /// <summary>
    /// Interacts with <paramref name="target"/>, like a client's use on it: through
    /// <see cref="Events.EntityInteractEventArgs"/>, then the entity's interaction, like boarding a saddled pig, or
    /// the item's use on it. Those run on the target's level tick.
    /// </summary>
    /// <returns>Whether the target was in reach and the interaction's event wasn't cancelled.</returns>
    public ValueTask<bool> InteractAsync(IEntity target, InteractionHand hand = InteractionHand.MainHand,
        bool sneaking = false);

    /// <summary>
    /// Uses the item in <paramref name="hand"/>, like a client's use in the air: through
    /// <see cref="Events.PlayerInteractEventArgs"/>, then the item's behaviour, or the player starts eating, drawing a
    /// bow or the like until <see cref="ReleaseUsingItemAsync"/>.
    /// </summary>
    public ValueTask UseItemAsync(InteractionHand hand = InteractionHand.MainHand);

    /// <summary>
    /// Lets go of the item the player is using, like a client does: a drawn bow or trident fires, and eating stops.
    /// </summary>
    public ValueTask ReleaseUsingItemAsync();

    /// <summary>
    /// Stops using an item without its effect, as when the player picks another hotbar slot.
    /// </summary>
    public void StopUsingItem();

    /// <summary>
    /// Jabs with the spear in the main hand, like a client's stab, once the attack has recharged.
    /// </summary>
    public ValueTask StabAsync();

    /// <summary>
    /// Gives the player experience points, which can level them up, as an orb or an advancement's reward does. Unlike
    /// an orb's, they don't mend items.
    /// </summary>
    public ValueTask GiveExperienceAsync(int points);

    /// <summary>
    /// Starts gliding, like a client's jump in mid-air: with a glider on the chest or the ability to glide, and out of
    /// fluids.
    /// </summary>
    /// <returns>Whether the player glides.</returns>
    public bool TryStartGliding();

    // Server hooks for what a client has been shown. The defaults suit players without a client, which have nothing to
    // keep in sync; the server's network player overrides them.
    internal ValueTask SynchronizeTrackedEntitiesAsync() => default;
    internal void TrackEntity(IEntity entity) { }
    internal void UntrackEntity(int entityId) { }
    internal void ForgetVisiblePlayer(IPlayer player) { }

    // After entity tracking each tick: what changed in the player's attachments and movement abilities.
    internal void SynchronizeOwnState() { }

    // The recipe book's changes, and the crafting result they change.
    internal ValueTask SynchronizeRecipeBookAsync() => default;

    // The window the player has open, or else their inventory, sent again whole.
    internal ValueTask RefreshWindowAsync() => default;

    // Closes the player's container for the server, as when its block goes or the plugin unloading owns it.
    internal ValueTask CloseContainerAsync(Plugins.IPluginContainer? unloading) => default;

    // Gameplay every server player takes part in, with or without a client: hunger, item wear and advancements.
    // Gameplay code calls these rather than testing for a player type. The defaults are for players the server didn't
    // make.
    internal void AddExhaustion(float amount) { }
    internal ValueTask DamageInventoryItemAsync(int slot, int amount, bool armor = false, ItemStack? expected = null) =>
        default;
    internal void RecordProgressCraft(string identifier, ItemStack result) { }
    internal ValueTask RecordProgressKillAsync(IEntity victim) => default;

    // Runs after every level has ticked, like vanilla's per-player inventory tick.
    internal ValueTask TickAfterLevelsAsync() => default;

    // Whether the player is still on the server: one who left can't take back their thrown trident.
    internal bool Connected => true;

    // Combat: whether the player reaches the target with the item, by default the held one, and the hit an attack's
    // event lets through. The defaults are a plain hit in reach.
    internal bool CanAttack(IEntity target, ItemStack? item = null) => target.IsInRange(this, 4);
    internal ValueTask ResolveAttackAsync(Events.PlayerAttackEntityEventArgs attack) =>
        attack.Entity.DamageAsync(this, attack.Damage > 0 ? attack.Damage : 1);

    // Picking up an arrow or trident's item, and an experience orb's experience; whether the player took it. An orb
    // goes to the nearest player, so by default its experience is given, lest such a player hold on to it.
    internal ValueTask<bool> ReceiveProjectileAsync(ItemStack item, bool creativeOnly) => new(false);
    internal async ValueTask<bool> PickUpExperienceAsync(int experience)
    {
        await this.GiveExperienceAsync(experience);
        return true;
    }

    // Items and windows: sending a slot of the player's inventory again, the closed menus' hooks, a window button's
    // click, the enchanting table's offers, and counting a drop from the creative screen, which fails when too fast.
    internal ValueTask SyncInventorySlotAsync(int slot) => default;
    internal void RunMenusClosed() { }
    internal ValueTask ClickContainerButtonAsync(int containerId, int button) => default;
    internal IEnumerable<IClientboundPacket> EnchantingProperties() => [];
    internal bool CountCreativeDrop() => false;

    // Using items: starting to eat, and an item behaviour's use, with the player's inventory changes sent after it and
    // the use counted in their statistics.
    internal void StartEating(InteractionHand hand) { }
    internal ValueTask<bool> RunItemUseAsync(Item item, Func<ValueTask<bool>> use) => use();

    // Movement: the jump key, which double jumps, and taking back an unloading plugin's grants.
    internal void TrackJumpInput(PlayerInput previous, PlayerInput current) { }
    internal void ReleaseMovementAbilities(Plugins.IPluginContainer plugin) { }

    // Travel: leaving a portal's countdown, and moving to another level, as the end of the dragon fight does.
    internal Task CancelPortalTravelAsync() => Task.CompletedTask;
    internal Task TransferDimensionAsync(ILevel destination, VectorD? position = null, Angle? yaw = null,
        Angle? pitch = null, VectorD? motion = null, bool portal = false, bool relativeMotion = false) =>
        Task.CompletedTask;

    // Advancements: a vanilla trigger's event, a plugin trigger's criteria matched against their JSON conditions,
    // sending the changes to the client, a grant run in the player's turn, and an advancement's reward.
    internal ValueTask FireProgressAsync(string trigger, ItemStack? item = null, IEntity? entity = null,
        IBlock? block = null, Vector? position = null, string? recipe = null, string? from = null,
        string? to = null, int? levels = null) => default;
    internal ValueTask FireAdvancementTriggerAsync(string trigger, Func<JsonElement, bool> matches) => default;
    internal ValueTask FlushAdvancementsAsync() => default;
    internal ValueTask<bool> GrantAdvancementAsync(Func<ValueTask<bool>> grant) => grant();
    internal ValueTask RewardAdvancementAsync(Advancements.Advancement advancement) => default;

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
