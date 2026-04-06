using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Common;

namespace Mnemo.Infrastructure.Services.Notes;

/// <summary>
/// On first launch with an empty notes library, inserts the packaged welcome note and copies its image asset.
/// </summary>
public static class WelcomeNoteFirstRunSeed
{
    private const string NotesIndexKey = "notes_index";
    private const string SeedFlagKey = "seed.welcome_note.v1";
    private const string JsonResourceName = "Mnemo.Infrastructure.Seed.WelcomeNote.json";
    private const string PngResourceName = "Mnemo.Infrastructure.Seed.welcome-mitochondrion.png";
    private const string WelcomeImageBlockId = "37349c1c-5f21-4962-9f6e-0a5a7b4fb959";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Idempotent: runs once per machine for empty libraries; marks a flag so clearing all notes does not re-insert.
    /// </summary>
    public static async Task TrySeedIfNeededAsync(
        INoteService noteService,
        IStorageProvider storage,
        ILoggerService logger)
    {
        try
        {
            var indexResult = await storage.LoadAsync<List<string>>(NotesIndexKey).ConfigureAwait(false);
            if (indexResult.IsSuccess && indexResult.Value is { Count: > 0 })
            {
                await MarkSeededIfNeededAsync(storage).ConfigureAwait(false);
                return;
            }

            var flagResult = await storage.LoadAsync<bool?>(SeedFlagKey).ConfigureAwait(false);
            if (flagResult.IsSuccess && flagResult.Value == true)
                return;

            await using var jsonStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(JsonResourceName);
            if (jsonStream == null)
            {
                logger.Warning("WelcomeNoteSeed", $"Missing embedded resource {JsonResourceName}");
                return;
            }

            var note = await JsonSerializer.DeserializeAsync<Note>(jsonStream, JsonOptions).ConfigureAwait(false);
            if (note == null || string.IsNullOrEmpty(note.NoteId))
            {
                logger.Warning("WelcomeNoteSeed", "Failed to deserialize welcome note JSON.");
                return;
            }

            var imagesDir = MnemoAppPaths.GetImagesDirectory();
            Directory.CreateDirectory(imagesDir);
            var destImagePath = Path.Combine(imagesDir, WelcomeImageBlockId + ".png");

            await using var pngStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PngResourceName);
            if (pngStream != null)
            {
                await using var fs = new FileStream(destImagePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await pngStream.CopyToAsync(fs).ConfigureAwait(false);
            }
            else
            {
                logger.Warning("WelcomeNoteSeed", $"Missing embedded resource {PngResourceName}; image block may be empty.");
            }

            if (File.Exists(destImagePath))
                ApplyWelcomeImagePath(note.Blocks, destImagePath);

            foreach (var b in EnumerateBlocks(note.Blocks))
                b.EnsureInlineRuns();

            var save = await noteService.SaveNoteAsync(note).ConfigureAwait(false);
            if (!save.IsSuccess)
            {
                logger.Error("WelcomeNoteSeed", $"Save welcome note failed: {save.ErrorMessage}");
                return;
            }

            await storage.SaveAsync(SeedFlagKey, true).ConfigureAwait(false);
            logger.Info("WelcomeNoteSeed", "Inserted default welcome note.");
        }
        catch (Exception ex)
        {
            logger.Error("WelcomeNoteSeed", "Unexpected error seeding welcome note.", ex);
        }
    }

    private static async Task MarkSeededIfNeededAsync(IStorageProvider storage)
    {
        var r = await storage.LoadAsync<bool?>(SeedFlagKey).ConfigureAwait(false);
        if (r.IsSuccess && r.Value == true)
            return;
        await storage.SaveAsync(SeedFlagKey, true).ConfigureAwait(false);
    }

    private static void ApplyWelcomeImagePath(List<Block>? blocks, string absolutePath)
    {
        foreach (var b in EnumerateBlocks(blocks))
        {
            if (b.Type != BlockType.Image || b.Id != WelcomeImageBlockId)
                continue;
            b.Meta["imagePath"] = absolutePath;
            break;
        }
    }

    private static IEnumerable<Block> EnumerateBlocks(List<Block>? blocks)
    {
        if (blocks == null) yield break;
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var nested in EnumerateBlocks(b.Children))
                yield return nested;
        }
    }
}
