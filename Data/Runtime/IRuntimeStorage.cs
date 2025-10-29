using System;
using System.Collections.Generic;

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
        
        /// <summary>
        /// List all content items of a specific type.
        /// </summary>
        IEnumerable<ContentItem<T>> ListContent<T>(string contentType);
    }
    
    /// <summary>
    /// Represents a content item with metadata.
    /// </summary>
    public class ContentItem<T>
    {
        public string ContentId { get; set; } = string.Empty;
        public T Data { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}


