using Obsidian.API.Boss;

namespace Obsidian.API;

public interface IBossBar
{
    public Guid Uuid { get; }

    public bool HasPlayer(int id);

    public void AddPlayer(int id);

    public void RemovePlayer(int id);

    public void UpdateTitle(ChatMessage newTitle);

    public void UpdateHealth(float newHealth);

    public void UpdateColor(BossBarColor newColor);

    public void UpdateDivision(BossBarDivisionType newDivision);

    public void AddFlags(BossBarFlags newFlags);

    public void RemoveFlag(BossBarFlags flag);
}
