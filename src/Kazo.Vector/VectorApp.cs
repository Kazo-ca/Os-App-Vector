using Kazo.Vector.Documents;
using Kazo.Os.App.Abstractions;

namespace Kazo.Vector;

/// <summary>
/// The app's manifest (doc/en/plan.md, decision 1). The build generates <c>manifest.json</c> from
/// this class (Kazo.Os.App.Build); it is never instantiated at run time.
/// </summary>
/// <remarks>
/// Work item 2 declares the identity, the window and its size, several windows at once, a window that
/// stays on its screen (decision 3), the capabilities of Kazo Photo and the file associations of
/// question Q2 = A (<see cref="VectorFormats"/>). Opening and saving those files come with work
/// item 3, once VectorCraft has its host mode (fork, F1).
/// </remarks>
[AppManifest]
public sealed class VectorApp : AppDefinition
{
    /// <summary>
    /// Smallest usable width of VectorCraft, in pixels: its desktop minimum (tools, canvas, the
    /// panels; doc/en/plan.md, "Window size").
    /// </summary>
    public const int ContentMinWidth = 800;

    /// <summary>Smallest usable height of VectorCraft, in pixels: its desktop minimum.</summary>
    public const int ContentMinHeight = 500;

    /// <inheritdoc />
    public override string Id => "ca.kazo.vector";

    /// <inheritdoc />
    public override string Name => "Kazo Vector";

    /// <inheritdoc />
    public override AppKind Kind => AppKind.Window;

    /// <inheritdoc />
    public override Type EntryPoint => typeof(VectorWindow);

    /// <inheritdoc />
    public override string PublisherId => "pub.kazo";

    /// <inheritdoc />
    public override string Description => "A vector drawing app for logos, illustrations, icons and diagrams, based on VectorCraft by the ArtCraft team";

    /// <inheritdoc />
    /// <remarks>The category of Kazo Photo and Kazo Fig (decision 1).</remarks>
    public override string Category => "graphics";

    /// <inheritdoc />
    /// <remarks>The tag that publishes to the Kazo OS Store must be v + this version.</remarks>
    public override string Version => "0.1.3";

    /// <inheritdoc />
    /// <remarks>
    /// Several windows at once: each one runs its own VectorCraft in its own iframe, because the
    /// VectorCraft canvas id is fixed and allows one instance per HTML document (doc/en/plan.md,
    /// "Canvas").
    /// </remarks>
    public override bool SingleInstancePerUser => false;

    /// <inheritdoc />
    /// <remarks>
    /// Kazo Photo's list: the OS Open and Save dialogs, the user's files, the app's private folder
    /// (per-user settings, should VectorCraft ever store any; decision 12), and launching a new window
    /// of the app (a double click in the File Explorer opens a new window, decisions 7 and 9).
    /// </remarks>
    public override IReadOnlyList<string> Capabilities =>
    [
        AppCapabilities.DialogFileOpen,
        AppCapabilities.DialogFileSave,
        AppCapabilities.FileSystemUserHomeRead,
        AppCapabilities.FileSystemUserHomeWrite,
        AppCapabilities.FileSystemPrivateRead,
        AppCapabilities.FileSystemPrivateWrite,
        AppCapabilities.AppsLaunch,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// Default app (<c>open</c> and <c>edit</c>) of the vector formats nothing else opens, "Open with"
    /// (<c>open</c>) for <c>.pdf</c> and <c>.afdesign</c> (question Q2 = A).
    /// </remarks>
    public override IReadOnlyList<FileHandler> FileHandlers => VectorFormats.FileHandlers;

    /// <inheritdoc />
    public override IReadOnlyList<string> Icons => ["icons/app.svg"];

    /// <inheritdoc />
    public override IReadOnlyList<AssetManifest> Assets =>
        [new("icons/app.svg", AssetKind.Image, new string('0', 64), "app-icon")];

    /// <inheritdoc />
    /// <remarks>
    /// Sizes are the outer size of the OS window (title bar included): 1280 × 820 by default
    /// (decision 1); at least VectorCraft's desktop minimum (800 × 500) plus the frame. <see cref="WindowOptions.CanChangeScreen"/> is
    /// <see langword="false"/>: moving the window to another screen moves its element into another
    /// document, which reloads VectorCraft's iframe and loses the open documents (Kazo OS ADR 0263,
    /// decision 3, issue #2).
    /// </remarks>
    public override WindowOptions? Window => new()
    {
        DefaultWidth = 1280,
        DefaultHeight = 820,
        MinWidth = ContentMinWidth + WindowChromeWidth,
        MinHeight = ContentMinHeight + WindowChromeHeight,
        CanChangeScreen = false,
    };

    // Borders and title bar of an OS window (the tallest title bar of the Kazo OS themes, 2.35rem).
    private const int WindowChromeWidth = 2;
    private const int WindowChromeHeight = 40;
}
