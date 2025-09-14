using MnemoApp.Core.Common;
using System.Windows.Input;
using MnemoApp.Core.MnemoAPI;
using CommunityToolkit.Mvvm.Input;

namespace MnemoApp.Modules.Paths;

public class PathsViewModel : ViewModelBase
{
    private readonly IMnemoAPI _mnemoAPI;
    
        public PathsViewModel(IMnemoAPI mnemoAPI)
    {
        // This is a mock implementation for testing
        CreatePathCommand = new RelayCommand(CreatePath);
        _mnemoAPI = mnemoAPI;
    }

    public ICommand CreatePathCommand { get; }

    private void CreatePath()
    {
        // Create the overlay with dependencies
        var overlay = new Modules.Paths.Overlays.CreatePathOverlay(_mnemoAPI);
        _mnemoAPI.ui.overlay.Show<string?>(overlay, name: "CreatePathOverlay");
    }
}