using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Screens;
using Frosty.Core.Windows;
using FrostySdk.Interfaces;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using FrostySdk.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using AssetBankPlugin.Export;

namespace AssetBankPlugin
{
    public class AssetBankEditor : FrostyAssetEditor
    {
        #region -- GridVisible --
        public static readonly DependencyProperty GridVisibleProperty = DependencyProperty.Register("GridVisible", typeof(bool), typeof(AssetBankEditor), new FrameworkPropertyMetadata(true));
        public bool GridVisible
        {
            get => (bool)GetValue(GridVisibleProperty);
            set => SetValue(GridVisibleProperty, value);
        }
        #endregion

        private TextBlock m_textureFormatText;
        private Texture m_textureAsset;
        private TextBox m_debugTextBox;
        private ComboBox m_mipsComboBox;
        private ComboBox m_sliceComboBox;
        private Border m_sliceToolBarItem;
        private bool m_textureIsSrgb;

        static AssetBankEditor()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(AssetBankEditor), new FrameworkPropertyMetadata(typeof(AssetBankEditor)));
        }

        public AssetBankEditor(ILogger inLogger)
            : base(inLogger)
        {
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            UpdateControls();
        }

        public override List<ToolbarItem> RegisterToolbarItems()
        {
            List<ToolbarItem> toolbarItems = base.RegisterToolbarItems();
            toolbarItems.Add(new ToolbarItem("Export", "Export Texture", "Images/Export.png", new RelayCommand((object state) => { ExportButton_Click(this, new RoutedEventArgs()); })));
            toolbarItems.Add(new ToolbarItem("Import", "Import Texture", "Images/Import.png", new RelayCommand((object state) => { ImportButton_Click(this, new RoutedEventArgs()); })));

            return toolbarItems;
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            FrostyOpenFileDialog ofd = new FrostyOpenFileDialog("Import Texture", "PNG (*.png)|*.png|TGA (*.tga)|*.tga|HDR (*.hdr)|*.hdr|DDS (*.dds)|*.dds", "Texture");
            if (m_textureAsset.Type != TextureType.TT_2d)
            {
                ofd.Multiselect = true;
                ofd.Title = "Import Textures";
            }

            if (ofd.ShowDialog())
            {
                EbxAssetEntry assetEntry = AssetEntry as EbxAssetEntry;
                ulong resRid = ((dynamic)RootObject).Resource;

                bool bFailed = false;
                string errorMsg = "";

                FrostyTaskWindow.Show("Importing Texture", "", (task) =>
                {

                });

                string message = "Texture " + ofd.FileName + " failed to import: " + errorMsg;
                if (!bFailed)
                {

                    UpdateControls();
                    InvokeOnAssetModified();

                    message = "Texture " + ofd.FileName + " successfully imported";
                }

                logger.Log(message);
            }
        }

        private void UpdateControls()
        {
            float newWidth = m_textureAsset.Width;
            float newHeight = m_textureAsset.Height;

            if (newWidth > 2048)
            {
                newWidth = 2048;
                newHeight = (newHeight * (newWidth / m_textureAsset.Width));
            }
            if (newHeight > 2048)
            {
                newHeight = 2048;
                newWidth = (newWidth * (newHeight / m_textureAsset.Height));
            }

            string pf = m_textureAsset.PixelFormat;
            if (pf.StartsWith("BC") && m_textureAsset.Flags.HasFlag(TextureFlags.SrgbGamma))
                pf = pf.Replace("UNORM", "SRGB");

            m_textureFormatText.Text = pf;;

            ushort width = m_textureAsset.Width;
            ushort height = m_textureAsset.Height;

            m_mipsComboBox.Items.Clear();
            for (int i = 0; i < m_textureAsset.MipCount; i++)
            {
                m_mipsComboBox.Items.Add(string.Format("{0}x{1}", width, height));

                width >>= 1;
                height >>= 1;
            }
            m_mipsComboBox.SelectedIndex = 0;

            if (m_textureAsset.Depth > 1)
            {
                m_sliceComboBox.ItemsSource = null;
                if (m_textureAsset.Type == TextureType.TT_Cube)
                {
                    string[] cubeItems = new string[] { "X+", "X-", "Y+", "Y-", "Z+", "Z-" };
                    m_sliceComboBox.ItemsSource = cubeItems;
                }
                else
                {
                    string[] sliceItems = new string[m_textureAsset.Depth];
                    for (int i = 0; i < m_textureAsset.Depth; i++)
                        sliceItems[i] = i.ToString();
                    m_sliceComboBox.ItemsSource = sliceItems;
                }
                m_sliceComboBox.SelectedIndex = 0;
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            bool bResult = false;

            FrostySaveFileDialog sfd = new FrostySaveFileDialog("Export Texture", "PNG (*.png)|*.png|TGA (*.tga)|*.tga|HDR (*.hdr)|*.hdr|DDS (*.dds)|*.dds", "Texture", AssetEntry.Filename, false);

            if (!bResult)
                return;

            FrostyTaskWindow.Show("Exporting Animation", AssetEntry.Filename, (task) =>
            {
                string[] filters = new string[] { "*.seanim" };

                AnimationExporterSEANIM exporter = new AnimationExporterSEANIM();
            });
            logger.Log("Texture successfully exported to " + sfd.FileName);
        }
    }
}
