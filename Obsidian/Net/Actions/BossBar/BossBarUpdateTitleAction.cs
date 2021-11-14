using Obsidian.API;
using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar;

public class BossBarUpdateTitleAction : BossBarAction
{
    public ChatMessage Title { get; set; }

    public BossBarUpdateTitleAction() : base(3) { }

    public override void WriteTo(MinecraftStream stream)
    {
        base.WriteTo(stream);

        stream.WriteChat(Title);
    }

    public override async Task WriteToAsync(MinecraftStream stream)
    {
        await base.WriteToAsync(stream);

        await stream.WriteChatAsync(Title);
    }
}
