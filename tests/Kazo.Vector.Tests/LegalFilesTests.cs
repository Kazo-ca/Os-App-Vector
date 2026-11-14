using System.Text.Json;
using System.Xml.Linq;

namespace Kazo.Vector.Tests;

/// <summary>
/// The licenses travel with the app (work item 4, THIRD-PARTY-NOTICES.md): the project links the
/// repository's license and notices into the package's web root, VectorCraft's own notices are in
/// its build folder, the files its NOTICE refers to are copied next to them, and the notices name
/// the VectorCraft build actually bundled.
/// </summary>
public sealed class LegalFilesTests
{
    private static readonly string Root = Repository.Root;

    private static readonly string VectorCraftFolder = Path.Combine(Root, "src", "Kazo.Vector", "wwwroot", "vectorcraft");

    [Theory]
    [InlineData("LICENSE", "wwwroot/legal/LICENSE.txt")]
    [InlineData("THIRD-PARTY-NOTICES.md", "wwwroot/legal/THIRD-PARTY-NOTICES.md")]
    [InlineData("THIRD-PARTY-NOTICES-fr.md", "wwwroot/legal/THIRD-PARTY-NOTICES-fr.md")]
    public void The_project_puts_the_repository_legal_files_in_the_package(string file, string link)
    {
        XDocument project = XDocument.Load(Path.Combine(Root, "src", "Kazo.Vector", "Kazo.Vector.csproj"));

        // A Content item linked under wwwroot/ is a static web asset: KazoOsPack packs it.
        XElement? item = project.Descendants("Content")
            .FirstOrDefault(element => (string?)element.Attribute("Link") == link);
        Assert.NotNull(item);
        Assert.Equal($"../../{file}", (string?)item.Attribute("Include"));
        Assert.True(File.Exists(Path.Combine(Root, file)), $"{file} is missing at the repository root");
    }

    [Theory]
    [InlineData("LICENSE-MIT")]
    [InlineData("LICENSE-APACHE")]
    [InlineData("NOTICE")]
    [InlineData("ASSETS.md")]
    [InlineData("ATTRIBUTION.md")]
    [InlineData("licenses/pdf-backend/hayro-cmap/LICENSE.txt")]
    public void VectorCraft_ships_its_own_notices(string file)
    {
        Assert.True(File.Exists(Path.Combine(VectorCraftFolder, file)), $"vectorcraft/{file} is missing");
    }

    [Fact]
    public void Every_font_license_of_the_build_is_named_in_the_notices()
    {
        // The build carries one OFL-<family>.txt per embedded family; the notices cover them as a set.
        string[] licenses = Directory.GetFiles(VectorCraftFolder, "OFL-*.txt");
        Assert.NotEmpty(licenses);
        Assert.Contains("`OFL-*.txt`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")), StringComparison.Ordinal);
        Assert.Contains("`OFL-*.txt`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES-fr.md")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LICENSE-lucide.txt")]
    [InlineData("LICENSE-ubuntu-font.txt")]
    [InlineData("LICENSE-hack.txt")]
    [InlineData("OFL-noto-emoji.txt")]
    [InlineData("LICENSE-emoji-icon-font.txt")]
    [InlineData("LICENSE-indic-hyphenation-MIT.txt")]
    public void The_files_VectorCraft_refers_to_ship_in_the_package(string file)
    {
        // VectorCraft's NOTICE names these files and licenses but its web build does not carry them:
        // they are copied unmodified, and listed in THIRD-PARTY-NOTICES.md.
        string path = Path.Combine(Root, "src", "Kazo.Vector", "wwwroot", "legal", "vectorcraft", file);
        Assert.True(File.Exists(path), $"legal/vectorcraft/{file} is missing");
        Assert.True(new FileInfo(path).Length > 0, $"legal/vectorcraft/{file} is empty");
        Assert.Contains($"`{file}`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")), StringComparison.Ordinal);
        Assert.Contains($"`{file}`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES-fr.md")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    [InlineData("THIRD-PARTY-NOTICES-fr.md")]
    public void The_notices_name_the_bundled_VectorCraft_commit_version_and_fonts(string file)
    {
        // The files above were copied at this commit: moving to another build without copying them
        // again and updating the notices fails here.
        using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(VectorCraftFolder, "vectorcraft.json")));
        JsonElement source = provenance.RootElement.GetProperty("source");
        string version = provenance.RootElement.GetProperty("version").GetString()!;

        string notices = File.ReadAllText(Path.Combine(Root, file));
        Assert.Contains(source.GetProperty("commit").GetString()!, notices, StringComparison.Ordinal);
        Assert.Contains(source.GetProperty("craftFonts").GetString()!, notices, StringComparison.Ordinal);
        Assert.Contains($"| {version}, ", notices, StringComparison.Ordinal);
        Assert.Contains($"`v{version}`", notices, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("description.en.md", "VectorCraft")]
    [InlineData("description.fr.md", "VectorCraft")]
    [InlineData("description.en.md", "ArtCraft")]
    [InlineData("description.fr.md", "ArtCraft")]
    [InlineData("description.en.md", "Open Font License")]
    [InlineData("description.en.md", "crash")]
    [InlineData("description.fr.md", "arrêt brutal")]
    [InlineData("description.fr.md", "Open Font License")]
    public void The_store_listing_credits_VectorCraft_and_the_fonts(string file, string name)
    {
        Assert.Contains(name, File.ReadAllText(Path.Combine(Root, "store", file)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_listing_names_the_minimum_Kazo_OS_version_of_the_manifest()
    {
        // The listing tells users which Kazo OS they need: it must follow the manifest.
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Kazo.Vector", "manifest.json")));
        string minimum = manifest.RootElement.GetProperty("sdkCompatibility").GetProperty("minimumVersion").GetString()!;
        Assert.Contains($"Kazo OS {minimum}", File.ReadAllText(Path.Combine(Root, "store", "description.en.md")), StringComparison.Ordinal);
        Assert.Contains($"Kazo OS {minimum}", File.ReadAllText(Path.Combine(Root, "store", "description.fr.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_listing_names_every_extension_Kazo_Vector_opens()
    {
        // The listing says which files open in Kazo Vector: it must follow the manifest's handlers.
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Kazo.Vector", "manifest.json")));
        string en = File.ReadAllText(Path.Combine(Root, "store", "description.en.md"));
        string fr = File.ReadAllText(Path.Combine(Root, "store", "description.fr.md"));
        foreach (JsonElement handler in manifest.RootElement.GetProperty("fileHandlers").EnumerateArray())
        {
            foreach (JsonElement extension in handler.GetProperty("extensions").EnumerateArray())
            {
                Assert.Contains($"`{extension.GetString()}`", en, StringComparison.Ordinal);
                Assert.Contains($"`{extension.GetString()}`", fr, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_store_tags_are_ones_the_catalog_accepts()
    {
        // Os-AppMarketPlace refuses any other tag (lower case, digits and '-', 30 characters at most):
        // Kazo Word 0.1.0 had "word processor" and its catalog folder had to be corrected by hand.
        using JsonDocument store = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "store", "store.json")));
        foreach (JsonElement tag in store.RootElement.GetProperty("tags").EnumerateArray())
        {
            Assert.Matches("^[a-z0-9][a-z0-9-]{0,29}$", tag.GetString());
        }

        Assert.Equal("https://github.com/Kazo-ca/Os-App-Vector", store.RootElement.GetProperty("repository").GetString());
        Assert.Equal("src/Kazo.Vector/Kazo.Vector.csproj", store.RootElement.GetProperty("project").GetString());
    }
}
