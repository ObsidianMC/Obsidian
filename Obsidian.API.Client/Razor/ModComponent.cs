using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Obsidian.API.Client.Razor;

/// <summary>
/// The base of a mod's Razor components, in place of Blazor's <c>ComponentBase</c>, with the same lifecycle: put
/// <c>@inherits ModComponent</c> in the mod's <c>_Imports.razor</c>. The client renders mods' components only if they
/// inherit it.
/// </summary>
/// <remarks>
/// <para>
/// The client runs the component's code, its lifecycle methods, its rendering and its event handlers, on the game's
/// thread inside a call into its mod, as it does a <see cref="Screens.ModScreen"/>'s: once the mod unloads it no
/// longer runs, and if it throws, the screen it's on is logged and closed. An async lifecycle method or handler stays inside
/// the call until its task completes.
/// </para>
/// <para>
/// For that, the component can't implement Blazor's component interfaces (<c>IComponent</c>, <c>IHandleEvent</c>,
/// <c>IHandleAfterRender</c>, <c>IDisposable</c>, <c>IAsyncDisposable</c>) itself: clean up in
/// <see cref="OnDispose"/> rather than with <c>@implements IDisposable</c>.
/// </para>
/// </remarks>
public abstract class ModComponent : IComponent, IHandleEvent, IHandleAfterRender, IDisposable
{
    private readonly RenderFragment renderFragment;
    private RenderHandle renderHandle;
    private bool initialized;
    private bool rendered;
    private bool renderQueued;
    private bool afterRendered;

    protected ModComponent() => this.renderFragment = this.Render;

    // What the client runs the component's code inside, set as it creates the component; null where nothing guards
    // it (a renderer other than the client's), which runs it directly.
    internal IComponentCalls? Calls { get; set; }

    /// <summary>
    /// Runs once, when the component first gets its parameters, like Blazor's <c>OnInitialized</c>.
    /// </summary>
    protected virtual void OnInitialized()
    {
    }

    /// <summary>
    /// Runs once after <see cref="OnInitialized"/>; while its task runs, the component renders as it is.
    /// </summary>
    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    /// <summary>
    /// Runs each time the component gets its parameters, before it renders.
    /// </summary>
    protected virtual void OnParametersSet()
    {
    }

    /// <summary>
    /// Runs after <see cref="OnParametersSet"/>; the component renders again when its task completes.
    /// </summary>
    protected virtual Task OnParametersSetAsync() => Task.CompletedTask;

    /// <summary>
    /// Runs after each render; <paramref name="firstRender"/> is true the first time.
    /// </summary>
    protected virtual void OnAfterRender(bool firstRender)
    {
    }

    /// <summary>
    /// Runs after <see cref="OnAfterRender"/>.
    /// </summary>
    protected virtual Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;

    /// <summary>
    /// Whether the component renders when asked to, after its first render.
    /// </summary>
    protected virtual bool ShouldRender() => true;

    /// <summary>
    /// Runs as the component is removed, like when its screen closes, unless its mod is unloading.
    /// </summary>
    protected virtual void OnDispose()
    {
    }

    /// <summary>
    /// Renders the component: what its Razor markup compiles to.
    /// </summary>
    protected virtual void BuildRenderTree(RenderTreeBuilder builder)
    {
    }

    /// <summary>
    /// Renders the component again, like after changing a field from a payload handler (through
    /// <see cref="InvokeAsync(Action)"/>, on the game's thread).
    /// </summary>
    protected void StateHasChanged()
    {
        if (this.renderQueued || (this.rendered && !this.ShouldRender()))
            return;

        this.renderQueued = true;
        try
        {
            this.renderHandle.Render(this.renderFragment);
        }
        catch
        {
            this.renderQueued = false;
            throw;
        }
    }

    /// <summary>
    /// Runs work on the game's thread, inside a call into the mod, like from another thread.
    /// </summary>
    protected Task InvokeAsync(Action work) => this.renderHandle.Dispatcher.InvokeAsync(() =>
    {
        if (!this.TryEnter(out var call))
            return;

        using (call)
            work();
    });

    /// <inheritdoc cref="InvokeAsync(Action)"/>
    protected Task InvokeAsync(Func<Task> work) => this.renderHandle.Dispatcher.InvokeAsync(async () =>
    {
        if (!this.TryEnter(out var call))
            return;

        using (call)
            await work();
    });

    /// <summary>
    /// Fails the component with an exception from work outside its lifecycle, like a background task, which closes
    /// its screen as if the component had thrown it.
    /// </summary>
    protected Task DispatchExceptionAsync(Exception exception) => this.renderHandle.DispatchExceptionAsync(exception);

    void IComponent.Attach(RenderHandle renderHandle)
    {
        if (this.renderHandle.IsInitialized)
            throw new InvalidOperationException("The component is attached already.");

        this.renderHandle = renderHandle;
    }

    // Blazor's lifecycle: set the parameters, initialize the first time, then render, waiting for async lifecycle
    // methods with a render while they run. The call lasts until all of it completes.
    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        if (!this.TryEnter(out var call))
            return;

        using (call)
        {
            parameters.SetParameterProperties(this);
            if (!this.initialized)
            {
                this.initialized = true;
                this.OnInitialized();

                var initializing = this.OnInitializedAsync();
                if (!initializing.IsCompleted)
                    this.StateHasChanged();

                await WaitAsync(initializing);
            }

            this.OnParametersSet();
            var setting = this.OnParametersSetAsync();
            this.StateHasChanged();
            if (await WaitAsync(setting))
                this.StateHasChanged();
        }
    }

    // An event handler in the component's markup, which renders the component after it runs, and again once its
    // task completes.
    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? argument)
    {
        if (!this.TryEnter(out var call))
            return;

        using (call)
        {
            var handling = callback.InvokeAsync(argument);
            this.StateHasChanged();
            if (await WaitAsync(handling))
                this.StateHasChanged();
        }
    }

    async Task IHandleAfterRender.OnAfterRenderAsync()
    {
        if (!this.TryEnter(out var call))
            return;

        using (call)
        {
            var firstRender = !this.afterRendered;
            this.afterRendered = true;
            this.OnAfterRender(firstRender);
            await this.OnAfterRenderAsync(firstRender);
        }
    }

    void IDisposable.Dispose()
    {
        try
        {
            if (this.TryEnter(out var call))
            {
                using (call)
                    this.OnDispose();
            }
        }
        finally
        {
            this.Calls?.Disposed();
        }
    }

    private void Render(RenderTreeBuilder builder)
    {
        this.renderQueued = false;
        this.rendered = true;
        if (!this.TryEnter(out var call))
            return;

        using (call)
            this.BuildRenderTree(builder);
    }

    // Starts a call into the component's mod: false once the mod is unloading, when the component's code doesn't run.
    private bool TryEnter(out IDisposable? call)
    {
        call = this.Calls?.Enter();

        return call is not null || this.Calls is null;
    }

    // Waits for a lifecycle method's task, ignoring its cancellation like Blazor's; returns whether it was running.
    private static async Task<bool> WaitAsync(Task task)
    {
        var running = !task.IsCompleted;
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (task.IsCanceled)
        {
        }

        return running;
    }
}

/// <summary>
/// How the client runs a mod's component's code (see <see cref="ModComponent.Calls"/>).
/// </summary>
internal interface IComponentCalls
{
    /// <summary>
    /// Starts a call into the component's mod, to dispose when the code returns; null once the mod is unloading.
    /// </summary>
    public IDisposable? Enter();

    /// <summary>
    /// The component was removed, as its screen closed.
    /// </summary>
    public void Disposed();
}
