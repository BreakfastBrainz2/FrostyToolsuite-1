using Frosty.Core;
using CustomizationCreatorPlugin.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomizationCreatorPlugin
{
    public class AddCustomizationMenuExtension : MenuExtension
    {
        public override string TopLevelMenuName => "Tools";

        public override string MenuItemName => "Create New Customization";

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            AddCustomizationWindow win = new AddCustomizationWindow();
            win.ShowDialog();
            return;
        });
    }
}
