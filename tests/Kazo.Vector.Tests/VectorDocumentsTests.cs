using System.Text.Json;
using Kazo.Os.AppSdk.Craft;
using Kazo.Vector.Documents;

namespace Kazo.Vector.Tests;

/// <summary>
/// The tabs of a Kazo Vector window and their files (doc/en/plan.md, work item 3, decisions 7 to 10):
/// what VectorCraft lists, where a file opens, where each save goes, the title.
/// </summary>
public sealed class VectorDocumentsTests
{
    private const string Untitled = "Untitled";

    [Fact]
    public void VectorCraft_s_ui_inspect_is_read_by_drawing_id()
    {
        // The fork's H7 shape: { id (uid), title, path, dirty, active }, among many other fields.
        IReadOnlyList<OpenDocument> tabs = VectorDocuments.ListFrom(Inspect(
            """
            { "tool": "select", "activeDocument": 1, "documents": [
                { "id": 7, "title": "logo.svg", "path": "logo.svg", "dirty": false, "active": false },
                { "id": 9, "title": "Untitled-1", "path": null, "dirty": true, "active": true },
                { "title": "no id", "dirty": true } ] }
            """));

        Assert.Equal(
            [
                new OpenDocument(7, "logo.svg", "logo.svg", false, false),
                new OpenDocument(9, null, "Untitled-1", true, true),
            ],
            tabs);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "documents": 3 }""")]
    [InlineData("""{ "documents": [ 1, { "title": "no id" }, { "id": "7" } ] }""")]
    public void A_malformed_answer_from_the_iframe_lists_nothing(string json)
    {
        Assert.Empty(VectorDocuments.ListFrom(Inspect(json)));
    }

    [Fact]
    public void The_index_of_a_tab_is_its_place_in_VectorCraft_s_order()
    {
        VectorDocuments documents = new();
        documents.Update([Tab(40, "a.svg"), Tab(12, "b.svg", active: true)]);

        Assert.Equal(1, documents.IndexOf(Tab(12, "b.svg")));
        Assert.Equal(-1, documents.IndexOf(Tab(3, "gone.svg")));
    }

    [Fact]
    public void An_opened_SVG_is_rewritten_by_Save()
    {
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true)]);

        SaveDecision decision = documents.Decide("logo.svg", saveAs: false);

        Assert.Equal(SaveAction.Rewrite, decision.Action);
        Assert.Equal("/home/ana/Drawings/logo.svg", decision.Path);
    }

    [Fact]
    public void Save_as_always_asks_in_the_folder_of_the_file()
    {
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true)]);

        SaveDecision decision = documents.Decide("logo.svg", saveAs: true);

        Assert.Equal(SaveAction.AskAndBind, decision.Action);
        Assert.Equal("/home/ana/Drawings", decision.Folder);
    }

    [Fact]
    public void A_format_change_asks_for_the_new_file_next_to_the_old_one()
    {
        // F1 case 4: the SVG saved as a .vectorcraft (VectorCraft's saveOptions dialog, saveAs true).
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true)]);

        SaveDecision decision = documents.Decide("logo.vectorcraft", saveAs: true);

        Assert.Equal(SaveAction.AskAndBind, decision.Action);
        Assert.Equal("logo.vectorcraft", decision.Name);
        Assert.Equal("/home/ana/Drawings", decision.Folder);
    }

    [Fact]
    public void After_the_first_save_the_next_Save_writes_the_file_chosen()
    {
        // F1 case 2: VectorCraft keeps the name it gave (Untitled-1.svg) as the drawing's path, and
        // that name is bound to the file the user chose.
        VectorDocuments documents = new();
        documents.Update([Tab(1, null, title: "Untitled-1", dirty: true, active: true)]);
        SaveDecision first = documents.Decide("Untitled-1.svg", saveAs: true);
        documents.Saved(first, "/home/ana/Drawings/badge.svg");
        documents.Update([Tab(1, "Untitled-1.svg", active: true)]);

        SaveDecision next = documents.Decide("Untitled-1.svg", saveAs: false);

        Assert.Equal(SaveAction.Rewrite, next.Action);
        Assert.Equal("/home/ana/Drawings/badge.svg", next.Path);
        Assert.Equal("badge.svg — Kazo Vector", documents.Title(Untitled));
    }

    [Fact]
    public void A_cancelled_or_failed_save_asks_again_and_keeps_the_window_modified()
    {
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true)]);
        SaveDecision decision = documents.Decide("logo.svg", saveAs: false);

        documents.SaveFailed(decision);

        Assert.True(documents.IsDirty);
        Assert.Equal(SaveAction.AskAndBind, documents.Decide("logo.svg", saveAs: false).Action);
        Assert.Single(documents.WithUnsavedWork());
    }

    [Fact]
    public void An_export_is_always_a_copy_in_the_folder_of_the_drawing_in_front()
    {
        // Save a Copy, Save as Template, Export As, Export for Screens (.zip): downloads, ADR 0264.
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true)]);

        SaveDecision decision = documents.DecideCopy("logo.svg");

        Assert.Equal(SaveAction.AskForCopy, decision.Action);
        Assert.Equal("/home/ana/Drawings", decision.Folder);
        Assert.False(decision.SavesTheDocument);
        Assert.Equal(SaveAction.AskForCopy, documents.DecideCopy("Assets.zip").Action);
    }

    [Fact]
    public void A_new_file_opens_here()
    {
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true)]);

        Assert.Equal(OpenPlace.Here, documents.PlaceOf("/home/ana/Drawings/poster.ai"));
    }

    [Fact]
    public void A_file_already_open_brings_its_tab_to_the_front()
    {
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(3, "poster.ai", active: true), Tab(5, "logo.svg")]);

        Assert.Equal(OpenPlace.AlreadyOpen, documents.PlaceOf("/home/ana/Drawings/logo.svg"));
        Assert.Equal(5, documents.TabOf("/home/ana/Drawings/logo.svg")?.Id);
    }

    [Fact]
    public void Two_files_with_one_name_never_share_a_window()
    {
        // Decision 9: VectorCraft would bring back the first tab (same_open_path).
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true)]);

        Assert.Equal(OpenPlace.NewWindow, documents.PlaceOf("/home/ana/Work/logo.svg"));
    }

    [Fact]
    public void A_name_a_new_drawing_already_saves_under_sends_the_file_to_a_new_window()
    {
        VectorDocuments documents = new();
        documents.Update([Tab(1, "logo.svg", active: true)]);

        Assert.Equal(OpenPlace.NewWindow, documents.PlaceOf("/home/ana/Drawings/logo.svg"));
    }

    [Fact]
    public void A_file_VectorCraft_keeps_no_path_for_is_found_by_the_tab_it_became()
    {
        // A DXF opens as a drawing with no path (not a save format, fork H2); the window remembers
        // the tab it became, by the tab's id.
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Plans/floor.dxf");
        documents.Update([Tab(1, "logo.svg"), Tab(2, null, title: "floor.dxf", active: true)]);

        Assert.True(documents.Shown("/home/ana/Plans/floor.dxf", [1]));
        Assert.Equal(OpenPlace.AlreadyOpen, documents.PlaceOf("/home/ana/Plans/floor.dxf"));
        Assert.Equal(2, documents.TabOf("/home/ana/Plans/floor.dxf")?.Id);
        Assert.Equal(OpenPlace.NewWindow, documents.PlaceOf("/home/ana/Other/floor.dxf"));
        Assert.Equal("floor.dxf — Kazo Vector", documents.Title(Untitled));

        // Its first save asks next to the DXF.
        SaveDecision save = documents.Decide("floor.vectorcraft", saveAs: true);
        Assert.Equal("/home/ana/Plans", save.Folder);
    }

    [Fact]
    public void A_tab_that_was_there_before_the_file_was_sent_is_not_taken_for_it()
    {
        VectorDocuments documents = new();
        documents.Update([Tab(1, null, title: "floor.dxf", active: true)]);

        Assert.False(documents.Shown("/home/ana/Plans/floor.dxf", [1]));
        Assert.Null(documents.TabOf("/home/ana/Plans/floor.dxf"));
    }

    [Fact]
    public void A_closed_tab_forgets_the_file_it_came_from()
    {
        VectorDocuments documents = new();
        documents.Update([Tab(2, null, title: "floor.dxf", active: true)]);
        documents.Shown("/home/ana/Plans/floor.dxf", []);

        documents.Update([]);
        documents.Update([Tab(2, null, title: "floor.dxf", active: true)]);

        Assert.Null(documents.TabOf("/home/ana/Plans/floor.dxf"));
    }

    [Theory]
    [InlineData("/home/ana/brand.vcswatches")]
    [InlineData("/home/ana/palette.gpl")]
    [InlineData("/home/ana/kit.vclibrary")]
    [InlineData("/home/ana/web.vcexportpresets")]
    [InlineData("/home/ana/mine.kys")]
    [InlineData("/home/ana/tidy.jsx")]
    [InlineData("/home/ana/filter.wasm")]
    public void A_file_that_is_not_a_drawing_goes_to_VectorCraft_and_is_never_bound(string path)
    {
        VectorDocuments documents = new();

        Assert.Equal(OpenPlace.NotADocument, documents.PlaceOf(path));
    }

    [Fact]
    public void The_title_names_the_file_of_the_tab_in_front_and_marks_unsaved_work()
    {
        VectorDocuments documents = new();
        documents.Opened("/home/ana/Drawings/logo.svg");
        documents.Update([Tab(1, "logo.svg", active: true), Tab(2, null, title: "Untitled-1", dirty: true)]);

        Assert.Equal("*logo.svg — Kazo Vector", documents.Title(Untitled));

        documents.Update([Tab(1, "logo.svg"), Tab(2, null, title: "Untitled-1", active: true)]);
        Assert.Equal("Untitled-1 — Kazo Vector", documents.Title(Untitled));
    }

    [Fact]
    public void The_title_is_the_app_s_name_on_the_home_screen_and_before_VectorCraft_runs()
    {
        VectorDocuments documents = new();

        Assert.Equal("Kazo Vector", documents.Title(Untitled));
        Assert.Equal("Kazo Vector", documents.Title(Untitled, running: false));
    }

    [Fact]
    public void The_dirty_event_alone_marks_the_window_modified()
    {
        VectorDocuments documents = new();
        documents.SetFrameDirty(true);

        Assert.True(documents.IsDirty);
        Assert.Equal("*Kazo Vector", documents.Title(Untitled));
    }

    [Fact]
    public void The_close_guard_lists_every_modified_tab_in_VectorCraft_s_order()
    {
        VectorDocuments documents = new();
        documents.Update([Tab(4, "a.svg", dirty: true), Tab(5, "b.svg"), Tab(6, null, title: "Untitled-2", dirty: true, active: true)]);

        Assert.Equal([4L, 6L], documents.WithUnsavedWork().Select(tab => tab.Id));
    }

    private static OpenDocument Tab(long id, string? path, string? title = null, bool dirty = false, bool active = false) =>
        new(id, path, title ?? path ?? Untitled, dirty, active);

    private static JsonElement Inspect(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
