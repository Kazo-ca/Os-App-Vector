namespace Kazo.Vector;

/// <summary>
/// The VectorCraft web build this app carries in <c>wwwroot/vectorcraft/</c>, described by its
/// provenance file <c>vectorcraft.json</c> (written by <c>tools/vendor-vectorcraft.sh</c>, checked by
/// the tests).
/// </summary>
public static class VectorCraftBuild
{
    /// <summary>
    /// The VectorCraft version of the build. Kept equal to the <c>version</c> of
    /// <c>wwwroot/vectorcraft/vectorcraft.json</c> by a test, so the window can show it without
    /// reading the file at run time.
    /// </summary>
    public const string Version = "0.25.0";

    /// <summary>Folder of the build under the app's static files (<c>_content/Kazo.Vector/</c>).</summary>
    public const string Folder = "vectorcraft";
}
