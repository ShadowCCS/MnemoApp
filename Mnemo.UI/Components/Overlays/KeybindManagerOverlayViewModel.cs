using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Keybinds;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components.Overlays;

public partial class KeybindManagerOverlayViewModel : ViewModelBase
{
    private readonly IKeyMap _keyMap;

    public ObservableCollection<KeybindActionRowVm> Actions { get; } = new();
    public ObservableCollection<KeybindConflictRowVm> Conflicts { get; } = new();

    public KeybindManagerOverlayViewModel(IKeyMap keyMap)
    {
        _keyMap = keyMap;
        Refresh();
    }

    [RelayCommand]
    private async Task ResetAllAsync()
    {
        await _keyMap.ResetAllOverridesAsync().ConfigureAwait(true);
        Refresh();
    }

    public void Refresh()
    {
        Actions.Clear();
        foreach (var def in _keyMap.GetStaticArmedDefinitions().OrderBy(d => d.Namespace).ThenBy(d => d.ActionId))
        {
            var bindStr = string.Join("; ", def.Bindings.Select(FormatBinding));
            Actions.Add(new KeybindActionRowVm(def.ActionId, def.Namespace, def.Scope.ToString(), bindStr, def.Enabled));
        }

        Conflicts.Clear();
        foreach (var c in _keyMap.CheckConflictsStaticArmed())
        {
            Conflicts.Add(new KeybindConflictRowVm(c.Severity.ToString(), c.Message, c.ActionIdA, c.ActionIdB));
        }
    }

    private static string FormatBinding(KeybindBindingEntry b)
    {
        if (b.Kind == KeybindBindingKind.Chord && b.Chord is { } ch)
            return CanonicalKeyGestureCodec.ToCanonicalString(ch);
        if (b.Kind == KeybindBindingKind.Sequence && b.SequenceSteps is { Count: > 0 } steps)
            return string.Join(" → ", steps.Select(CanonicalKeyGestureCodec.ToCanonicalString));
        return "?";
    }
}

public sealed record KeybindActionRowVm(string ActionId, string Namespace, string Scope, string Bindings, bool Enabled);

public sealed record KeybindConflictRowVm(string Severity, string Message, string? ActionIdA, string? ActionIdB);
