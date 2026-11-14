using System.Text.Json;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Vector.Documents;

/// <summary>Where a file asked for opens (<see cref="VectorDocuments.PlaceOf"/>).</summary>
public enum OpenPlace
{
    /// <summary>A new tab of this window, bound to its file.</summary>
    Here,

    /// <summary>The file is already open in this window: its tab is brought to the front.</summary>
    AlreadyOpen,

    /// <summary>
    /// Another Kazo Vector window: a file of the same name is already in this one, and VectorCraft
    /// would take the two for one file and bring back the first tab (decision 9).
    /// </summary>
    NewWindow,

    /// <summary>
    /// A file that is not a drawing (swatches, presets, a script…): handed to VectorCraft, never
    /// bound (<see cref="VectorFormats.IsDocument"/>).
    /// </summary>
    NotADocument,
}

/// <summary>
/// The drawings of one Kazo Vector window and their files (doc/en/plan.md, work item 3, decisions 7
/// to 10): each VectorCraft tab is bound to its own file, by the name VectorCraft saves it under.
/// </summary>
/// <remarks>
/// <para>
/// In host mode VectorCraft keeps a file the window opened under its name, when the format can hold
/// the drawing (fork, H2), and hands each save of a drawing over with that name and <c>saveAs</c>
/// (H3). Its <c>ui.inspect</c> lists every tab as <c>{ id, title, path, dirty, active }</c> (H7),
/// WordCraft's shape, which <see cref="OpenDocument.ListFrom(JsonElement)"/> reads: the id is the
/// drawing's stable <c>uid</c>, the name its <c>path</c>. The module's <see cref="DocumentSession"/>
/// binds names to files and decides each save; this class adds what a window with tabs needs around
/// it: where a file opens (decision 9), the file each tab came from, the save dialog's folder, and
/// the title.
/// </para>
/// <para>
/// A file whose format VectorCraft does not save (<c>.dxf</c>, <c>.emf</c>, <c>.ait</c>…) opens as a
/// drawing with no path: its first save asks where to write it. The window still remembers which
/// file the tab came from (<see cref="Shown"/>), so opening that file again brings the tab back and
/// the save dialog starts in its folder.
/// </para>
/// <para>
/// VectorCraft marks a drawing saved only when the window answers that the file was written (H4), so
/// its <c>dirty</c> state is trusted as is: a save the window refused leaves the tab modified.
/// </para>
/// </remarks>
public sealed class VectorDocuments
{
    /// <summary>The window's name for its app, at the end of its title.</summary>
    public const string AppName = "Kazo Vector";

    private readonly DocumentSession _session = new(VectorFormats.Formats, AppName);

    // The file each tab was opened from, by the drawing's id: the only link for a format VectorCraft
    // keeps no path for.
    private readonly Dictionary<long, string> _shownFrom = [];
    private IReadOnlyList<OpenDocument> _tabs = [];

    /// <summary>Gets the tabs as VectorCraft last listed them.</summary>
    public IReadOnlyList<OpenDocument> Tabs => _tabs;

    /// <summary>Gets the tab in front, or <see langword="null"/> when VectorCraft shows its home screen.</summary>
    public OpenDocument? Active => _tabs.FirstOrDefault(tab => tab.Active);

    /// <summary>Gets whether the window has work that is not in a file.</summary>
    public bool IsDirty => _session.FrameDirty || _tabs.Any(tab => tab.Dirty) || _session.UnsavedNames.Count > 0;

    /// <summary>Reads VectorCraft's tabs from its <c>ui.inspect</c> answer.</summary>
    /// <param name="inspect">VectorCraft's answer; anything malformed is skipped.</param>
    /// <returns>The tabs, in VectorCraft's order.</returns>
    public static IReadOnlyList<OpenDocument> ListFrom(JsonElement inspect) => OpenDocument.ListFrom(inspect);

    /// <summary>Gets the file a name is bound to.</summary>
    /// <param name="name">The name VectorCraft saves a drawing under.</param>
    /// <returns>The file's absolute path, or <see langword="null"/>.</returns>
    public string? PathOf(string name) => _session.PathOf(name);

