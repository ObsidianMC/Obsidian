using Obsidian.Chat;
using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoAddAction : PlayerInfoAction
    {
        public string Name { get; set; }

        public List<SkinProperties> Properties { get; set; } = new List<SkinProperties>();

        public int Gamemode { get; set; }

        public int Ping { get; set; }

        public ChatMessage DisplayName { get; set; } = null;

        public bool HasDisplayName => DisplayName != null;

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteUuidAsync(this.Uuid);

            await stream.WriteStringAsync(this.Name, 16);

            await stream.WriteVarIntAsync(0);
            //foreach (SkinProperties props in this.Properties)
            //    await stream.WriteAsync(await props.ToArrayAsync());

            await stream.WriteVarIntAsync(this.Gamemode);

            await stream.WriteVarIntAsync(this.Ping);

            await stream.WriteBooleanAsync(this.HasDisplayName);

            if (this.HasDisplayName)
            {
                await stream.WriteChatAsync(this.DisplayName);
            }
        }
    }
}