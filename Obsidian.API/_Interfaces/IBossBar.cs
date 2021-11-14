using Obsidian.API.Boss;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.API;

public interface IBossBar
{
    public Guid Uuid { get; }

    public bool HasPlayer(Guid uuid);

    public Task AddPlayerAsync(Guid uuid);

    public Task RemovePlayerAsync(Guid uuid);

    public Task UpdateTitleAsync(ChatMessage newTitle);

    public Task UpdateHealthAsync(float newHealth);

    public Task UpdateColorAsync(BossBarColor newColor);

    public Task UpdateDivisionAsync(BossBarDivisionType newDivision);

    public Task AddFlagsAsync(BossBarFlags newFlags);

    public Task RemoveFlagAsync(BossBarFlags flag);
}
