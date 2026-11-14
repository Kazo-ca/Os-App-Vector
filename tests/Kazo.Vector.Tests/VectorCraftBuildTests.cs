using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kazo.Vector.Tests;

/// <summary>
/// The VectorCraft build in <c>src/Kazo.Vector/wwwroot/vectorcraft/</c> matches its provenance file
/// (decision 5 of doc/en/plan.md): nothing was edited by hand, nothing is missing, nothing was added.
/// </summary>
public sealed partial class VectorCraftBuildTests
{
    private static readonly string BuildFolder = Path.Combine(FindRepositoryRoot(), "src", "Kazo.Vector", "wwwroot", "vectorcraft");

    private static readonly JsonElement Provenance = ReadProvenance();

    [Fact]
    public void The_provenance_names_the_fork_and_the_commit_of_the_submodule()
    {
        // Decision 5: Kazo Vector is always compiled from our fork. The build must come from the
        // commit the submodule points at, so moving the submodule without rebuilding (or the
        // reverse) fails here.
        JsonElement source = Provenance.GetProperty("source");
        Assert.Equal("source-build", source.GetProperty("kind").GetString());
        Assert.Equal(SubmoduleCommit(), source.GetProperty("commit").GetString());
        Assert.Equal("https://github.com/Kazo-ca/vectorcraft.git", source.GetProperty("repository").GetString());
    }

    [Fact]
    public void The_build_embeds_the_craft_fonts_VectorCraft_releases_pin()
    {
        // tools/build-vectorcraft.sh builds with CRAFT_FONTS_REQUIRED at the craft-fonts-ref of the
        // fork's release workflow, like VectorCraft's own releases: the Japanese interface face in the
        // .wasm, and the faces of crates/text/web-fonts.txt served beside it (decision 14).
        string? fonts = Provenance.GetProperty("source").GetProperty("craftFonts").GetString();
        Assert.Matches("^[0-9a-f]{40}$", fonts);
    }

    [Fact]
    public void The_build_is_optimised_for_size_and_far_from_GitHub_s_limit()
    {
        // Question Q1 = A (doc/en/plan.md, "Build size"): opt-level "s" with VectorCraft's thin LTO
        // keeps the .wasm around 70 MB, under the 90 MiB tools/build-vectorcraft.sh allows; GitHub
        // refuses files above 100 MiB.
        Assert.Equal("opt-level=s, lto=thin, codegen-units=1, strip=debuginfo", Provenance.GetProperty("source").GetProperty("profile").GetString());
        FileInfo wasm = new(Path.Combine(BuildFolder, Provenance.GetProperty("wasm").GetString()!));
        Assert.InRange(wasm.Length, 1, 90L * 1024 * 1024);
    }

    [Fact]
    public void The_version_shown_by_the_window_is_the_version_of_the_build()
    {
        Assert.Equal(VectorCraftBuild.Version, Provenance.GetProperty("version").GetString());
    }

    [Fact]
    public void The_wasm_stays_under_the_Kazo_OS_file_size_limit()
    {
        // ADR 0245 of Kazo OS: 128 MiB per file. VectorCraft 0.24.1 is about 68 MiB built for size.
        const long maximumFileBytes = 128L * 1024 * 1024;
        FileInfo wasm = new(Path.Combine(BuildFolder, Provenance.GetProperty("wasm").GetString()!));
        Assert.InRange(wasm.Length, 1, maximumFileBytes);
    }

    [Fact]
    public void Every_listed_file_has_its_size_and_SHA_256()
    {
        foreach (JsonElement file in Provenance.GetProperty("files").EnumerateArray())
        {
            string path = file.GetProperty("path").GetString()!;
            string full = Path.Combine(BuildFolder, path);
            Assert.True(File.Exists(full), $"{path} is listed but missing");
            Assert.Equal(file.GetProperty("size").GetInt64(), new FileInfo(full).Length);
            using FileStream stream = File.OpenRead(full);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(stream)));
        }
    }

    [Fact]
    public void No_file_is_there_without_being_listed()
    {
        HashSet<string> listed = Provenance.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()!)
            .Append("vectorcraft.json")
            .ToHashSet(StringComparer.Ordinal);

        IEnumerable<string> present = Directory.EnumerateFiles(BuildFolder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(BuildFolder, file).Replace('\\', '/'));

        Assert.All(present, path => Assert.True(listed.Contains(path), $"{path} is not in vectorcraft.json"));
    }

    [Fact]
    public void The_glue_and_the_wasm_are_listed_with_the_licences()
    {
        HashSet<string> listed = Provenance.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(Provenance.GetProperty("glue").GetString()!, listed);
        Assert.Contains(Provenance.GetProperty("wasm").GetString()!, listed);
        Assert.Contains("LICENSE-MIT", listed);
        Assert.Contains("LICENSE-APACHE", listed);
        Assert.Contains("NOTICE", listed);
        Assert.Contains("ASSETS.md", listed);
        Assert.Contains("ATTRIBUTION.md", listed);
        // The fonts of the build come with their OFL licence: VectorCraft's four (assets/fonts),
        // the craft-fonts face the .wasm embeds and the craft-fonts faces served beside it.
        Assert.Contains("OFL-Inter.txt", listed);
        Assert.Contains("OFL-JetBrainsMono.txt", listed);
        Assert.Contains("OFL-SourceSans3.txt", listed);
        Assert.Contains("OFL-SourceSerif4.txt", listed);
        Assert.Contains("OFL-craft-fonts-biz-ud-pgothic.txt", listed);
        Assert.Contains(listed, path => path.StartsWith("fonts/", StringComparison.Ordinal));
        Assert.Contains("OFL-craft-fonts-noto-sans-arabic.txt", listed);
        // The licences of the PDF backend VectorCraft ships (licenses/pdf-backend).
        Assert.Contains("licenses/pdf-backend/hayro-syntax/LICENSE-MIT", listed);
    }

    [Fact]
    public void Every_module_the_glue_imports_is_there()
    {
        string glue = File.ReadAllText(Path.Combine(BuildFolder, Provenance.GetProperty("glue").GetString()!));

        string[] imports = [.. RelativeImport().Matches(glue).Select(match => match.Groups["path"].Value)];

        Assert.All(imports, path => Assert.True(File.Exists(Path.Combine(BuildFolder, path)), $"{path} is imported but missing"));
    }

    // `import ... from './snippets/...'` in the wasm-bindgen glue.
    [GeneratedRegex(@"from\s+['""]\./(?<path>[^'""]+)['""]")]
    private static partial Regex RelativeImport();

    // The commit recorded for external/vectorcraft in git's index (the gitlink), which is there even
    // when the submodule itself is not checked out (CI fetches only the Kazo OS one).
    private static string SubmoduleCommit()
    {
        System.Diagnostics.ProcessStartInfo start = new("git", ["ls-files", "--stage", "--", "external/vectorcraft"])
        {
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using System.Diagnostics.Process git = System.Diagnostics.Process.Start(start)!;
        string output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();

        // "160000 <commit> 0\texternal/vectorcraft"
        Match match = Gitlink().Match(output);
        Assert.True(git.ExitCode == 0 && match.Success, $"external/vectorcraft is not a submodule in git's index: {output}");
        return match.Groups["commit"].Value;
    }

    [GeneratedRegex(@"^160000 (?<commit>[0-9a-f]{40}) ")]
    private static partial Regex Gitlink();

    private static JsonElement ReadProvenance()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildFolder, "vectorcraft.json")));
        return document.RootElement.Clone();
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kazo.Vector.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Kazo.Vector.slnx not found above " + AppContext.BaseDirectory);
    }
}
