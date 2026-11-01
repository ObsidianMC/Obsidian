namespace Obsidian.WorldData;

/// <summary>
/// A world can't be loaded for a reason the server's operator can act on, such as a save the world upgrade doesn't
/// support. Its message says why, for the operator; the server stops without treating it as a crash (see
/// <see cref="Hosting.IServerEnvironment.OnWorldLoadFailedAsync"/>).
/// </summary>
internal sealed class WorldLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException);
