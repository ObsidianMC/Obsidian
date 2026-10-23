namespace Obsidian.API.Client;

/// <summary>
/// Local sound playback for a mod, obtained from IClientRegistry.Sounds and usable in screens and client ticks.
/// Calls may come from any thread: playback is queued onto the game thread, and OpenAL runs on its audio worker.
/// Returns false when this mod is inactive or audio is unavailable. Missing assets are silently ignored.
/// Queued requests and this facade stop working when the mod unloads; obtain a new facade on reload.
/// </summary>
public interface IClientSounds
{
    /// <summary>Queues a positional sound; category volume settings and resource-pack sound variants apply.</summary>
    public bool Play(
        SoundEvent sound,
        VectorD position,
        SoundCategory category = SoundCategory.Master,
        float volume = 1,
        float pitch = 1);

    /// <summary>Queues UI audio, also without a server connected. It plays while menus pause the world.</summary>
    public bool PlayUi(SoundEvent sound, float volume = 1, float pitch = 1);
}
