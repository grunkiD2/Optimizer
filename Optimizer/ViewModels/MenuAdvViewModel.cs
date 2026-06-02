using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using Optimizer.Contracts.Services;
using Optimizer.Helpers;
using Optimizer.Models;

using Syncfusion.Windows.Shared;

namespace Optimizer.ViewModels
{
    public class MenuAdvViewModel : Observable
    {
        /// <summary>
        /// Maintains a collection of string for displaying events.
        /// </summary>
        private ObservableCollection<string> eventlog = new ObservableCollection<string>();
        /// <summary>
        /// Maintains a collection of model class.
        /// </summary>
        private ObservableCollection<MenuAdvModel> menuModel;

        /// <summary>
        /// Maintains the command for menu items.
        /// </summary>
        private ICommand itemCommand;

        /// <summary>
        /// Maintains the command for help.
        /// </summary>
        private ICommand helpCommand;

        /// <summary>
        /// Maintains the command for about.
        /// </summary>
        private ICommand aboutCommand;

        /// <summary>
        /// Maintains the command for essential studio.
        /// </summary>
        private ICommand essentialStudioCommand;

        /// <summary>
        /// Holds the required resouces for Icon.
        /// </summary>
        private ResourceDictionary CommonResourceDictionary { get; set; }

        /// <summary>
        /// Initializes  the instance of the <see cref="MenuAdvViewModel"/>class.
        /// </summary>
        public MenuAdvViewModel()
        {
            menuModel = new ObservableCollection<MenuAdvModel>();
            itemCommand = new Helpers.DelegateCommand<object>(PropertyChangedHandler);
            helpCommand = new Helpers.DelegateCommand<object>(ExecuteHelpCommand);
            aboutCommand = new Helpers.DelegateCommand<object>(ExecuteAboutCommand);
            CommonResourceDictionary = new ResourceDictionary() { Source = new Uri("/Assets/Menu/Icon.xaml", UriKind.RelativeOrAbsolute) };
           essentialStudioCommand = new Helpers.DelegateCommand<object>(ExecuteEssentialStudioCommand);

            MenuAdvModel menuModel0 = new MenuAdvModel() { MenuItemName = "File" };
            menuModel0.MenuItems.Add(new MenuAdvModel() { MenuItemName = "New", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["New"] as object });
            menuModel0.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Open", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Open"] as object });
            menuModel0.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Close", MenuItemClicked = ItemCommand });
            menuModel0.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Save", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Save"] as object });
            menuModel0.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Advanced Save Options", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["AdvSave"] as object });
            menuModel0.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Print", GestureText = "Ctrl + P", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Print"] as object });
            menuModel0.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Exit", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Delete"] as object });

            MenuAdvModel menuModel1 = new MenuAdvModel() { MenuItemName = "Edit" };
            menuModel1.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Undo", GestureText = "Ctrl + Z", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Undo"] as object });
            menuModel1.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Redo", GestureText = "Ctrl + Y", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Redo"] as object });
            menuModel1.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Cut", GestureText = "Ctrl + X", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Cut"] as object });
            menuModel1.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Copy", GestureText = "Ctrl + C", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Copy"] as object });
            menuModel1.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Paste", GestureText = "Ctrl + V", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Paste"] as object });
            menuModel1.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Check Spelling", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["SpellChecker"] as object });

            MenuAdvModel menuModel2 = new MenuAdvModel() { MenuItemName = "View" };
            MenuAdvModel menuModel21 = new MenuAdvModel() { MenuItemName = "Find Results", MenuItemClicked = ItemCommand };
            menuModel21.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Find Results 1", CheckIconType = CheckIconType.RadioButton, IsCheckable = true, MenuItemClicked = ItemCommand });
            menuModel21.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Find Results 2", IsCheckable = true, MenuItemClicked = ItemCommand });
            menuModel21.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Find Symbol Results", IsCheckable = true, MenuItemClicked = ItemCommand });

            MenuAdvModel menuModel22 = new MenuAdvModel() { MenuItemName = "Other Windows", MenuItemClicked = ItemCommand };
            menuModel22.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Immediate Window", IsCheckable = true, MenuItemClicked = ItemCommand });
            menuModel22.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Output", IsCheckable = true, MenuItemClicked = ItemCommand });
            menuModel22.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Command Window", IsCheckable = true, MenuItemClicked = ItemCommand });
            menuModel22.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Macro Explorer", IsCheckable = true, MenuItemClicked = ItemCommand });
            menuModel22.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Solution Explorer", IsCheckable = true, MenuItemClicked = ItemCommand });
            menuModel2.MenuItems.Add(menuModel21);
            menuModel2.MenuItems.Add(menuModel22);

            MenuAdvModel menuModel4 = new MenuAdvModel() { MenuItemName = "Project" };
            menuModel4.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Add Window..", MenuItemClicked = ItemCommand });
            menuModel4.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Add User Control..", MenuItemClicked = ItemCommand });
            menuModel4.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Add Resource Dictionary..", MenuItemClicked = ItemCommand });
            menuModel4.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Add Page..", MenuItemClicked = ItemCommand });
            menuModel4.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Add Class..", MenuItemClicked = ItemCommand });

            MenuAdvModel menuModel5 = new MenuAdvModel() { MenuItemName = "Build" };
            menuModel5.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Build Solution", GestureText = "F6", MenuItemClicked = ItemCommand });
            menuModel5.MenuItems.Add(new MenuAdvModel() { MenuItemName = "ReBuild Solution", MenuItemClicked = ItemCommand });
            menuModel5.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Clean Solution", MenuItemClicked = ItemCommand });
            menuModel5.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Batch Build...", MenuItemClicked = ItemCommand });
            menuModel5.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Configuration Manager...", MenuItemClicked = ItemCommand });

            MenuAdvModel menuModel6 = new MenuAdvModel() { MenuItemName = "Debug" };
            MenuAdvModel menuModel23 = new MenuAdvModel() { MenuItemName = "Windows", MenuItemClicked = ItemCommand };
            menuModel23.MenuItems.Add(new MenuAdvModel() { MenuItemName = "BreakPoints", MenuItemClicked = ItemCommand });
            menuModel23.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Output", MenuItemClicked = ItemCommand });
            menuModel23.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Immediate", MenuItemClicked = ItemCommand });
            menuModel6.MenuItems.Add(menuModel23);

            menuModel6.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Start Debugging", GestureText = "F6", MenuItemClicked = ItemCommand, ImagePath = CommonResourceDictionary["Start"] as object });
            menuModel6.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Start Without Debugging", MenuItemClicked = ItemCommand });
            menuModel6.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Attach to Process...", MenuItemClicked = ItemCommand });
            menuModel6.MenuItems.Add(new MenuAdvModel() { MenuItemName = "Excpetions", MenuItemClicked = ItemCommand });

            MenuAdvModel menuModel7 = new MenuAdvModel() { MenuItemName = "Help" };
            menuModel7.MenuItems.Add(new MenuAdvModel() { MenuItemName = "About", MenuItemClicked = AboutCommand });
            menuModel7.MenuItems.Add(new MenuAdvModel() { MenuItemName = "View Help", GestureText = "F6", MenuItemClicked = HelpCommand, ImagePath = CommonResourceDictionary["Help"] as object });
            //menuModel7.MenuItems.Add(new MenuAdvModel() { MenuItemName = "About Syncfusion Essential Studio", MenuItemClicked = EssentialStudioCommand, ImagePath = new Image() { Source = new BitmapImage(new Uri("/syncfusion.navigationdemos.wpf;component/Assets/Menu/syncfusion.png", UriKind.RelativeOrAbsolute)) } });

            menuModel.Add(menuModel0);
            menuModel.Add(menuModel1);
            menuModel.Add(menuModel2);
            menuModel.Add(menuModel4);
            menuModel.Add(menuModel5);
            menuModel.Add(menuModel6);
            MenuModel.Add(menuModel7);
        }

        /// <summary>
        /// Gets or sets the command for help  <see cref="ViewModel"/> class.
        /// </summary>
        public ICommand HelpCommand
        {
            get
            {
                return helpCommand;
            }
        }

        /// <summary>
        /// Gets or sets the command for essential studio  <see cref="MenuAdvModel"/> class.
        /// </summary>
        public ICommand EssentialStudioCommand
        {
            get
            {
                return essentialStudioCommand;
            }
        }

        /// <summary>
        /// Gets or sets the command for about <see cref="MenuAdvModel"/> class.
        /// </summary>
        public ICommand AboutCommand
        {
            get
            {
                return aboutCommand;
            }
        }

        /// <summary>
        /// Gets or seta the command for menu items <see cref="MenuAdvModel"/>class.
        /// </summary>
        public ICommand ItemCommand
        {
            get
            {
                return itemCommand;
            }
        }

        /// <summary>
        /// Gets or sets the collection of string to display events  <see cref="MenuAdvModel"/>class.
        /// </summary>
        public ObservableCollection<string> EventLog
        {
            get
            {
                return eventlog;
            }
            set
            {
                eventlog = value;
                RaisePropertyChanged("EventLog");
            }
        }

        /// <summary>
        /// Gets or sets the collection of <see cref="MenuAdvModel"/>class.
        /// </summary>
        public ObservableCollection<MenuAdvModel> MenuModel
        {
            get
            {
                return menuModel;
            }
            set
            {
                menuModel = value;
                RaisePropertyChanged("MenuModel");
            }
        }

        /// <summary>
        /// Method to execute menu item clicked.
        /// </summary>
        /// <param name="param">Specifies the menu item</param>
        public void PropertyChangedHandler(object param)
        {
            EventLog.Add("Selection Changed:" + param.ToString());
        }

        /// <summary>
        /// Method used to execute the helpCommand.
        /// </summary>
        /// <param name="parameter">Specifies the object type.</param>
        private void ExecuteHelpCommand(object parameter)
        {
            if (MessageBox.Show("Are you sure to visit the page ?", "Help", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("https://help.syncfusion.com/wpf/welcome-to-syncfusion-essential-wpf");
                info.UseShellExecute = true;
                System.Diagnostics.Process.Start(info);
            }
        }

        /// <summary>
        /// method used to execute aboutCommand
        /// </summary>
        /// <param name="parameter">Specifies the object type.</param>
        private void ExecuteAboutCommand(object parameter)
        {
            if (MessageBox.Show("Are you sure to visit the page ?", "About", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("https://www.syncfusion.com/company/about-us");
                info.UseShellExecute = true;
                System.Diagnostics.Process.Start(info);
            }
        }

        /// <summary>
        /// method used to execute essentialstudioCommand
        /// </summary>
        /// <param name="parameter">Specifies the object type.</param>
        private void ExecuteEssentialStudioCommand(object parameter)
        {
            if (MessageBox.Show("Are you sure to visit the page ?", "Essential Studio", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("https://www.syncfusion.com/products/essential-studio");
                info.UseShellExecute = true;
                System.Diagnostics.Process.Start(info);
            }
        }

        internal void Dispose()
        {
            if (MenuModel != null)
            {
                this.MenuModel.Clear();
                this.MenuModel = null;
            }

            if (eventlog != null)
            {
                eventlog.Clear();
                this.eventlog = null;
            }

            this.CommonResourceDictionary = null;
            this.itemCommand = null;
            this.aboutCommand = null;
            this.helpCommand = null;
            this.essentialStudioCommand = null;
        }
    }
}
