namespace Obsidian.API;

public interface IPlayer : ILiving
{
    public Container Inventory { get; }
    public Container EnderInventory { get; }
    public BaseContainer? OpenedContainer { get; set; }

    public List<SkinProperty> SkinProperties { get; set; }

    public ClientInformation ClientInformation { get; }

    public string Username { get; }

    public bool IsOperator { get; }

    public Vector? LastDeathLocation { get; set; }

    public string? ClientIP { get; }
    public Gamemode Gamemode { get; set; }

    public PlayerAbility Abilities { get; }

    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }
    public short CurrentSlot { get; }

    public int Ping { get; }
    public int FoodLevel { get; set; }
    public int FoodTickTimer { get; set; }
    public int XpLevel { get; set; }
    public int XpTotal { get; set; }

    public double HeadY { get; }

    public float AdditionalHearts { get; set; }
    public float FallDistance { get; set; }
    public float FoodExhaustionLevel { get; set; }
    public float FoodSaturationLevel { get; set; }

    public ValueTask SendMessageAsync(ChatMessage message);
    public ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature);
    public ValueTask SetActionBarTextAsync(ChatMessage message);
    public ValueTask SendSoundAsync(ISoundEffect soundEffect);
    public ValueTask KickAsync(ChatMessage reason);
    public ValueTask KickAsync(string reason);
    public ValueTask OpenInventoryAsync(BaseContainer container);
    public ValueTask DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot position);

    /// <summary>
    /// Sends a title message to the player.
    /// </summary>
    /// <param name="title">The title text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public ValueTask SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends a title and subtitle message to the player.
    /// </summary>
    /// <param name="title">The title text</param>
    /// <param name="subtitle">The subtitle text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public ValueTask SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends a subtitle message to the player.
    /// </summary>
    /// <param name="subtitle">The title text</param>
    /// <param name="fadeIn">Time in ticks for the title to fade in</param>
    /// <param name="stay">Time in ticks for the title to stay on screen</param>
    /// <param name="fadeOut">Time in ticks for the title to fade out</param>
    public ValueTask SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut);

    /// <summary>
    /// Sends an action bar text to the player.
    /// </summary>
    /// <param name="text">The text of the action bar.</param>
    public ValueTask SendActionBarAsync(string text);

    public ValueTask SpawnParticleAsync(ParticleData data);

    public Task<bool> GrantPermissionAsync(string permission);
    public Task<bool> RevokePermissionAsync(string permission);
    public bool HasPermission(string permission);
    public bool HasAnyPermission(IEnumerable<string> permissions);
    public bool HasAllPermissions(IEnumerable<string> permissions);
    public ValueTask SetGamemodeAsync(Gamemode gamemode);

    public ValueTask UpdateDisplayNameAsync(string newDisplayName);

    public ItemStack? GetHeldItem();
    public ItemStack? GetOffHandItem();
}
