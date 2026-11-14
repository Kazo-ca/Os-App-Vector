using Kazo.Vector.Documents;
using Kazo.Os.App.Abstractions;

namespace Kazo.Vector.Tests;

/// <summary>The file associations of question Q2 = A (doc/en/plan.md).</summary>
public sealed class VectorFormatsTests
{
    [Theory]
    [InlineData(".vectorcraft")]
    [InlineData(".vctemplate")]
    [InlineData(".svg")]
    [InlineData(".svgz")]
    [InlineData(".ai")]
    [InlineData(".ait")]
    [InlineData(".eps")]
    [InlineData(".epsf")]
    [InlineData(".ps")]
    [InlineData(".dxf")]
    [InlineData(".emf")]
    [InlineData(".wmf")]
    public void Kazo_Vector_is_the_default_app_of_the_vector_formats(string extension)
    {
        FileHandler handler = Assert.Single(VectorFormats.FileHandlers, handler => handler.Extensions.Contains(extension));
        Assert.Equal(["open", "edit"], handler.Actions);
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".afdesign")]
    public void Kazo_Vector_is_only_in_Open_with_for_PDF_and_Affinity_Designer(string extension)
    {
        // Kazo Pdf stays the default of .pdf, Kazo Photo of the Affinity files.
        FileHandler handler = Assert.Single(VectorFormats.FileHandlers, handler => handler.Extensions.Contains(extension));
        Assert.Equal(["open"], handler.Actions);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".psd")]
    [InlineData(".tif")]
    [InlineData(".webp")]
    [InlineData(".af")]
    [InlineData(".afpub")]
    [InlineData(".docx")]
    [InlineData(".txt")]
    public void Images_and_other_files_get_no_association(string extension)
    {
        Assert.DoesNotContain(VectorFormats.FileHandlers, handler => handler.Extensions.Contains(extension));
    }

    [Fact]
    public void Every_format_opens_none_is_a_copy_and_the_native_one_comes_first()
    {
        // Copies are told apart by VectorCraft's host mode (fork, H3), not by the extension.
        Assert.All(VectorFormats.Formats.All, format =>
        {
            Assert.True(format.Opens);
            Assert.False(format.IsCopy);
        });
        Assert.Equal(
            [".vectorcraft", ".vctemplate", ".svg", ".svgz", ".ai", ".ait", ".eps", ".epsf", ".ps", ".dxf", ".emf", ".wmf", ".pdf", ".afdesign"],
            VectorFormats.OpenExtensions);
        Assert.Equal(VectorFormats.NativeExtension, VectorFormats.OpenExtensions[0]);
    }
}
