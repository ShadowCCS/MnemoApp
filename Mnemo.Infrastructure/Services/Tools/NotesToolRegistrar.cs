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
            "Lists or searches notes: sort newest; optional search; snippet_search for block-level hits; match_all for AND; mode semantic for KB.",
            svc.ListNotesAsync);
        Reg<NoteIdParameters>("get_note",
            "Returns full structured block content for a note (canonical read).",
            svc.GetNoteAsync);
        Reg<UpdateNoteParameters>("update_note",
            "Replace title and/or body. Body is parsed as markdown into blocks.",
            svc.UpdateNoteAsync);
        Reg<AppendToNoteParameters>("append_to_note",
            "Append markdown text to the end as blocks.",
            svc.AppendToNoteAsync);
        Reg<InsertBlocksParameters>("insert_blocks", "Insert blocks at top, bottom, before_block_id, or after_block_id.",
            svc.InsertBlocksAsync);
        Reg<ReplaceBlockParameters>("replace_block", "Surgical replacement of one block by block_id.",
            svc.ReplaceBlockAsync);
        Reg<DeleteBlocksParameters>("delete_blocks", "Delete one or more blocks by id.",
            svc.DeleteBlocksAsync);
        Reg<NoteIdParameters>("get_note_outline", "Heading-only table of contents.",
            svc.GetNoteOutlineAsync);
        Reg<OpenNoteParameters>("open_note", "Opens the note in the Notes editor.",
            svc.OpenNoteAsync);
    }
}
