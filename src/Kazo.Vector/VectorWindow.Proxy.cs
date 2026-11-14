using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.IO;
using Kazo.Vector.Documents;
using Kazo.Vector.Hosting;

namespace Kazo.Vector;

/// <summary>
/// What the window does with the module's proxy (doc/en/plan.md, work item 3): VectorCraft's file
/// pickers, and the OS theme and language.
/// </summary>
/// <remarks>
/// <para>
/// The proxy's messages arrive through a <c>[JSInvokable]</c> callback, outside the scope the OS opens
/// for app code: each handler that reaches the SDK (files, dialogs) enters the app's context itself.
/// </para>
/// <para>
/// The chords the window keeps are the OS's only (<see cref="ReservedKeys.Os"/>, decision 13), all
/// handled by the module, so the window has no chord of its own to answer: <c>Ctrl+N</c> stays
/// VectorCraft's New Document. VectorCraft's web build keeps no settings (decision 12), so the window
/// stores none: every start gets the theme and the language again.
/// </para>
/// </remarks>
public sealed partial class VectorWindow
{
    // The proxy's events of one frame (subscribed in StartFrameAsync, removed in StopFrameAsync).
    private void ConnectProxy(CraftFrame frame, bool connect)
    {
        if (connect)
        {
            frame.PickRequested += OnPickRequestedAsync;
            frame.SchemeChanged += OnSchemeChangedAsync;
        }
        else
        {
            frame.PickRequested -= OnPickRequestedAsync;
            frame.SchemeChanged -= OnSchemeChangedAsync;
        }
    }

    // VectorCraft is listening: it takes the OS theme and the user's language (decision 11). A refused
    // command leaves VectorCraft as it was: the drawings matter more.
    private async Task ApplyThemeAndLanguageAsync(CraftFrame frame)
    {
        await OnSchemeChangedAsync(await frame.GetColorSchemeAsync());
        await ExecuteAsync(frame, "prefs.set", new { key = VectorCraftLanguage.PreferenceKey, value = VectorCraftLanguage.Of(AppContext.Culture) });
    }

    // VectorCraft follows the OS theme, at start and at every change (decision 11): its default
    // "mediumDark" brightness for the dark scheme, "light" for the light one.
    private async Task OnSchemeChangedAsync(string scheme)
    {
        if (_frame is { } frame)
        {
            try
            {
                await frame.RequestAsync("ui.set", new { brightness = VectorCraftTheme.Of(scheme) }, cancellationToken: AppContext.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The window is closing: no theme to follow any more.
            }
        }
    }

    // VectorCraft opened its file picker: the OS dialog instead. File › Open (and the home screen's
    // Open…) is not handed to VectorCraft: it hears "cancelled", and the window opens each chosen
    // file itself (OpenPathAsync), bound to its file, or in a new window when its name is taken
    // (decision 9); the OS dialog takes several files, each one a tab. The other pickers (Place,
    // Scripts › Other Script) get the files read and handed over: VectorCraft reads their bytes
    // itself.
    private Task OnPickRequestedAsync(PickRequest request) => InvokeAsync(async () =>
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        CraftFrame? frame = _frame;
        if (frame is null)
        {
            return;
        }

        bool open = VectorFormats.IsOpenPicker(request);
        IReadOnlyList<PickedFile>? files = null;
        IReadOnlyList<string> toOpen = [];
        try
        {
            OpenFileDialogResult choice = await OpenFileAsync(
                new OpenFileDialogRequest
                {
                    Title = L[open ? "OpenTitle" : "PickTitle"],
                    InitialDirectory = VirtualPath.Parse(_documents.ActiveFolder() ?? AppContext.HomeDirectory),
                    AllowMultiple = open || request.Multiple,
                    Filters = FiltersOf(request, open),
                },
                AppContext.CancellationToken);
            if (choice.Status == FileDialogStatus.Selected && choice.Resources.Count > 0)
            {
                if (open)
                {
                    toOpen = [.. choice.Resources.Select(resource => resource.Path.Value)];
                }
                else
                {
                    files = await ReadPickedAsync(choice.Resources);
                }
            }
        }
        finally
        {
            // VectorCraft waits on its picker until it hears back: it always does, even on a failure.
            await frame.AnswerPickAsync(request.Id, files);
        }

        // After the answer: VectorCraft's picker is closed before the files open.
        foreach (string path in toOpen)
        {
            await OpenPathAsync(path);
        }
    });

    private async Task<IReadOnlyList<PickedFile>?> ReadPickedAsync(IReadOnlyList<SelectedFileResource> resources)
    {
        List<PickedFile> files = [];
        foreach (SelectedFileResource resource in resources)
        {
            string path = resource.Path.Value;
            string name = KazoPath.GetFileName(path) ?? path;
            try
            {
                byte[] bytes = await KazoFile.ReadAllBytesAsync(path, AppContext.CancellationToken);
                files.Add(new PickedFile(name, string.Empty, bytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                await ShowNoticeAsync(L["ReadFailed", name, exception.Message], error: true);
                return null;
            }
        }

        return files;
    }

    // For File › Open, the drawings Kazo Vector opens (in their order) and the other files VectorCraft
    // reads; for the other pickers, the extensions VectorCraft asked for. Then every file: VectorCraft
    // checks what it gets.
    private List<FileDialogFilter> FiltersOf(PickRequest request, bool open)
    {
        List<FileDialogFilter> filters = [];
        if (open)
        {
            filters.Add(new FileDialogFilter(L["DrawingsFilter"], VectorFormats.OpenExtensions, []));
            filters.Add(new FileDialogFilter(L["OtherFilesFilter"], VectorFormats.OtherFileExtensions, []));
        }
        else if (request.Extensions.Count > 0)
        {
            filters.Add(new FileDialogFilter(L["FilesFilter"], request.Extensions, []));
        }

        filters.Add(new FileDialogFilter(L["AllFiles"], [], []));
        return filters;
    }
}
