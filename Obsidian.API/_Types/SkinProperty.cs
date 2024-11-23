namespace Obsidian.API;
public sealed class SkinProperty
{
    public required string Name { get; set; }

    public required string Value { get; set; }
    public string? Signature { get; set; }

    public bool HasSignature => this.Signature != null;
}
