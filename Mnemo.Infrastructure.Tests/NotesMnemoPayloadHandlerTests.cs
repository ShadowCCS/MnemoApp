using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Packaging.PayloadHandlers;

namespace Mnemo.Infrastructure.Tests;

public sealed class NotesMnemoPayloadHandlerTests
{
    [Fact]
    public async Task ImportAsync_DuplicateIds_GeneratesNewIds()
    {
        var noteService = new InMemoryNoteService();
        var folderService = new InMemoryFolderService();
        await noteService.SaveNoteAsync(new Note { NoteId = "n1", Title = "Existing" }).ConfigureAwait(false);
        await folderService.SaveFolderAsync(new NoteFolder { FolderId = "f1", Name = "Existing" }).ConfigureAwait(false);

        var handler = new NotesMnemoPayloadHandler(noteService, folderService);
        var bytes = BuildNotesDb(
            [new Note { NoteId = "n1", Title = "Imported" }],
            [new NoteFolder { FolderId = "f1", Name = "Folder" }]);

        var result = await handler.ImportAsync(new MnemoPayloadImportContext
        {
            Entry = new MnemoPackageEntry { PayloadType = "notes", Path = "payloads/notes" },
            Options = new MnemoPackageImportOptions { DuplicateOnConflict = true },
            Files = new Dictionary<string, byte[]> { ["notes.db"] = bytes }
        }).ConfigureAwait(false);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, result.DuplicatedCount);
        var all = await noteService.GetAllNotesAsync().ConfigureAwait(false);
        Assert.Equal(2, all.Count());
    }

    private static byte[] BuildNotesDb(IReadOnlyList<Note> notes, IReadOnlyList<NoteFolder> folders)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-notes-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempPath}"))
            {
                connection.Open();
                using var create = connection.CreateCommand();
                create.CommandText = """
                                     CREATE TABLE Notes (NoteId TEXT PRIMARY KEY, Json TEXT NOT NULL);
                                     CREATE TABLE Folders (FolderId TEXT PRIMARY KEY, Json TEXT NOT NULL);
                                     """;
                create.ExecuteNonQuery();

                foreach (var note in notes)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO Notes (NoteId, Json) VALUES ($id, $json)";
                    cmd.Parameters.AddWithValue("$id", note.NoteId);
                    cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(note));
                    cmd.ExecuteNonQuery();
                }

                foreach (var folder in folders)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO Folders (FolderId, Json) VALUES ($id, $json)";
                    cmd.Parameters.AddWithValue("$id", folder.FolderId);
                    cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(folder));
                    cmd.ExecuteNonQuery();
                }
            }

            SqliteConnection.ClearAllPools();
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    private sealed class InMemoryNoteService : INoteService
    {
        private readonly Dictionary<string, Note> _notes = new(StringComparer.Ordinal);

        public Task<IEnumerable<Note>> GetAllNotesAsync() => Task.FromResult<IEnumerable<Note>>(_notes.Values.ToArray());

        public Task<Note?> GetNoteAsync(string noteId)
            => Task.FromResult(_notes.TryGetValue(noteId, out var note) ? note : null);

        public Task<Result> SaveNoteAsync(Note note)
        {
            _notes[note.NoteId] = note;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteNoteAsync(string noteId)
        {
            _notes.Remove(noteId);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class InMemoryFolderService : INoteFolderService
    {
        private readonly Dictionary<string, NoteFolder> _folders = new(StringComparer.Ordinal);

        public Task<IEnumerable<NoteFolder>> GetAllFoldersAsync()
            => Task.FromResult<IEnumerable<NoteFolder>>(_folders.Values.ToArray());

        public Task<NoteFolder?> GetFolderAsync(string folderId)
            => Task.FromResult(_folders.TryGetValue(folderId, out var folder) ? folder : null);

        public Task<Result> SaveFolderAsync(NoteFolder folder)
        {
            _folders[folder.FolderId] = folder;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteFolderAsync(string folderId)
        {
            _folders.Remove(folderId);
            return Task.FromResult(Result.Success());
        }
    }
}
