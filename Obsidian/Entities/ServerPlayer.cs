using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using System.Collections.Generic;

namespace Obsidian.Entities;

/// <summary>
/// A server-side player that participates in world and player lifecycle without a Minecraft network connection.
/// </summary>
public sealed class ServerPlayer : Avatar, IPlayer
{
    private readonly HashSet<string> permissions = new(StringComparer.OrdinalIgnoreCase);
    private IScoreboard? currentScoreboard;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ServerPlayer(Guid uuid, string username, IServer server, IWorld world)
    {
        this.Uuid = uuid;
        this.Username = username;
        this.Server = server;
        this.Level = world;
        this.EntityId = Obsidian.Server.GetNextEntityId();
        this.Type = EntityType.Player;
        this.Health = 20f;
        this.ClientInformation = this.ClientInformation with { AllowsListing = true };

        this.Inventory = new Container(9 * 5 + 1, InventoryType.Generic)
        {
            Owner = uuid,
            IsPlayerInventory = true
        };
        this.EnderInventory = new Container
        {
            Title = "Ender Chest"
        };
    }

    public IServer Server { get; }

    public string Username { get; }

    public byte CurrentContainerId { get; set; }

    public IScoreboard? CurrentScoreboard
    {
        get => this.currentScoreboard;
        set
        {
            if (this.currentScoreboard == value)
                return;

            this.currentScoreboard?.RemovePlayer(this.EntityId);
            value?.AddPlayer(this.EntityId);
            this.currentScoreboard = value;
        }
    }

    public PlayerInput Input { get; set; }

    public ConcurrentHashSet<long> LoadedChunks { get; } = [];

    public Container Inventory { get; }

    public Container EnderInventory { get; }

    public BaseContainer? OpenedContainer { get; set; }

    public ItemStack? CarriedItem { get; set; }

    public List<short> DraggedSlots { get; } = [];

    public List<SkinProperty> SkinProperties { get; set; } = [];

    public Vector? LastDeathLocation { get; set; }

    public string? ClientIP => null;

    public GameMode GameMode
    {
        get => field;
        set
        {
            this.Abilities = value switch
            {
                GameMode.Creative => PlayerAbility.CreativeMode | PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                GameMode.Spectator => PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                GameMode.Survival or GameMode.Adventure => PlayerAbility.None,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown gamemode.")
            };
            field = value;
        }
    }

    public PlayerAbility Abilities { get; set; }

    public bool IsDragging { get; set; }
    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }

    public short CurrentHeldItemSlot
    {
        get
        {
            if (field < 36 || field > 44)
                field = 36;

            return field;
        }
        set
        {
            if (value is < 0 or > 8)
                throw new IndexOutOfRangeException("Value must be >= 0 or <= 8");

            field = (short)(value + 36);
        }
    }

    public int TeleportId { get; set; }
    public int Ping => 0;
    public int FoodLevel { get; set; } = 20;
    public int FoodTickTimer { get; set; }
    public int XpLevel { get; set; }
    public int XpTotal { get; set; }

    public double HeadY => this.Position.Y + 1.62f;

    public float Absorption { get; set; }
    public float FallDistance { get; set; }
    public float FoodExhaustionLevel { get; set; }
    public float FoodSaturationLevel { get; set; } = 5;

    public async Task SaveAsync() => await Task.CompletedTask;

    public async Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        if (!double.IsFinite(this.Position.X) || !double.IsFinite(this.Position.Y) || !double.IsFinite(this.Position.Z) || this.Position == default)
            this.Position = this.Level.LevelData.SpawnPosition;

        if (!this.Alive)
            this.Health = 20f;

        await Task.CompletedTask;
    }

    // There is no client to receive packets, so anything the server would show this player is dropped.
    public bool SendPacket(IClientboundPacket packet) => false;

    public ValueTask QueuePacketAsync(IClientboundPacket packet) => default;

    public ValueTask DisconnectAsync(ChatMessage reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public ValueTask SendMessageAsync(ChatMessage message) => default;

    public ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature) => default;

    public ValueTask SetActionBarTextAsync(ChatMessage message) => default;

    public ValueTask SendSoundAsync(ISoundEffect soundEffect) => default;

    public ValueTask KickAsync(ChatMessage reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public ValueTask KickAsync(string reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public ValueTask OpenInventoryAsync(BaseContainer container)
    {
        this.OpenedContainer = container;
        return default;
    }

    public ValueTask DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot position)
    {
        this.CurrentScoreboard = scoreboard;
        return default;
    }

    public ValueTask UpdatePlayerInfoAsync() => default;

    public ValueTask SendPlayerInfoAsync() => default;

    public Task<bool> UpdateChunksAsync(bool unloadAll = false, int distance = 0) => Task.FromResult(true);

    public Task RespawnAsync(DataKept dataKept = DataKept.Metadata)
    {
        this.Health = 20f;
        this.Position = this.Level.LevelData.SpawnPosition;
        return Task.CompletedTask;
    }

    public ValueTask SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut) => default;

    public ValueTask SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut) => default;

    public ValueTask SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut) => default;

    public ValueTask SendActionBarAsync(string text) => default;

    public ValueTask SpawnParticleAsync(ParticleData data) => default;

    public Task<bool> GrantPermissionAsync(string permission) => Task.FromResult(this.permissions.Add(permission));

    public Task<bool> RevokePermissionAsync(string permission) => Task.FromResult(this.permissions.Remove(permission));

    public bool HasPermission(string permission) => this.permissions.Contains(permission);

    public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(this.permissions.Contains);

    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.All(this.permissions.Contains);

    public ValueTask SetGamemodeAsync(GameMode gamemode)
    {
        this.GameMode = gamemode;
        return default;
    }

    public ValueTask UpdateDisplayNameAsync(string newDisplayName) => default;

    public ValueTask ShowDialogAsync(Obsidian.API.Registry.Codecs.Dialogs.DialogElement dialog) => default;

    public ValueTask ClearDialogAsync() => default;

    public ItemStack? GetHeldItem() => this.Inventory.GetItem(this.CurrentHeldItemSlot);

    public ItemStack? GetOffHandItem() => this.Inventory.GetItem(45);
}
