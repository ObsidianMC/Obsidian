namespace Obsidian.API;

/// <summary>
/// Restricts a command, or every command of a class, to senders with permissions. The console and remote console
/// always pass, and a command block passes only at permission level 4.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute : BaseExecutionCheckAttribute
{
    private readonly string[] _permissions;
    private readonly PermissionCheckType _checkType;
    private readonly bool _op;

    /// <summary>
    /// Gets whether the player needs all of <see cref="RequiredPermissions"/> or any one of them.
    /// </summary>
    public PermissionCheckType CheckType => _checkType;
    /// <summary>
    /// Gets the permission nodes checked.
    /// </summary>
    public string[] RequiredPermissions => _permissions;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequirePermissionAttribute"/> class.
    /// </summary>
    /// <param name="checkType">Whether the player needs all of <paramref name="permissions"/> or any one of them.</param>
    /// <param name="op">Whether operators pass without the permissions.</param>
    /// <param name="permissions">
    /// The permission nodes checked. With none, only operators pass, and only if <paramref name="op"/> is set.
    /// </param>
    public RequirePermissionAttribute(PermissionCheckType checkType = PermissionCheckType.All, bool op = true, params string[] permissions)
    {
        _permissions = permissions;
        _checkType = checkType;
        _op = op;
    }

    /// <inheritdoc />
    public override Task<bool> RunChecksAsync(CommandContext context)
    {
        // A sender with a level of its own (a command block) runs these only at the console's level.
        if (context.Sender is Commands.IPermissionLevelSender leveled)
            return Task.FromResult(leveled.PermissionLevel >= 4);

        if (context.Sender.Issuer is CommandIssuers.Console or CommandIssuers.RemoteConsole)
            return Task.FromResult(true);

        // The permissions are the sender's, even for a command /execute runs as another player.
        var player = context.Sender.Player ?? context.Player;
        if (player is null)
            return Task.FromResult(false);
        if(_op && context.Server.Operators.IsOperator(player))
            return Task.FromResult(true);

        if (_permissions.Length > 0)
        {
            return Task.FromResult(_checkType == PermissionCheckType.All
                ? player.HasAllPermissions(_permissions)
                : player.HasAnyPermission(_permissions));
        }

        return Task.FromResult(false);
    }
}
