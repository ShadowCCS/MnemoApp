using System.Text.Json;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services;
using Mnemo.Infrastructure.Services.Spellcheck;

namespace Mnemo.Infrastructure.Tests;

public sealed class SpellcheckServicesTests
{
    [Fact]
    public async Task UserSpellbookService_PersistsWords_CaseInsensitive()
    {
        var settings = new SettingsService(new InMemoryStorageProvider());
        var service = new UserSpellbookService(settings);

        await service.AddWordAsync("en-US", "MnemoWord", CancellationToken.None);
        await service.AddWordAsync("en-us", "mnemoword", CancellationToken.None);

        var words = await service.GetWordsAsync("en-US", CancellationToken.None);
        Assert.Single(words);
        Assert.Contains("mnemoword", words, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpellDictionaryCatalogService_ResolvesPrimaryFallback()
    {
        var dictionaryRoot = Path.Combine(AppContext.BaseDirectory, "Dictionaries", "Spellcheck");
        Directory.CreateDirectory(dictionaryRoot);
        var affPath = Path.Combine(dictionaryRoot, "en.aff");
        var dicPath = Path.Combine(dictionaryRoot, "en.dic");
        File.WriteAllText(affPath, "SET UTF-8");
        File.WriteAllText(dicPath, "1\ntest");

        try
        {
            var service = new SpellDictionaryCatalogService();
            var resolved = service.TryResolve("en-US", out var resolvedAff, out var resolvedDic);

            Assert.True(resolved);
            Assert.Equal(affPath, resolvedAff);
            Assert.Equal(dicPath, resolvedDic);
        }
        finally
        {
            File.Delete(affPath);
            File.Delete(dicPath);
        }
    }

    private sealed class InMemoryStorageProvider : IStorageProvider
    {
        private readonly Dictionary<string, string> _storage = new(StringComparer.Ordinal);

        public Task<Result> SaveAsync<T>(string key, T data)
        {
            _storage[key] = JsonSerializer.Serialize(data);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<T?>> LoadAsync<T>(string key)
        {
            if (!_storage.TryGetValue(key, out var value))
                return Task.FromResult(Result<T?>.Failure("not found"));

            return Task.FromResult(Result<T?>.Success(JsonSerializer.Deserialize<T>(value)));
        }

        public Task<Result> DeleteAsync(string key)
        {
            _storage.Remove(key);
            return Task.FromResult(Result.Success());
        }
    }
}
