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

            _streamWriter = new StreamWriter(this.FilePath, true, Encoding.UTF8)
            {
                AutoFlush = true
            };
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

        public void LogDebug(string message) => this.LogMessage(message, LogLevel.Debug);

        public void LogWarning(string message) => this.LogMessage(message, LogLevel.Warning);

        public void LogError(string message) => this.LogMessage(message, LogLevel.Error);

        public void LogMessage(string message, LogLevel logLevel = LogLevel.Info) => _messages.Enqueue(new LogMessage(message, logLevel));

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

            Console.ForegroundColor = color;
            Console.WriteLine(line);
            
            await _streamWriter.WriteLineAsync(line);
        }

        private async Task LogMessageAsync(LogMessage message)
        {
            await LogMessageAsync(message, GetConsoleColor(message.Level));
        }

        private static ConsoleColor GetConsoleColor(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Info => ConsoleColor.Cyan,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.DarkRed,
                LogLevel.Debug => ConsoleColor.Magenta,
                _ => ConsoleColor.Gray
            };
        }

        public async ValueTask DisposeAsync()
        {
            await _streamWriter.DisposeAsync();
        }
    }
}