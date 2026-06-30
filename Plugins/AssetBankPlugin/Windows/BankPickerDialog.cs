using Frosty.Core;
using FrostySdk.Managers.Entries;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace AssetBankPlugin
{
    public class BankPickerDialog : AssetPickerDialog
    {
        private static readonly HashSet<EbxAssetEntry> _selectedCache
            = new HashSet<EbxAssetEntry>();

        public static void UpdateSelection(EbxAssetEntry entry, bool selected)
        {
            if (selected) _selectedCache.Add(entry);
            else _selectedCache.Remove(entry);
        }

        public static IEnumerable<EbxAssetEntry> PeekSelection() => _selectedCache;

        private List<EbxAssetEntry> _allEntries;
        private ObservableCollection<BankNode> _rootNodes = new ObservableCollection<BankNode>();

        public BankPickerDialog()
        {
            Title = "  Select Reference Banks";
            Width = 580;
            Height = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _allEntries = App.AssetManager
                             .EnumerateEbx("AntStateAsset")
                             .OrderBy(x => x.Path)
                             .ThenBy(x => x.Filename)
                             .ToList();

            BuildTree("");

            ConfigureFolderTree(
                assetType: "AntStateAsset",
                watermark: "Search by name or path…",
                rootNodes: _rootNodes,
                badgeColor: "#1C3852",
                confirmLabel: "Load Selected",
                footerHint: "Check banks then press Load Selected");

            OnTreeSearch = q =>
            {
                BuildTree(q ?? "");
                NotifySelectionChanged();
            };
        }

        private void BuildTree(string query)
        {
            _rootNodes.Clear();

            bool searching = !string.IsNullOrWhiteSpace(query);
            var folders = new Dictionary<string, BankNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _allEntries)
            {
                if (searching &&
                    entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string[] parts = entry.Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                BankNode currentParent = null;
                string currentPath = "";

                for (int i = 0; i < parts.Length; i++)
                {
                    currentPath += parts[i] + "/";
                    if (!folders.TryGetValue(currentPath, out BankNode folder))
                    {
                        folder = new BankNode
                        {
                            Name = parts[i],
                            IsFolder = true,
                            IsExpanded = searching             
                        };

                        if (currentParent == null) _rootNodes.Add(folder);
                        else currentParent.Children.Add(folder);

                        folders[currentPath] = folder;
                    }
                    currentParent = folder;
                }

                var leaf = new BankNode
                {
                    Name = entry.Filename,
                    Entry = entry,
                    IsFolder = false,
                    IsSelected = _selectedCache.Contains(entry)      
                };

                if (currentParent == null) _rootNodes.Add(leaf);
                else currentParent.Children.Add(leaf);
            }
        }
    }
}