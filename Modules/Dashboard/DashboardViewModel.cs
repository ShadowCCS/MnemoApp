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

        _mnemoAPI.ui.toast.show("System Error", "This is a system error", ToastType.Error, TimeSpan.FromSeconds(4), dismissable: true);
        _mnemoAPI.ui.toast.show("New Update Available", "V1.0.0 is available for download", ToastType.Info, TimeSpan.FromSeconds(4), dismissable: true);
        _mnemoAPI.ui.toast.show("Generating Path", "Starting to generate path", ToastType.Process, null, dismissable: true);

        var sid = _mnemoAPI.ui.toast.showStatus("Generating Path", "Reading uploaded files...", ToastType.Process, dismissable: false, 0, "0% created...");
        Task.Delay(4000).ContinueWith(_ => _mnemoAPI.ui.toast.updateStatus(sid, 60, "60% created..."));
        Task.Delay(8000).ContinueWith(_ => _mnemoAPI.ui.toast.updateStatus(sid, 100, "100% created..."));
    }
}
