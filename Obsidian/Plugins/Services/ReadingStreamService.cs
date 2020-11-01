using System.IO;
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

        public ReadingStreamService(Stream stream)
        {
            this.stream = stream;
            reader = new StreamReader(stream);
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

        public override void Dispose()
        {
            reader.Dispose();
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            reader.Dispose();
            return base.DisposeAsync();
        }
    }
}
