using Obsidian.API.Commands;
using Obsidian.API.Plugins;

namespace Obsidian.API;
public interface ICommandHandler
{
    public IServiceProvider ServiceProvider { get; }
    public Task ProcessCommand(CommandContext ctx);

    /// <summary>
    /// Suggests completions for the last word of the context's message, a command line typed up to the cursor with or
    /// without the command prefix.
    /// </summary>
    public Task<CommandCompletion> CompleteAsync(CommandContext ctx);

    public void RegisterCommands(IPluginContainer? pluginContainer = null);

    public bool IsValidArgumentType(Type type);

    public BaseArgumentParser GetArgumentParser(Type type);
    public (int id, string mctype) FindMinecraftType(Type type);

    public Command[] GetAllCommands();
}
