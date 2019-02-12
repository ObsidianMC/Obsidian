using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class LoginSuccess
    {
        public LoginSuccess(string uuid, string username)
        {
            this.Username = username;
            this.UUID = uuid;
        }

        public string Username { get; private set; }

        public string UUID { get; private set; }

        public static async Task<LoginSuccess> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            return new LoginSuccess(await stream.ReadStringAsync(), await stream.ReadStringAsync());
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteStringAsync(this.UUID);
            await stream.WriteStringAsync(this.Username);
            return stream.ToArray();
        }
    }
}