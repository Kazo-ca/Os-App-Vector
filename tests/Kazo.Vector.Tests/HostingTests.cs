using System.Globalization;
using Kazo.Vector.Hosting;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Vector.Tests;

/// <summary>What the window gives VectorCraft: the page's query, the language, the theme and the chords.</summary>
public sealed class HostingTests
{
    [Fact]
    public void The_page_needs_no_control_parameter()
    {
        // VectorCraft always installs window.vectorcraft (decision 6).
        Assert.Empty(VectorCraftParameters.For(webGl: false));
    }

    [Fact]
    public void The_webgl_argument_forces_WebGL_2()
    {
        IReadOnlyDictionary<string, string> parameters = VectorCraftParameters.For(webGl: true);

        Assert.Equal(new Dictionary<string, string> { ["webgl"] = string.Empty }, parameters);
    }

    [Theory]
    [InlineData("fr-CA", "fr")]
    [InlineData("de-DE", "de")]
    [InlineData("pt-PT", "pt-br")]
    [InlineData("zh-CN", "zh-hans")]
    [InlineData("zh-TW", "zh-hant")]
    [InlineData("ka-GE", "ka")]
    [InlineData("ko-KR", "ko")]
    [InlineData("bs-BA", "bs")]
    [InlineData("en-US", "en")]
    [InlineData("ar-SA", "en")]
    public void VectorCraft_gets_one_of_its_28_languages_or_English(string culture, string expected)
    {
        Assert.Equal(expected, VectorCraftLanguage.Of(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void VectorCraft_has_28_languages_besides_English()
    {
        Assert.Equal(28, VectorCraftLanguage.Available.Count);
        Assert.Equal(VectorCraftLanguage.Available.Count, VectorCraftLanguage.Available.Distinct().Count());
        Assert.DoesNotContain(CraftLanguage.English, VectorCraftLanguage.Available);
    }

    [Fact]
    public void Every_language_has_its_catalog_in_VectorCraft()
    {
        // The catalogs of the submodule, when it is checked out (CI fetches it only to build).
        string catalogs = Path.Combine(Repository.Root, "external", "vectorcraft", "crates", "ui-egui", "src", "i18n");
        if (!Directory.Exists(catalogs))
        {
            return;
        }

        string[] codes = [.. Directory.EnumerateFiles(catalogs, "*.tsv").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];
        Assert.Equal(codes, VectorCraftLanguage.Available.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData("LIGHT", "light")]
    [InlineData("dark", "mediumDark")]
    [InlineData(null, "mediumDark")]
    [InlineData("high-contrast", "mediumDark")]
    public void VectorCraft_follows_the_OS_scheme_not_the_browser(string? scheme, string expected)
    {
        Assert.Equal(expected, VectorCraftTheme.Of(scheme));
    }

    [Fact]
    public void Ctrl_N_stays_VectorCraft_s_New_document()
    {
        // Decision 13: the window reserves the OS chords only.
        string window = File.ReadAllText(Path.Combine(Repository.Root, "src", "Kazo.Vector", "VectorWindow.razor.cs"));
        Assert.Contains("ReservedKeys = ReservedKeys.Os,", window);
        Assert.DoesNotContain(ReservedKeys.NewWindow, ReservedKeys.Os);
    }
}
