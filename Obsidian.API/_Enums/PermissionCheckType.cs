namespace Obsidian.API;

/// <summary>How a command checks a list of required permissions.</summary>
public enum PermissionCheckType
{
    /// <summary>The sender needs every permission in the list.</summary>
    All,
    /// <summary>The sender needs at least one permission in the list.</summary>
    Any
}
