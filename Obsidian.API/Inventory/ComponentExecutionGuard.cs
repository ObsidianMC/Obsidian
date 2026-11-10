namespace Obsidian.API.Inventory;

// Tests enable this for the entire assembly. A violation is also reported before throwing so a plugin error
// boundary cannot hide unsafe callback execution from the test that exercised it.
internal static class ComponentExecutionGuard
{
    internal static bool Enabled { get; set; }

    internal static Func<bool>? TransferHeld { get; set; }

    internal static event Action<Exception>? Violation;

    private static bool IsUnsafe => Enabled && (InventorySnapshot.IsAdmitted || TransferHeld?.Invoke() == true);

    internal static void Check(string operation)
    {
        if (!IsUnsafe)
            return;

        var failure = new ComponentExecutionException(
            $"Component {operation} ran inside snapshot admission or an entity transfer lock.");
        Violation?.Invoke(failure);
        throw failure;
    }

    // Built-in models reach their delegates through SimpleDataComponent or registered codec guards. Custom
    // component overrides may implement the callback directly instead.
    internal static void CheckOverride(DataComponent component)
    {
        if (component.GetType().Assembly != typeof(DataComponent).Assembly)
            Check("override");
    }

    internal static void CheckDelegate(Delegate callback, string operation)
    {
        if (!IsUnsafe)
            return;

        foreach (var target in callback.GetInvocationList())
        {
            // Pure built-in copies/codecs are host code. Any nested component they reach has its own guard.
            if (target.Method.Module.Assembly != typeof(DataComponent).Assembly)
                Check(operation);
        }
    }

    internal sealed class ComponentExecutionException(string message) : Exception(message);
}
