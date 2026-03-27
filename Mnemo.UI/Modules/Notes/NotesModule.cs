using System;
using System.Linq;
using System.Text;
using Avalonia.Threading;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.UI.Modules.Notes.ViewModels;

namespace Mnemo.UI.Modules.Notes;

public class NotesModule : IModule
{
    private const int ReadNoteBodyMaxChars = 80_000;

    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<NotesViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
        // No module translations yet
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("notes", typeof(NotesViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Notes", "notes", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/book.svg", "Library", 1, int.MaxValue);
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
        var noteService = services.GetRequiredService<INoteService>();
        var navigation = services.GetRequiredService<INavigationService>();

        registry.RegisterTool(new AIToolDefinition(
            Name: "create_note",
            Description: "Creates a new persistent note with a title and optional content.",
            ParametersType: typeof(CreateNoteParameters),
            Handler: async args =>
            {
                var p = (CreateNoteParameters)args;
                if (string.IsNullOrWhiteSpace(p.Title))
                    return "Error: title is required.";

                var note = new Note
                {
                    Title = p.Title.Trim(),
                    Content = p.Content ?? string.Empty
                };

                var result = await noteService.SaveNoteAsync(note).ConfigureAwait(false);
                return result.IsSuccess
                    ? $"Note created successfully: \"{note.Title}\" (id: {note.NoteId})"
                    : $"Failed to create note: {result.ErrorMessage}";
            }));

        registry.RegisterTool(new AIToolDefinition(
            Name: "list_notes",
            Description: "Lists the user's notes (newest first). Optional search matches title or body text.",
            ParametersType: typeof(ListNotesParameters),
            Handler: async args =>
            {
                var p = (ListNotesParameters)args;
                var limit = p.Limit is > 0 and <= 100 ? p.Limit!.Value : 30;

                var all = (await noteService.GetAllNotesAsync().ConfigureAwait(false)).ToList();
                var ordered = all.OrderByDescending(n => n.ModifiedAt).ToList();

                if (!string.IsNullOrWhiteSpace(p.Search))
                {
                    var q = p.Search.Trim();
                    ordered = ordered.Where(n => NoteMatchesListSearch(n, q)).ToList();
                }

                var slice = ordered.Take(limit).ToList();
                if (slice.Count == 0)
                    return string.IsNullOrWhiteSpace(p.Search)
                        ? "No notes found."
                        : $"No notes matching \"{p.Search.Trim()}\".";

                var sb = new StringBuilder();
                foreach (var n in slice)
                {
                    sb.Append("- id: ").Append(n.NoteId);
                    sb.Append(" | title: ").Append(n.Title);
                    sb.Append(" | modified (UTC): ").AppendLine(n.ModifiedAt.ToString("o"));
                }

                sb.AppendLine($"Showing {slice.Count} of {ordered.Count} matching notes.");
                return sb.ToString().TrimEnd();
            }));

        registry.RegisterTool(new AIToolDefinition(
            Name: "read_note",
            Description: "Loads the full title and body of a note by id. Use list_notes to find ids.",
            ParametersType: typeof(ReadNoteParameters),
            Handler: async args =>
            {
                var p = (ReadNoteParameters)args;
                if (string.IsNullOrWhiteSpace(p.NoteId))
                    return "Error: note_id is required.";

                var note = await noteService.GetNoteAsync(p.NoteId.Trim()).ConfigureAwait(false);
                if (note == null)
                    return $"Error: no note with id \"{p.NoteId.Trim()}\".";

                var body = NoteToolContentHelper.GetPlainText(note);
                var truncated = false;
                if (body.Length > ReadNoteBodyMaxChars)
                {
                    body = body[..ReadNoteBodyMaxChars];
                    truncated = true;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Note id: {note.NoteId}");
                sb.AppendLine($"Title: {note.Title}");
                sb.AppendLine($"Modified (UTC): {note.ModifiedAt:o}");
                sb.AppendLine();
                sb.AppendLine(body);
                if (truncated)
                    sb.AppendLine().Append($"(Body truncated to {ReadNoteBodyMaxChars} characters.)");
                return sb.ToString();
            }));

        registry.RegisterTool(new AIToolDefinition(
            Name: "update_note",
            Description: "Updates an existing note: set a new title and/or replace the entire body. Omit a field to leave it unchanged. Pass content as an empty string to clear the body.",
            ParametersType: typeof(UpdateNoteParameters),
            Handler: async args =>
            {
                var p = (UpdateNoteParameters)args;
                if (string.IsNullOrWhiteSpace(p.NoteId))
                    return "Error: note_id is required.";

                var hasTitle = p.Title != null && !string.IsNullOrWhiteSpace(p.Title);
                var hasContent = p.Content != null;
                if (!hasTitle && !hasContent)
                    return "Error: provide a non-empty title and/or a content field (use empty string to clear the body).";

                var note = await noteService.GetNoteAsync(p.NoteId.Trim()).ConfigureAwait(false);
                if (note == null)
                    return $"Error: no note with id \"{p.NoteId.Trim()}\".";

                if (hasTitle)
                    note.Title = p.Title!.Trim();

                if (hasContent)
                    NoteToolContentHelper.SetBodyAsSingleTextBlock(note, p.Content!);

                var result = await noteService.SaveNoteAsync(note).ConfigureAwait(false);
                return result.IsSuccess
                    ? $"Note updated (id: {note.NoteId})."
                    : $"Failed to save note: {result.ErrorMessage}";
            }));

        registry.RegisterTool(new AIToolDefinition(
            Name: "append_to_note",
            Description: "Appends plain text or markdown to the end of an existing note's body (adds a blank line before the new text when the note is non-empty).",
            ParametersType: typeof(AppendToNoteParameters),
            Handler: async args =>
            {
                var p = (AppendToNoteParameters)args;
                if (string.IsNullOrWhiteSpace(p.NoteId))
                    return "Error: note_id is required.";
                if (p.Text == null)
                    return "Error: text is required.";

                var append = p.Text;
                if (append.Length == 0)
                    return "Error: text must not be empty.";

                var note = await noteService.GetNoteAsync(p.NoteId.Trim()).ConfigureAwait(false);
                if (note == null)
                    return $"Error: no note with id \"{p.NoteId.Trim()}\".";

                var existing = NoteToolContentHelper.GetPlainText(note);
                var merged = string.IsNullOrEmpty(existing) ? append : existing + "\n\n" + append;
                NoteToolContentHelper.SetBodyAsSingleTextBlock(note, merged);

                var result = await noteService.SaveNoteAsync(note).ConfigureAwait(false);
                return result.IsSuccess
                    ? $"Text appended to note (id: {note.NoteId})."
                    : $"Failed to save note: {result.ErrorMessage}";
            }));

        registry.RegisterTool(new AIToolDefinition(
            Name: "open_note",
            Description: "Opens the Notes module and selects the note with the given id in the editor.",
            ParametersType: typeof(OpenNoteParameters),
            Handler: async args =>
            {
                var p = (OpenNoteParameters)args;
                if (string.IsNullOrWhiteSpace(p.NoteId))
                    return "Error: note_id is required.";

                var id = p.NoteId.Trim();
                var note = await noteService.GetNoteAsync(id).ConfigureAwait(false);
                if (note == null)
                    return $"Error: no note with id \"{id}\".";

                await Dispatcher.UIThread.InvokeAsync(() => navigation.NavigateTo("notes", id));
                return $"Opened \"{note.Title}\" in the Notes editor (id: {note.NoteId}).";
            }));
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
        // No widgets for notes
    }

    /// <summary>
    /// Exact phrase match on title/body first. If the query has multiple words and nothing matched,
    /// fall back to matching if any word (length ≥2) appears in title or body so e.g. "spanish note"
    /// can find a note titled "Spanish 101".
    /// </summary>
    private static bool NoteMatchesListSearch(Note n, string q)
    {
        var title = n.Title ?? string.Empty;
        var body = NoteToolContentHelper.GetPlainText(n);

        if (title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            body.Contains(q, StringComparison.OrdinalIgnoreCase))
            return true;

        var tokens = SplitListSearchTokens(q);
        if (tokens.Count < 2)
            return false;

        return tokens.Exists(t =>
            title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            body.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> SplitListSearchTokens(string q)
    {
        return q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().Trim(',', '.', ';', ':', '"', '\'', '!', '?'))
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
