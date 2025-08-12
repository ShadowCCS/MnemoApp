using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace MnemoApp.Data.Packaged
{
    public static class PackagedStorage
    {
        private static JsonDocument LoadManifest(string packagePath, out ZipArchive archive, out ZipArchiveEntry entry)
        {
            var fs = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            archive = new ZipArchive(fs, ZipArchiveMode.Update);
            entry = archive.GetEntry("mnemo.json") ?? throw new InvalidOperationException("mnemo.json not found in package");
            using var manifestStream = entry.Open();
            using var ms = new MemoryStream();
            manifestStream.CopyTo(ms);
            ms.Position = 0;
            return JsonDocument.Parse(ms.ToArray());
        }

        private static void SaveManifest(ZipArchive archive, ZipArchiveEntry entry, JsonElement root)
        {
            entry.Delete();
            var newEntry = archive.CreateEntry("mnemo.json", CompressionLevel.NoCompression);
            using var stream = newEntry.Open();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            root.WriteTo(writer);
        }

        public static T? GetProperty<T>(string packagePath, string key)
        {
            using var fs = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = archive.GetEntry("mnemo.json") ?? throw new InvalidOperationException("mnemo.json not found in package");
            using var manifestStream = entry.Open();
            using var doc = JsonDocument.Parse(manifestStream);
            if (!TryResolvePropertyElement(doc.RootElement, key, out var element))
            {
                return default;
            }
            return element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null
                ? default
                : JsonSerializer.Deserialize<T>(element.GetRawText());
        }

        public static bool HasProperty(string packagePath, string key)
        {
            using var fs = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = archive.GetEntry("mnemo.json") ?? throw new InvalidOperationException("mnemo.json not found in package");
            using var manifestStream = entry.Open();
            using var doc = JsonDocument.Parse(manifestStream);
            return TryResolvePropertyElement(doc.RootElement, key, out _);
        }

        public static void SetProperty<T>(string packagePath, string key, T value)
        {
            using var doc = LoadManifest(packagePath, out var archive, out var entry);
            try
            {
                var root = SetPropertyOnRoot(doc.RootElement, key, value);
                SaveManifest(archive, entry, root);
            }
            finally
            {
                archive.Dispose();
            }
        }

        public static void RemoveProperty(string packagePath, string key)
        {
            using var doc = LoadManifest(packagePath, out var archive, out var entry);
            try
            {
                var root = RemovePropertyFromRoot(doc.RootElement, key);
                SaveManifest(archive, entry, root);
            }
            finally
            {
                archive.Dispose();
            }
        }

        private static bool TryResolvePropertyElement(JsonElement root, string key, out JsonElement element)
        {
            if (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                if (props.TryGetProperty(key, out element))
                {
                    return true;
                }
            }
            element = default;
            return false;
        }

        private static JsonElement SetPropertyOnRoot<T>(JsonElement root, string key, T value)
        {
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (!string.Equals(prop.Name, "properties", StringComparison.OrdinalIgnoreCase))
                {
                    prop.WriteTo(writer);
                }
            }

            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            if (root.TryGetProperty("properties", out var existing) && existing.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in existing.EnumerateObject())
                {
                    if (string.Equals(prop.Name, key, StringComparison.Ordinal))
                        continue;
                    prop.WriteTo(writer);
                }
            }

            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, value);
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.Flush();
            ms.Position = 0;
            using var newDoc = JsonDocument.Parse(ms.ToArray());
            return newDoc.RootElement.Clone();
        }

        private static JsonElement RemovePropertyFromRoot(JsonElement root, string key)
        {
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (!string.Equals(prop.Name, "properties", StringComparison.OrdinalIgnoreCase))
                {
                    prop.WriteTo(writer);
                }
            }

            if (root.TryGetProperty("properties", out var existing) && existing.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                foreach (var prop in existing.EnumerateObject())
                {
                    if (string.Equals(prop.Name, key, StringComparison.Ordinal))
                        continue;
                    prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.Flush();
            ms.Position = 0;
            using var newDoc = JsonDocument.Parse(ms.ToArray());
            return newDoc.RootElement.Clone();
        }
    }
}


