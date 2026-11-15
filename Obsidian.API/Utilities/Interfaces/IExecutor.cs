using System.Reflection;

namespace Obsidian.API.Utilities.Interfaces;

/// <summary>
/// Exposes handler parameter metadata and invocation using a service provider.
/// </summary>
public interface IExecutor
{
    /// <summary>
    /// Returns reflection metadata for the underlying handler's declared parameters.
    /// </summary>
    public ParameterInfo[] GetParameters();
    /// <summary>
    /// Returns attributes of the requested type declared on the underlying handler method.
    /// </summary>
    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute;

    /// <summary>
    /// Tests whether the supplied arguments match this executor's expected parameter count.
    /// </summary>
    public bool MatchParams(object[] args);

    /// <summary>
    /// Invokes the handler using the service provider and supplied arguments, awaiting asynchronous completion.
    /// </summary>
    public ValueTask Execute(IServiceProvider serviceProvider, params object[]? methodParams);
}

/// <summary>
/// Exposes handler invocation with a separate context argument and a service provider.
/// </summary>
public interface IExecutor<TArg>
{
    /// <summary>
    /// Returns reflection metadata for the underlying handler's declared parameters.
    /// </summary>
    public ParameterInfo[] GetParameters();
    /// <summary>
    /// Returns attributes of the requested type declared on the underlying handler method.
    /// </summary>
    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute;

    /// <summary>
    /// Tests whether the supplied arguments match this executor's expected parameter count.
    /// </summary>
    public bool MatchParams(object[] args);

    /// <summary>
    /// Invokes the handler using the service provider and supplied arguments, awaiting asynchronous completion.
    /// </summary>
    public ValueTask Execute(IServiceProvider serviceProvider, TArg arg, params object[]? methodParams);
}
