
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;
using MnemoApp.Core.Storage;
using MnemoApp.Data.Packaged;

namespace MnemoApp.Core.MnemoAPI
{
    public interface IMnemoAPI
    {
        INavigationService navigate { get; set; }
        ISidebarService sidebar { get; set; }
        UIApi ui { get; set; }
        SystemApi system { get; set; }
        MnemoDataApi data { get; set; }
        MnemoApp.Data.Packaged.MnemoStorageManager storage { get; set; }
        AIApi ai { get; set; }
        TaskApi tasks { get; set; }
        FileApi files { get; set; }
        LaTeXApi latex { get; set; }
        SettingsApi settings { get; set; }
    }
}