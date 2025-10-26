using MnemoApp.Core.Common;

namespace MnemoApp.Modules.Library
{
    public class LibraryViewModel : ViewModelBase
    {
        private int _selectedTabIndex = 0;

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public LibraryViewModel()
        {
            // Default to Extensions tab
            SelectedTabIndex = 0;
        }
    }
}

