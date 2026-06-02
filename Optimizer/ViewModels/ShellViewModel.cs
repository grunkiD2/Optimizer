using System.Windows.Input;

using Optimizer.Contracts.Services;
using Optimizer.Helpers;
using Optimizer.Properties;

using Syncfusion.Windows.Shared;

namespace Optimizer.ViewModels
{
    public class ShellViewModel : Observable
    {
		private readonly INavigationService _navigationService;
        private readonly IRightPaneService _rightPaneService;
        private ICommand _loadedCommand;
        private ICommand _unloadedCommand;
		private ICommand _viewCommand;

        public ICommand LoadedCommand => _loadedCommand ?? (_loadedCommand = new RelayCommand(OnLoaded));

        public ICommand UnloadedCommand => _unloadedCommand ?? (_unloadedCommand = new RelayCommand(OnUnloaded));

        public ShellViewModel(INavigationService navigationService, IRightPaneService rightPaneService)
        {
			_navigationService = navigationService;
            _rightPaneService = rightPaneService;
        }

        private void OnLoaded()
        {
        }

        private void OnUnloaded()
        {
            _rightPaneService.CleanUp();
        }
		
		public ICommand ViewSelectionChangedCommand => _viewCommand ?? (_viewCommand = new Helpers.DelegateCommand(OnViewSelected));
		
		 private void OnViewSelected(object viewName)
        {
            string header = viewName.ToString();
			if(header == null)
            {
                //_navigationService.NavigateTo(typeof(ItemNameViewModel).FullName, null, true);
            }
            //_navigationService.NavigateTo(typeof(ItemNameViewModel).FullName, null, true);
			else if(header == Resources.ShellMenuItemViewsMenuAdvPageHeader)
					_navigationService.NavigateTo(typeof(MenuAdvViewModel).FullName, null, true);
        

			else if(header == Resources.ShellMenuItemViewsMainPageHeader)
					_navigationService.NavigateTo(typeof(MainViewModel).FullName, null, true);
        
		}
    }
}
