using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Apps;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.IO;
using Kazo.Vector.Documents;
using Microsoft.JSInterop;

namespace Kazo.Vector;

/// <summary>
/// Opening, saving, the exports and closing (doc/en/plan.md, work item 3): the window reads and
/// writes the files with <see cref="KazoFile"/>, asks the OS dialogs, keeps the title and guards the
/// close. <see cref="VectorDocuments"/> binds each VectorCraft tab to its file and decides each save.
/// </summary>
/// <remarks>
/// <para>
/// VectorCraft in host mode (fork F1) hands over only the saves of a drawing to its own file
/// (<c>vectorcraft:save</c>, H3), with <c>saveAs</c> false only for a Save over the drawing's own
/// file; it marks the drawing saved only when the window answers that the file was written (H4).
/// Copies, templates, exports, packages, preset files and a save's other files (separate artboards)
/// are downloads, which the module hands to the window as copies (<see cref="CraftFrame.Exporting"/>,
/// ADR 0264 of Kazo OS).
/// </para>
/// <para>
/// A name is bound once per window (decision 9): a file whose name is already taken here opens in a
/// new Kazo Vector window, and so does every file the OS opens with Kazo Vector, since each one
/// starts its own window (decision 7).
/// </para>
/// </remarks>
public sealed partial class VectorWindow : IWindowCloseGuard
{
    /// <summary>The app's id, to start another Kazo Vector window on a file.</summary>
    private const string AppId = "ca.kazo.vector";

    /// <summary>
    /// How long the close guard waits for VectorCraft's save of one drawing: a drawing never saved
    /// first shows VectorCraft's own name and format dialog, and a save that never comes must not
    /// hold the window open for good.
    /// </summary>
    private static readonly TimeSpan CloseSaveTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long the window waits for VectorCraft to show a file it sent (a large drawing takes a while).</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(120);

    /// <summary>How often the window looks for the tab of a file it sent.</summary>
    private static readonly TimeSpan OpenPollInterval = TimeSpan.FromMilliseconds(150);

    private readonly VectorDocuments _documents = new();

    // Saves run one at a time: each may show a dialog, and the order is VectorCraft's.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    // Completed with the outcome of the next save (true = written), for the close guard.
    private TaskCompletionSource<bool>? _nextSave;

    // A message over VectorCraft: a failed write, a cancelled save, a copy written.
    private string? _notice;
    private bool _noticeIsError;

    /// <summary>Gets the title the OS shows for this window: <c>*name — Kazo Vector</c>.</summary>
    internal string DocumentTitle => _documents.Title(L["Untitled"], _startup.State == FrameState.Running);

