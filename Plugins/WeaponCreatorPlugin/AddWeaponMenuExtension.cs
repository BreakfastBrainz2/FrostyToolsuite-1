using Frosty.Core;
using WeaponCreatorPlugin.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeaponCreatorPlugin
{
    public class AddWeaponMenuExtension : MenuExtension
    {
        public override string TopLevelMenuName => "Tools";

        public override string MenuItemName => "Create New Weapon";

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            AddWeaponWindow win = new AddWeaponWindow();
            win.ShowDialog();
            return;
        });
    }
}
