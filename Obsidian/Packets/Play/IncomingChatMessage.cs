using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class IncomingChatMessage
    {
        public IncomingChatMessage(string message) => Message = message;

        public string Message { get; private set; }


        public static async Task<IncomingChatMessage> FromArrayAsync(byte[] data) => new IncomingChatMessage(await new MemoryStream(data).ReadStringAsync(256));

        public async Task<byte[]> ToArrayAsync() => throw new NotImplementedException();
    }
}