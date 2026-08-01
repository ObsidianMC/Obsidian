using Microsoft.Extensions.Logging;
using Obsidian.GuiConsole.Services.Command;
using System.Collections.ObjectModel;  
using System.Collections.Specialized;  
using Terminal.Gui.Drawing;  
using Terminal.Gui.Input;  
using Terminal.Gui.ViewBase;  
using Terminal.Gui.Views;  
using Attribute = Terminal.Gui.Drawing.Attribute;  
  
namespace Obsidian.GuiConsole.Window;  
/// <summary>
/// Render a GUI Console for Obsidian Server
/// </summary>
public class ObsidianConsole : Terminal.Gui.Views.Window  
{  
    private readonly ListView logView;  
    private readonly TextField commandInput;  
    private readonly ObservableCollection<string> logEntries;  
    private readonly Dictionary<int, LogLevel> logLevels = new();
    private readonly List<(LogLevel level, string message)> originalLogs = new(); // 存储原始日志
    private int maxLineWidth = 0;
    
    private CommandMiddleware? commandMiddleware;
  
    public string GuiTitle => $"Obsidian GUI Console for Minecraft {ServerConstants.DefaultProtocol}";  
  
    public ListView LogView => logView;
    
    /// <summary>
    /// Add Command Middleware to handle command input
    /// </summary>
    /// <param name="comm">Instance</param>
    public void AddCommandMiddleware(CommandMiddleware? comm)
    {
        commandMiddleware = comm;
        CommandEntered += async (command) =>
        {
            if (this.commandMiddleware != null)
            {
                AppendLog(LogLevel.Trace, $"> {command}");
                try
                {
                    await this.commandMiddleware.ExecuteCommandFromConsoleAsync(command);
                }
                catch (Exception e)
                {
                    AppendLog(LogLevel.Error, $"Error: {e.Message}");
                }
            }
        };
    }
  
    /// <summary>
    /// Build the GUI Console Window
    /// </summary>
    public ObsidianConsole()  
    {  
        Title = GuiTitle;  
          
        // Initial logs source
        logEntries = new ObservableCollection<string>();
        
        FrameChanged += (_, s) =>
        {
            var newWidth = s.Value.Width - 4;
            if (newWidth != maxLineWidth && newWidth > 0)
            {
                maxLineWidth = newWidth;
                RewrapAllLogs();
            }
        };
        logView = new ListView  
        {  
            X = 0,  
            Y = 1,  
            Width = Dim.Fill(),  
            Height = Dim.Fill(1),  
            Source = new ListWrapper<string>(logEntries),
            Arrangement = ViewArrangement.BottomResizable
        };  
  
        //Add Color Rendering
        logView.RowRender += OnRowRender;  
  
        //Auto Scroll Handling
        logEntries.CollectionChanged += OnLogEntriesChanged;  
  
        commandInput = new TextField  
        {  
            X = 2,  
            Y = Pos.AnchorEnd(1),  
            Width = Dim.Fill(),  
            Height = 1  
        };  
          
        var prompt = new Label  
        {  
            X = 0,  
            Y = Pos.AnchorEnd(1),  
            Text = "> ",  
            Width = 2,  
            Height = 1,  
        };  
        prompt.SetScheme(new Scheme()  
        {  
            Normal = new Attribute(Color.DarkGray, Color.Black)  
        });  
          
        commandInput.KeyDown += (_, x) =>  
        {  
            if (x == Key.Enter)  
            {  
                var command = commandInput.Text;  
                OnCommandEntered(command);  
                commandInput.Text = "";  
                x.Handled = true;  
            }  
        };  
          
        Add(prompt);  
        Add(logView);  
        Add(commandInput);  
  
        var windowScheme = GetScheme();  
        var newScheme = new Scheme()  
        {  
            Normal = new Attribute(windowScheme.Normal.Foreground, Color.Black),  
            Focus = new Attribute(windowScheme.Focus.Foreground, windowScheme.Focus.Background),  
            HotNormal = new Attribute(windowScheme.HotNormal.Foreground, windowScheme.HotNormal.Background),  
            HotFocus = new Attribute(windowScheme.HotFocus.Foreground, windowScheme.HotFocus.Background)  
        };  
        SetScheme(newScheme);  
    }  
    /// <summary>
    /// Command Entered Event
    /// </summary>
    public event Action<string>? CommandEntered;  
    /// <summary>
    /// On Command Entered
    /// </summary>
    protected virtual void OnCommandEntered(string command)  
    {  
        CommandEntered?.Invoke(command);
        
    }  
  
