using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Notes;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Notes.Markdown;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Notes;

/// <summary>Module-owned tool logic for the Notes skill. Registered via <see cref="NotesToolRegistrar"/>.</summary>
public sealed class NotesToolService
{
    private const int ReadNoteBodyMaxChars = 80_000;
    private readonly INoteService _notes;
    private readonly INavigationService _nav;
    private readonly IMainThreadDispatcher _ui;
    private readonly IKnowledgeService? _knowledge;

    public NotesToolService(
        INoteService notes,
        INavigationService nav,
        IMainThreadDispatcher ui,
        IKnowledgeService? knowledge = null)
    {
        _notes = notes;
        _nav = nav;
        _ui = ui;
        _knowledge = knowledge;
    }

    public async Task<ToolInvocationResult> CreateNoteAsync(CreateNoteParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.Title))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "title is required.");

        var note = new Note { Title = p.Title.Trim() };

        if (p.Blocks is { Count: > 0 })
        {
            note.Blocks = NoteToolBlockFactory.FromPayloads(p.Blocks);
            NoteDocumentHelper.NormalizeOrders(note.Blocks);
            note.Content = string.Empty;
        }
        else if (!string.IsNullOrEmpty(p.Content))
        {
            note.Blocks = NoteBlockMarkdownConverter.Deserialize(p.Content);
            NoteDocumentHelper.NormalizeOrders(note.Blocks);
            note.Content = string.Empty;
        }

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success($"Note created (id: {note.NoteId})", new { note_id = note.NoteId, title = note.Title })
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> ListNotesAsync(ListNotesParameters p)
    {
        var limit = p.Limit is > 0 and <= 100 ? p.Limit!.Value : 30;
        var mode = (p.Mode ?? "text").Trim();

        if (string.Equals(mode, "semantic", StringComparison.OrdinalIgnoreCase))
        {
            if (_knowledge == null)
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                    "Semantic search is not available in this environment.");

            if (string.IsNullOrWhiteSpace(p.Search))
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                    "mode=semantic requires a non-empty search string.");

            return await SearchNotesAsync(new SearchNotesParameters
            {
                Query = p.Search!.Trim(),
                Limit = limit,
                Mode = "semantic",
                MatchAll = p.MatchAll,
                Fuzzy = p.Fuzzy
            }).ConfigureAwait(false);
        }

        if (p.SnippetSearch == true)
        {
            if (string.IsNullOrWhiteSpace(p.Search))
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                    "snippet_search requires a non-empty search string.");

            return await SearchNotesAsync(new SearchNotesParameters
            {
                Query = p.Search!.Trim(),
                Limit = limit,
                Mode = "text",
                MatchAll = p.MatchAll,
                Fuzzy = p.Fuzzy
            }).ConfigureAwait(false);
        }

        var all = (await _notes.GetAllNotesAsync().ConfigureAwait(false)).ToList();
        var ordered = all.OrderByDescending(n => n.ModifiedAt).ToList();

        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var q = p.Search.Trim();
            var fuzzy = p.Fuzzy ?? true;
            var matchAll = p.MatchAll ?? false;
            ordered = ordered.Where(n => NoteMatchesListSearch(n, q, fuzzy, matchAll)).ToList();
        }

        var slice = ordered.Take(limit).ToList();
        if (slice.Count == 0)
            return ToolInvocationResult.Success(
                string.IsNullOrWhiteSpace(p.Search) ? "No notes found." : $"No notes matching \"{p.Search.Trim()}\".");

        var lines = slice.Select(n => new { id = n.NoteId, title = n.Title, modifiedUtc = n.ModifiedAt }).ToList();
        return ToolInvocationResult.Success($"Showing {slice.Count} of {ordered.Count} notes.", new { notes = lines });
    }

    public async Task<ToolInvocationResult> GetNoteAsync(NoteIdParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var blocks = note.Blocks!.OrderBy(b => b.Order).Select(NoteDocumentHelper.BlockToDto).ToList();
        return ToolInvocationResult.Success("OK", new
        {
            note_id = note.NoteId,
            title = note.Title,
            modifiedUtc = note.ModifiedAt,
            blocks
        });
    }

    public async Task<ToolInvocationResult> NoteExistsAsync(NoteIdParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Success("Note does not exist.", new { note_id = id, exists = false });

        return ToolInvocationResult.Success("Note exists.", new
        {
            note_id = note.NoteId,
            exists = true,
            title = note.Title,
            modifiedUtc = note.ModifiedAt
        });
    }

    public async Task<ToolInvocationResult> ReadNoteAsync(NoteIdParameters p) => await GetNoteAsync(p).ConfigureAwait(false);

    public async Task<ToolInvocationResult> SearchNotesAsync(SearchNotesParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.Query))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "query is required.");

        var limit = p.Limit is > 0 and <= 50 ? p.Limit!.Value : 15;
        var mode = (p.Mode ?? "text").Trim();

        if (string.Equals(mode, "semantic", StringComparison.OrdinalIgnoreCase) && _knowledge != null)
        {
            var kb = await _knowledge.SearchAsync(p.Query.Trim(), Math.Min(limit, 10), null).ConfigureAwait(false);
            var chunks = kb.IsSuccess && kb.Value != null
                ? kb.Value.Select(c => new { content = c.Content, source_id = c.SourceId }).ToList<object>()
                : [];
            return ToolInvocationResult.Success("Semantic knowledge search results (may not map to notes).", new
            {
                mode = "semantic",
                chunks
            });
        }

        var rawQuery = p.Query.Trim();
        var tokens = TextSearchMatch.ResolveSearchTokens(rawQuery);
        if (tokens.Count == 0)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "query has no searchable tokens.");

        var matchAll = p.MatchAll ?? false;
        var fuzzy = p.Fuzzy ?? true;

        var all = (await _notes.GetAllNotesAsync().ConfigureAwait(false)).ToList();
        var hits = new List<object>();

        foreach (var note in all.OrderByDescending(n => n.ModifiedAt))
        {
            NoteDocumentHelper.EnsureBlocks(note);
            var blocks = note.Blocks ?? [];
            var title = note.Title ?? string.Empty;
            var titleMatched = TextSearchMatch.MatchTokens(title, tokens, matchAll, fuzzy);
            var anyBlockHit = false;

            foreach (var b in blocks.OrderBy(x => x.Order))
            {
                b.EnsureSpans();
                var text = b.Content ?? string.Empty;
                if (!TextSearchMatch.MatchTokens(text, tokens, matchAll, fuzzy))
                    continue;

                anyBlockHit = true;
                string snippet;
                if (TextSearchMatch.TryGetSnippetSpan(text, tokens, fuzzy, out var snStart, out var snLen))
                    snippet = text.Substring(snStart, snLen);
                else
                    snippet = text.Length <= 120 ? text : text[..120];

                hits.Add(new
                {
                    note_id = note.NoteId,
                    title = note.Title,
                    block_id = b.Id,
                    block_type = b.Type.ToString(),
                    snippet
                });
                if (hits.Count >= limit) goto done;
            }

            if (!anyBlockHit && titleMatched && blocks.Count > 0)
            {
                var t = title.Length > 120 ? title[..120] : title;
                hits.Add(new
                {
                    note_id = note.NoteId,
                    title = note.Title,
                    block_id = (string?)null,
                    block_type = "Title",
                    snippet = t
                });
                if (hits.Count >= limit) goto done;
            }

            if (blocks.Count == 0 && titleMatched)
            {
                var t = title.Length > 120 ? title[..120] : title;
                hits.Add(new
                {
                    note_id = note.NoteId,
                    title = note.Title,
                    block_id = (string?)null,
                    block_type = "Title",
                    snippet = t
                });
                if (hits.Count >= limit) goto done;
            }
        }

    done:
        return ToolInvocationResult.Success($"Found {hits.Count} hit(s).", new { mode = "text", hits });
    }

    public async Task<ToolInvocationResult> GetRecentNotesAsync(GetRecentNotesParameters p)
    {
        var limit = p.Limit is > 0 and <= 100 ? p.Limit!.Value : 20;
        var all = (await _notes.GetAllNotesAsync().ConfigureAwait(false))
            .OrderByDescending(n => n.ModifiedAt)
            .Take(limit)
            .Select(n => new { id = n.NoteId, title = n.Title, modifiedUtc = n.ModifiedAt })
            .ToList();

        return ToolInvocationResult.Success($"Recent {all.Count} notes.", new { notes = all });
    }

    public async Task<ToolInvocationResult> UpdateNoteAsync(UpdateNoteParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var hasTitle = p.Title != null && !string.IsNullOrWhiteSpace(p.Title);
        var hasContent = p.Content != null;
        var hasBlocks = p.Blocks is { Count: > 0 };
        if (!hasTitle && !hasContent && !hasBlocks)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                "Provide title, content, and/or blocks.");

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        if (hasTitle)
            note.Title = p.Title!.Trim();

        if (hasBlocks)
        {
            note.Blocks = NoteToolBlockFactory.FromPayloads(p.Blocks!);
            NoteDocumentHelper.NormalizeOrders(note.Blocks);
            note.Content = string.Empty;
        }
        else if (hasContent)
        {
            note.Blocks = NoteBlockMarkdownConverter.Deserialize(p.Content!);
            NoteDocumentHelper.NormalizeOrders(note.Blocks);
            note.Content = string.Empty;
        }

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success($"Note updated (id: {note.NoteId})")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> AppendToNoteAsync(AppendToNoteParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);

        List<Block> toAppend;
        if (p.Blocks is { Count: > 0 })
        {
            toAppend = NoteToolBlockFactory.FromPayloads(p.Blocks, startOrder: note.Blocks!.Count);
        }
        else if (!string.IsNullOrEmpty(p.Text))
        {
            toAppend = NoteBlockMarkdownConverter.Deserialize(p.Text);
            if (toAppend.Count == 0)
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "text/blocks must not be empty.");
        }
        else
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "Provide text or blocks.");

        var maxOrder = note.Blocks!.Count == 0 ? -1 : note.Blocks.Max(b => b.Order);
        foreach (var b in toAppend)
            b.Order = ++maxOrder;

        note.Blocks.AddRange(toAppend);
        NoteDocumentHelper.NormalizeOrders(note.Blocks);

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success($"Appended to note (id: {note.NoteId})")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> InsertBlocksAsync(InsertBlocksParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;
        if (p.Blocks.Count == 0)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "blocks is required.");

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var list = note.Blocks!;
        var newBlocks = NoteToolBlockFactory.FromPayloads(p.Blocks, startOrder: 0);
        var pos = (p.Position ?? "bottom").Trim();

        int insertIndex;
        if (string.Equals(pos, "top", StringComparison.OrdinalIgnoreCase))
            insertIndex = 0;
        else if (string.Equals(pos, "bottom", StringComparison.OrdinalIgnoreCase))
            insertIndex = list.Count;
        else if (string.Equals(pos, "after_block_id", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(p.AnchorBlockId))
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "anchor_block_id is required.");
            var ix = list.FindIndex(b => string.Equals(b.Id, p.AnchorBlockId.Trim(), StringComparison.Ordinal));
            if (ix < 0)
                return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "anchor block not found.");
            insertIndex = ix + 1;
        }
        else if (string.Equals(pos, "before_block_id", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(p.AnchorBlockId))
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "anchor_block_id is required.");
            var ix = list.FindIndex(b => string.Equals(b.Id, p.AnchorBlockId.Trim(), StringComparison.Ordinal));
            if (ix < 0)
                return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "anchor block not found.");
            insertIndex = ix;
        }
        else
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                "position must be top, bottom, before_block_id, or after_block_id.");

        list.InsertRange(insertIndex, newBlocks);
        NoteDocumentHelper.NormalizeOrders(list);

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success($"Inserted blocks (id: {note.NoteId})")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> ReplaceBlockAsync(ReplaceBlockParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;
        if (string.IsNullOrWhiteSpace(p.BlockId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "block_id is required.");

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var list = note.Blocks!;
        var ix = list.FindIndex(b => string.Equals(b.Id, p.BlockId.Trim(), StringComparison.Ordinal));
        if (ix < 0)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "block not found.");

        var order = list[ix].Order;
        var replacement = NoteToolBlockFactory.FromPayload(p.Block, order);
        replacement.Id = string.IsNullOrWhiteSpace(p.Block.BlockId) ? list[ix].Id : p.Block.BlockId.Trim();
        list[ix] = replacement;

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success("Block replaced.")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> DeleteBlocksAsync(DeleteBlocksParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;
        if (p.BlockIds == null || p.BlockIds.Count == 0)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "block_ids is required.");

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var remove = new HashSet<string>(p.BlockIds.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.Ordinal);
        note.Blocks!.RemoveAll(b => remove.Contains(b.Id));
        NoteDocumentHelper.NormalizeOrders(note.Blocks);

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success("Blocks deleted.")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> RestructureNoteAsync(RestructureNoteParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        note.Blocks = NoteToolBlockFactory.FromPayloads(p.Blocks);
        NoteDocumentHelper.NormalizeOrders(note.Blocks);
        note.Content = string.Empty;

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success("Note restructured.")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> ConvertBlockAsync(ConvertBlockParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;
        if (!Enum.TryParse<BlockType>(p.NewType, true, out var newType))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "invalid new_type.");

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var b = note.Blocks!.FirstOrDefault(x => string.Equals(x.Id, p.BlockId.Trim(), StringComparison.Ordinal));
        if (b == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "block not found.");

        b.Type = newType;
        if (newType == BlockType.Checklist)
        {
            var chk = b.Payload is ChecklistPayload cp
                ? cp.Checked
                : ReadMetaCheckedFlag(b.Meta);
            b.Payload = new ChecklistPayload(chk);
            b.Meta.Remove("checked");
        }
        else
        {
            b.Meta.Remove("checked");
            if (b.Payload is ChecklistPayload)
                b.Payload = new EmptyPayload();
        }

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success("Block type converted.")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> FindRelatedNotesAsync(FindRelatedNotesParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;
        var limit = p.Limit is > 0 and <= 30 ? p.Limit!.Value : 8;

        var source = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (source == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        var sourceTokens = Tokenize(NoteDocumentHelper.GetPlainText(source));
        if (sourceTokens.Count == 0)
            return ToolInvocationResult.Success("No tokens to compare.", new { related = Array.Empty<object>() });

        var all = await _notes.GetAllNotesAsync().ConfigureAwait(false);
        var scored = new List<(Note n, double score)>();

        foreach (var n in all)
        {
            if (string.Equals(n.NoteId, id, StringComparison.Ordinal)) continue;
            var tokens = Tokenize(NoteDocumentHelper.GetPlainText(n));
            var score = Jaccard(sourceTokens, tokens);
            if (score > 0.05)
                scored.Add((n, score));
        }

        var top = scored.OrderByDescending(x => x.score).ThenByDescending(x => x.n.ModifiedAt).Take(limit)
            .Select(x => new { note_id = x.n.NoteId, title = x.n.Title, score = Math.Round(x.score, 4) }).ToList();

        return ToolInvocationResult.Success($"Found {top.Count} related notes.", new { related = top });
    }

    public Task<ToolInvocationResult> GetBacklinksAsync(NoteIdParameters p) =>
        Task.FromResult(ToolInvocationResult.Success("Wiki-style backlinks are not enabled yet.",
            new { backlinks = Array.Empty<object>(), feature = "unavailable" }));

    public async Task<ToolInvocationResult> GetNoteOutlineAsync(NoteIdParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var headings = note.Blocks!
            .Where(b => b.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
            .OrderBy(b => b.Order)
            .Select(b =>
            {
                b.EnsureSpans();
                return new { block_id = b.Id, type = b.Type.ToString(), text = b.Content, order = b.Order };
            }).ToList();

        return ToolInvocationResult.Success($"Outline: {headings.Count} heading(s).", new { headings });
    }

    public async Task<ToolInvocationResult> GetWorkspaceSummaryAsync()
    {
        var all = (await _notes.GetAllNotesAsync().ConfigureAwait(false)).ToList();
        var recent = all.OrderByDescending(n => n.ModifiedAt).Take(5)
            .Select(n => new { n.NoteId, n.Title, n.ModifiedAt }).ToList();
        var totalBlocks = 0;
        foreach (var n in all)
        {
            NoteDocumentHelper.EnsureBlocks(n);
            totalBlocks += n.Blocks?.Count ?? 0;
        }

        return ToolInvocationResult.Success("Workspace summary.", new
        {
            note_count = all.Count,
            total_blocks = totalBlocks,
            recent_notes = recent
        });
    }

    public async Task<ToolInvocationResult> ReadNoteLinesAsync(ReadNoteLinesParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var md = NoteBlockMarkdownConverter.Serialize(note.Blocks!);
        var lines = md.Replace("\r\n", "\n").Split('\n');
        if (string.IsNullOrWhiteSpace(p.LineRange))
        {
            var numbered = lines.Select((t, i) => $"{i + 1:D4}| {t}").ToList();
            var text = string.Join("\n", numbered);
            if (text.Length > ReadNoteBodyMaxChars)
                text = text[..ReadNoteBodyMaxChars] + "\n(truncated)";
            return ToolInvocationResult.Success("Lines (full document markdown projection).", new { lines = text, line_count = lines.Length });
        }

        if (!LineRangeParser.TryParseRange(p.LineRange, out var start, out var end, out var rangeErr))
            return LineRangeParser.InvalidRange(rangeErr!);

        if (start > lines.Length)
            return ToolInvocationResult.Failure(ToolResultCodes.OutOfRange, "start line past end of document.");

        end = Math.Min(end, lines.Length);
        var slice = new string[end - start + 1];
        for (var i = start; i <= end; i++)
            slice[i - start] = $"{i:D4}| {lines[i - 1]}";

        return ToolInvocationResult.Success("Line range.",
            new { lines = string.Join("\n", slice), line_count = lines.Length });
    }

    public async Task<ToolInvocationResult> ReplaceNoteLinesAsync(ReplaceNoteLinesParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        if (!LineRangeParser.TryParseRange(p.ReplaceLines, out var start, out var end, out var rangeErr))
            return LineRangeParser.InvalidRange(rangeErr!);

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var md = NoteBlockMarkdownConverter.Serialize(note.Blocks!);
        var lines = md.Replace("\r\n", "\n").Split('\n').ToList();
        if (start < 1 || end > lines.Count || start > end)
            return ToolInvocationResult.Failure(ToolResultCodes.OutOfRange, "invalid line range for this note.");

        var insertLines = (p.ContentMarkdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        lines.RemoveRange(start - 1, end - start + 1);
        lines.InsertRange(start - 1, insertLines);
        var newMd = string.Join("\n", lines);
        note.Blocks = NoteBlockMarkdownConverter.Deserialize(newMd);
        NoteDocumentHelper.NormalizeOrders(note.Blocks);
        note.Content = string.Empty;

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success("Lines replaced and blocks rebuilt from markdown.")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> InsertNoteLinesAsync(InsertNoteLinesParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;
        if (p.AtLine < 1)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "at_line must be >= 1.");

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        NoteDocumentHelper.EnsureBlocks(note);
        var md = NoteBlockMarkdownConverter.Serialize(note.Blocks!);
        var lines = md.Replace("\r\n", "\n").Split('\n').ToList();
        var at = Math.Min(p.AtLine - 1, lines.Count);
        var insertLines = (p.ContentMarkdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        lines.InsertRange(at, insertLines);
        var newMd = string.Join("\n", lines);
        note.Blocks = NoteBlockMarkdownConverter.Deserialize(newMd);
        NoteDocumentHelper.NormalizeOrders(note.Blocks);
        note.Content = string.Empty;

        var res = await _notes.SaveNoteAsync(note).ConfigureAwait(false);
        return res.IsSuccess
            ? ToolInvocationResult.Success("Lines inserted.")
            : ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Save failed.");
    }

    public async Task<ToolInvocationResult> OpenNoteAsync(OpenNoteParameters p)
    {
        var err = RequireNoteId(p.NoteId, out var id);
        if (err != null) return err;

        var note = await _notes.GetNoteAsync(id).ConfigureAwait(false);
        if (note == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, $"No note with id \"{id}\".");

        await _ui.InvokeAsync(() =>
        {
            _nav.NavigateTo("notes", id);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        return ToolInvocationResult.Success($"Opened note \"{note.Title}\" (id: {note.NoteId}).");
    }

    private static ToolInvocationResult? RequireNoteId(string raw, out string id)
    {
        id = raw?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "note_id is required.");
        return null;
    }

    private static bool NoteMatchesListSearch(Note n, string q, bool fuzzy, bool matchAll)
    {
        var title = n.Title ?? string.Empty;
        var body = NoteDocumentHelper.GetPlainText(n);
        var hay = title + "\n" + body;

        if (hay.Contains(q, StringComparison.OrdinalIgnoreCase))
            return true;

        var tokens = TextSearchMatch.ResolveSearchTokens(q);
        if (tokens.Count == 0)
            return false;

        return TextSearchMatch.MatchTokens(hay, tokens, matchAll, fuzzy);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var separators = new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '\"', '\'', '/' };
        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Count(x => b.Contains(x));
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    private static bool ReadMetaCheckedFlag(Dictionary<string, object> meta)
    {
        if (!meta.TryGetValue("checked", out var v) || v == null)
            return false;
        return v switch
        {
            bool b => b,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            _ => false
        };
    }
}
