namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute : BaseExecutionCheckAttribute
{
    private readonly string[] _permissions;
    private readonly PermissionCheckType _checkType;
    private readonly bool _op;

    public PermissionCheckType CheckType => _checkType;
    public string[] RequiredPermissions => _permissions;

    public RequirePermissionAttribute(PermissionCheckType checkType = PermissionCheckType.All, bool op = true, params string[] permissions)
    {
        _permissions = permissions;
        _checkType = checkType;
        _op = op;
    }

    public override Task<bool> RunChecksAsync(CommandContext context)
    {
        if (context.Sender.Issuer is CommandIssuers.Console or CommandIssuers.RemoteConsole)
            return Task.FromResult(true);
        if (context.Player == null)
            return Task.FromResult(false);
        if(_op && context.Server.Operators.IsOperator(context.Player))
            return Task.FromResult(true);

        if (_permissions.Length > 0)
            return Task.FromResult(_checkType == PermissionCheckType.All ? context.Player.HasAllPermissions(_permissions) : context.Player.HasAnyPermission(_permissions));

        return Task.FromResult(false);
    }
}
