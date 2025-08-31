using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.Navigation;
using MnemoApp.Core.MnemoAPI;
using Avalonia;
using Avalonia.Media;
using MnemoApp.UI.Components.Sidebar;
using MnemoApp.Modules.Dashboard;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Services;
using System;
using System.Threading.Tasks;

namespace MnemoApp.Core.Shell
{
    
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ViewModelBase? CurrentPage => _mnemoAPI?.navigate?.CurrentViewModel;
        public SidebarViewModel? SidebarViewModel { get; }
        public UIApi? ui => _mnemoAPI?.ui;

        private readonly IMnemoAPI? _mnemoAPI;

        public ICommand ToggleSidebarCommand { get; }

        //Topbar Commands
        public ICommand ShowNotificationsCommand { get; }
        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand OptionsCommand { get; }

        // Parameterless constructor for design-time
        public MainWindowViewModel()
        {
            _mnemoAPI = null;
            SidebarViewModel = null;
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            ShowNotificationsCommand = new RelayCommand(() => { });
            MinimizeCommand = new RelayCommand(() => { });
            MaximizeCommand = new RelayCommand(() => { });
            CloseCommand = new RelayCommand(() => { });
            OptionsCommand = new RelayCommand(() => { });
            
        }
        
        public MainWindowViewModel(IMnemoAPI mnemoAPI, SidebarViewModel sidebarViewModel)
        {
            _mnemoAPI = mnemoAPI;
            SidebarViewModel = sidebarViewModel;
            _mnemoAPI.navigate.ViewModelChanged += OnViewModelChanged;
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);

            // Initialize Topbar Commands for runtime
            ShowNotificationsCommand = new RelayCommand(ShowNotifications);
            MinimizeCommand = new RelayCommand(Minimize);
            MaximizeCommand = new RelayCommand(Maximize);
            
            
            CloseCommand = new RelayCommand(Close);
            OptionsCommand = new RelayCommand(Options);
            
            // Navigate to default page (Dashboard)
            NavigateToDefaultPage();

            // Populate topbar
            SetupTopbar();
            // Show welcome toast - TODO: only show first launch
            _mnemoAPI.ui.toast.show(_mnemoAPI.ui.language.get("System", "Title"), _mnemoAPI.ui.language.get("System", "Welcome"), ToastType.Info, TimeSpan.FromSeconds(10), dismissable: true);

        }

        private void NavigateToDefaultPage()
        {
            if (_mnemoAPI?.navigate != null)
            {
                _mnemoAPI.navigate.Navigate<DashboardViewModel>();
            }
        }

        private void OnViewModelChanged(ViewModelBase vm)
        {
            OnPropertyChanged(nameof(CurrentPage));
        }

        public void ToggleSidebar()
        {
            if (SidebarViewModel != null)
                SidebarViewModel.IsSidebarCollapsed = !SidebarViewModel.IsSidebarCollapsed;
        }

        private void SetupTopbar()
        {
            if (_mnemoAPI?.ui == null)
                return;

            IBrush? iconsBrush = null;
            if (Application.Current?.Resources.TryGetResource("IconsTopbarBrush", null, out var brushObj) == true)
            {
                iconsBrush = brushObj as IBrush;
            }

            _mnemoAPI.ui.topbar.addButton(
                iconPath: "avares://MnemoApp/UI/Icons/Tabler/outline/bell.svg",
                notification: true,
                order: 20,
                command: ShowNotificationsCommand,
                toolTip: "Notifications"
            );
            
            _mnemoAPI.ui.topbar.addSeparator(order: 30, height: 16, thickness: 2);

            _mnemoAPI.ui.topbar.addButton(
                iconPath: "avares://MnemoApp/UI/Icons/Tabler/outline/square-rounded-minus.svg",
                notification: false,
                order: 30,
                command: MinimizeCommand,
                toolTip: "Minimize Application"
            );

            _mnemoAPI.ui.topbar.addButton(
                iconPath: "avares://MnemoApp/UI/Icons/Tabler/outline/maximize.svg",
                notification: false,
                order: 50,
                command: MaximizeCommand,
                toolTip: "Maximize Application"
            );

            _mnemoAPI.ui.topbar.addButton(
                iconPath: "avares://MnemoApp/UI/Icons/Tabler/outline/square-rounded-x.svg",
                notification: false,
                order: 60,
                command: CloseCommand,
                toolTip: "Close Application"
            );

            _mnemoAPI.ui.topbar.addButton(
                iconPath: "avares://MnemoApp/UI/Icons/Tabler/outline/dots-circle-horizontal.svg",
                notification: false,
                order: 60,
                command: OptionsCommand,
                toolTip: "Options"
            );
        }

        private void ShowNotifications()
        {
            //TODO: Implement
        }
        
        private void Minimize() { _mnemoAPI?.system.minimize(); }
        private void Maximize() { _mnemoAPI?.system.maximize(); }
        private async void Close() {  
            var choice = await _mnemoAPI!.ui.overlay.CreateDialog(
            "Close Application",
            "Are you sure you want to close the application?",
            "Cancel",
            "Close",
            new OverlayOptions { ShowBackdrop = true });

            if (choice == "Close") {
                _mnemoAPI!.system.exit();
            }
            else if (choice == "Cancel") {
                return;
            }
        }
        private void Options() { /* TODO: open options */ }

        
    }
}
