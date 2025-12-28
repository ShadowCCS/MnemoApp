using System;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

/// <summary>
/// Defines a service for managing application-wide settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets a setting value by key.
    /// </summary>
    /// <typeparam name="T">The type of the setting value.</typeparam>
    /// <param name="key">The unique key of the setting.</param>
    /// <param name="defaultValue">The value to return if the setting is not found.</param>
    /// <returns>The setting value.</returns>
    Task<T> GetAsync<T>(string key, T defaultValue = default!);

    /// <summary>
    /// Sets a setting value by key.
    /// </summary>
    /// <typeparam name="T">The type of the setting value.</typeparam>
    /// <param name="key">The unique key of the setting.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetAsync<T>(string key, T value);

    /// <summary>
    /// Occurs when a setting value changes.
    /// </summary>
    event EventHandler<string> SettingChanged;
}

