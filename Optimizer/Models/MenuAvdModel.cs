using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using Syncfusion.Windows.Shared;

namespace Optimizer
{
    public class MenuAdvModel : NotificationObject
    {
        /// <summary>
        /// Maintains a collection for the menu items.
        /// </summary>
        private ObservableCollection<MenuAdvModel> menuItems;

        /// <summary>
        /// Maintains the value for menu item check icon type.
        /// </summary>
        private CheckIconType checkIconType;

        /// <summary>
        /// Maintains the name of the menu item.
        /// </summary>
        private string menuItemName;

        /// <summary>
        /// Maintains the icon of the menu item.
        /// </summary>
        private object imagePath;

        /// <summary>
        /// Maintains the gesture text of the menu  item.
        /// </summary>
        private string gestureText;

        /// <summary>
        /// Maintains the command for menu item click.
        /// </summary>
        private ICommand menuItemCommand;

        /// <summary>
        /// Maintains a value indicating the menu item is whether checkable.
        /// </summary>
        private bool isCheckable;

        /// <summary>
        /// Initializes the instance of the <see cref="MenuAdvModel"/>class.
        /// </summary>
        public MenuAdvModel()
        {
            MenuItems = new ObservableCollection<MenuAdvModel>();
        }

        /// <summary>
        /// Gets or sets the collection for menu items <see cref="MenuAdvModel"/>class.
        /// </summary>
        public ObservableCollection<MenuAdvModel> MenuItems
        {
            get
            {
                return menuItems;
            }
            set
            {
                menuItems = value;
                RaisePropertyChanged("MenuItems");
            }
        }

        /// <summary>
        /// Gets or sets the name of the menu item <see cref="MenuAdvModel"/>class.
        /// </summary>
        public string MenuItemName
        {
            get
            {
                return menuItemName;

            }
            set
            {
                menuItemName = value;
                RaisePropertyChanged("MenuItemName");
            }
        }

        /// <summary>
        /// Gets or sets the icon of the menu item <see cref="MenuAdvModel"/>class.
        /// </summary>
        public object ImagePath
        {
            get
            {
                return imagePath;
            }
            set
            {
                imagePath = value;
                RaisePropertyChanged("ImagePath");
            }
        }

        /// <summary>
        /// Gets or sets the value gesture text <see cref="MenuAdvModel"/>class.
        /// </summary>
        public string GestureText
        {
            get
            {
                return gestureText;
            }
            set
            {
                gestureText = value;
                RaisePropertyChanged("GestureText");
            }
        }

        /// <summary>
        /// Gets or sets the command for menu item click <see cref="MenuAdvModel"/>class.
        /// </summary>
        public ICommand MenuItemClicked
        {
            get
            {
                return menuItemCommand;
            }
            set
            {
                menuItemCommand = value;
                RaisePropertyChanged("MenuItemClicked");
            }
        }

        /// <summary>
        /// Indicates whether the menu item is checkable <see cref="MenuAdvModel"/>class.
        /// </summary>
        public bool IsCheckable
        {
            get
            {
                return isCheckable;
            }
            set
            {
                isCheckable = value;
                RaisePropertyChanged("IsCheckable");
            }
        }

        /// <summary>
        /// Gets or sets the value for menu item check icon type <see cref="MenuAdvModel"/>class.
        /// </summary>
        public CheckIconType CheckIconType
        {
            get
            {
                return checkIconType;
            }
            set
            {
                checkIconType = value;
                RaisePropertyChanged("CheckIconType");
            }
        }
    }
}
