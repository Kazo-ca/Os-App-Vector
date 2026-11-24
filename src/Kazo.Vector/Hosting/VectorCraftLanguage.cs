using System.Globalization;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Vector.Hosting;

/// <summary>
/// The language given to VectorCraft by its <c>interfaceLanguage</c> preference
/// (<c>prefs.set { key: "interfaceLanguage", value }</c>, decision 11).
/// </summary>
/// <remarks>
/// VectorCraft's languages are compiled in (<c>crates/ui-egui/src/i18n/</c>): English (<c>en</c>) and
/// the 28 codes of <see cref="Available"/>. Its own default, <c>auto</c>, follows the browser; the
/// window gives the Kazo user's language instead, since the browser's may differ. Gujarati
/// (<c>gu</c>, new in 0.25.0) needs a Gujarati interface face that the web build does not embed:
/// VectorCraft then ignores the preference and follows the browser's language
/// (<c>Lang::from_pref</c>), as it did for every unknown code before 0.25.0.
/// </remarks>
public static class VectorCraftLanguage
{
    /// <summary>The name of VectorCraft's language preference.</summary>
    public const string PreferenceKey = "interfaceLanguage";

    /// <summary>The languages VectorCraft 0.25.0 has, besides English.</summary>
    public static IReadOnlyList<string> Available { get; } =
    [
        "bg", "bs", "ca", "cs", "da", "de", "el", "es", "fi", "fr", "gu", "hi", "hu", "id", "it",
        "ja", "ka", "ko", "nl", "pl", "pt-br", "ru", "sv", "tr", "uk", "vi", "zh-hans", "zh-hant",
    ];

    /// <summary>The code for a culture: one of <see cref="Available"/>, or <see cref="CraftLanguage.English"/>.</summary>
    /// <param name="culture">The Kazo user's culture.</param>
    /// <returns>A code VectorCraft has, or <c>en</c>, which is also VectorCraft's code for English.</returns>
    public static string Of(CultureInfo culture) => CraftLanguage.Pick(culture, (IReadOnlyCollection<string>)Available);
}