    /// <summary>Takes VectorCraft's tab list; a closed tab forgets the file it came from.</summary>
    /// <param name="tabs">The tabs VectorCraft has open.</param>
    public void Update(IReadOnlyList<OpenDocument> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        _tabs = tabs;
        foreach (long closed in _shownFrom.Keys.Where(id => tabs.All(tab => tab.Id != id)).ToList())
        {
            _shownFrom.Remove(closed);
        }
    }

    /// <summary>The place of a tab in VectorCraft's order: what <c>document.activate { index }</c> takes.</summary>
    /// <param name="tab">A tab of <see cref="Tabs"/>.</param>
    /// <returns>Its index, or -1 when it is no longer listed.</returns>
    public int IndexOf(OpenDocument tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        for (int index = 0; index < _tabs.Count; index++)
        {
            if (_tabs[index].Id == tab.Id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>VectorCraft reported that unsaved changes appeared, or went away, in its tabs (H6).</summary>
    /// <param name="dirty">The new state.</param>
    public void SetFrameDirty(bool dirty) => _session.SetFrameDirty(dirty);

    /// <summary>
    /// Says where a file opens: here, in its tab when it is already open, in a new window when its
    /// name is taken by another file (decision 9), or straight to VectorCraft when it is not a drawing.
    /// </summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>The place.</returns>
    public OpenPlace PlaceOf(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string name = CraftDocumentFormats.FileNameOf(path);
        if (!VectorFormats.IsDocument(name))
        {
            return OpenPlace.NotADocument;
        }

        if (TabOf(path) is not null)
        {
            return OpenPlace.AlreadyOpen;
        }

        // A tab that already saves under this name (VectorCraft would bring it back instead of
        // opening the file: same_open_path), a name bound to another file, or another file of this
        // name shown here (a tab bound after Save As keeps VectorCraft's name; a tab of a format with
        // no path was opened from a file of this name): the file gets its own window.
        bool saving = _tabs.Any(tab => string.Equals(tab.Name, name, StringComparison.Ordinal));
        string? bound = _session.PathOf(name);
        bool shown = _tabs.Any(tab => FileOf(tab) is { } other && string.Equals(CraftDocumentFormats.FileNameOf(other), name, StringComparison.Ordinal));
        return saving || shown || (bound is not null && !string.Equals(bound, path, StringComparison.Ordinal))
            ? OpenPlace.NewWindow
            : OpenPlace.Here;
    }

    /// <summary>The tab of an open file: the tab bound to it, or opened from it, whatever VectorCraft names it.</summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>The tab, or <see langword="null"/>.</returns>
    public OpenDocument? TabOf(string path) =>
        _tabs.FirstOrDefault(tab => string.Equals(FileOf(tab), path, StringComparison.Ordinal));

    /// <summary>
    /// The file a tab lives in: the one its name is bound to, else the one it was opened from
    /// (a format VectorCraft keeps no path for); <see langword="null"/> for a drawing never saved.
    /// </summary>
    /// <param name="tab">A tab.</param>
    /// <returns>The file's absolute path, or <see langword="null"/>.</returns>
    public string? FileOf(OpenDocument tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return tab.Name is { } name && _session.PathOf(name) is { } bound
            ? bound
            : _shownFrom.GetValueOrDefault(tab.Id);
    }

    /// <summary>
    /// A drawing was handed to VectorCraft, which keeps it under its name when its format can hold it
    /// (H2): the name is bound to the file at once, so the next save of this name, even one made
    /// before the tab shows, is this file's.
    /// </summary>
    /// <param name="path">The file's absolute path.</param>
    public void Opened(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _session.Opened(CraftDocumentFormats.FileNameOf(path), path);
    }

    /// <summary>
    /// Finds the tab a file just sent became (a tab not listed before, saved under the file's name or
    /// titled after it) and remembers which file it came from.
    /// </summary>
    /// <param name="path">The file sent.</param>
    /// <param name="before">The ids of the tabs listed before it was sent.</param>
    /// <returns><see langword="true"/> once the tab is listed.</returns>
    public bool Shown(string path, IReadOnlyCollection<long> before)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(before);
        string name = CraftDocumentFormats.FileNameOf(path);
        string stem = Path.GetFileNameWithoutExtension(name);
        OpenDocument? shown = _tabs.FirstOrDefault(tab => !before.Contains(tab.Id)
            && (string.Equals(tab.Name, name, StringComparison.Ordinal)
                || string.Equals(tab.Title, name, StringComparison.Ordinal)
                || (stem.Length > 0 && tab.Title.StartsWith(stem, StringComparison.Ordinal))));
        if (shown is null)
        {
            return false;
        }

        _shownFrom[shown.Id] = path;
        return true;
    }

    /// <summary>
    /// Decides a save of the drawing in front: the module's rule (<see cref="DocumentSession.Decide"/>),
    /// with the save dialog opening in the folder of the tab's own file when the name is new (a DXF
    /// saved as a <c>.vectorcraft</c> goes next to it).
    /// </summary>
    /// <param name="name">The name VectorCraft gave.</param>
    /// <param name="saveAs">VectorCraft's <c>saveAs</c>.</param>
    /// <returns>The decision.</returns>
    public SaveDecision Decide(string name, bool saveAs)
    {
        SaveDecision decision = _session.Decide(name, saveAs);
        return decision.Folder is null && ActiveFolder() is { } folder ? decision with { Folder = folder } : decision;
    }

    /// <summary>
    /// Decides an export (a copy, ADR 0264 of Kazo OS: Save a Copy, Save as Template, Export As,
    /// Export for Screens, a package, a preset file…): always asks, in the folder of the tab in front.
    /// </summary>
    /// <param name="name">The name VectorCraft gave.</param>
    /// <returns>The decision.</returns>
    public SaveDecision DecideCopy(string name)
    {
        SaveDecision decision = _session.DecideCopy(name);
        return decision.Folder is null && ActiveFolder() is { } folder ? decision with { Folder = folder } : decision;
    }

    /// <summary>The file was written.</summary>
    /// <param name="decision">The decision carried out.</param>
    /// <param name="path">The file written.</param>
    public void Saved(SaveDecision decision, string path) => _session.Saved(decision, path);

    /// <summary>The file was not written: the dialog was cancelled, or the write failed.</summary>
    /// <param name="decision">The decision that did not complete.</param>
    public void SaveFailed(SaveDecision decision) => _session.SaveFailed(decision);

    /// <summary>Picks the tabs with work that is not in a file, in VectorCraft's order.</summary>
    /// <returns>The tabs to save before the window closes.</returns>
    public IReadOnlyList<OpenDocument> WithUnsavedWork() => _session.WithUnsavedWork(_tabs);

    /// <summary>The name of a tab for the user: its file's name, else its title, else <paramref name="untitled"/>.</summary>
    /// <param name="tab">A tab.</param>
    /// <param name="untitled">The name of a drawing with no name, in the user's language.</param>
    /// <returns>The name.</returns>
    public string NameOf(OpenDocument tab, string untitled)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (FileOf(tab) is { } path)
        {
            return CraftDocumentFormats.FileNameOf(path);
        }

        return tab.Title.Length > 0 ? tab.Title : tab.Name ?? untitled;
    }

    /// <summary>
    /// Builds the window title: <c>*name — Kazo Vector</c>, the name of the front tab's file (or of the
    /// tab when it has none), the asterisk while any tab has work that is not in a file.
    /// </summary>
    /// <param name="untitled">The name of a drawing with no name, in the user's language.</param>
    /// <param name="running">Whether VectorCraft is running.</param>
    /// <returns>The title.</returns>
    public string Title(string untitled, bool running = true)
    {
        if (!running)
        {
            return AppName;
        }

        string mark = IsDirty ? "*" : string.Empty;
        return Active is { } tab
            ? $"{mark}{NameOf(tab, untitled)} — {AppName}"
            : $"{mark}{AppName}";
    }

    /// <summary>The folder of the front tab's file, where the Open and Save dialogs start.</summary>
    /// <returns>A folder, or <see langword="null"/> when the front tab has no file.</returns>
    public string? ActiveFolder() =>
        Active is { } tab && FileOf(tab) is { } path ? CraftDocumentFormats.FolderOf(path) : null;
}
