using Mnemo.Core.Models.Keybinds;

namespace Mnemo.Core.Services;

/// <summary>Central keybind registry: matching only; execution is the UI keybind action router.</summary>
public interface IKeyMap
{
    /// <summary>Replace built-in manifest table (from bootstrap). Does not clear overrides.</summary>
    void ReplaceManifestDefinitions(IReadOnlyList<KeybindActionDefinition> definitions);

    /// <summary>Clears previous ephemeral bindings and registers new ones for the active route.</summary>
    void ReplaceEphemeralDefinitions(IReadOnlyList<KeybindActionDefinition> definitions);

    void SetActiveRoute(string? route, string? activeNamespace);

    void PushSuppression(string scope, KeybindSuppressionPolicy? policy);
    void PopSuppression(string scope);

    /// <summary>Rich text / editor capture: while depth &gt; 0, global shortcuts are not matched (typing safety).</summary>
    void EnterTextCapture();
    void LeaveTextCapture();

    KeybindTunnelResult ProcessGlobalKeyDown(KeybindPhysicalInput input, DateTime utcNow, SequenceSwallowMode swallowMode);
    KeybindBubbleResult ProcessLocalKeyDown(KeybindPhysicalInput input, DateTime utcNow, SequenceSwallowMode swallowMode);

    void ResetSequences(KeybindSequenceScope scope);
    void OnNavigationChanged();

    /// <summary>Enabled globals + locals for active namespace + ephemeral, merged with overrides, before suppression.</summary>
    IReadOnlyList<KeybindActionDefinition> GetStaticArmedDefinitions();

    IReadOnlyList<KeybindConflict> CheckConflictsStaticArmed();

    Task ReloadOverridesAsync(CancellationToken cancellationToken = default);

    /// <summary>Apply user override document for one action (from overlay).</summary>
    Task ApplyUserOverrideAsync(string actionId, KeybindOverrideDocument? document, CancellationToken cancellationToken = default);

    Task ResetAllOverridesAsync(CancellationToken cancellationToken = default);
}
