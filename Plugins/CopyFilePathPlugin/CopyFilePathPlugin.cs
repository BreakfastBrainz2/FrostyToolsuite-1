using Frosty.Core;
using FrostySdk.Managers.Entries;
using System.Windows;
using System.Windows.Media;

namespace CopyFilePathPlugin
{
	public class CopyFilePathMenuItem : DataExplorerContextMenuExtension
       {
        public override string ContextItemName => "Copy File Path";
        public override ImageSource Icon => new ImageSourceConverter().ConvertFromString("pack://application:,,,/FrostyCore;component/Images/Copy.png") as ImageSource;

        public override RelayCommand ContextItemClicked => new RelayCommand((o) => {
            EbxAssetEntry entry = App.SelectedAsset as EbxAssetEntry;
            if (entry == null)
            {
                App.Logger.LogError("App.SelectedAsset was null; unable to copy.");
                return;
            }
            Clipboard.SetText(entry.Name);
        });
    }
}