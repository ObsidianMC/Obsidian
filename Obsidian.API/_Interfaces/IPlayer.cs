using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IPlayer : ILiving
    {
        public Inventory Inventory { get; }

        public string Username { get; }

        public Guid Uuid { get; }
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
        public short CurrentSlot { get; }

        public int Ping { get; }
        public string Dimension { get; set; }
        public int FoodLevel { get; set; }
        public int FoodTickTimer { get; set; }
        public int XpLevel { get; set; }
        public int XpTotal { get; set; }

        public double HeadY { get; }

        public float AdditionalHearts { get; set; }
        public float FallDistance { get; set; }
        public float FoodExhaustionLevel { get; set; }
        public float FoodSaturationLevel { get; set; }

        public Task TeleportAsync(VectorF position);
        public Task TeleportAsync(IPlayer to);
        public Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null);
        public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null);
        public Task SendSoundAsync(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f);
        public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f);
        public Task KickAsync(ChatMessage reason);
        public Task KickAsync(string reason);
        public Task OpenInventoryAsync(Inventory inventory);
        public Task DisplayScoreboardAsync(IScoreboard scoreboard, ScoreboardPosition position);

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

        public Task<bool> GrantPermissionAsync(string permission);
        public Task<bool> RevokePermissionAsync(string permission);
        public bool HasPermission(string permission);
        public bool HasAnyPermission(IEnumerable<string> permissions);
        public bool HasAllPermissions(IEnumerable<string> permissions);
        public Task SetGamemodeAsync(Gamemode gamemode);

        public Task UpdateDisplayNameAsync(string newDisplayName);

        public ItemStack GetHeldItem();
        public ItemStack GetOffHandItem();
    }
}
