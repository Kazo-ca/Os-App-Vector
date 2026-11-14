using Kazo.Vector.Hosting;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Kazo.Vector;

/// <summary>
/// The Kazo Vector window (doc/en/plan.md, work item 2): VectorCraft in an iframe started by the Kazo
/// OS module <c>Kazo.Os.AppSdk.Craft</c> (<see cref="CraftFrame"/>), with a loading screen until it is
/// ready and an error screen with "Retry" when it fails or does not answer within
/// <see cref="FrameStartup.Timeout"/>.
/// </summary>
/// <remarks>
/// Once VectorCraft is ready (<c>vectorcraft:ready</c>, which only the host mode of our fork sends,
/// fork H1) the window gives it the OS theme and the user's language (decision 11), then opens the
/// file it was started for. Opening, saving, the exports, the tabs bound to their files and the close
/// guard are in <c>VectorWindow.Documents.cs</c> (work item 3); the file picker, the theme and the
/// language in <c>VectorWindow.Proxy.cs</c>. The launch argument <c>--webgl</c> forces VectorCraft's
/// WebGL 2 renderer, to try it without WebGPU.
/// </remarks>
public sealed partial class VectorWindow : IAsyncDisposable
{
    private readonly FrameStartup _startup = new();
    private ElementReference _frameHost;
    private CraftFrame? _frame;
    private CancellationTokenSource? _timeout;
    private IStringLocalizer<Strings>? _localizer;
    private OpenRequest _request = new(null, false);

    [Inject]
    private IJSRuntime JavaScript { get; set; } = default!;

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    /// <summary>The error screen's detail: VectorCraft's own error, or the timeout.</summary>
    private string FailureText => _startup.TimedOut
        ? L["TimedOut", (int)FrameStartup.Timeout.TotalSeconds]
        : _startup.Error ?? string.Empty;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        CancelTimeout();
        await StopFrameAsync();
        _nextSave?.TrySetResult(false);
        _saveGate.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>Reads what the window was started for, and names it before it is first shown.</remarks>
    protected override async Task OnAppInitializedAsync()
    {
        _request = OpenRequest.FromArguments(LaunchArguments);
        await SetWindowTitleAsync(DocumentTitle);
    }

    /// <inheritdoc />
    protected override async Task OnAppAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await StartFrameAsync();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The window came to the front: the keyboard goes to VectorCraft, and the title follows the tab
    /// in front, which VectorCraft may have changed without any change to save.
    /// </remarks>
    protected override async Task OnWindowActivatedAsync()
    {
        if (_frame is { } frame && _startup.State == FrameState.Running)
        {
            await frame.FocusAsync();
            await RefreshTabsAsync(frame);
            await UpdateTitleAsync();
        }
    }

    private async Task StartFrameAsync()
    {
        CraftFrame frame = new(JavaScript);
        frame.Ready += OnReadyAsync;
        frame.Failed += OnFailedAsync;
        frame.DirtyChanged += OnDirtyChangedAsync;
        frame.Saving += OnSavingAsync;
        frame.Exporting += OnExportingAsync;
        ConnectProxy(frame, connect: true);
        _frame = frame;
        ArmTimeout(_startup.Attempt);

        try
        {
            await frame.StartAsync(_frameHost, FrameOptions(), AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            await OnFailedAsync(exception.Message);
        }
    }

    // VectorCraft's page, its language, the WebGL 2 switch and the chords the OS keeps. No control
    // parameter: window.vectorcraft is always there (decision 6). Ctrl+N stays VectorCraft's "New
    // document" (decision 13): in a tabbed editor a new document is a new tab, not a new window.
    private CraftFrameOptions FrameOptions() =>
        new(CraftFrameOptions.PagePathOf(typeof(VectorWindow).Assembly.GetName().Name!), L["EditorLabel"])
        {
            Language = VectorCraftLanguage.Of(AppContext.Culture),
            Parameters = VectorCraftParameters.For(_request.WebGl),
            ReservedKeys = ReservedKeys.Os,
        };

    private async Task StopFrameAsync()
    {
        if (_frame is null)
        {
            return;
        }

        _frame.Ready -= OnReadyAsync;
        _frame.Failed -= OnFailedAsync;
        _frame.DirtyChanged -= OnDirtyChangedAsync;
        _frame.Saving -= OnSavingAsync;
        _frame.Exporting -= OnExportingAsync;
        ConnectProxy(_frame, connect: false);
        await _frame.DisposeAsync();
        _frame = null;
    }

    private async Task RetryAsync()
    {
        CancelTimeout();
        await StopFrameAsync();
        _startup.Retry();
        StateHasChanged();
        await StartFrameAsync();
    }

    private async Task OnReadyAsync(string? appVersion)
    {
        if (!_startup.OnReady(appVersion))
        {
            return;
        }

        CancelTimeout();
        await InvokeAsync(StateHasChanged);
        if (_frame is not { } frame)
        {
            return;
        }

        await ApplyThemeAndLanguageAsync(frame);
        await PrepareDocumentAsync();
        if (_frame is { } ready && IsWindowActive)
        {
            await ready.FocusAsync();
        }
    }

    private async Task OnFailedAsync(string? error)
    {
        if (_startup.State == FrameState.Running)
        {
            // Once running, VectorCraft also reports a file it refused (H2: too large, unreadable)
            // as `failed`: the app is still there, so that is a message, not the error screen.
            await ShowNoticeAsync(error ?? string.Empty, error: true);
            return;
        }

        if (_startup.OnFailed(error))
        {
            CancelTimeout();
            await InvokeAsync(StateHasChanged);
        }
    }

    private Task<FrameCommandResult> ExecuteAsync(CraftFrame frame, string command, object parameters, CancellationToken cancellationToken = default) =>
        frame.RequestAsync(
            "engine.execute",
            new { command, @params = parameters },
            cancellationToken: cancellationToken == default ? AppContext.CancellationToken : cancellationToken);

    // Shows the error screen if attempt `attempt` gets no answer in time.
    private void ArmTimeout(int attempt)
    {
        CancelTimeout();
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(AppContext.CancellationToken);
        _timeout = timeout;
        _ = WaitForTimeoutAsync(attempt, timeout.Token);
    }

    private async Task WaitForTimeoutAsync(int attempt, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FrameStartup.Timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            if (_startup.OnTimeout(attempt))
            {
                StateHasChanged();
            }
        });
    }

    private void CancelTimeout()
    {
        _timeout?.Cancel();
        _timeout?.Dispose();
        _timeout = null;
    }
}
