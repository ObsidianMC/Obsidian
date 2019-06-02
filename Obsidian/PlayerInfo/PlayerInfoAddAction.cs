using Obsidian.Chat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.PlayerInfo
{
    class PlayerInfoAddAction : PlayerInfoAction
    {
        public Guid UUID { get; set; }

        public string Name { get; set; }

        public List<object> Properties { get; set; } = new List<object>();

        public int Gamemode { get; set; }

        public int Ping { get; set; }

        public ChatMessage DisplayName { get; set; } = null;

        public bool HasDisplayName => DisplayName != null;

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WriteUuidAsync(this.UUID);

                await stream.WriteStringAsync(this.Name, 16);

                await stream.WriteVarIntAsync(Properties.Count);

                foreach (var property in Properties)
                {
                    throw new NotImplementedException();
                }

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
