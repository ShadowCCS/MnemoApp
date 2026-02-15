using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.LaTeXTest.ViewModels;

public partial class LaTeXTestViewModel : ViewModelBase
{
    private readonly ILaTeXEngine _latexEngine;

    [ObservableProperty]
    private ObservableCollection<LaTeXSampleItem> _samples = new();

    public LaTeXTestViewModel(ILaTeXEngine latexEngine)
    {
        _latexEngine = latexEngine;
        _ = LoadSamplesAsync();
    }

    private static string[] GetSampleExpressions()
    {
        return
        [
            "x^2 + y^2 = z^2",
            "\\frac{a}{b} + \\frac{c}{d}",
            "\\sqrt{x}",
            "\\sqrt[3]{x^2 + 1}",
            "\\mathbb{N} \\subset \\mathbb{Z}",
            "e^{i\\pi} + 1 = 0",
            "\\alpha + \\beta = \\gamma",
            "\\sum_{i=1}^{n} i = \\frac{n(n+1)}{2}",
            "\\left( \\frac{1}{2} \\right)",
            "\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}",
            "x_i^2 + y_j^2",
            "\\text{Hello, world}",
        ];
    }

    private async Task LoadSamplesAsync()
    {
        var expressions = GetSampleExpressions();
        const double fontSize = 20.0;

        foreach (var source in expressions)
        {
            var item = new LaTeXSampleItem { Source = source };
            Samples.Add(item);

            var control = await _latexEngine.BuildLayoutAsync(source, fontSize).ConfigureAwait(true);
            if (control is Control c)
                item.Rendered = c;
        }
    }
}
