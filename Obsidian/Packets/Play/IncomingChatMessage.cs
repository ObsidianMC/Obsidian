using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class IncomingChatMessage
    {
        public IncomingChatMessage(string message) => Message = message;

        public string Message { get; private set; }


        public static async Task<IncomingChatMessage> FromArrayAsync(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                return new IncomingChatMessage(await stream.ReadStringAsync(256));
            }
        }

        public async Task<byte[]> ToArrayAsync()
        {
            await Task.Yield(); throw new NotImplementedException();
        }
    }
}