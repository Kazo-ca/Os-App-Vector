using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kazo.Vector.Tests;

/// <summary>The iframe page (decision 6) and the window's texts.</summary>
public sealed partial class HostPageTests
{
    private static readonly string App = Path.Combine(Repository.Root, "src", "Kazo.Vector");
    private static readonly string Page = File.ReadAllText(Path.Combine(App, "wwwroot", "host", "index.html"));

    [Fact]
    public void The_page_describes_VectorCraft_for_the_module_proxy()
    {
        // apps/vectorcraft-web/src/web.rs looks up these two ids (doc/en/plan.md, "Canvas").
        Assert.Matches(
            """<canvas id="vectorcraft_canvas" data-host="page"\s+data-craft-app="vectorcraft" data-craft-build="../vectorcraft/vectorcraft.json">""",
            Page);
        Assert.Contains("""<script type="module" src="../../Kazo.Os.AppSdk.Craft/craft-proxy.js"></script>""", Page);
        Assert.Contains("""<link rel="stylesheet" href="../../Kazo.Os.AppSdk.Craft/craft-page.css">""", Page);
        Assert.Contains("""id="vectorcraft_loading" data-craft-loading""", Page);
    }

    [Fact]
    public void The_page_base_is_the_build_folder_so_VectorCraft_finds_its_served_fonts()
    {
        // VectorCraft fetches fonts/<hash>/… relative to the page (decision 14).
        Assert.Contains("""<base href="../vectorcraft/">""", Page);
        Assert.True(Directory.Exists(Path.Combine(App, "wwwroot", "vectorcraft", "fonts")));
    }

    [Fact]
    public void The_build_the_page_names_is_in_the_package()
    {
        Assert.True(File.Exists(Path.Combine(App, "wwwroot", "vectorcraft", "vectorcraft.json")));
    }

    [Fact]
    public void The_page_has_no_inline_script_or_style()
    {
        Assert.DoesNotMatch("<script(?![^>]*\\ssrc=)[^>]*>", Page);
        Assert.DoesNotContain("<style", Page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", Page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void English_and_French_texts_have_the_same_keys()
    {
        Assert.Equal(KeysOf("Strings.resx"), KeysOf("Strings.fr.resx"));
    }

    [Fact]
    public void Every_text_the_window_reads_exists()
    {
        HashSet<string> keys = KeysOf("Strings.resx");
        IEnumerable<string> sources = Directory.EnumerateFiles(App, "VectorWindow*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal))
            .Select(File.ReadAllText);
        string[] used = [.. sources.SelectMany(text => TextKey().Matches(text).Select(match => match.Groups[1].Value)).Distinct()];

        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.Contains(key, keys));
    }

    private static HashSet<string> KeysOf(string file) =>
        [.. XDocument.Load(Path.Combine(App, "Resources", file)).Root!.Elements("data").Select(data => (string)data.Attribute("name")!)];

    // L["Key"] and L["Key", ...] in the window, and the keys picked in a ternary (L[x ? "A" : "B"]).
    [GeneratedRegex("""(?:L\[|\? |: )"([A-Za-z]+)"(?=[\],])""")]
    private static partial Regex TextKey();
}
