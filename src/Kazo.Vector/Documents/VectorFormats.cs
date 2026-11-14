using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Vector.Documents;

/// <summary>
/// The files Kazo Vector opens by double click or "Open with" (doc/en/plan.md, question Q2 = A).
/// </summary>
/// <remarks>
/// <para>
/// Kazo Vector is the default app of the vector formats nothing else in Kazo OS opens: VectorCraft's
/// own documents and templates, SVG, Illustrator, PostScript, DXF and the Windows metafiles. It is
/// only offered in "Open with" for <c>.pdf</c> (Kazo Pdf stays the default) and <c>.afdesign</c>
/// (Kazo Photo is already the default of the Affinity files). Images get no association: Kazo Vector
/// places them in a drawing with File › Import, it does not open them by double click.
/// </para>
/// <para>
/// Which saves are the document and which are copies (<see cref="CraftDocumentFormat.IsCopy"/>) is
/// decided by VectorCraft itself in host mode (fork, F1, H3: only the document's own save is a
/// <c>save</c> message, copies and exports stay downloads), so no format is marked as a copy here.
/// </para>
/// <para>
/// VectorCraft also opens files that are not drawings (swatch libraries, presets, shortcut sets,
/// scripts, plug-ins: <see cref="OtherFileExtensions"/>). They get no association (Q2 = A), but a
/// file picked with File › Open may be one: the window hands it to VectorCraft without binding it to
/// a tab (<see cref="IsDocument"/>, work item 3).
/// </para>
/// </remarks>
public static class VectorFormats
{
    /// <summary>VectorCraft's own document format.</summary>
    public const string NativeExtension = ".vectorcraft";

    /// <summary>Gets every format, with what Kazo Vector does with it.</summary>
    public static CraftDocumentFormats Formats { get; } = new(
    [
        Default(NativeExtension, "application/x-vectorcraft"),
        Default(".vctemplate", "application/x-vectorcraft-template"),
        Default(".svg", "image/svg+xml"),
        // Gzipped SVG: same media type, the file is compressed.
        Default(".svgz", "image/svg+xml"),
        Default(".ai", "application/illustrator"),
        Default(".ait", "application/x-illustrator-template"),
        Default(".eps", "application/postscript"),
        Default(".epsf", "application/postscript"),
        Default(".ps", "application/postscript"),
        Default(".dxf", "image/vnd.dxf"),
        Default(".emf", "image/emf"),
        Default(".wmf", "image/wmf"),
        // "Open with" only: another app is their default.
        OpenWith(".pdf", "application/pdf"),
        OpenWith(".afdesign", "application/x-affinity-designer"),
    ]);

    /// <summary>Gets the extensions the OS Open dialog proposes, in the order above.</summary>
    public static IReadOnlyList<string> OpenExtensions => Formats.OpenExtensions;

    /// <summary>Gets the file associations of the manifest.</summary>
    public static IReadOnlyList<FileHandler> FileHandlers => Formats.FileHandlers;

    /// <summary>
    /// The files VectorCraft 0.24.1 opens without creating a drawing (<c>io::opens_as</c> in
    /// <c>crates/ui-egui/src/io/open.rs</c>): swatch libraries, Libraries panel libraries, graphic
    /// styles, flattener, PDF, print, perspective grid and export presets, composite fonts, shortcut
    /// sets, scripts and plug-ins.
    /// </summary>
    public static IReadOnlyList<string> OtherFileExtensions { get; } =
    [
        ".vcswatches", ".gpl", ".ase", ".acb", ".vclibrary", ".vcstyles", ".vcflattener", ".vcpdfpresets",
        ".vcprintpresets", ".vcperspective", ".vcexportpresets", ".vccomposite", ".kys", ".jsx", ".jsxinc",
        ".jsxbin", ".wasm",
    ];

    /// <summary>
    /// Whether a file opens as a drawing in a tab, which the window binds to the file; the
    /// <see cref="OtherFileExtensions"/> do not (VectorCraft's host mode opens them without a path,
    /// fork H2).
    /// </summary>
    /// <param name="fileName">The file's name or path.</param>
    /// <returns><see langword="false"/> for a library, preset, shortcut set, script or plug-in.</returns>
    public static bool IsDocument(string fileName) =>
        !OtherFileExtensions.Contains(CraftDocumentFormats.ExtensionOf(fileName), StringComparer.Ordinal);

    /// <summary>
    /// Whether VectorCraft's file picker is File › Open (or the home screen's Open…). The window
    /// answers it with the OS Open dialog and opens the chosen files itself, bound to them
    /// (decision 9), instead of handing their bytes to VectorCraft, which would open them with no
    /// file to save back to.
    /// </summary>
    /// <param name="request">The picker VectorCraft opened.</param>
    /// <returns><see langword="true"/> for File › Open.</returns>
    /// <remarks>
    /// VectorCraft's web build has three pickers (<c>apps/vectorcraft-web/src/web.rs</c>): Open, one
    /// file of every readable format, libraries and presets included; Place, several files of the
    /// placeable formats; Scripts › Other Script, one script. Only Open takes both drawings and
    /// swatch libraries.
    /// </remarks>
    public static bool IsOpenPicker(PickRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.AcceptsAny([NativeExtension]) && request.AcceptsAny([".vcswatches"]);
    }

    private static CraftDocumentFormat Default(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: true, IsCopy: false);

    private static CraftDocumentFormat OpenWith(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: false, IsCopy: false);
}
