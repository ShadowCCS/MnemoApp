using System;
using Avalonia;
using ExCSS;
using MnemoApp.Core.Common;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Services;
using System.Threading.Tasks;

namespace MnemoApp.Modules.Dashboard;

public class DashboardViewModel : ViewModelBase
{
    private readonly IMnemoAPI? _mnemoAPI;
    public DashboardViewModel(IMnemoAPI mnemoAPI)
    {
        _mnemoAPI = mnemoAPI;
    }
}
