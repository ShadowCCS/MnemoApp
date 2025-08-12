using System;

namespace MnemoApp.Data.Runtime
{
    /// <summary>
    /// Unified key-value storage interface for runtime data (backed by SQLite).
    /// </summary>
    public interface IRuntimeStorage
    {
        T? GetProperty<T>(string key);
        void SetProperty<T>(string key, T value);
        bool HasProperty(string key);
        void RemoveProperty(string key);
        void AddProperty<T>(string key, T value);
    }
}


