using Frosty.Core;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using PvZBundleManagerPlugin.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PvZBundleManagerPlugin.Extensions.MEs
{
    public class ManualAdd : MenuExtension
    {
        public override string MenuItemName => "Check for SharedBudnles";

        public override string TopLevelMenuName => "Tools";

        public override RelayCommand MenuItemClicked => new RelayCommand(delegate (object execute)
        {
            App.Logger.Log("Printing shared bundles:");
            foreach(BundleEntry sharedBundle in App.AssetManager.EnumerateBundles(BundleType.SharedBundle))
            {
                App.Logger.Log("Shared bundle: " + sharedBundle.Name);
            }
        });
    }
}
