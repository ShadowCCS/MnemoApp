using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MnemoApp.Core.Services.FileProcessing
{
    /// <summary>
    /// Registry for managing file processors
    /// </summary>
    public class FileProcessorRegistry
    {
        private readonly List<IFileProcessor> _processors = new();
        private readonly object _lock = new();

        public FileProcessorRegistry()
        {
            // Built-in processors will be registered by the service
        }

        /// <summary>
        /// Register a file processor
        /// </summary>
        public void RegisterProcessor(IFileProcessor processor)
        {
            if (processor == null)
                throw new ArgumentNullException(nameof(processor));

            lock (_lock)
            {
                // Remove existing processor with same name if it exists
                _processors.RemoveAll(p => p.ProcessorName == processor.ProcessorName);
                _processors.Add(processor);
            }
        }

        /// <summary>
        /// Get the appropriate processor for a file
        /// </summary>
        public IFileProcessor? GetProcessorForFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
                return null;

            lock (_lock)
            {
                return _processors.FirstOrDefault(p => p.CanHandle(extension));
            }
        }

        /// <summary>
        /// Get all registered processor names
        /// </summary>
        public IEnumerable<string> GetRegisteredProcessorNames()
        {
            lock (_lock)
            {
                return _processors.Select(p => p.ProcessorName).ToList();
            }
        }

        /// <summary>
        /// Get all supported extensions
        /// </summary>
        public IEnumerable<string> GetSupportedExtensions()
        {
            lock (_lock)
            {
                return _processors
                    .SelectMany(p => p.SupportedExtensions)
                    .Distinct()
                    .OrderBy(e => e)
                    .ToList();
            }
        }

        /// <summary>
        /// Check if an extension is supported
        /// </summary>
        public bool IsExtensionSupported(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            var ext = extension.ToLowerInvariant();
            if (!ext.StartsWith("."))
                ext = "." + ext;

            lock (_lock)
            {
                return _processors.Any(p => p.CanHandle(ext));
            }
        }
    }
}

