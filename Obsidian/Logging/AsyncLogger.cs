using Obsidian.Events;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Logging
{
    /*public class AsyncLogger : IAsyncDisposable
    {
        private readonly AsyncEvent<LoggerEventArgs> messageLogged;
        private readonly ConcurrentQueue<LogMessage> messages = new ConcurrentQueue<LogMessage>();
        private readonly Task _dispatcherTask;
            
        private CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        private readonly StreamWriter _streamWriter;
        
        public string Prefix { get; }
        
        public string FilePath { get; }
        
        public LogLevel LogLevel { get; }

        public event AsyncEventHandler<LoggerEventArgs> MessageLogged
        {
            add { this.messageLogged.Register(value); }
            remove { this.messageLogged.Unregister(value); }
        }

        internal AsyncLogger(string prefix, LogLevel logLevel, string filePath)
        {
            this.messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
            this.LogLevel = logLevel;
            this.FilePath = filePath;

            /*_streamWriter = new StreamWriter(this.FilePath, true, Encoding.UTF8)
            {
                AutoFlush = true
            };
            _dispatcherTask = Task.Run(TaskLoop);
        }

        public AsyncLogger(string prefix, LogLevel logLevel = LogLevel.Info) : this(prefix, logLevel, "") { }

        public void StopLogging() => this.Cancellation.Cancel();

        private async Task TaskLoop()
        {
            while (!this.Cancellation.IsCancellationRequested)
            {
                if (this.messages.TryDequeue(out LogMessage message))
                    await this.LogMessageAsync(message);
                
                await Task.Delay(100);
            }
        }

        private void LogError(string eventname, Exception ex)
        {
        }

        public async Task LogDebugAsync(string message) => await this.LogMessageAsync(message, LogLevel.Debug);

        public async Task LogWarningAsync(string message) => await this.LogMessageAsync(message, LogLevel.Warn);

        public async Task LogErrorAsync(string message) => await this.LogMessageAsync(message, LogLevel.Error);

        public async Task LogMessageAsync(string message) => await this.LogMessageAsync(message, LogLevel.Info);
        
        private async Task LogMessageAsync(string message, LogLevel level) => await this.LogMessageAsync(new LogMessage(message, level));

        private async Task LogMessageAsync(LogMessage message, ConsoleColor color)
        {
            await this.messageLogged.InvokeAsync(new LoggerEventArgs(message));

            //checking if message should be printed or not
            if (message.Level > LogLevel)
                return;

            Console.Write($"[{message.DateTime:t}] ");

            Console.ForegroundColor = color;

            Console.Write($"[{message.Level}] ");

            Console.ResetColor();

            if (!string.IsNullOrWhiteSpace(this.Prefix))
                Console.Write($"{"",3}[{this.Prefix}] ");

            Console.WriteLine(message.Message);
           
        }

        private async Task LogMessageAsync(LogMessage message) => await LogMessageAsync(message, GetConsoleColor(message.Level));

        //I'm sorry but this messes with my eyes so much
        /// <summary>
        /// Returns a console color based on the hash of the text provided. 
        /// </summary>
        private static ConsoleColor GetConsoleColorByString(string text)
        {
            var hash = text.GetHashCode();

            // make hash positive if negative
            if (hash < 0)
                hash *= -1;
            
            var colorIndex = (hash + 1) % 14;
            var color = (ConsoleColor)colorIndex;

            if (color == Console.BackgroundColor)
                color = ConsoleColor.White;

            return color;
        }

        private static ConsoleColor GetConsoleColor(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warn => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.DarkRed,
                LogLevel.Debug => ConsoleColor.Cyan,
                _ => ConsoleColor.Gray
            };
        }

        public async ValueTask DisposeAsync()
        {
            await _streamWriter.DisposeAsync();
        }
    }*/
}