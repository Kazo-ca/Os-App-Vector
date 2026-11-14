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
/// Work item 2 only declares the associations. Which saves are the document and which are copies
/// (<see cref="CraftDocumentFormat.IsCopy"/>) is decided by VectorCraft itself in host mode (fork,
/// F1, H3: only the document's own save is a <c>save</c> message, copies and exports stay downloads),
/// so no format is marked as a copy here.
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

    private static CraftDocumentFormat Default(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: true, IsCopy: false);

    private static CraftDocumentFormat OpenWith(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: false, IsCopy: false);
}
