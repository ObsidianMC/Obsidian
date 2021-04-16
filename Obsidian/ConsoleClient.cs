using Microsoft.Extensions.Logging;
using Obsidian.API;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian
{
    internal class ConsoleClient : IPlayer
    {
        public ConsoleClient(ILogger logger, IServer server)
        {
            Logger = logger;
            Server = server;
        }

        public string Username => "SERVER";

        public Guid Uuid => Guid.Empty;

        public IServer Server { get; }

        public bool IsOperator => true;

        public Gamemode Gamemode { get => default; set => _ = value; }
        public Hand MainHand { get => default; set => _ = value; }
        public PlayerBitMask PlayerBitMask { get => default; set => _ = value; }
        public bool Sleeping { get => default; set => _ = value; }
        public bool Sneaking { get => default; set => _ = value; }
        public bool Sprinting { get => default; set => _ = value; }
        public bool FlyingWithElytra { get => default; set => _ = value; }
        public bool InHorseInventory { get => default; set => _ = value; }
        public short AttackTime { get => default; set => _ = value; }
        public short DeathTime { get => default; set => _ = value; }
        public short HurtTime { get => default; set => _ = value; }
        public short SleepTimer { get => default; set => _ = value; }
        public short CurrentSlot { get => default; set => _ = value; }

        public int Ping => default;

        public int Dimension { get => default; set => _ = value; }
        public int FoodLevel { get => default; set => _ = value; }
        public int FoodTickTimer { get => default; set => _ = value; }
        public int XpLevel { get => default; set => _ = value; }
        public int XpTotal { get => default; set => _ = value; }

        public double HeadY => default;

        public float AdditionalHearts { get => default; set => _ = value; }
        public float FallDistance { get => default; set => _ = value; }
        public float FoodExhastionLevel { get => default; set => _ = value; }
        public float FoodSaturationLevel { get => default; set => _ = value; }
        public LivingBitMask LivingBitMask { get => default; set => _ = value; }
        public float Health { get => default; set => _ = value; }

        public uint ActiveEffectColor => default;

        public bool AmbientPotionEffect { get => default; set => _ = value; }
        public int AbsorbedArrows { get => default; set => _ = value; }
        public int AbsorbtionAmount { get => default; set => _ = value; }
        public Vector BedBlockPosition { get => default; set => _ = value; }

        public IWorld WorldLocation => default;

        public VectorF Position { get => default; set => _ = value; }
        public Angle Pitch { get => default; set => _ = value; }
        public Angle Yaw { get => default; set => _ = value; }

        public VectorF LookDirection => default;

        public int EntityId => default;

        public EntityBitMask EntityBitMask { get => default; set => _ = value; }
        public Pose Pose { get => default; set => _ = value; }
        public int Air { get => default; set => _ = value; }

        public bool CustomNameVisible => default;

        public bool Silent => default;

        public bool NoGravity { get => default; set => _ = value; }
        public bool OnGround { get => default; set => _ = value; }
        private ILogger Logger { get; }

        public Task DisplayScoreboardAsync(IScoreboard scoreboard, ScoreboardPosition position)
        {
            return Task.CompletedTask;
        }

        public Task<bool> GrantPermission(string permission)
        {
            return Task.FromResult(true);
        }

        public Task<bool> HasAllPermissions(IEnumerable<string> permissions)
        {
            return Task.FromResult(true);
        }

        public Task<bool> HasAnyPermission(IEnumerable<string> permissions)
        {
            return Task.FromResult(true);
        }

        public Task<bool> HasPermission(string permission)
        {
            return Task.FromResult(true);
        }

        public Task KickAsync(IChatMessage reason)
        {
            return Task.CompletedTask;
        }

        public Task KickAsync(string reason)
        {
            return Task.CompletedTask;
        }

        public Task OpenInventoryAsync(Inventory inventory)
        {
            return Task.CompletedTask;
        }

        public Task RemoveAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> RevokePermission(string permission)
        {
            return Task.FromResult(true);
        }

        public Task SendMessageAsync(IChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null)
        {
            string messageString = message.Text;
            foreach (var extra in message.Extras)
            {
                messageString += extra.Text;
            }

            Logger.LogInformation(messageString);
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null)
        {
            Logger.LogInformation(message);
            return Task.CompletedTask;
        }

        public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1, float volume = 1)
        {
            return Task.CompletedTask;
        }

        public Task SendSoundAsync(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1, float volume = 1)
        {
            return Task.CompletedTask;
        }

        public Task SetGamemodeAsync(Gamemode gamemode)
        {
            return Task.CompletedTask;
        }

        public Task TeleportAsync(VectorF position)
        {
            return Task.CompletedTask;
        }

        public Task TeleportAsync(IPlayer to)
        {
            return Task.CompletedTask;
        }

        public Task TickAsync()
        {
            return Task.CompletedTask;
        }
    }
}