    /// <summary>
    /// Asks before closing a window with work that is not in a file: VectorCraft lists its tabs
    /// (<c>ui.inspect</c>, H7); "Yes" brings each modified one to the front and saves it with
    /// VectorCraft's own File › Save, and the window closes only once every write succeeded
    /// (decision 10).
    /// </summary>
    /// <param name="cancellationToken">Cancels the close request.</param>
    /// <returns><see langword="true"/> to close.</returns>
    public async ValueTask<bool> ConfirmCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_frame is not { } frame || _startup.State != FrameState.Running)
        {
            return true;
        }

        // A save still being written must end first: closing now would lose the write.
        await _saveGate.WaitAsync(cancellationToken);
        _saveGate.Release();

        if (!await RefreshTabsAsync(frame, cancellationToken))
        {
            // VectorCraft does not answer, so it cannot save either: closing without saving is the
            // only choice left, and it is the user's.
            if (!_documents.IsDirty)
            {
                return true;
            }

            MessageBoxDialogResult discard = await MessageBox(
                L["CloseTitle"], L["CloseUnanswered", NameOfActive()], MessageBoxType.YesNo, cancellationToken);
            return discard.Result == MessageBoxResult.Yes;
        }

        IReadOnlyList<OpenDocument> unsaved = _documents.WithUnsavedWork();
        if (unsaved.Count == 0)
        {
            return true;
        }

        string names = string.Join(", ", unsaved.Select(NameOf));
        MessageBoxDialogResult answer = await MessageBox(
            L["CloseTitle"], L["CloseUnsaved", names], MessageBoxType.YesNoCancel, cancellationToken);
        return answer.Result switch
        {
            MessageBoxResult.Yes => await SaveAllAsync(frame, unsaved, cancellationToken),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    // After `ready`: the file the window was started for. VectorCraft starts on its home screen
    // (New, Open…), as on the desktop: a window with no file stays there.
    private async Task PrepareDocumentAsync()
    {
        // Runs after `ready`, a [JSInvokable] callback: the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        if (_request.Path is { } path)
        {
            await OpenPathAsync(path);
        }

        await UpdateTitleAsync();
    }

    /// <summary>
    /// Opens a file where it belongs (<see cref="VectorDocuments.PlaceOf"/>): a new tab here, the tab
    /// that already has it, a new Kazo Vector window when its name is taken, or straight to
    /// VectorCraft for a file that is not a drawing.
    /// </summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>A task that completes once the file is shown, or a message says why not.</returns>
    private async Task OpenPathAsync(string path)
    {
        if (_frame is not { } frame)
        {
            return;
        }

        await RefreshTabsAsync(frame);
        switch (_documents.PlaceOf(path))
        {
            case OpenPlace.AlreadyOpen when _documents.TabOf(path) is { } tab:
                await ExecuteAsync(frame, "document.activate", new { index = _documents.IndexOf(tab) });
                await RefreshTabsAsync(frame);
                await UpdateTitleAsync();
                return;
            case OpenPlace.NewWindow:
                await LaunchWindowAsync(path);
                return;
            case OpenPlace.NotADocument:
                await SendFileAsync(frame, path, bind: false);
                return;
            default:
                await SendFileAsync(frame, path, bind: true);
                return;
        }
    }

    // Reads a file and sends it to VectorCraft (H2), which opens it in a new tab under its name; a
    // drawing is then bound to its file. A file that does not read, or that VectorCraft refuses,
    // leaves a message (VectorCraft's refusal comes back as `failed`).
    private async Task SendFileAsync(CraftFrame frame, string path, bool bind)
    {
        string name = CraftDocumentFormats.FileNameOf(path);
        byte[] bytes;
        try
        {
            bytes = await KazoFile.ReadAllBytesAsync(path, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowNoticeAsync(L["OpenFailed", name, exception.Message], error: true);
            return;
        }

        HashSet<long> before = [.. _documents.Tabs.Select(tab => tab.Id)];
        if (bind)
        {
            // Bound before VectorCraft sees it: the next save of this name, even one made before the
            // tab shows, is this file's.
            _documents.Opened(path);
        }

        if (!await frame.OpenAsync(name, bytes))
        {
            await ShowNoticeAsync(L["OpenFailed", name, L["NotConnected"]], error: true);
            return;
        }

        if (!bind)
        {
            // A library, preset, script or plug-in: VectorCraft says itself what it did with it.
            return;
        }

        await ClearNoticeAsync();
        await WaitForTabAsync(frame, path, before);
        await UpdateTitleAsync();
    }

    // VectorCraft opens the file on its next frames, or later for a large one: wait for its tab, so
    // the title names it. A file VectorCraft cannot read comes back as `failed`, a message.
    private async Task WaitForTabAsync(CraftFrame frame, string path, IReadOnlyCollection<long> before)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + OpenTimeout;
        while (DateTimeOffset.UtcNow < deadline && !AppContext.CancellationToken.IsCancellationRequested)
        {
            if (await RefreshTabsAsync(frame) && _documents.Shown(path, before))
            {
                return;
            }

            try
            {
                await Task.Delay(OpenPollInterval, AppContext.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Another Kazo Vector window on a file (decision 9).
    private async Task LaunchWindowAsync(string path)
    {
        try
        {
            await KazoApps.LaunchAsync(AppId, [path], AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is KazoAppLaunchException or KazoUnauthorizedAccessException)
        {
            await ShowNoticeAsync(L["OpenFailed", CraftDocumentFormats.FileNameOf(path), exception.Message], error: true);
        }
    }

    // Asks VectorCraft for its tabs (ui.inspect, H7); false when it does not answer.
    private async Task<bool> RefreshTabsAsync(CraftFrame frame, CancellationToken cancellationToken = default)
    {
        FrameCommandResult inspect;
        try
        {
            inspect = await frame.RequestAsync(
                "ui.inspect",
                new { },
                cancellationToken: cancellationToken == default ? AppContext.CancellationToken : cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The window is closing (or the caller gave up): no tabs to read, and nothing to report.
            return false;
        }

        if (!inspect.Ok)
        {
            return false;
        }

        _documents.Update(VectorDocuments.ListFrom(inspect.Value));
        return true;
    }

    private async Task OnDirtyChangedAsync(bool dirty)
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        _documents.SetFrameDirty(dirty);
        if (_frame is { } frame)
        {
            // The tab in front may have changed too: the title names it.
            await RefreshTabsAsync(frame);
        }

        await UpdateTitleAsync();
    }

    // One save of a drawing VectorCraft hands over: decided by VectorDocuments, written here. The
    // answer is what VectorCraft hears (`saved` only when true, H4).
    private async Task<bool> OnSavingAsync(FrameSave save)
    {
        // Called from JavaScript ([JSInvokable]): the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        await _saveGate.WaitAsync(AppContext.CancellationToken);
        bool written = false;
        try
        {
            written = await SaveAsync(save);
        }
        finally
        {
            _saveGate.Release();
            Interlocked.Exchange(ref _nextSave, null)?.TrySetResult(written);
            await UpdateTitleAsync();
        }

        return written;
    }

    // One file VectorCraft exports (Save a Copy, Save as Template, Export As, Export for Screens,
    // Save for Web, Package, preset files, a save's separate artboards…; ADR 0264 of Kazo OS): always
    // a copy, in the folder of the drawing in front. It does not wait for the save gate: it never
    // touches the drawing.
    private async Task<bool> OnExportingAsync(FrameSave export)
    {
        // Called from JavaScript ([JSInvokable]): the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        if (string.IsNullOrEmpty(export.Name))
        {
            return false;
        }

        SaveDecision decision = _documents.DecideCopy(export.Name);
        try
        {
            return await CarryOutAsync(decision, export.Bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowNoticeAsync(L["SaveFailed", export.Name, exception.Message], error: true);
            return false;
        }
    }

    private async Task<bool> SaveAsync(FrameSave save)
    {
        if (string.IsNullOrEmpty(save.Name))
        {
            return false;
        }

        if (_frame is { } frame)
        {
            // The save is the tab's in front: its file gives the dialog's folder.
            await RefreshTabsAsync(frame);
        }

        SaveDecision decision = _documents.Decide(save.Name, save.SaveAs);
        try
        {
            return await CarryOutAsync(decision, save.Bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever went wrong (a dialog, the file system), the drawing must not pass for saved.
            _documents.SaveFailed(decision);
            await ShowNoticeAsync(L["SaveFailed", save.Name, exception.Message], error: true);
            return false;
        }
    }

    // Asks for the path when the decision says so, writes the bytes, and records the outcome.
    private async Task<bool> CarryOutAsync(SaveDecision decision, byte[] bytes)
    {
        string? path = decision.AsksForPath ? await AskSavePathAsync(decision) : decision.Path;
        if (path is null)
        {
            _documents.SaveFailed(decision);
            if (decision.SavesTheDocument)
            {
                await ShowNoticeAsync(L["SaveCancelled", decision.Name], error: false);
            }

            return false;
        }

        try
        {
            await KazoFile.WriteAllBytesAsync(path, bytes, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _documents.SaveFailed(decision);
            await ShowNoticeAsync(L["SaveFailed", CraftDocumentFormats.FileNameOf(path), exception.Message], error: true);
            return false;
        }

        _documents.Saved(decision, path);
        if (decision.Action == SaveAction.AskForCopy)
        {
            await ShowNoticeAsync(L["CopySaved", CraftDocumentFormats.FileNameOf(path)], error: false);
            return true;
        }

        // VectorCraft keeps the name it gave as the drawing's path once it hears `saved` (H4): its
        // next save under that name goes to the file just chosen (the binding).
        await ClearNoticeAsync();
        return true;
    }

    // The OS save dialog, in the drawing's folder, for the format VectorCraft chose.
    private async Task<string?> AskSavePathAsync(SaveDecision decision)
    {
        string extension = CraftDocumentFormats.ExtensionOf(decision.Name);
        CraftDocumentFormat? format = VectorFormats.Formats.Find(decision.Name);
        SaveFileDialogResult result = await SaveFileAsync(new SaveFileDialogRequest
        {
            Title = decision.Action == SaveAction.AskForCopy ? L["SaveCopyTitle"] : L["SaveTitle"],
            InitialDirectory = VirtualPath.Parse(decision.Folder ?? AppContext.HomeDirectory),
            SuggestedFileName = decision.Name,
            DefaultExtension = extension.Length > 0 ? extension : null,
            Filters = extension.Length > 0
                ? [new FileDialogFilter(L["FilterFormat", extension], [extension], format is null ? [] : [format.MediaType])]
                : [],
            ConfirmOverwrite = true,
        });
        return result.Status == FileDialogStatus.Selected && result.Resource is { } chosen
            ? CraftDocumentFormats.WithExtensionOf(chosen.Path.Value, decision.Name)
            : null;
    }

    // "Yes" in the close dialog: each drawing is brought to the front (document.activate) and saved
    // by VectorCraft's own File › Save; the window closes only when all of them reached a file.
    private async Task<bool> SaveAllAsync(CraftFrame frame, IReadOnlyList<OpenDocument> documents, CancellationToken cancellationToken)
    {
        foreach (OpenDocument document in documents)
        {
            // The list may have moved since the question: the drawing is found again by its id.
            await RefreshTabsAsync(frame, cancellationToken);
            int index = _documents.IndexOf(document);
            if (index < 0)
            {
                // Closed in VectorCraft meanwhile: nothing left to save.
                continue;
            }

            TaskCompletionSource<bool> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _nextSave = outcome;
            FrameCommandResult result = await ExecuteAsync(frame, "document.activate", new { index }, cancellationToken);
            if (result.Ok)
            {
                result = await frame.RequestAsync("ui.menu.invoke", new { id = "file.save" }, cancellationToken: cancellationToken);
            }

            if (!result.Ok)
            {
                _nextSave = null;
                await ShowNoticeAsync(L["SaveFailed", NameOf(document), result.Error ?? string.Empty], error: true);
                return false;
            }

            bool written;
            try
            {
                written = await outcome.Task.WaitAsync(CloseSaveTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _nextSave = null;
                written = false;
            }

            if (!written)
            {
                return false;
            }
        }

        return true;
    }

    // The name of a drawing in a message: its file's, else its tab's.
    private string NameOf(OpenDocument document) => _documents.NameOf(document, L["Untitled"]);

    private string NameOfActive() => _documents.Active is { } tab ? NameOf(tab) : L["Untitled"].Value;

    private async Task UpdateTitleAsync()
    {
        try
        {
            await SetWindowTitleAsync(DocumentTitle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JSDisconnectedException)
        {
            // The window is closing.
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task ShowNoticeAsync(string text, bool error)
    {
        _notice = text;
        _noticeIsError = error;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ClearNoticeAsync()
    {
        if (_notice is not null)
        {
            _notice = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void DismissNotice() => _notice = null;
}
