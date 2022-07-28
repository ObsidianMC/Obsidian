namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute : BaseExecutionCheckAttribute
{
    private string[] permissions;
    private PermissionCheckType checkType;
    private bool op;

    public RequirePermissionAttribute(PermissionCheckType checkType = PermissionCheckType.All, bool op = true, params string[] permissions)
    {
        this.permissions = permissions;
        this.checkType = checkType;
        this.op = op;
    }

    public override Task<bool> RunChecksAsync(CommandContext context)
    {
        if (context.Sender.Issuer is CommandIssuers.Console or CommandIssuers.RemoteConsole)
            return Task.FromResult(true);
        if (context.Player == null)
            return Task.FromResult(false);
        if (this.op && context.Player.IsOperator)
            return Task.FromResult(true);

        if (this.permissions.Length > 0)
            return Task.FromResult(checkType == PermissionCheckType.All ? context.Player.HasAllPermissions(permissions) : context.Player.HasAnyPermission(permissions));

        return Task.FromResult(false);
    }
}
