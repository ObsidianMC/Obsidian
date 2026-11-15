using Microsoft.Extensions.Logging;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities.Concurrency;
using System.ComponentModel;
using System.Text.Json;

namespace Obsidian.API;

/// <summary>
/// A player on the server, connected through a client or added by the server (see
/// <see cref="IServer.AddServerPlayerAsync"/>).
/// </summary>
public interface IPlayer : ILiving
{
    /// <summary>
    /// The player's statistics. A player that keeps none, like another engine's by default, reads zero for each and
    /// drops increments, so that gameplay counting them works for any player.
    /// </summary>
    public Progress.IStatistics Statistics => Progress.NoStatistics.Instance;
    /// <summary>
    /// Gets the player's advancements.
    /// </summary>
    /// <exception cref="NotSupportedException">The player keeps no advancements.</exception>
    public Advancements.IAdvancements Advancements =>
        throw new NotSupportedException("Player advancements are unavailable.");

    /// <summary>
    /// Gets the server the player is on.
    /// </summary>
    public IServer Server { get; }
    /// <summary>
    /// Gets the player's logger.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Shows <paramref name="dialog"/> on the player's screen. The player's choices arrive as
    /// <see cref="Events.DialogActionEventArgs"/>.
    /// </summary>
    /// <param name="dialog">The dialog to show.</param>
    /// <exception cref="ArgumentException">The dialog is invalid, such as an unknown dialog type.</exception>
    public ValueTask ShowDialogAsync(Registry.Codecs.Dialogs.DialogElement dialog);
    /// <summary>
    /// Closes the dialog the player is shown, if any.
    /// </summary>
    public ValueTask ClearDialogAsync();

    /// <summary>
    /// Gets the window id of the container the player has open, from 1 to 255; it moves on with each container opened.
    /// </summary>
    public byte CurrentContainerId { get; }
    /// <summary>
    /// Gets or sets the scoreboard shown to the player. Setting it adds the player to the new scoreboard and removes them
    /// from the old one.
    /// </summary>
    public IScoreboard? CurrentScoreboard { get; set; }

    /// <summary>
    /// Gets or sets the movement keys the player's client last reported held.
    /// </summary>
    public PlayerInput Input { get; set; }

    /// <summary>
    /// Gets the chunks the player should have loaded around them, as packed chunk coordinates.
    /// </summary>
    public ConcurrentHashSet<long> LoadedChunks { get; }

    /// <summary>
    /// Gets the player's inventory, numbered like vanilla's player window: crafting 0 to 4, armor 5 to 8, main 9 to 35,
    /// hotbar 36 to 44 and off hand 45.
    /// </summary>
    public Container Inventory { get; }
    /// <summary>
    /// Gets the player's ender chest.
    /// </summary>
    public Container EnderInventory { get; }
    /// <summary>
    /// Gets or sets the container the player has open, or <see langword="null"/> if none. Use
    /// <see cref="OpenInventoryAsync"/> to open one on the player's screen.
    /// </summary>
    public BaseContainer? OpenedContainer { get; set; }

    /// <summary>
    /// The item currently being held by the player. Not null if they're in a container screen and carrying the item.
    /// </summary>
    public ItemStack? CarriedItem { get; set; }

    /// <summary>
    /// Gets the slots the player has dragged items across, while <see cref="IsDragging"/>.
    /// </summary>
    public List<short> DraggedSlots { get; }
    /// <summary>
    /// Gets or sets the player's profile properties, such as their signed skin texture.
    /// </summary>
    public List<SkinProperty> SkinProperties { get; set; }

    /// <summary>
    /// Gets or sets the settings the player's client sent, like its language and view distance.
    /// </summary>
    public ClientInformation ClientInformation { get; set; }

    /// <summary>
    /// Gets the player's username.
    /// </summary>
    public string Username { get; }

    /// <summary>
    /// Gets or sets where the player last died, or <see langword="null"/> if they haven't.
    /// </summary>
    public Vector? LastDeathLocation { get; set; }

    /// <summary>
    /// Gets the IP address of the player's client, or <see langword="null"/> for a player without one.
    /// </summary>
    public string? ClientIP { get; }
    /// <summary>
    /// Gets or sets the player's game mode, which also sets their <see cref="Abilities"/>. Setting it doesn't tell the
    /// player's client; use <see cref="SetGamemodeAsync"/> for that.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The game mode set is unknown.</exception>
    public GameMode GameMode { get; set; }

    /// <summary>
    /// Gets or sets the player's abilities, like flying and invulnerability.
    /// </summary>
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

    /// <summary>
    /// Gets or sets a value indicating whether the player is dragging items across slots.
    /// </summary>
    public bool IsDragging { get; set; }

