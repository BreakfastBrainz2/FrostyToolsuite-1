using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;

namespace MovieTexture2AssetEditorPlugin
{
    public class MovieTexture2AssetDefition : AssetDefinition
    {

        protected static ImageSource iconSource = new ImageSourceConverter().ConvertFromString("pack://application:,,,/FrostyCore;Component/Images/Assets/MovieTextureFileType.png") as ImageSource;

        public override FrostyAssetEditor GetEditor(ILogger logger)
        {
            return new MovieTexture2Editor(logger);
        }

        public override ImageSource GetIcon()
        {
            return iconSource;
        }
    }

    public class MovieTexture2Editor : FrostyAssetEditor
    {
        public MovieTexture2Editor(ILogger logger) : base(logger)
        {
        }

        public override List<ToolbarItem> RegisterToolbarItems() {
            return new List<ToolbarItem>
            {
                new ToolbarItem("Export", "Export Movie", "Images/Export.png", new RelayCommand((object state) => { ExportButton_Click(this, new RoutedEventArgs()); })),
                new ToolbarItem("Import", "Import Movie", "Images/Import.png", new RelayCommand((object state) => { ImportButton_Click(this, new RoutedEventArgs()); })),
            };
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            dynamic root = asset.RootObject;
            ChunkAssetEntry chunkAssetEntry = App.AssetManager.GetChunkEntry(root.ChunkGuid);

            if (root != null)
            {
                FrostySaveFileDialog saveFileDialog = new FrostySaveFileDialog("Export Movie Asset", "WEBM (*.webm)|*.webm", "Movie", AssetEntry.Filename, false);

                string initialDir = saveFileDialog.InitialDirectory;

                if (saveFileDialog.ShowDialog())
                {
                    FileInfo fileInfo = new FileInfo(saveFileDialog.FileName);
                    saveFileDialog.InitialDirectory = fileInfo.DirectoryName;

                    if (fileInfo.Exists)
                    {
                        SystemSounds.Exclamation.Play();
                        if (FrostyMessageBox.Show("'" + saveFileDialog.FileName + "' already exists.\r\nDo you want to replace it?", "Export Movie Asset", MessageBoxButton.YesNo) == MessageBoxResult.No)
                            return;
                    }

                    FrostyTaskWindow.Show("Exporting video", "Exporting video...", (task) =>
                    {
                        Stream chunkStream = App.AssetManager.GetChunk(chunkAssetEntry);
                        if (chunkStream != null)
                        {
                            using (NativeWriter writer = new NativeWriter(new FileStream(saveFileDialog.FileName, FileMode.Create)))
                            {
                                using (NativeReader reader = new NativeReader(chunkStream))
                                    writer.Write(reader.ReadToEnd());
                            }
                        }
                        else
                        {
                            logger.LogError("Failed to export chunk ${chunkAssetEntry.ChunkGuid}. Maybe it doesn't exist?");
                        }
                    });
                    logger.Log("Exported Movie Asset to " + saveFileDialog.FileName);
                }
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {

            dynamic root = asset.RootObject;
            ChunkAssetEntry chunkAssetEntry = App.AssetManager.GetChunkEntry(root.ChunkGuid);

            FrostyOpenFileDialog openFileDialog = new FrostyOpenFileDialog("Import Movie Asset", "WEBM (*.webm)|*.webm", "Movie");
            if (openFileDialog.ShowDialog())
            {
                uint chunkSize = 0;
                FrostyTaskWindow.Show("Importing Video as Chunk", "Importing...", (task) =>
                {
                    using (NativeReader reader = new NativeReader(new FileStream(openFileDialog.FileName, FileMode.Open, FileAccess.Read)))
                    {
                        byte[] buffer = reader.ReadToEnd();
                        App.AssetManager.ModifyChunk(chunkAssetEntry.Id, buffer);
                        chunkSize = (uint)buffer.Length;
                    }
                });
                root.ChunkSize = chunkSize;
                App.AssetManager.ModifyEbx(AssetEntry.Name, asset);
                // Refresh the property grid UI
                FrostyPropertyGrid pg = (GetTemplateChild("PART_AssetPropertyGrid") as FrostyPropertyGrid);
                pg.Object = asset.RootObject;
                InvokeOnAssetModified();

                logger.Log($"Succesfully imported {AssetEntry.Filename}. (ChunkSize was updated automatically; refresh to see changes.)");
            }
        }
    }
}
