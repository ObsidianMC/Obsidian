using System.Collections.Generic;

namespace Obsidian.Utilities.Mojang;

public class JoinedResponse
{
    public string Id { get; set; }

    public string PlayerName { get; set; }

    public List<JoinedProperty> Properties { get; set; }
}

public class JoinedProperty
{
    public string Name { get; set; }

    public string Value { get; set; }

    public string Signature { get; set; }
}
