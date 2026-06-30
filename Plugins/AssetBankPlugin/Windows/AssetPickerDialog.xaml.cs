using Frosty.Controls;
using Frosty.Core;
using FrostySdk.Managers.Entries;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace AssetBankPlugin
{
    public enum AssetPickerMode { FlatList, FolderTree }

    public partial class AssetPickerDialog : FrostyWindow
    {
        public EbxAssetEntry SelectedEntry { get; private set; }
        public IEnumerable<EbxAssetEntry> SelectedEntries => BankPickerDialog.PeekSelection();

        private List<EbxAssetEntry> _flatAll;
        private ObservableCollection<EbxAssetEntry> _flatView;

        public AssetPickerDialog()
        {
            InitializeComponent();

            if (m_cancelBtn != null) m_cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            if (m_confirmBtn != null) m_confirmBtn.Click += (s, e) => Confirm();
        }

        protected void ConfigureFlatList(
            string assetType,
            string watermark,
            IEnumerable<EbxAssetEntry> entries,
            string badgeColor = "#007ACC",
            string confirmLabel = "Select",
            string footerHint = "Double-click or press Select to confirm")
        {
            if (m_typeBadgeText != null) m_typeBadgeText.Text = assetType;
            if (m_searchBox != null) m_searchBox.WatermarkText = watermark;
            if (m_confirmBtnText != null) m_confirmBtnText.Text = confirmLabel;
            if (m_footerHint != null) m_footerHint.Text = footerHint;

            var badge = FindName("m_typeBadge") as Border;
            if (badge != null)
            {
                try { badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor)); }
                catch { }
            }

            m_listPanel.Visibility = Visibility.Visible;
            m_treePanel.Visibility = Visibility.Collapsed;

            _flatAll = entries.ToList();
            _flatView = new ObservableCollection<EbxAssetEntry>(_flatAll);

            if (m_itemList != null)
            {
                m_itemList.ItemsSource = _flatView;
                m_itemList.MouseDoubleClick += (s, e) => Confirm();
            }

            if (m_searchBox != null)
                m_searchBox.TextChanged += (s, e) => FilterFlatList(m_searchBox.Text);

            UpdateSelectionCount();
        }

        protected void ConfigureFolderTree(
            string assetType,
            string watermark,
            ObservableCollection<BankNode> rootNodes,
            string badgeColor = "#1C3852",
            string confirmLabel = "Load Selected",
            string footerHint = "Check assets then press Load Selected")
        {
            if (m_typeBadgeText != null) m_typeBadgeText.Text = assetType;
            if (m_searchBox != null) m_searchBox.WatermarkText = watermark;
            if (m_confirmBtnText != null) m_confirmBtnText.Text = confirmLabel;
            if (m_footerHint != null) m_footerHint.Text = footerHint;

            var badge = FindName("m_typeBadge") as Border;
            if (badge != null)
            {
                try { badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor)); }
                catch { }
            }

            m_listPanel.Visibility = Visibility.Collapsed;
            m_treePanel.Visibility = Visibility.Visible;

            if (m_itemTree != null)
                m_itemTree.ItemsSource = rootNodes;

            if (m_searchBox != null)
                m_searchBox.TextChanged += (s, e) => OnTreeSearch?.Invoke(m_searchBox.Text);

            UpdateSelectionCount();
        }

        internal void NotifySelectionChanged() => UpdateSelectionCount();

        internal Action<string> OnTreeSearch;

        private void FilterFlatList(string query)
        {
            if (_flatAll == null || _flatView == null) return;
            _flatView.Clear();
            foreach (var e in _flatAll)
                if (string.IsNullOrEmpty(query) || e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _flatView.Add(e);
            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            if (m_selectionCountText == null) return;
            if (m_listPanel?.Visibility == Visibility.Visible && _flatView != null)
            {
                m_selectionCountText.Text = $"{_flatView.Count} asset{(_flatView.Count == 1 ? "" : "s")}";
            }
            else
            {
                int selected = BankPickerDialog.PeekSelection().Count();
                m_selectionCountText.Text = selected > 0 ? $"{selected} selected" : "";
            }
        }

        private void Confirm()
        {
            if (m_listPanel?.Visibility == Visibility.Visible)
            {
                SelectedEntry = m_itemList?.SelectedItem as EbxAssetEntry
                             ?? (m_itemList?.Items.Count > 0 ? m_itemList.Items[0] as EbxAssetEntry : null);
                if (SelectedEntry == null) return;
            }
            DialogResult = true;
            Close();
        }
    }

    public class BankNode : INotifyPropertyChanged
    {
        public static readonly ImageSource FolderIcon =
            new BitmapImage(new Uri("pack://application:,,,/FrostyEditor;component/Images/CloseFolder.png"));
        public static readonly ImageSource OpenFolderIcon =
            new BitmapImage(new Uri("pack://application:,,,/FrostyEditor;component/Images/OpenFolder.png"));

        public string Name { get; set; }
        public bool IsFolder { get; set; }
        public EbxAssetEntry Entry { get; set; }
        public ObservableCollection<BankNode> Children { get; } = new ObservableCollection<BankNode>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                if (IsFolder) OnPropertyChanged(nameof(Icon));
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                if (Entry != null) BankPickerDialog.UpdateSelection(Entry, value);
            }
        }

        public ImageSource Icon => IsFolder
            ? (_isExpanded ? OpenFolderIcon : FolderIcon)
            : null;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string p) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class InverseBoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}