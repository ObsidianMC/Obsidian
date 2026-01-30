using Microsoft.Extensions.Logging;  
using System.Collections.ObjectModel;  
using System.Collections.Specialized;  
using Terminal.Gui.Drawing;  
using Terminal.Gui.Input;  
using Terminal.Gui.ViewBase;  
using Terminal.Gui.Views;  
using Attribute = Terminal.Gui.Drawing.Attribute;  
  
namespace Obsidian.GuiConsole.Window;  
  
public class ObsdianConsole : Terminal.Gui.Views.Window  
{  
    private readonly ListView logView;  
    private readonly TextField commandInput;  
    private readonly ObservableCollection<string> logEntries;  
    private readonly Dictionary<int, LogLevel> logLevels = new();  
  
    public string GuiTitle => $"Obsidian GUI Console for Minecraft {ServerConstants.DefaultProtocol}";  
  
    public ListView LogView => logView;  
  
    public ObsdianConsole()  
    {  
        Title = GuiTitle;  
          
        // 初始化日志数据源  
        logEntries = new ObservableCollection<string>();  
          
        logView = new ListView  
        {  
            X = 0,  
            Y = 1,  
            Width = Dim.Fill(),  
            Height = Dim.Fill(1),  
            Source = new ListWrapper<string>(logEntries)  
        };  
  
        // 添加行渲染事件处理器来实现颜色显示  
        logView.RowRender += OnRowRender;  
  
        // 监听数据源变化以实现自动滚动  
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
          
        commandInput.KeyDown += (args, x) =>  
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
      
    public event Action<string>? CommandEntered;  
  
    protected virtual void OnCommandEntered(string command)  
    {  
        CommandEntered?.Invoke(command);  
    }  
  
    // 行渲染事件处理器 - 用于设置不同日志级别的颜色  
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
  
    // 数据源变化事件处理器 - 用于自动滚动  
    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)  
    {  
        if (e.Action == NotifyCollectionChangedAction.Add)  
        {  
            AutoScrollToBottom();   
        }  
    }  
  
    // 自动滚动到底部  
    private void AutoScrollToBottom()  
    {  
        if (logEntries.Count > 0)  
        {  
            logView.SelectedItem = logEntries.Count - 1;  
            logView.TopItem = Math.Max(0, logEntries.Count - logView.Viewport.Height);  
        }  
    }  
  
    public void AppendLog(LogLevel level, string message)  
    {  
        var logEntry = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";  
          
        // 存储日志级别  
        logLevels[logEntries.Count] = level;  
          
        // 添加到数据源  
        logEntries.Add(logEntry);  
    }  
}
