using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemo.UI.Modules.LaTeXTest.ViewModels;

public partial class LaTeXSampleItem : ObservableObject
{
    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private Control? _rendered;
}
