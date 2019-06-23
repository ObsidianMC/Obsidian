using Obsidian.Chat;
using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoAddAction : PlayerInfoAction
    {
        private Logger logger = new Logger("Player Info", LogLevel.Debug);
        public Guid Uuid { get; set; }
        
        public string Uuid3 { get; set; }

        public string Name { get; set; }

        public List<SkinProperties> Properties { get; set; } = new List<SkinProperties>();

        public int Gamemode { get; set; }

        public int Ping { get; set; }

        public ChatMessage DisplayName { get; set; } = null;

        public bool HasDisplayName => DisplayName != null;

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                if(this.Uuid == null)
                {
                    await stream.WriteStringAsync(this.Uuid3, 16);
                }
                else
                {
                    await stream.WriteUuidAsync(this.Uuid);
                }

                await stream.WriteStringAsync(this.Name, 16);

                await stream.WriteVarIntAsync(Properties.Count);
                foreach (SkinProperties props in this.Properties)
                    await stream.WriteAsync(await props.ToArrayAsync());

                await stream.WriteVarIntAsync(this.Gamemode);

                await stream.WriteVarIntAsync(this.Ping);

                await stream.WriteBooleanAsync(this.HasDisplayName);

                if (this.HasDisplayName)
                {
                    await stream.WriteChatAsync(this.DisplayName);
                }

                return stream.ToArray();
            }
        }
    }
}
