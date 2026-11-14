using Kazo.Vector.Documents;
using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Craft;

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

    [Fact]
    public void VectorCraft_s_File_Open_picker_is_told_from_Place_and_Other_Script()
    {
        // The three pickers of apps/vectorcraft-web/src/web.rs, with a part of their accept lists.
        PickRequest open = new(1, ".vectorcraft,.svg,.ai,.pdf,.png,.vcswatches,.gpl,.kys,.jsx,.wasm", Multiple: false);
        PickRequest place = new(2, ".svg,.ai,.pdf,.png,.jpg,.txt,.rtf,.docx", Multiple: true);
        PickRequest script = new(3, ".jsx,.jsxinc,.jsxbin", Multiple: false);

        Assert.True(VectorFormats.IsOpenPicker(open));
        Assert.False(VectorFormats.IsOpenPicker(place));
        Assert.False(VectorFormats.IsOpenPicker(script));
    }

    [Theory]
    [InlineData("logo.svg", true)]
    [InlineData("floor.dxf", true)]
    [InlineData("brand.vcswatches", false)]
    [InlineData("Styles.VCSTYLES", false)]
    [InlineData("tidy.jsx", false)]
    public void Only_drawings_are_bound_to_a_tab(string fileName, bool isDocument)
    {
        Assert.Equal(isDocument, VectorFormats.IsDocument(fileName));
    }

    [Fact]
    public void No_file_that_is_not_a_drawing_gets_an_association()
    {
        // Q2 = A: VectorCraft reads them from File › Open, a double click never opens them.
        Assert.DoesNotContain(VectorFormats.FileHandlers, handler => handler.Extensions.Intersect(VectorFormats.OtherFileExtensions).Any());
    }
}
