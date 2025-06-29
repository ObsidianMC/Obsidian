using System.Reflection;

namespace Obsidian.API.Utilities.Interfaces;

public interface IExecutor
{
    public ParameterInfo[] GetParameters();
    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute;

    public bool MatchParams(object[] args);

    public ValueTask Execute(IServiceProvider serviceProvider, params object[]? methodParams);
}

public interface IExecutor<TArg>
{
    public ParameterInfo[] GetParameters();
    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute;

    public bool MatchParams(object[] args);

    public ValueTask Execute(IServiceProvider serviceProvider, TArg arg, params object[]? methodParams);
}
