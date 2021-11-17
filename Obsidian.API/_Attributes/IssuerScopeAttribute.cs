namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class IssuerScopeAttribute : Attribute
{
    public CommandIssuers Issuers { get; }

    public IssuerScopeAttribute(CommandIssuers issuers)
    {
        Issuers = issuers;
    }
}
