using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class LoginStart
    {
        public LoginStart(string username) => this.Username = username;

        public string Username { get; private set; }

        public static async Task<LoginStart> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            return new LoginStart(await stream.ReadStringAsync());
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteStringAsync(this.Username);
            return stream.ToArray();
        }
    }
}