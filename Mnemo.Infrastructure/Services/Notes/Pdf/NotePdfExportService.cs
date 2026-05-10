using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Mnemo.Infrastructure.Services.Notes.Pdf;

public sealed class NotePdfExportService : INotePdfExportService
{
    private readonly INotePdfLatexImageRenderer? _latexImageRenderer;

    static NotePdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public NotePdfExportService(IEnumerable<INotePdfLatexImageRenderer> latexImageRenderers)
    {
        _latexImageRenderer = latexImageRenderers.FirstOrDefault();
    }

    public async Task<byte[]> GeneratePdfAsync(Note note, NotePdfExportOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var copy = CloneNote(note);
        var latexImages = await BuildLatexImageMapAsync(copy, options, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = NotePdfDocumentComposer.CreateDocument(copy, options, latexImages);
            return doc.GeneratePdf();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<byte[]>> GeneratePreviewPngPagesAsync(Note note, NotePdfExportOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var copy = CloneNote(note);
        var latexImages = await BuildLatexImageMapAsync(copy, options, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = NotePdfDocumentComposer.CreateDocument(copy, options, latexImages);
            var settings = new ImageGenerationSettings
            {
                RasterDpi = Math.Clamp(options.PreviewRasterDpi, 72, 300),
                ImageFormat = ImageFormat.Png,
                ImageCompressionQuality = ImageCompressionQuality.Medium
            };
            var pages = doc.GenerateImages(settings);
            return (IReadOnlyList<byte[]>)pages.ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, NotePdfLatexRaster>> BuildLatexImageMapAsync(
        Note note,
        NotePdfExportOptions options,
        CancellationToken cancellationToken)
    {
        if (_latexImageRenderer == null)
            return new Dictionary<string, NotePdfLatexRaster>();

        var requests = new HashSet<(string Latex, double FontSize, bool Inline)>();
        foreach (var block in note.Blocks ?? [])
            CollectLatexRequests(block, options, requests);

        var result = new Dictionary<string, NotePdfLatexRaster>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raster = await _latexImageRenderer.RenderLatexPngAsync(
                request.Latex,
                request.FontSize,
                request.Inline,
                cancellationToken).ConfigureAwait(false);
            if (raster is { Png.Length: > 0 })
                result[NotePdfDocumentComposer.GetLatexImageKey(request.Latex, request.FontSize)] = raster;
        }

        return result;
    }

    private static void CollectLatexRequests(
        Block block,
        NotePdfExportOptions options,
        HashSet<(string Latex, double FontSize, bool Inline)> requests)
    {
        if (block.Type == BlockType.Page)
            return;

        if (block.Payload is EquationPayload equation && !string.IsNullOrWhiteSpace(equation.Latex))
            // Match notes editor display equation raster (LaTeX BuildLayoutAsync font size 16).
            requests.Add((equation.Latex, 16d, false));

        var inlineEqFont = NotePdfDocumentComposer.InlineEquationRasterLayoutFont(block.Type, options);
        foreach (var span in block.Spans)
        {
            if (span is EquationSpan equationSpan && !string.IsNullOrWhiteSpace(equationSpan.Latex))
                requests.Add((equationSpan.Latex, inlineEqFont, true));
        }

        if (block.Children is not { Count: > 0 })
            return;

        foreach (var child in block.Children)
            CollectLatexRequests(child, options, requests);
    }

    private static Note CloneNote(Note note)
    {
        var json = JsonSerializer.Serialize(note);
        return JsonSerializer.Deserialize<Note>(json) ?? new Note { Title = note.Title ?? string.Empty, Content = note.Content ?? string.Empty };
    }
}
