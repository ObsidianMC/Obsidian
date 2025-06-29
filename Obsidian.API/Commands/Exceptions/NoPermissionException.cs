namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Exception indicating that a command can not be executed by a certain CommandExecutor due to missing required permissions.
/// </summary>
internal class NoPermissionException : CommandExecutionCheckException
{
    public PermissionCheckType CheckType { get; }
    public string[] RequiredPermissions { get; }


    public NoPermissionException(string[] requiredPermsissions, PermissionCheckType checkType) : base("CommandSender does not have the required permissions.")
    {
        RequiredPermissions = requiredPermsissions;
        CheckType = checkType;
    }

}

