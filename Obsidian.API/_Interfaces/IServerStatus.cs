namespace Obsidian.API;

public interface IServerStatus
{
    public IServerVersion Version { get; set; }

    public IServerPlayers? Players { get; set; }

    public IServerDescription Description { get; set; }

    /// <summary>
    /// This is a base64 png image, that has dimensions of 64x64
    /// </summary>
    public string Favicon { get; set; }
}

public interface IServerVersion
{
    public string Name { get; }

    public ProtocolVersion Protocol { get; }
}

public interface IServerPlayers
{
    public int Max { get; set; }

    public int Online { get; set; }

    public List<object> Sample { get; set; }

    public void Clear();

    public void AddPlayer(string name, Guid uuid);
}

public interface IServerDescription
{
    public string Text { get; set; }
}
