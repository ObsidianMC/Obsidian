using Microsoft.Extensions.Logging;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Obsidian.GuiConsole.Window;

public class ObsdianConsole : Terminal.Gui.Views.Window
{
    private readonly TextView logView;
    private readonly TextField commandInput;

    public string GuiTitle => $"Obsidian GUI Console for Minecraft {ServerConstants.DefaultProtocol}";

    public TextView LogView => logView;

    public ObsdianConsole()
    {
        Title = GuiTitle;
        
        logView = new TextView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            WordWrap = true,
            Text = "",
            ScrollBars = true
        };
        
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
        
        commandInput.KeyDown += (args,x) =>
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
    }
    
    public event Action<string>? CommandEntered;

    protected virtual void OnCommandEntered(string command)
    {
        CommandEntered?.Invoke(command);
    }

    public void AppendLog(LogLevel level, string message)
    {
        Color color = new Color();
        switch (level)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
                color = Color.Gray;
                break;
            case LogLevel.Information:
                color = Color.Green;
                break;
            case LogLevel.Warning:
                color = Color.Yellow;
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                color = Color.Red;
                break;
            case LogLevel.None:
                color = Color.Gray;
                break;
        }
        logView.Text += $"{DateTime.Now:HH:mm:ss} [{level}] {message}{Environment.NewLine}";
    }
}