    /// <summary>
    /// Row Render Event for Color Coding
    /// </summary>
    /// <param name="sender">nope</param>
    /// <param name="e">row</param>
    private void OnRowRender(object? sender, ListViewRowEventArgs e)  
    {  
        if (logLevels.TryGetValue(e.Row, out LogLevel level))  
        {  
            Color color = level switch  
            {  
                LogLevel.Trace or LogLevel.Debug => Color.Gray,  
                LogLevel.Information => Color.Green,  
                LogLevel.Warning => Color.Yellow,  
                LogLevel.Error or LogLevel.Critical => Color.Red,  
                _ => Color.Gray  
            };  
              
            e.RowAttribute = new Attribute(color, Color.Black);  
        }  
    }  
  
    /// <summary>
    /// Auto Scroll to Bottom on New Log Entry
    /// </summary>
    /// <param name="sender">nope</param>
    /// <param name="e">argument</param>
    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)  
    {  
        if (e.Action == NotifyCollectionChangedAction.Add)  
        {  
            AutoScrollToBottom();   
        }  
    }  
    /// <summary>
    /// Auto Scroll to Bottom Implementation
    /// </summary>
    private void AutoScrollToBottom()  
    {  
        if (logEntries.Count > 0)  
        {  
            logView.SelectedItem = logEntries.Count - 1;  
            logView.TopItem = Math.Max(0, logEntries.Count - logView.Viewport.Height);  
        }  
    }  
  
    /// <summary>
    /// Add Log Entry to Console
    /// </summary>
    /// <param name="level">Log level</param>
    /// <param name="message">message</param>
    public void AppendLog(LogLevel level, string message)  
    {
        //keep original log for re-wrapping
        originalLogs.Add((level, message));
        
        var logEntry = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        
        var wrapWidth = maxLineWidth > 0 ? maxLineWidth : 80;
        
        //shift lines if too long
        if (logEntry.Length > wrapWidth)
        {
            var lines = WrapText(logEntry, wrapWidth);
            foreach (var line in lines)
            {
                logLevels[logEntries.Count] = level;
                logEntries.Add(line);
            }
        }
        else
        {
            logLevels[logEntries.Count] = level;  
            logEntries.Add(logEntry);
        }
    }
    
    /// <summary>
    /// Wrap text into multiple lines with indentation for wrapped lines
    /// </summary>
    /// <param name="text">text content</param>
    /// <param name="width">console width</param>
    /// <returns></returns>
    private List<string> WrapText(string text, int width)
    {
        var result = new List<string>();
        
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            result.Add(text ?? string.Empty);
            return result;
        }
        
        var currentIndex = 0;
        var isFirstLine = true;
        
        while (currentIndex < text.Length)
        {
            var remainingLength = text.Length - currentIndex;
            var takeLength = Math.Min(width, remainingLength);
            
            // add line
            var line = text.Substring(currentIndex, takeLength);
            if (!isFirstLine)
            {
                line = "  " + line.TrimStart();
            }
            
            result.Add(line);
            currentIndex += takeLength;
            isFirstLine = false;
        }
        
        return result;
    }
    
    /// <summary>
    /// Rewrap all logs when windows size changed
    /// </summary>
    private void RewrapAllLogs()
    {
        if (originalLogs.Count == 0 || maxLineWidth <= 0)
            return;
        var wasAtBottom = logEntries.Count > 0 && 
                          logView.TopItem >= logEntries.Count - logView.Viewport.Height - 5;
        
        //clear current logs
        logEntries.Clear();
        logLevels.Clear();
        
        //Re-add all logs with new wrapping
        foreach (var (level, message) in originalLogs)
        {
            var logEntry = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
            
            if (logEntry.Length > maxLineWidth)
            {
                var lines = WrapText(logEntry, maxLineWidth);
                foreach (var line in lines)
                {
                    logLevels[logEntries.Count] = level;
                    logEntries.Add(line);
                }
            }
            else
            {
                logLevels[logEntries.Count] = level;
                logEntries.Add(logEntry);
            }
        }
        if (wasAtBottom)
        {
            AutoScrollToBottom();
        }
    }  
}