    /// <summary>
    /// How the player started dragging items across slots, while <see cref="IsDragging"/>:
    /// <see cref="DraggingState.StartLeft"/> splits the carried stack evenly, <see cref="DraggingState.StartRight"/>
    /// places one item per slot and <see cref="DraggingState.StartMiddle"/> (creative only) a full stack.
    /// </summary>
    public DraggingState DragStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the player is in a bed.
    /// </summary>
    public bool Sleeping { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether the player has a horse's inventory open.
    /// </summary>
    public bool InHorseInventory { get; set; }

    /// <summary>
    /// Gets or sets the player's attack timer, in ticks.
    /// </summary>
    public short AttackTime { get; set; }
    /// <summary>
    /// Gets or sets the ticks since the player died.
    /// </summary>
    public short DeathTime { get; set; }
    /// <summary>
    /// Gets or sets the ticks left of the player's hurt animation.
    /// </summary>
    public short HurtTime { get; set; }
    /// <summary>
    /// Gets or sets the ticks the player has been in bed.
    /// </summary>
    public short SleepTimer { get; set; }
    /// <summary>
    /// Gets or sets the player's selected hotbar slot. Set it to a hotbar index from 0 to 8; it reads back as the
    /// <see cref="Inventory"/> slot, from 36 to 44.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The value set is outside 0 to 8.</exception>
    public short CurrentHeldItemSlot { get; set; }

    /// <summary>
    /// Gets or sets the id of the last teleport sent to the player's client, which it confirms.
    /// </summary>
    public int TeleportId { get; set; }
    /// <summary>
    /// Gets the player's round-trip time in milliseconds.
    /// </summary>
    public int Ping { get; }
    /// <summary>
    /// Gets or sets the player's food level, from 0 to 20.
    /// </summary>
    public int FoodLevel { get; set; }
    /// <summary>
    /// Gets or sets the ticks counted towards the player's next healing or starving from their food level.
    /// </summary>
    public int FoodTickTimer { get; set; }
    /// <summary>
    /// Gets or sets the player's experience level.
    /// </summary>
    public int XpLevel { get; set; }
    /// <summary>
    /// Gets or sets the player's total experience points.
    /// </summary>
    public int XpTotal { get; set; }

    /// <summary>
    /// How far the player is through their current level, from 0 to 1.
    /// </summary>
    public float XpP { get; set; }

    /// <summary>
    /// Whether the player is between dying and coming back in a level. They can't act meanwhile.
    /// </summary>
    public bool Respawning { get; }

    /// <summary>
    /// Gets the y coordinate of the player's eyes.
    /// </summary>
    public double HeadY { get; }

    /// <summary>
    /// Gets or sets the player's absorption health, in half-hearts.
    /// </summary>
    public float Absorption { get; set; }
    /// <summary>
    /// Gets or sets how far the player has fallen since last on the ground, in blocks.
    /// </summary>
    public float FallDistance { get; set; }
    /// <summary>
    /// Gets or sets the player's food exhaustion, which uses up saturation and food as it grows.
    /// </summary>
    public float FoodExhaustionLevel { get; set; }
    /// <summary>
    /// Gets or sets the player's food saturation, used up before their food level.
    /// </summary>
    public float FoodSaturationLevel { get; set; }

    /// <summary>
    /// Saves the player's data, statistics and advancements.
    /// </summary>
    /// <remarks>
    /// It waits for the player's tick, grant or save under way. Called from the player's own, it saves at once as part
    /// of it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Called from inside a level's tick, or another player's tick, grant or save (a plugin's event handler run by one,
    /// say): it would wait for a tick that may be waiting for it. Plugins should save outside ticks: from a scheduled
    /// task or a command, say.
    /// </exception>
    public Task SaveAsync();

    /// <summary>
    /// Loads the player's saved data, statistics and advancements.
    /// </summary>
    /// <param name="loadFromPersistentWorld">Whether to put the player in the world they were saved in.</param>
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
    // A level's tracking pass, which does nothing once the player is in another level.
    internal ValueTask SynchronizeTrackedEntitiesAsync(ILevel level) => default;
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
    internal Task TransferDimensionAsync(ILevel destination, VectorD? position = null, float? yaw = null,
        float? pitch = null, VectorD? motion = null, bool portal = false, bool relativeMotion = false) =>
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

    /// <summary>
    /// Disconnects the player and waits for their leave, which waits for their tick under way. Called from inside a
    /// level's tick, or any player's tick, grant or save, the leave finishes after the ticks, without being waited for.
    /// </summary>
    public ValueTask DisconnectAsync(ChatMessage reason);
    /// <summary>
    /// Sends <paramref name="message"/> to the player's chat as a system message.
    /// </summary>
    /// <param name="message">The message to send.</param>
    public ValueTask SendMessageAsync(ChatMessage message);
    /// <summary>
    /// Sends <paramref name="message"/> as a signed chat message from another player. Not implemented for connected
    /// players: it throws <see cref="NotImplementedException"/>.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="sender">The UUID of the message's sender.</param>
    /// <param name="messageSignature">The message's signature.</param>
    public ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature);
    /// <summary>
    /// Shows <paramref name="message"/> above the player's hotbar.
    /// </summary>
    /// <param name="message">The message to show.</param>
    public ValueTask SetActionBarTextAsync(ChatMessage message);
    /// <summary>
    /// Plays a sound to the player only, at the effect's position or from its entity.
    /// </summary>
    /// <param name="soundEffect">The sound to play.</param>
    public ValueTask SendSoundAsync(ISoundEffect soundEffect);
    /// <summary>Kicks the player; it waits for their leave like <see cref="DisconnectAsync"/>.</summary>
    public ValueTask KickAsync(ChatMessage reason);

