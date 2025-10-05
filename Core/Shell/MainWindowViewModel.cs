using System.Windows.Input;
using System.Collections.Generic;
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
using MnemoApp.UI.Components.Overlays;
using MnemoApp.Core.Models;
using System;
using System.Threading.Tasks;

namespace MnemoApp.Core.Shell
{
    
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ViewModelBase? CurrentPage => _mnemoAPI?.navigate?.CurrentViewModel;
        public SidebarViewModel? SidebarViewModel { get; }
        public UIApi? ui => _mnemoAPI?.ui;
        
        // Expose services for binding
        public IToastService? ToastService => _mnemoAPI?.ui?.toast.Service;
        public IOverlayService? OverlayService => _mnemoAPI?.ui?.overlay.Service;

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
            ToggleSidebarCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleSidebar);
            ShowNotificationsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            MinimizeCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            MaximizeCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            CloseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            OptionsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            
        }
        
        public MainWindowViewModel(IMnemoAPI mnemoAPI, SidebarViewModel sidebarViewModel)
        {
            _mnemoAPI = mnemoAPI;
            SidebarViewModel = sidebarViewModel;
            _mnemoAPI.navigate.ViewModelChanged += OnViewModelChanged;
            ToggleSidebarCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleSidebar);

            // Initialize Topbar Commands for runtime
            ShowNotificationsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ShowNotifications);
            MinimizeCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(Minimize);
            MaximizeCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(Maximize);
            
            
            CloseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(Close);
            OptionsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(Options);
            
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
            // Create a simple dropdown without anchor control for now
            // In a real implementation, you'd get the button position from the topbar
            var items = new List<DropdownItemBase>
            {
                new DropdownHeader { Text = "Notifications" },
                new DropdownOption { Text = "Notification 1", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }) },
                new DropdownOption { Text = "Notification 2", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }) },
                new DropdownOption { Text = "Notification 3", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }) }
            };

            var dropdown = new UI.Components.Overlays.DropdownOverlay();
            dropdown.SetItems(items);

            var options = new OverlayOptions
            {
                ShowBackdrop = true,
                BackdropOpacity = 0.0, // Invisible backdrop for click detection
                CloseOnOutsideClick = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 50, 20, 0) // Position in top-right area
            };

            _mnemoAPI?.ui.overlay.CreateOverlay(dropdown, options, "NotificationsDropdown");
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
        private void Options() 
        { 
            var items = new List<DropdownItemBase>
            {
                new DropdownOption { Icon="avares://MnemoApp/UI/Icons/Tabler/outline/clipboard-text.svg", Text = "Documentation", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { /* TODO: open settings */ }) },
                new DropdownOption { Icon="avares://MnemoApp/UI/Icons/Tabler/outline/progress-down.svg", Text = "Check Update", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { /* TODO: open preferences */ }) },
                new DropdownOption { Icon="avares://MnemoApp/UI/Icons/Tabler/outline/help-square.svg", Text = "Get Help", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { /* TODO: show about */ }) }
            };

            var dropdown = new UI.Components.Overlays.DropdownOverlay();
            dropdown.SetItems(items);

            var options = new OverlayOptions
            {
                ShowBackdrop = true,
                BackdropOpacity = 0.0,
                CloseOnOutsideClick = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 50, 20, 0)
            };

            _mnemoAPI?.ui.overlay.CreateOverlay(dropdown, options, "OptionsDropdown");
        }

        
    }
}
