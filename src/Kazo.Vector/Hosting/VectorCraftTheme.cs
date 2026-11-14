namespace Kazo.Vector.Hosting;

/// <summary>
/// The VectorCraft interface brightness (<c>ui.set { brightness }</c>) for a Kazo OS colour scheme
/// (decision 11).
/// </summary>
public static class VectorCraftTheme
{
    /// <summary>VectorCraft's default brightness, a medium dark grey.</summary>
    public const string MediumDark = "mediumDark";

    /// <summary>VectorCraft's lightest brightness.</summary>
    public const string Light = "light";

    /// <summary>
    /// <see cref="Light"/> for the OS's light scheme, <see cref="MediumDark"/> (VectorCraft's own
    /// default) for anything else (the OS default is dark).
    /// </summary>
    /// <param name="scheme">The scheme <c>CraftFrame</c> reports (<c>light</c> or <c>dark</c>).</param>
    /// <returns>The value of VectorCraft's <c>brightness</c> setting.</returns>
    public static string Of(string? scheme) =>
        string.Equals(scheme, "light", StringComparison.OrdinalIgnoreCase) ? Light : MediumDark;
}
