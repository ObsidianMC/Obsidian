using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Obsidian.Plugins.Services
{
    public class ReadingStreamService : StreamService
    {
        private readonly StreamReader reader;

        public ReadingStreamService(FileStream fileStream)
        {
            stream = fileStream;
            reader = new StreamReader(fileStream);
            name = fileStream.Name;
        }

        public ReadingStreamService(NetworkStream networkStream)
        {
            stream = networkStream;
            reader = new StreamReader(networkStream);
        }

        public ReadingStreamService(MemoryStream memoryStream)
        {
            stream = memoryStream;
            reader = new StreamReader(memoryStream);
        }

        public override string ReadLine()
        {
            return reader.ReadLine();
        }

        public override Task<string> ReadLineAsync()
        {
            return reader.ReadLineAsync();
        }

        public override string ReadToEnd()
        {
            return reader.ReadToEnd();
        }

        public override Task<string> ReadToEndAsync()
        {
            return reader.ReadToEndAsync();
        }
    }
}
