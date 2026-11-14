using System.Globalization;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Vector.Hosting;

/// <summary>
/// The language given to VectorCraft by its <c>interfaceLanguage</c> preference
/// (<c>prefs.set { key: "interfaceLanguage", value }</c>, decision 11).
/// </summary>
/// <remarks>
/// VectorCraft's languages are compiled in (<c>crates/ui-egui/src/i18n/</c>): English (<c>en</c>) and
/// the 27 codes of <see cref="Available"/>. Its own default, <c>auto</c>, follows the browser; the
/// window gives the Kazo user's language instead, since the browser's may differ.
/// </remarks>
public static class VectorCraftLanguage
{
    /// <summary>The name of VectorCraft's language preference.</summary>
    public const string PreferenceKey = "interfaceLanguage";

    /// <summary>The languages VectorCraft 0.24.1 has, besides English.</summary>
    public static IReadOnlyList<string> Available { get; } =
    [
        "bg", "bs", "ca", "cs", "da", "de", "el", "es", "fi", "fr", "hi", "hu", "id", "it",
        "ja", "ka", "ko", "nl", "pl", "pt-br", "ru", "sv", "tr", "uk", "vi", "zh-hans", "zh-hant",
    ];

    /// <summary>The code for a culture: one of <see cref="Available"/>, or <see cref="CraftLanguage.English"/>.</summary>
    /// <param name="culture">The Kazo user's culture.</param>
    /// <returns>A code VectorCraft has, or <c>en</c>, which is also VectorCraft's code for English.</returns>
    public static string Of(CultureInfo culture) => CraftLanguage.Pick(culture, (IReadOnlyCollection<string>)Available);
}
