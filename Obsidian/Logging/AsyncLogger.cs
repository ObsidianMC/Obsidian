using Obsidian.Events;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Logging
{
    public class AsyncLogger : IAsyncDisposable
    {
        private readonly AsyncEvent<LoggerEventArgs> _messageLogged;
        private readonly ConcurrentQueue<LogMessage> _messages = new ConcurrentQueue<LogMessage>();
        private readonly Task _dispatcherTask;
            
        private CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        private readonly StreamWriter _streamWriter;
        
        public string Prefix { get; }
        
        public string FilePath { get; }
        
        public LogLevel LogLevel { get; set; }

        public event AsyncEventHandler<LoggerEventArgs> MessageLogged
        {
            add { this._messageLogged.Register(value); }
            remove { this._messageLogged.Unregister(value); }
        }

        internal AsyncLogger(string prefix, LogLevel logLevel, string filePath)
        {
            this._messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
            this.LogLevel = logLevel;
            this.FilePath = filePath;

            /*_streamWriter = new StreamWriter(this.FilePath, true, Encoding.UTF8)
            {
                AutoFlush = true
            };*/
            _dispatcherTask = Task.Run(TaskLoop);
        }

        public void StopLogging() => Cancellation.Cancel();

        private async Task TaskLoop()
        {
            while (!Cancellation.IsCancellationRequested)
            {
                if (_messages.TryDequeue(out LogMessage message))
                    await LogMessageAsync(message);
                
                await Task.Delay(100);
            }
        }

        private void LogError(string eventname, Exception ex)
        {
        }

        public async Task LogDebugAsync(string message) => await LogMessageAsync(message, LogLevel.Debug);

        public async Task LogWarningAsync(string message) => await LogMessageAsync(message, LogLevel.Warning);

        public async Task LogErrorAsync(string message) => await LogMessageAsync(message, LogLevel.Error);

        public async Task LogMessageAsync(string message) => await LogMessageAsync(message, LogLevel.Info);
        
        private async Task LogMessageAsync(string message, LogLevel level) => await LogMessageAsync(new LogMessage(message, level));

        private async Task LogMessageAsync(LogMessage message, ConsoleColor color)
        {
            await _messageLogged.InvokeAsync(new LoggerEventArgs(message));

            //checking if message should be printed or not
            if (message.Level > LogLevel)
                return;

            var line = $"[{message.DateTime:t}] [{message.Level}] ";

            if (!string.IsNullOrWhiteSpace(Prefix))
                line += $"[{Prefix}] ";

            line += message.Message;

            // Console logging is console lagging h
            Console.ForegroundColor = color;
            Console.WriteLine(line);
        }

        private async Task LogMessageAsync(LogMessage message)
        {
            var color = message.Level == LogLevel.Debug
                ? GetConsoleColorByString(Prefix)
                : GetConsoleColor(message.Level);

            await LogMessageAsync(message, color);
        }

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
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.DarkRed,
                LogLevel.Debug => ConsoleColor.Cyan,
                _ => ConsoleColor.Gray
            };
        }

        public async ValueTask DisposeAsync()
        {
            await _streamWriter.DisposeAsync();
        }
    }
}