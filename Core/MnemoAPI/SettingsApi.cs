using System;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// Settings and preferences management API
    /// Provides persistent key-value storage for user settings
    /// </summary>
    public class SettingsApi
    {
        private readonly IRuntimeStorage _storage;
        private const string SETTINGS_PREFIX = "mnemo.settings.";

        public SettingsApi(IRuntimeStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <summary>
        /// Get a setting value by key
        /// </summary>
        /// <typeparam name="T">Type of the setting value</typeparam>
        /// <param name="key">Setting key</param>
        /// <param name="defaultValue">Default value if setting doesn't exist</param>
        /// <returns>The setting value or default</returns>
        public T? Get<T>(string key, T? defaultValue = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            var fullKey = SETTINGS_PREFIX + key;
            
            if (!_storage.HasProperty(fullKey))
                return defaultValue;

            try
            {
                return _storage.GetProperty<T>(fullKey);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Set a setting value by key
        /// </summary>
        /// <typeparam name="T">Type of the setting value</typeparam>
        /// <param name="key">Setting key</param>
        /// <param name="value">Value to store</param>
        public void Set<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            var fullKey = SETTINGS_PREFIX + key;
            _storage.SetProperty(fullKey, value);
        }

        /// <summary>
        /// Check if a setting exists
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <returns>True if the setting exists</returns>
        public bool Has(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            var fullKey = SETTINGS_PREFIX + key;
            return _storage.HasProperty(fullKey);
        }

        /// <summary>
        /// Remove a setting by key
        /// </summary>
        /// <param name="key">Setting key</param>
        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var fullKey = SETTINGS_PREFIX + key;
            _storage.RemoveProperty(fullKey);
        }

        /// <summary>
        /// Get a string setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="defaultValue">Default value if setting doesn't exist</param>
        /// <returns>The setting value or default</returns>
        public string GetString(string key, string defaultValue = "")
        {
            return Get(key, defaultValue) ?? defaultValue;
        }

        /// <summary>
        /// Set a string setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="value">Value to store</param>
        public void SetString(string key, string value)
        {
            Set(key, value);
        }

        /// <summary>
        /// Get an integer setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="defaultValue">Default value if setting doesn't exist</param>
        /// <returns>The setting value or default</returns>
        public int GetInt(string key, int defaultValue = 0)
        {
            return Get(key, defaultValue);
        }

        /// <summary>
        /// Set an integer setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="value">Value to store</param>
        public void SetInt(string key, int value)
        {
            Set(key, value);
        }

        /// <summary>
        /// Get a boolean setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="defaultValue">Default value if setting doesn't exist</param>
        /// <returns>The setting value or default</returns>
        public bool GetBool(string key, bool defaultValue = false)
        {
            return Get(key, defaultValue);
        }

        /// <summary>
        /// Set a boolean setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="value">Value to store</param>
        public void SetBool(string key, bool value)
        {
            Set(key, value);
        }

        /// <summary>
        /// Get a double setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="defaultValue">Default value if setting doesn't exist</param>
        /// <returns>The setting value or default</returns>
        public double GetDouble(string key, double defaultValue = 0.0)
        {
            return Get(key, defaultValue);
        }

        /// <summary>
        /// Set a double setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <param name="value">Value to store</param>
        public void SetDouble(string key, double value)
        {
            Set(key, value);
        }
    }
}

