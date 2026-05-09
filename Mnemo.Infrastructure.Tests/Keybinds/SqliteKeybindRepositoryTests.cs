using Microsoft.Data.Sqlite;
using Mnemo.Infrastructure.Services.Keybinds;

namespace Mnemo.Infrastructure.Tests.Keybinds;

public sealed class SqliteKeybindRepositoryTests
{
    [Fact]
    public async Task LoadOverridesAsync_SkipsInvalidJson_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mnemo_keybind_test_{Guid.NewGuid():N}.db");
        var logger = new TestLogger();
        try
        {
            var repo = new SqliteKeybindRepository(logger, path);
            _ = await repo.LoadOverridesAsync();

            await using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO keybind_overrides (action_id, value) VALUES ($id, $val)";
                cmd.Parameters.AddWithValue("$id", "bad-row");
                cmd.Parameters.AddWithValue("$val", "{ not json");
                await cmd.ExecuteNonQueryAsync();
            }

            var loaded = await repo.LoadOverridesAsync();
            Assert.False(loaded.ContainsKey("bad-row"));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }
    }
}
