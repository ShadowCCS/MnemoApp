using System;
using System.Collections.Generic;
using System.Text.Json;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Core.Extensions.Security
{
    /// <summary>
    /// Stores user trust decisions for extensions
    /// </summary>
    public class TrustStore
    {
        private readonly IRuntimeStorage _storage;
        private const string StorageKey = "extensions:trust";

        public TrustStore(IRuntimeStorage storage)
        {
            _storage = storage;
        }

        /// <summary>
        /// Get trust level for an extension
        /// </summary>
        public ExtensionTrustLevel GetTrustLevel(string extensionName)
        {
            var trustData = LoadTrustData();
            
            if (trustData.TryGetValue(extensionName, out var entry))
            {
                return entry.TrustLevel;
            }

            return ExtensionTrustLevel.Untrusted;
        }

        /// <summary>
        /// Set trust level for an extension
        /// </summary>
        public void SetTrustLevel(string extensionName, ExtensionTrustLevel trustLevel)
        {
            var trustData = LoadTrustData();
            
            if (trustData.ContainsKey(extensionName))
            {
                trustData[extensionName].TrustLevel = trustLevel;
                trustData[extensionName].LastModified = DateTime.UtcNow;
            }
            else
            {
                trustData[extensionName] = new TrustEntry
                {
                    ExtensionName = extensionName,
                    TrustLevel = trustLevel,
                    LastModified = DateTime.UtcNow
                };
            }

            SaveTrustData(trustData);
        }

        /// <summary>
        /// Get granted permissions for an extension
        /// </summary>
        public ExtensionPermission GetGrantedPermissions(string extensionName)
        {
            var trustData = LoadTrustData();
            
            if (trustData.TryGetValue(extensionName, out var entry))
            {
                return entry.GrantedPermissions;
            }

            return ExtensionPermission.None;
        }

        /// <summary>
        /// Set granted permissions for an extension
        /// </summary>
        public void SetGrantedPermissions(string extensionName, ExtensionPermission permissions)
        {
            var trustData = LoadTrustData();
            
            if (trustData.ContainsKey(extensionName))
            {
                trustData[extensionName].GrantedPermissions = permissions;
                trustData[extensionName].LastModified = DateTime.UtcNow;
            }
            else
            {
                trustData[extensionName] = new TrustEntry
                {
                    ExtensionName = extensionName,
                    GrantedPermissions = permissions,
                    LastModified = DateTime.UtcNow
                };
            }

            SaveTrustData(trustData);
        }

        /// <summary>
        /// Remove trust entry for an extension
        /// </summary>
        public void RemoveTrustEntry(string extensionName)
        {
            var trustData = LoadTrustData();
            trustData.Remove(extensionName);
            SaveTrustData(trustData);
        }

        /// <summary>
        /// Check if user has made a trust decision
        /// </summary>
        public bool HasTrustDecision(string extensionName)
        {
            var trustData = LoadTrustData();
            return trustData.ContainsKey(extensionName);
        }

        private Dictionary<string, TrustEntry> LoadTrustData()
        {
            try
            {
                var json = _storage.GetProperty<string>(StorageKey);
                if (string.IsNullOrEmpty(json))
                {
                    return new Dictionary<string, TrustEntry>();
                }

                var entries = JsonSerializer.Deserialize<List<TrustEntry>>(json);
                if (entries == null)
                {
                    return new Dictionary<string, TrustEntry>();
                }

                var dict = new Dictionary<string, TrustEntry>();
                foreach (var entry in entries)
                {
                    dict[entry.ExtensionName] = entry;
                }

                return dict;
            }
            catch
            {
                return new Dictionary<string, TrustEntry>();
            }
        }

        private void SaveTrustData(Dictionary<string, TrustEntry> trustData)
        {
            try
            {
                var entries = new List<TrustEntry>(trustData.Values);
                var json = JsonSerializer.Serialize(entries);
                _storage.SetProperty(StorageKey, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TRUST_STORE] Failed to save trust data: {ex.Message}");
            }
        }

        private class TrustEntry
        {
            public string ExtensionName { get; set; } = string.Empty;
            public ExtensionTrustLevel TrustLevel { get; set; }
            public ExtensionPermission GrantedPermissions { get; set; }
            public DateTime LastModified { get; set; }
        }
    }
}

