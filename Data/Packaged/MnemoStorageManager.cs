using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace MnemoApp.Data.Packaged
{
    public class MnemoStorageManager
    {
        public record MnemoManifest(
            string Type,
            DateTimeOffset CreationDate,
            string? MnemoVersion = null,
            string? AppVersion = null
        );

        private string? _currentPackagePath;
        private MnemoManifest? _currentManifest;

        public string? CurrentPackagePath => _currentPackagePath;
        public MnemoManifest? CurrentManifest => _currentManifest;
        public bool HasActivePackage => !string.IsNullOrWhiteSpace(_currentPackagePath);

        public void Open(string packagePath)
        {
            if (!File.Exists(packagePath)) throw new FileNotFoundException("Package not found", packagePath);
            using var fs = File.OpenRead(packagePath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("mnemo.json") ?? throw new InvalidOperationException("mnemo.json not found in package");
            using var manifestStream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<MnemoManifest>(manifestStream) ?? throw new InvalidOperationException("Invalid mnemo.json");
            _currentPackagePath = packagePath;
            _currentManifest = manifest;
        }

        public void Close()
        {
            _currentPackagePath = null;
            _currentManifest = null;
        }

        public (MnemoManifest manifest, Stream contentZipStream) ReadPackage(Stream mnemoStream)
        {
            using var archive = new ZipArchive(mnemoStream, ZipArchiveMode.Read, leaveOpen: true);
            var manifestEntry = archive.GetEntry("mnemo.json") ?? throw new InvalidOperationException("mnemo.json not found in package");
            using var manifestStream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<MnemoManifest>(manifestStream) ?? throw new InvalidOperationException("Invalid mnemo.json");

            var contentEntry = archive.GetEntry("content.zip") ?? throw new InvalidOperationException("content.zip not found in package");
            var contentStream = new MemoryStream();
            using (var entryStream = contentEntry.Open())
            {
                entryStream.CopyTo(contentStream);
            }
            contentStream.Position = 0;
            return (manifest, contentStream);
        }

        public void WritePackage(Stream destination, MnemoManifest manifest, Stream contentZipStream)
        {
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            var manifestEntry = archive.CreateEntry("mnemo.json", CompressionLevel.NoCompression);
            using (var writer = new Utf8JsonWriter(manifestEntry.Open(), new JsonWriterOptions { Indented = true }))
            {
                JsonSerializer.Serialize(writer, manifest);
            }

            var contentEntry = archive.CreateEntry("content.zip", CompressionLevel.Optimal);
            using var contentEntryStream = contentEntry.Open();
            contentZipStream.CopyTo(contentEntryStream);
        }

        public void CreatePackageFromDirectory(string type, string sourceDirectory, string destinationPackagePath, string? mnemoVersion = null, string? appVersion = null)
        {
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
            var tempZip = Path.GetTempFileName();
            try
            {
                if (File.Exists(tempZip)) File.Delete(tempZip);
                ZipFile.CreateFromDirectory(sourceDirectory, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);
                using var destStream = File.Create(destinationPackagePath);
                using var contentStream = File.OpenRead(tempZip);
                var manifest = new MnemoManifest(type, DateTimeOffset.UtcNow, mnemoVersion, appVersion);
                WritePackage(destStream, manifest, contentStream);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }

        public void ExportPackageContentToDirectory(string packagePath, string destinationDirectory)
        {
            using var fs = File.OpenRead(packagePath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var contentEntry = archive.GetEntry("content.zip") ?? throw new InvalidOperationException("content.zip not found in package");
            using var contentStream = contentEntry.Open();
            var tempZip = Path.GetTempFileName();
            try
            {
                using (var f = File.Create(tempZip))
                {
                    contentStream.CopyTo(f);
                }
                if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
                ZipFile.ExtractToDirectory(tempZip, destinationDirectory, overwriteFiles: true);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }

        // Convenience wrappers when a package is active
        public T? GetProperty<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(_currentPackagePath)) throw new InvalidOperationException("No active package");
            return PackagedStorage.GetProperty<T>(_currentPackagePath, key);
        }

        public bool HasProperty(string key)
        {
            if (string.IsNullOrWhiteSpace(_currentPackagePath)) throw new InvalidOperationException("No active package");
            return PackagedStorage.HasProperty(_currentPackagePath, key);
        }

        public void SetProperty<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(_currentPackagePath)) throw new InvalidOperationException("No active package");
            PackagedStorage.SetProperty(_currentPackagePath, key, value);
        }

        public void RemoveProperty(string key)
        {
            if (string.IsNullOrWhiteSpace(_currentPackagePath)) throw new InvalidOperationException("No active package");
            PackagedStorage.RemoveProperty(_currentPackagePath, key);
        }
    }
}


