using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Hosting;
public class StreamLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _streamWriter;

    public StreamLoggerProvider(Stream stream)
    {
        _streamWriter = new StreamWriter(stream);
    }

    public ILogger CreateLogger(string category)
    {
        return new StreamLogger(category, _streamWriter);
    }

    public void Dispose()
    {
        _streamWriter.Dispose();
    }
}

public class StreamLogger : ILogger
{
    private readonly string _category;
    private readonly StreamWriter _streamWriter;

    public StreamLogger(string category, StreamWriter streamWriter)
    {
        _category = category;
        _streamWriter = streamWriter;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var msg = formatter(state, exception);

        if (string.IsNullOrEmpty(msg))
        {
            return;
        }

        var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (exception != null)
        {
            _streamWriter.WriteLine("[{0}] [{1}] ({2})\n--------------------\n{3}\n--------------------", dateTime, logLevel, _category, exception.ToString());
        }
        else
        {
            _streamWriter.WriteLine("[{0}] [{1}] ({2}) {3}", dateTime, logLevel, _category, msg);
        }

        _streamWriter.Flush();
    }
}