    /// <inheritdoc cref="KickAsync(ChatMessage)"/>
    public ValueTask KickAsync(string reason);
    /// <summary>
    /// Opens <paramref name="container"/> on the player's screen, closing the container they have open. Does nothing
    /// for a dead player.
    /// </summary>
    /// <param name="container">The container to open.</param>
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

    /// <summary>
    /// Sends the player the tab list entries of every online player, and their own abilities.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ValueTask UpdatePlayerInfoAsync();

    /// <summary>
    /// Sends the player their inventory, their own entity data and their selected hotbar slot.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ValueTask SendPlayerInfoAsync();

    /// <summary>
    /// Sends the player's client the chunks around them and unloads those out of view. A player without a client has
    /// none to update.
    /// </summary>
    /// <param name="unloadAll">Whether the client starts over without chunks, as after changing level.</param>
    /// <param name="distance">The view distance to use, or 0 for the client's.</param>
    /// <returns><see langword="true"/> if every chunk in view has been sent.</returns>
    public Task<bool> UpdateChunksAsync(bool unloadAll = false, int distance = 0);
    /// <summary>
    /// Respawns the player after they die, once their death has finished.
    /// </summary>
    /// <param name="dataKept">What the client keeps across the respawn.</param>
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

    /// <summary>
    /// Shows a particle effect to the player. Not implemented for connected players: it throws
    /// <see cref="NotImplementedException"/>.
    /// </summary>
    /// <param name="data">The particle to show.</param>
    public ValueTask SpawnParticleAsync(ParticleData data);

    /// <summary>
    /// Adds an explicit permission path, like <c>myplugin.home.set</c>, and saves the player's permissions. Raises
    /// <see cref="Events.PermissionGrantedEventArgs"/> if it was new.
    /// </summary>
    /// <param name="permission">The permission node, with parts separated by dots; case is ignored.</param>
    /// <returns>
    /// <see langword="true"/> if the explicit permission path was added, even if a wildcard already granted access;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public Task<bool> GrantPermissionAsync(string permission);
    /// <summary>
    /// Removes an explicit permission node, with every node under it, and saves the player's permissions. Raises
    /// <see cref="Events.PermissionRevokedEventArgs"/> if the node existed.
    /// </summary>
    /// <param name="permission">The permission node, with parts separated by dots; case is ignored.</param>
    /// <returns>
    /// <see langword="true"/> if the explicit node was removed; otherwise, <see langword="false"/>. A wildcard may
    /// still grant access after removal.
    /// </returns>
    public Task<bool> RevokePermissionAsync(string permission);
    /// <summary>
    /// Returns whether the permission's full path is granted, or a <c>*</c> wildcard on that path grants it.
    /// </summary>
    /// <param name="permission">The permission node, with parts separated by dots; case is ignored.</param>
    public bool HasPermission(string permission);
    /// <summary>
    /// Returns whether the player has at least one of <paramref name="permissions"/> (see <see cref="HasPermission"/>).
    /// </summary>
    /// <param name="permissions">The permission nodes to check.</param>
    public bool HasAnyPermission(IEnumerable<string> permissions);
    /// <summary>
    /// Returns whether the player has every one of <paramref name="permissions"/> (see <see cref="HasPermission"/>).
    /// </summary>
    /// <param name="permissions">The permission nodes to check.</param>
    public bool HasAllPermissions(IEnumerable<string> permissions);
    /// <summary>
    /// Sets the player's game mode and tells their client and the players in their level.
    /// </summary>
    /// <param name="gamemode">The new game mode.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gamemode"/> is unknown.</exception>
    public ValueTask SetGamemodeAsync(GameMode gamemode);

    /// <summary>
    /// Sets the player's custom name and the name shown for them in the tab list of players in their level.
    /// </summary>
    /// <param name="newDisplayName">The new name.</param>
    public ValueTask UpdateDisplayNameAsync(string newDisplayName);

    /// <summary>
    /// Gets the item in the player's selected hotbar slot.
    /// </summary>
    public ItemStack? GetHeldItem();
    /// <summary>
    /// Gets the item in the player's off hand.
    /// </summary>
    public ItemStack? GetOffHandItem();
}
