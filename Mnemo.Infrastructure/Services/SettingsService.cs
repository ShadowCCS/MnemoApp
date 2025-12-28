using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly IStorageProvider _storage;
    private readonly ConcurrentDictionary<string, object?> _cache = new();

    public event EventHandler<string>? SettingChanged;

    public SettingsService(IStorageProvider storage)
    {
        _storage = storage;
    }

    public async Task<T> GetAsync<T>(string key, T defaultValue = default!)
    {
        if (_cache.TryGetValue(key, out var cachedValue))
        {
            return cachedValue is T typedValue ? typedValue : defaultValue;
        }

        var result = await _storage.LoadAsync<T>(key).ConfigureAwait(false);
        
        if (result.IsSuccess && result.Value != null)
        {
            _cache[key] = result.Value;
            return result.Value;
        }

        return defaultValue;
    }

    public async Task SetAsync<T>(string key, T value)
    {
        _cache[key] = value;
        await _storage.SaveAsync(key, value).ConfigureAwait(false);
        SettingChanged?.Invoke(this, key);
    }
}

