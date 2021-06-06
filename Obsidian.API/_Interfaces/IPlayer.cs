using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IPlayer : ILiving
    {
        public string Username { get; }
        public string DisplayName { get; }

        public Guid Uuid { get; }
        public IServer Server { get; }
        public bool IsOperator { get; }

        public Gamemode Gamemode { get; set; }
        public Hand MainHand { get; set; }
        public PlayerBitMask PlayerBitMask { get; set; }

        public bool Sleeping { get; set; }
        public bool InHorseInventory { get; set; }

        public short AttackTime { get; set; }
        public short DeathTime { get; set; }
        public short HurtTime { get; set; }
        public short SleepTimer { get; set; }
        public short CurrentSlot { get; set; }

        public int Ping { get; }
        public int Dimension { get; set; }
        public int FoodLevel { get; set; }
        public int FoodTickTimer { get; set; }
        public int XpLevel { get; set; }
        public int XpTotal { get; set; }

        public double HeadY { get; }

        public float AdditionalHearts { get; set; }
        public float FallDistance { get; set; }
        public float FoodExhastionLevel { get; set; }
        public float FoodSaturationLevel { get; set; }

        public Task TeleportAsync(VectorF position);
        public Task TeleportAsync(IPlayer to);
        public Task SendMessageAsync(IChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null);
        public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null);
        public Task SendSoundAsync(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f);
        public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f);
        public Task KickAsync(IChatMessage reason);
        public Task KickAsync(string reason);
        public Task OpenInventoryAsync(Inventory inventory);

        public Task DisplayScoreboardAsync(IScoreboard scoreboard, ScoreboardPosition position);

        public Task<bool> GrantPermission(string permission);
        public Task<bool> RevokePermission(string permission);
        public Task<bool> HasPermission(string permission);
        public Task<bool> HasAnyPermission(IEnumerable<string> permissions);
        public Task<bool> HasAllPermissions(IEnumerable<string> permissions);
        public Task SetGamemodeAsync(Gamemode gamemode);

        public Task UpdateDisplayNameAsync(string newDisplayName);
    }
}
