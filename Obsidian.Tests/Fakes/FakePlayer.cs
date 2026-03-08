using Obsidian.API;
using Obsidian.API.AI;
using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities.Concurrency;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Tests.Fakes;
public sealed class FakePlayer : IPlayer
{
    public byte CurrentContainerId => 0;

    public IClient Client => throw new NotImplementedException();

    public IScoreboard CurrentScoreboard { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public PlayerInput Input { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public ConcurrentHashSet<long> LoadedChunks => throw new NotImplementedException();

    public Container Inventory { get; set; } = new(45);

    public Container EnderInventory { get; set; } = new(18);

    public BaseContainer OpenedContainer { get; set; }
    public ItemStack CarriedItem { get; set; }

    public List<short> DraggedSlots { get; } = [];

    public List<SkinProperty> SkinProperties { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public ClientInformation ClientInformation { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string Username => throw new NotImplementedException();

    public Vector? LastDeathLocation { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string ClientIP => throw new NotImplementedException();

    public Gamemode Gamemode { get; set; }

    public PlayerAbility Abilities { get; set; }
    public bool IsDragging { get; set; }
    public bool Sleeping { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool InHorseInventory { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public short AttackTime { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public short DeathTime { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public short HurtTime { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public short SleepTimer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public short CurrentHeldItemSlot { get; set; }
    public int TeleportId { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public int Ping => throw new NotImplementedException();

    public int FoodLevel { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public int FoodTickTimer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public int XpLevel { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public int XpTotal { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public double HeadY { get; set; }

    public float Absorption { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public float FallDistance { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public float FoodExhaustionLevel { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public float FoodSaturationLevel { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public LivingBitMask LivingBitMask { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public uint ActiveEffectColor => throw new NotImplementedException();

    public bool AmbientPotionEffect { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool Alive => throw new NotImplementedException();

    public int AbsorbedArrows { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public int AbsorbtionAmount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public Vector? BedBlockPosition { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public IReadOnlyDictionary<int, EffectWithCurrentDuration> ActivePotionEffects => throw new NotImplementedException();

    public IWorld World => throw new NotImplementedException();

    public INavigator Navigator { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public IGoalController GoalController { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public Guid Uuid { get; set; }
    public VectorF LastPosition { get; set; }
    public VectorF Position { get;set; }
    public Angle Pitch { get; set; }
    public Angle Yaw { get; set; }

    public int EntityId => 0;

    public Pose Pose { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public EntityType Type => throw new NotImplementedException();

    public BoundingBox BoundingBox => throw new NotImplementedException();

    public EntityDimension Dimension => throw new NotImplementedException();

    public short Air { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public float Health { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public ChatMessage CustomName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string TranslationKey => throw new NotImplementedException();

    public bool CustomNameVisible { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool Silent => throw new NotImplementedException();

    public bool NoGravity => throw new NotImplementedException();

    public MovementFlags MovementFlags => throw new NotImplementedException();

    public bool Sneaking { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool Sprinting { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool Glowing => throw new NotImplementedException();

    public bool Invisible => throw new NotImplementedException();

    public bool Burning => throw new NotImplementedException();

    public bool Swimming { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool FlyingWithElytra { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool Summonable => throw new NotImplementedException();

    public bool IsFireImmune => throw new NotImplementedException();

    public void AddPotionEffect(int effectId, int duration, int amplifier = 0, EntityEffectFlags effectFlags = EntityEffectFlags.None) => throw new NotImplementedException();
    public void ClearPotionEffects() => throw new NotImplementedException();
    public ValueTask DamageAsync(IEntity source, float amount = 1) => throw new NotImplementedException();
    public ValueTask DisconnectAsync(ChatMessage reason) => throw new NotImplementedException();
    public ValueTask DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot position) => throw new NotImplementedException();
    public float GetAttributeValue(string attributeResourceName) => throw new NotImplementedException();
    public IEnumerable<IEntity> GetEntitiesNear(float distance) => throw new NotImplementedException();
    public ItemStack GetHeldItem() => throw new NotImplementedException();
    public VectorF GetLookDirection() => throw new NotImplementedException();
    public ItemStack GetOffHandItem() => throw new NotImplementedException();
    public Task<bool> GrantPermissionAsync(string permission) => throw new NotImplementedException();
    public bool HasAllPermissions(IEnumerable<string> permissions) => throw new NotImplementedException();
    public bool HasAnyPermission(IEnumerable<string> permissions) => throw new NotImplementedException();
    public bool HasAttribute(string attributeResourceName) => throw new NotImplementedException();
    public bool HasPermission(string permission) => throw new NotImplementedException();
    public bool HasPotionEffect(int effectId) => throw new NotImplementedException();
    public bool IsInRange(IEntity entity, float distance) => throw new NotImplementedException();
    public ValueTask KickAsync(ChatMessage reason) => throw new NotImplementedException();
    public ValueTask KickAsync(string reason) => throw new NotImplementedException();
    public ValueTask KillAsync(IEntity source) => throw new NotImplementedException();
    public ValueTask KillAsync(IEntity source, ChatMessage message) => throw new NotImplementedException();
    public Task LoadAsync(bool loadFromPersistentWorld = true) => throw new NotImplementedException();
    public ValueTask OpenInventoryAsync(BaseContainer container) => throw new NotImplementedException();
    public ValueTask RemoveAsync() => throw new NotImplementedException();
    public void RemovePotionEffect(int effectId) => throw new NotImplementedException();
    public Task RespawnAsync(DataKept dataKept = DataKept.Metadata) => throw new NotImplementedException();
    public Task<bool> RevokePermissionAsync(string permission) => throw new NotImplementedException();
    public Task SaveAsync() => throw new NotImplementedException();
    public ValueTask SendActionBarAsync(string text) => throw new NotImplementedException();
    public ValueTask SendMessageAsync(ChatMessage message) => throw new NotImplementedException();
    public ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature) => throw new NotImplementedException();
    public ValueTask SendPlayerInfoAsync() => throw new NotImplementedException();
    public ValueTask SendSoundAsync(ISoundEffect soundEffect) => throw new NotImplementedException();
    public ValueTask SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut) => throw new NotImplementedException();
    public ValueTask SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut) => throw new NotImplementedException();
    public ValueTask SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut) => throw new NotImplementedException();
    public ValueTask SetActionBarTextAsync(ChatMessage message) => throw new NotImplementedException();
    public ValueTask SetGamemodeAsync(Gamemode gamemode) => throw new NotImplementedException();
    public void SetHeadRotation(Angle headYaw) => throw new NotImplementedException();
    public void SetRotation(Angle yaw, Angle pitch, MovementFlags movementFlags) => throw new NotImplementedException();
    public void SpawnEntity(Velocity? velocity = null, int additionalData = 0) => throw new NotImplementedException();
    public ValueTask SpawnParticleAsync(ParticleData data) => throw new NotImplementedException();
    public ValueTask TeleportAsync(IWorld world) => throw new NotImplementedException();
    public ValueTask TeleportAsync(IEntity to) => throw new NotImplementedException();
    public ValueTask TeleportAsync(VectorF pos) => throw new NotImplementedException();
    public ValueTask TickAsync() => throw new NotImplementedException();
    public bool TryAddAttribute(string attributeResourceName, float value) => throw new NotImplementedException();
    public bool TryUpdateAttribute(string attributeResourceName, float newValue) => throw new NotImplementedException();
    public ValueTask UpdateAsync(VectorF position, MovementFlags movementFlags) => throw new NotImplementedException();
    public ValueTask UpdateAsync(VectorF position, Angle yaw, Angle pitch, MovementFlags movementFlags) => throw new NotImplementedException();
    public ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags) => throw new NotImplementedException();
    public Task<bool> UpdateChunksAsync(bool unloadAll = false, int distance = 0) => throw new NotImplementedException();
    public ValueTask UpdateDisplayNameAsync(string newDisplayName) => throw new NotImplementedException();
    public ValueTask UpdatePlayerInfoAsync() => throw new NotImplementedException();
    public void Write(INetStreamWriter writer) => throw new NotImplementedException();
}
