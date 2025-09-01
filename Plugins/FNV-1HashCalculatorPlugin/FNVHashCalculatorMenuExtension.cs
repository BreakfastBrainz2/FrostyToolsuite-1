using Frosty.Core;
using FNVHashCalculatorPlugin.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FNVHashCalculatorPlugin
{
    public class FNVHashCalculatorMenuExtension : MenuExtension
    {
        public override string TopLevelMenuName => "Tools";

        public override string MenuItemName => "Calculate FNV-1 Hash";

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            FNVHashCalculatorWindow win = new FNVHashCalculatorWindow();
            win.ShowDialog();
            return;
        });
    }
}
