using System;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Notes;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Notes;

namespace Mnemo.Infrastructure.Services.Tools;

public static class NotesToolRegistrar
{
    public static void Register(IFunctionRegistry registry, NotesToolService svc)
    {
        void Reg<T>(string name, string desc, Func<T, Task<ToolInvocationResult>> fn) where T : class =>
            registry.RegisterTool(new AIToolDefinition(name, desc, typeof(T),
                async args => await fn((T)args).ConfigureAwait(false)));

        Reg<CreateNoteParameters>("create_note",
            "Creates a new note. Prefer update_note / append_to_note / insert_blocks when a note already exists.",
            svc.CreateNoteAsync);
        Reg<ListNotesParameters>("list_notes",
            "Lists notes (newest first). Optional search: comma/space-separated keywords (OR); optional fuzzy (default true) tolerates small typos.",
            svc.ListNotesAsync);
        Reg<NoteIdParameters>("get_note",
            "Returns full structured block content for a note (canonical read).",
            svc.GetNoteAsync);
        Reg<NoteIdParameters>("note_exists",
            "Checks whether a note id exists without loading full content.",
            svc.NoteExistsAsync);
        Reg<NoteIdParameters>("read_note",
            "Alias for get_note (legacy). Full block content.",
            svc.ReadNoteAsync);
        Reg<SearchNotesParameters>("search_notes",
            "Search notes with block snippets. query: comma/space-separated keywords; match_all=true requires every keyword (AND); fuzzy (default true) allows typos. mode=text or semantic (knowledge base).",
            svc.SearchNotesAsync);
        Reg<GetRecentNotesParameters>("get_recent_notes",
            "Recency-ordered notes for 'recent work' flows.",
            svc.GetRecentNotesAsync);
        Reg<UpdateNoteParameters>("update_note",
            "Replace title and/or body. Body is parsed as markdown into blocks; optional blocks[] replaces structure.",
            svc.UpdateNoteAsync);
        Reg<AppendToNoteParameters>("append_to_note",
            "Append blocks or markdown text to the end (block-aware; no single-block collapse).",
            svc.AppendToNoteAsync);
        Reg<InsertBlocksParameters>("insert_blocks", "Insert blocks at top, bottom, before_block_id, or after_block_id.",
            svc.InsertBlocksAsync);
        Reg<ReplaceBlockParameters>("replace_block", "Surgical replacement of one block by block_id.",
            svc.ReplaceBlockAsync);
        Reg<DeleteBlocksParameters>("delete_blocks", "Delete one or more blocks by id.",
            svc.DeleteBlocksAsync);
        Reg<RestructureNoteParameters>("restructure_note",
            "Full replace of the note's block list (use sparingly).",
            svc.RestructureNoteAsync);
        Reg<ConvertBlockParameters>("convert_block", "Change a block's type (e.g. Heading1 -> Heading2).",
            svc.ConvertBlockAsync);
        Reg<FindRelatedNotesParameters>("find_related_notes",
            "Token overlap-based related notes (local heuristic).",
            svc.FindRelatedNotesAsync);
        Reg<NoteIdParameters>("get_backlinks", "Reserved; wiki links not yet enabled.",
            svc.GetBacklinksAsync);
        Reg<NoteIdParameters>("get_note_outline", "Heading-only table of contents.",
            svc.GetNoteOutlineAsync);
        Reg<EmptyToolParameters>("get_workspace_summary",
            "Aggregate stats across the note collection.",
            _ => svc.GetWorkspaceSummaryAsync());
        Reg<ReadNoteLinesParameters>("read_note_lines",
            "Read note as numbered markdown lines (optionally line_range like 1-20).",
            svc.ReadNoteLinesAsync);
        Reg<ReplaceNoteLinesParameters>("replace_note_lines",
            "Replace inclusive 1-based line range in markdown projection, then rebuild blocks.",
            svc.ReplaceNoteLinesAsync);
        Reg<InsertNoteLinesParameters>("insert_note_lines",
            "Insert markdown lines before 1-based at_line in markdown projection.",
            svc.InsertNoteLinesAsync);
        Reg<OpenNoteParameters>("open_note", "Opens the note in the Notes editor.",
            svc.OpenNoteAsync);
    }
}
