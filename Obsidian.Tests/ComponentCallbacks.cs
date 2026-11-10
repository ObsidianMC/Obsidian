using Obsidian.API.Inventory;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using Xunit;
using Xunit.Sdk;

[assembly: Obsidian.Tests.ComponentCallbacks]

namespace Obsidian.Tests;

// Each test owns a queue flowing into its async/background work. Even deliberately swallowed plugin errors
// fail that test; parallel tests do not share failure state. Plugin copies of this assembly install no handlers.
public sealed class ComponentCallbacks : BeforeAfterTestAttribute
{
    private static readonly AsyncLocal<ConcurrentQueue<Exception>> failures = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (AssemblyLoadContext.GetLoadContext(typeof(ComponentCallbacks).Assembly)?.IsCollectible == true)
            return;

        ComponentExecutionGuard.Enabled = true;
        ComponentExecutionGuard.Violation += failure => failures.Value?.Enqueue(failure);
    }

    public override void Before(MethodInfo methodUnderTest) => failures.Value = new();

    public override void After(MethodInfo methodUnderTest)
    {
        var observed = failures.Value;
        failures.Value = null;
        if (observed is { IsEmpty: false })
            throw new AggregateException("Unsafe component callbacks were observed by this test.", observed);
    }

    internal static void AssertViolation(Action action)
    {
        var parent = failures.Value;
        var expected = new ConcurrentQueue<Exception>();
        failures.Value = expected;
        try
        {
            Assert.Throws<ComponentExecutionGuard.ComponentExecutionException>(action);
            // The notification survives a caller catching the exception, so After still fails that test.
            Assert.NotEmpty(expected);
        }
        finally
        {
            failures.Value = parent;
        }
    }
}
