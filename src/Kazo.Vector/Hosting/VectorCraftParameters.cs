namespace Kazo.Vector.Hosting;

/// <summary>The query of VectorCraft's iframe page (<c>CraftFrameOptions.Parameters</c>).</summary>
/// <remarks>
/// No <c>control</c> parameter (decision 6): VectorCraft always installs <c>window.vectorcraft</c>,
/// the command API the module's proxy drives it with.
/// </remarks>
public static class VectorCraftParameters
{
    /// <summary>
    /// The parameters for one window: <c>webgl</c> forces the WebGL 2 renderer, which VectorCraft
    /// otherwise only uses when the browser has no WebGPU. Bare flags are empty values: VectorCraft
    /// only looks for the name.
    /// </summary>
    /// <param name="webGl">Whether to force WebGL 2.</param>
    /// <returns>The parameters, empty when there is nothing to force.</returns>
    public static IReadOnlyDictionary<string, string> For(bool webGl)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal);
        if (webGl)
        {
            parameters["webgl"] = string.Empty;
        }

        return parameters;
    }
}
