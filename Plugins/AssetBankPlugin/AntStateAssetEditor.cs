using AssetBankPlugin.Ant;
using AssetBankPlugin.Export;
using AssetBankPlugin.Formats;
using AssetBankPlugin.Render;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using Frosty.Core.Windows;
using FrostySdk.Ebx;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using MeshSetPlugin.Render;
using MeshSetPlugin.Resources;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssetBankPlugin
{
    public class AntTypeGroup
    {
        public string TypeName { get; set; }
        public int ItemCount => Assets.Count;
        public ObservableCollection<AntAssetViewModel> Assets { get; set; } = new ObservableCollection<AntAssetViewModel>();
    }

    public class AntAssetViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public Guid Id { get; set; }
        public object AssetInstance { get; set; }
        public AntStateAssetEditor ParentEditor { get; set; }

        private bool _isNew;
        public bool IsNew
        {
            get => _isNew;
            set { _isNew = value; OnPropertyChanged(nameof(IsNew)); OnPropertyChanged(nameof(NameFontWeight)); OnPropertyChanged(nameof(DisplayName)); }
        }

        private bool _isModified;
        public bool IsModified
        {
            get => _isModified;
            set { _isModified = value; OnPropertyChanged(nameof(IsModified)); OnPropertyChanged(nameof(NameFontWeight)); OnPropertyChanged(nameof(DisplayName)); }
        }

        private bool _isUnsaved;
        public bool IsUnsaved
        {
            get => _isUnsaved;
            set { _isUnsaved = value; OnPropertyChanged(nameof(IsUnsaved)); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string DisplayName => IsUnsaved ? Name + " *" : Name;

        public System.Windows.FontWeight NameFontWeight
            => (IsNew || IsModified) ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

        public void MarkModified()
        {
            IsModified = true;
            IsUnsaved = true;
        }

        private bool _isSelectedForExport;
        public bool IsSelectedForExport
        {
            get => _isSelectedForExport;
            set
            {
                _isSelectedForExport = value;
                OnPropertyChanged(nameof(IsSelectedForExport));
                ParentEditor?.NotifyExportStatus();
            }
        }

        private bool _showCheckbox;
        public Visibility CheckboxVisibility => (_showCheckbox && AssetInstance is AnimationAsset) ? Visibility.Visible : Visibility.Collapsed;

        public void SetBulkMode(bool show)
        {
            _showCheckbox = show;
            if (!show) IsSelectedForExport = false;
            OnPropertyChanged(nameof(CheckboxVisibility));
        }

        public ICommand CopyGuidCommand => new RelayCommand((o) => Clipboard.SetText(Id.ToString()));
        public Visibility ExportVisibility => (AssetInstance is AnimationAsset) ? Visibility.Visible : Visibility.Collapsed;
        public ICommand ExportCommand => new RelayCommand((o) => ExportAsset());

        public Visibility ImportAnimVisibility => (AssetInstance is VbrAnimationAsset) ? Visibility.Visible : Visibility.Collapsed;
        public ICommand ImportAnimationCommand => new RelayCommand((o) => ParentEditor?.ImportAnimation(this));

        public Visibility DuplicateVisibility => (AssetInstance is AntAsset) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowTreeVisibility => (AssetInstance is AntAsset) ? Visibility.Visible : Visibility.Collapsed;
        public ICommand DuplicateCommand => new RelayCommand((o) => ParentEditor?.DuplicateAsset(this));
        public ICommand ShowTreeCommand => new RelayCommand((o) => ParentEditor?.ShowAssetTree(this));

        public Visibility EditAnimAssetsVisibility => (AssetInstance is AntAsset ant && ant.AssetType == "ActorControllerAsset") ? Visibility.Visible : Visibility.Collapsed;
        public ICommand EditAnimAssetsCommand => new RelayCommand((o) => ParentEditor?.EditAnimAssets(this));

        private void ExportAsset()
        {
            if (!(AssetInstance is AnimationAsset anim)) return;

            var opt = new AnimationOptions();
            opt.Load();

            if (string.IsNullOrEmpty(opt.ExportSkeletonAsset))
            {
                FrostyMessageBox.Show("Please set an Export Skeleton in Options first.", "Missing Skeleton");
                return;
            }

            string cleanName = Name;
            int idx = cleanName.LastIndexOf(" (");
            if (idx > 0 && cleanName.EndsWith(")"))
                cleanName = cleanName.Substring(0, idx);

            FrostySaveFileDialog sfd = new FrostySaveFileDialog("Save Animation", "*.seanim (SEAnim)|*.seanim", "SEAnim", cleanName);
            if (sfd.ShowDialog())
            {
                string exportDirectory = Path.GetDirectoryName(sfd.FileName);
                string assetName = cleanName;

                FrostyTaskWindow.Show("Exporting Animation", "", (task) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            EbxAssetEntry skelEntry = App.AssetManager.GetEbxEntry(opt.ExportSkeletonAsset);
                            var skelEbx = App.AssetManager.GetEbx(skelEntry);
                            dynamic skel = skelEbx.RootObject;
                            var skeleton = SkeletonAssetExport.ConvertToInternal(skel);

                            anim.Name = assetName;
                            anim.Channels = anim.GetChannels(anim.ChannelToDofAsset);
                            var intern = anim.ConvertToInternal();

                            if (intern != null)
                            {
                                new AnimationExporterSEANIM().Export(intern, skeleton, exportDirectory);
                                App.Logger.Log($"[AntStateEditor] Successfully exported {assetName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger.LogError($"[AntStateEditor] Failed to export {assetName}: {ex.Message}");
                        }
                    });
                });
            }
        }

        private ObservableCollection<AntPropertyViewModel> _properties;
        public ObservableCollection<AntPropertyViewModel> Properties
        {
            get
            {
                if (_properties == null)
                {
                    _properties = new ObservableCollection<AntPropertyViewModel>();
                    if (AssetInstance != null)
                    {
                        if (AssetInstance is GenericAntAsset genAsset && genAsset.RawData != null)
                        {
                            foreach (var kvp in genAsset.RawData)
                            {
                                if (kvp.Key == "__name" || kvp.Key == "__guid" || kvp.Key == "__key") continue;

                                var vm = new AntPropertyViewModel(kvp.Key, kvp.Value) { IsEditable = true };
                                vm.ModifiedCallback = MarkModified;
                                _properties.Add(vm);
                            }
                        }
                        else
                        {
                            Type type = AssetInstance.GetType();

                            bool editable = true;

                            foreach (var p in ReflectionHelper.GetAllProperties(type))
                            {
                                if (p.Name == "Name" || p.Name == "ID" || p.Name == "Bank" || p.Name == "AssetType" || p.Name == "RawData") continue;
                                try
                                {
                                    var vm = new AntPropertyViewModel(p.Name, p.GetValue(AssetInstance));
                                    if (editable) { vm.IsEditable = true; vm.ModifiedCallback = MarkModified; }
                                    _properties.Add(vm);
                                }
                                catch { }
                            }

                            foreach (var f in ReflectionHelper.GetAllFields(type))
                            {
                                if (f.Name.Contains("<") || f.Name.Contains("k__BackingField")) continue;
                                try
                                {
                                    var vm = new AntPropertyViewModel(f.Name, f.GetValue(AssetInstance));
                                    if (editable) { vm.IsEditable = true; vm.ModifiedCallback = MarkModified; }
                                    _properties.Add(vm);
                                }
                                catch { }
                            }
                        }
                    }
                }
                return _properties;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class AntPropertyViewModel
    {
        public string Name { get; set; }
        public string TypeStr { get; set; }
        public string ValueStr { get; set; }
        public object ValueObj { get; set; }

        public bool IsEditable { get; set; }
        private string _editedValue;

        public Action ModifiedCallback { get; set; }

        public string EditedValue
        {
            get => _editedValue ?? ValueStr;
            set
            {
                if (_editedValue == value) return;
                _editedValue = value;
                ModifiedCallback?.Invoke();
            }
        }
        public bool WasEdited => IsEditable && _editedValue != null && _editedValue != ValueStr;
        public Visibility EditableVisibility => IsEditable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReadOnlyVisibility => IsEditable ? Visibility.Collapsed : Visibility.Visible;

        public ICommand CopyValueCommand => new RelayCommand((o) =>
        {
            if (!string.IsNullOrEmpty(ValueStr))
            {
                try { Clipboard.SetText(ValueStr); } catch { }
            }
        });

        public AntPropertyViewModel(string name, object val)
        {
            Name = name;
            ValueObj = val;
            TypeStr = val != null ? GetFriendlyTypeName(val.GetType()) : "Null";
            ValueStr = GetString(val);
        }

        private string GetFriendlyTypeName(Type t)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return $"List of {t.GetGenericArguments()[0].Name}";
            return t.Name;
        }

        private string GetString(object obj)
        {
            if (obj == null) return "null";
            Type t = obj.GetType();

            if (obj is string s)
            {
                if (s.Length == 16 && ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out ulong keyVal))
                {
                    return KeyToGuid(keyVal).ToString();
                }
                return s;
            }

            if (obj is ulong ul && (Name.Contains("Anim") || Name.Contains("Target") || Name.Contains("Key") || Name.Contains("Asset") || Name.Contains("TagCollectionSet")))
            {
                return KeyToGuid(ul).ToString();
            }

            if (t.IsPrimitive || t == typeof(Guid) || t.IsEnum)
                return obj.ToString();

            if (obj is Dictionary<string, object>)
                return "[Struct]";

            if (obj is IEnumerable enumerable)
            {
                int count = 0;
                var enumerator = enumerable.GetEnumerator();
                while (enumerator.MoveNext()) count++;
                return $"[{count} Items]";
            }

            return $"[{t.Name}]";
        }

        public static Guid KeyToGuid(ulong key)
        {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(key).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        private ObservableCollection<AntPropertyViewModel> _children;
        public ObservableCollection<AntPropertyViewModel> Children
        {
            get
            {
                if (_children == null)
                {
                    _children = new ObservableCollection<AntPropertyViewModel>();
                    if (ValueObj != null)
                    {
                        Type t = ValueObj.GetType();
                        if (!(t.IsPrimitive || t == typeof(string) || t == typeof(Guid) || t.IsEnum))
                        {
                            if (ValueObj is Dictionary<string, object> dict)
                            {
                                foreach (var kvp in dict)
                                {
                                    if (kvp.Key == "__typeHash" || kvp.Key == "__guid" || kvp.Key == "__key" || kvp.Key == "__name") continue;
                                    var vm = new AntPropertyViewModel(kvp.Key, kvp.Value) { IsEditable = true };
                                    vm.ModifiedCallback = ModifiedCallback;
                                    _children.Add(vm);
                                }
                            }
                            else if (ValueObj is IEnumerable enumerable)
                            {
                                int i = 0;
                                foreach (var item in enumerable)
                                {
                                    var vm = new AntPropertyViewModel($"[{i++}]", item) { IsEditable = true };
                                    vm.ModifiedCallback = ModifiedCallback;
                                    _children.Add(vm);
                                }
                            }
                            else
                            {
                                foreach (var p in ReflectionHelper.GetAllProperties(t))
                                {
                                    try
                                    {
                                        var vm = new AntPropertyViewModel(p.Name, p.GetValue(ValueObj)) { IsEditable = true };
                                        vm.ModifiedCallback = ModifiedCallback;
                                        _children.Add(vm);
                                    }
                                    catch { }
                                }
                                foreach (var f in ReflectionHelper.GetAllFields(t))
                                {
                                    if (f.Name.Contains("<") || f.Name.Contains("k__BackingField")) continue;
                                    try
                                    {
                                        var vm = new AntPropertyViewModel(f.Name, f.GetValue(ValueObj)) { IsEditable = true };
                                        vm.ModifiedCallback = ModifiedCallback;
                                        _children.Add(vm);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
                return _children;
            }
        }
    }

    public class LoadedMeshViewModel : INotifyPropertyChanged
    {
        private readonly AntStateAssetEditor _editor;

        public int InternalIndex { get; set; }

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
                _editor?.OnMeshVisibilityChanged();
            }
        }

        public ICommand UseSelectedCommand => new RelayCommand(_ => _editor?.ReplaceMeshFromExplorer(InternalIndex));
        public ICommand RemoveCommand => new RelayCommand(_ => _editor?.RemoveMeshAt(InternalIndex));

        public LoadedMeshViewModel(AntStateAssetEditor editor) => _editor = editor;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string p) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public static class ReflectionHelper
    {
        public static IEnumerable<FieldInfo> GetAllFields(Type t)
        {
            if (t == null) return Enumerable.Empty<FieldInfo>();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            return t.GetFields(flags).Concat(GetAllFields(t.BaseType));
        }

        public static IEnumerable<PropertyInfo> GetAllProperties(Type t)
        {
            if (t == null) return Enumerable.Empty<PropertyInfo>();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            return t.GetProperties(flags).Concat(GetAllProperties(t.BaseType));
        }
    }

    [TemplatePart(Name = "PART_SearchBox", Type = typeof(TextBox))]
    [TemplatePart(Name = "PART_AssetTreeView", Type = typeof(TreeView))]
    [TemplatePart(Name = "PART_LoadingOverlay", Type = typeof(Border))]
    [TemplatePart(Name = "PART_LoadingText", Type = typeof(TextBlock))]
    [TemplatePart(Name = "PART_BulkToggle", Type = typeof(System.Windows.Controls.Primitives.ToggleButton))]
    [TemplatePart(Name = "PART_ExportBulkButton", Type = typeof(Button))]
    [TemplatePart(Name = "PART_AddRefButton", Type = typeof(Button))]
    [TemplatePart(Name = "PART_Viewport", Type = typeof(FrostyViewport))]
    [TemplatePart(Name = "PART_SkeletonPath", Type = typeof(TextBox))]
    [TemplatePart(Name = "PART_SkeletonSetDefault", Type = typeof(Button))]
    [TemplatePart(Name = "PART_SkeletonOverrideBtn", Type = typeof(System.Windows.Controls.Primitives.ToggleButton))]
    [TemplatePart(Name = "PART_MeshPath", Type = typeof(TextBox))]
    [TemplatePart(Name = "PART_MeshLoad", Type = typeof(Button))]
    [TemplatePart(Name = "PART_MeshUseSelected", Type = typeof(Button))]
    [TemplatePart(Name = "PART_RestartBtn", Type = typeof(Button))]
    [TemplatePart(Name = "PART_PlayPauseBtn", Type = typeof(System.Windows.Controls.Primitives.ToggleButton))]
    [TemplatePart(Name = "PART_TimelineSlider", Type = typeof(Slider))]
    [TemplatePart(Name = "PART_FrameLabel", Type = typeof(TextBlock))]
    [TemplatePart(Name = "PART_TotalFramesLabel", Type = typeof(TextBlock))]
    [TemplatePart(Name = "PART_SpeedSlider", Type = typeof(Slider))]
    [TemplatePart(Name = "PART_SpeedLabel", Type = typeof(TextBlock))]
    [TemplatePart(Name = "PART_LodCombo", Type = typeof(ComboBox))]
    [TemplatePart(Name = "PART_RenderModeCombo", Type = typeof(ComboBox))]
    [TemplatePart(Name = "PART_MeshListPopup", Type = typeof(System.Windows.Controls.Primitives.Popup))]
    [TemplatePart(Name = "PART_SaveButton", Type = typeof(Button))]
    public partial class AntStateAssetEditor : FrostyAssetEditor, INotifyPropertyChanged
    {
        private TextBox m_searchBox;
        private TreeView m_assetTreeView;
        private Border m_loadingOverlay;
        private TextBlock m_loadingText;
        private System.Windows.Controls.Primitives.ToggleButton m_bulkToggle;

        private Bank _bank;
        private byte[] _bankBytes;
        private ResAssetEntry _bankResEntry;
        private ChunkAssetEntry _bankChunkEntry;
        private bool _bankIsBigEndian;

        private FrostyViewport m_viewport;
        private readonly AntAnimPreviewScreen m_screen = new AntAnimPreviewScreen();
        private TextBox m_meshPathBox;
        private bool _meshLoaded = false;
        private string _currentSkeletonPath = "";
        private bool _suppressSkeletonToggle = false;
        private bool _skeletonOverrideActive = false;

        private Button m_restartBtn;
        private System.Windows.Controls.Primitives.ToggleButton m_playPauseBtn;
        private Slider m_timelineSlider;
        private TextBlock m_frameLabel;
        private TextBlock m_totalFramesLabel;
        private Slider m_speedSlider;
        private TextBlock m_speedLabel;
        private ComboBox m_lodCombo;
        private ComboBox m_renderModeCombo;

        private System.Windows.Controls.Primitives.Popup _meshListPopup;

        private sealed class LoadedMeshEntry
        {
            public string DisplayName;
            public MeshSet MeshSet;
            public MeshMaterialCollection Materials;
            public MeshRenderSkeleton PerMeshSkeleton;
            public EbxAssetEntry SourceEntry;
            public PointerRef VariationRef;
            public string BpbName;
        }
        private readonly List<LoadedMeshEntry> _loadedMeshData = new List<LoadedMeshEntry>();
        public ObservableCollection<LoadedMeshViewModel> LoadedMeshes { get; }
            = new ObservableCollection<LoadedMeshViewModel>();

        private AnimationAsset _currentPreviewAsset;
        private string _currentPreviewName;
        private bool _isProgrammaticSliderUpdate = false;

        private List<AntTypeGroup> _masterGroups = new List<AntTypeGroup>();
        private ObservableCollection<AntTypeGroup> _filteredGroups = new ObservableCollection<AntTypeGroup>();

        private AntAssetViewModel _selectedAsset;
        private bool _isBulkMode = false;
        private AntAssetViewModel _shiftAnchor = null;

        public bool IsExportEnabled
        {
            get
            {
                if (_isBulkMode)
                    return _masterGroups.SelectMany(g => g.Assets).Any(a => a.IsSelectedForExport);

                return _selectedAsset != null && _selectedAsset.AssetInstance is AnimationAsset;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyExportStatus() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExportEnabled)));

        static AntStateAssetEditor()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(AntStateAssetEditor), new FrameworkPropertyMetadata(typeof(AntStateAssetEditor)));
        }

        public AntStateAssetEditor(ILogger inLogger) : base(inLogger) { }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            m_searchBox = GetTemplateChild("PART_SearchBox") as TextBox;
            m_assetTreeView = GetTemplateChild("PART_AssetTreeView") as TreeView;
            m_loadingOverlay = GetTemplateChild("PART_LoadingOverlay") as Border;
            m_loadingText = GetTemplateChild("PART_LoadingText") as TextBlock;
            m_bulkToggle = GetTemplateChild("PART_BulkToggle") as System.Windows.Controls.Primitives.ToggleButton;
            var exportBulkBtn = GetTemplateChild("PART_ExportBulkButton") as Button;
            var addRefBtn = GetTemplateChild("PART_AddRefButton") as Button;

            m_viewport = GetTemplateChild("PART_Viewport") as FrostyViewport;
            m_meshPathBox = GetTemplateChild("PART_MeshPath") as TextBox;
            var meshLoadBtn = GetTemplateChild("PART_MeshLoad") as Button;
            var skelOverride = GetTemplateChild("PART_SkeletonOverrideBtn") as System.Windows.Controls.Primitives.ToggleButton;
            var meshUseSel = GetTemplateChild("PART_MeshUseSelected") as Button;

            var opt0 = new AnimationOptions();
            opt0.Load();
            _currentSkeletonPath = opt0.ExportSkeletonAsset ?? "";

            if (skelOverride != null)
            {
                skelOverride.Checked += (s, e) =>
                {
                    if (_suppressSkeletonToggle) return;
                    var picker = new SkeletonPickerDialog { Owner = Window.GetWindow(this) };
                    if (picker.ShowDialog() == true && picker.SelectedEntry != null)
                    {
                        _currentSkeletonPath = picker.SelectedEntry.Name;
                        _skeletonOverrideActive = true;
                        var opt = new AnimationOptions();
                        opt.Load();
                        opt.ExportSkeletonAsset = _currentSkeletonPath;
                        opt.Save();
                        if (_currentPreviewAsset != null)
                            _ = LoadPreviewAsync(_currentPreviewAsset, _currentPreviewName);
                    }
                    else
                    {
                        _suppressSkeletonToggle = true;
                        skelOverride.IsChecked = false;
                        _suppressSkeletonToggle = false;
                    }
                };

                skelOverride.Unchecked += (s, e) =>
                {
                    if (_suppressSkeletonToggle) return;
                    _skeletonOverrideActive = false;
                    _currentSkeletonPath = "";
                    if (_currentPreviewAsset != null)
                        _ = LoadPreviewAsync(_currentPreviewAsset, _currentPreviewName);
                };
            }

            if (meshLoadBtn != null)
                meshLoadBtn.Click += (s, e) => _ = LoadMeshAsync(m_meshPathBox?.Text?.Trim() ?? "");

            if (meshUseSel != null)
                meshUseSel.Click += (s, e) =>
                {
                    try
                    {
                        dynamic mainWin = App.EditorWindow;
                        EbxAssetEntry entry = mainWin?.DataExplorer?.SelectedAsset as EbxAssetEntry;
                        if (entry != null && m_meshPathBox != null)
                        {
                            m_meshPathBox.Text = entry.Name;
                            App.Logger.Log($"[AntStateEditor] Mesh path set to: {entry.Name}");
                        }
                        else
                        {
                            App.Logger.LogWarning("[AntStateEditor] No asset selected in data explorer.");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.LogError($"[AntStateEditor] Could not read data explorer selection: {ex.Message}");
                    }
                };

            if (m_bulkToggle != null)
            {
                m_bulkToggle.Checked += (s, e) => { _isBulkMode = true; ToggleBulkMode(true); NotifyExportStatus(); };
                m_bulkToggle.Unchecked += (s, e) => { _isBulkMode = false; ToggleBulkMode(false); NotifyExportStatus(); };
            }

            if (exportBulkBtn != null) exportBulkBtn.Click += (s, e) => ExportSelected();
            if (addRefBtn != null) addRefBtn.Click += (s, e) => OpenReferenceSelector();

            var saveBtn = GetTemplateChild("PART_SaveButton") as Button;
            if (saveBtn != null) saveBtn.Click += (s, e) => SaveAllUnsaved();

            if (m_assetTreeView != null)
            {
                m_assetTreeView.ItemsSource = _filteredGroups;
                m_assetTreeView.SelectedItemChanged += (s, e) =>
                {
                    _selectedAsset = e.NewValue as AntAssetViewModel;
                    NotifyExportStatus();

                    if (e.NewValue is AntAssetViewModel vm && vm.AssetInstance is AnimationAsset animAsset)
                    {
                        string cleanName = vm.Name;
                        int parenIdx = cleanName.LastIndexOf(" (");
                        if (parenIdx > 0 && cleanName.EndsWith(")"))
                            cleanName = cleanName.Substring(0, parenIdx);

                        _currentPreviewAsset = animAsset;
                        _currentPreviewName = cleanName;
                        _ = LoadPreviewAsync(animAsset, cleanName);
                    }
                };
                m_assetTreeView.PreviewMouseLeftButtonDown += OnTreeViewPreviewMouseDown;
            }

            if (m_searchBox != null)
                m_searchBox.TextChanged += (s, e) => ApplySearchFilter(m_searchBox.Text);

            if (m_viewport != null)
                m_viewport.Screen = m_screen;

            _meshListPopup = GetTemplateChild("PART_MeshListPopup") as System.Windows.Controls.Primitives.Popup;
            if (_meshListPopup != null)
                _meshListPopup.DataContext = this;

            /*var skeletonBtn = GetTemplateChild("PART_PreviewSkeletonBtn") as ToggleButton;
            if (skeletonBtn != null)
            {
                skeletonBtn.Checked += (s, e) => m_screen.ShowSkeleton = true;
                skeletonBtn.Unchecked += (s, e) => m_screen.ShowSkeleton = false;
            }*/

            m_restartBtn = GetTemplateChild("PART_RestartBtn") as Button;
            m_playPauseBtn = GetTemplateChild("PART_PlayPauseBtn") as System.Windows.Controls.Primitives.ToggleButton;
            m_timelineSlider = GetTemplateChild("PART_TimelineSlider") as Slider;
            m_frameLabel = GetTemplateChild("PART_FrameLabel") as TextBlock;
            m_totalFramesLabel = GetTemplateChild("PART_TotalFramesLabel") as TextBlock;
            m_speedSlider = GetTemplateChild("PART_SpeedSlider") as Slider;
            m_speedLabel = GetTemplateChild("PART_SpeedLabel") as TextBlock;
            m_lodCombo = GetTemplateChild("PART_LodCombo") as ComboBox;
            m_renderModeCombo = GetTemplateChild("PART_RenderModeCombo") as ComboBox;

            if (m_restartBtn != null)
                m_restartBtn.Click += (s, e) => m_screen.Restart();

            if (m_playPauseBtn != null)
            {
                m_playPauseBtn.Checked += (s, e) => m_screen.IsPaused = false;
                m_playPauseBtn.Unchecked += (s, e) => m_screen.IsPaused = true;
            }

            if (m_speedSlider != null)
            {
                m_speedSlider.ValueChanged += (s, e) =>
                {
                    m_screen.SpeedMultiplier = e.NewValue;
                    if (m_speedLabel != null) m_speedLabel.Text = $"{e.NewValue:F1}x";
                };
                m_speedSlider.PreviewMouseWheel += (s, e) =>
                {
                    double delta = e.Delta > 0 ? 0.1 : -0.1;
                    double newVal = Math.Max(m_speedSlider.Minimum, Math.Min(m_speedSlider.Maximum, m_speedSlider.Value + delta));
                    m_speedSlider.Value = newVal;
                    e.Handled = true;
                };
            }

            if (m_lodCombo != null)
            {
                m_lodCombo.ItemsSource = new[] { "LOD 0", "LOD 1", "LOD 2", "LOD 3", "LOD 4", "LOD 5" };
                m_lodCombo.SelectedIndex = 0;
                m_lodCombo.SelectionChanged += (s, e) => m_screen.CurrentLOD = m_lodCombo.SelectedIndex;
            }

            if (m_renderModeCombo != null)
            {
                m_renderModeCombo.SelectedIndex = 0;
                m_renderModeCombo.SelectionChanged += (s, e) =>
                    m_screen.RenderMode = (DebugRenderMode)m_renderModeCombo.SelectedIndex;
            }

            m_screen.FrameChanged += frame =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isProgrammaticSliderUpdate = true;
                    if (m_timelineSlider != null) m_timelineSlider.Value = frame;
                    if (m_frameLabel != null) m_frameLabel.Text = frame.ToString();
                    _isProgrammaticSliderUpdate = false;
                }));

            if (m_timelineSlider != null)
            {
                m_timelineSlider.ValueChanged += (s, e) =>
                {
                    if (_isProgrammaticSliderUpdate) return;
                    m_screen.SeekToFrame((int)e.NewValue);
                };
                m_timelineSlider.PreviewMouseWheel += (s, e) =>
                {
                    if (_isProgrammaticSliderUpdate) return;
                    int target = (int)Math.Max(0, Math.Min(m_screen.TotalFrames - 1, m_timelineSlider.Value + (e.Delta > 0 ? 1 : -1)));
                    m_screen.SeekToFrame(target);
                    e.Handled = true;
                };
            }

            _ = LoadAsync();
        }

        public override void Closed()
        {
            m_viewport?.Shutdown();
            base.Closed();
        }

        public void ShowAssetTree(AntAssetViewModel sourceVm)
        {
            if (sourceVm?.AssetInstance == null || !(sourceVm.AssetInstance is AntAsset root)) return;

            var allVms = _masterGroups.SelectMany(g => g.Assets).ToList();

            var window = new AssetBankPlugin.Windows.TreePreviewer(root, sourceVm.Id, allVms)
            {
                Owner = Window.GetWindow(this),
            };
            window.AssetDoubleClicked = asset => SelectAndRevealAsset(asset);
            window.Show();
        }

        private void SelectAndRevealAsset(AntAsset asset)
        {
            AntAssetViewModel targetVm = null;
            foreach (var g in _masterGroups)
            {
                foreach (var vm in g.Assets)
                {
                    if (vm.AssetInstance != asset) continue;
                    targetVm = vm;
                    break;
                }
                if (targetVm != null) break;
            }
            if (targetVm == null) return;

            var searchText = asset.ID.ToString();
            if (m_searchBox != null)
                m_searchBox.Text = searchText;
            ApplySearchFilter(searchText);

            _selectedAsset = targetVm;

            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => ExpandAndSelectInTree(targetVm, asset)));
        }

        private void ExpandAndSelectInTree(AntAssetViewModel targetVm, AntAsset asset)
        {
            if (m_assetTreeView == null) return;

            AntTypeGroup targetGroup = null;
            foreach (var g in _filteredGroups)
            {
                foreach (var vm in g.Assets)
                {
                    if (vm.AssetInstance != asset) { continue; }
                    targetGroup = g;
                    break;
                }
                if (targetGroup != null) break;
            }
            if (targetGroup == null) return;

            var groupItem = m_assetTreeView.ItemContainerGenerator
                                .ContainerFromItem(targetGroup)
                                as System.Windows.Controls.TreeViewItem;
            if (groupItem == null) return;

            groupItem.IsExpanded = true;

            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    groupItem.UpdateLayout();

                    var assetItem = groupItem.ItemContainerGenerator
                                        .ContainerFromItem(targetVm)
                                        as System.Windows.Controls.TreeViewItem;
                    if (assetItem == null) return;

                    assetItem.IsSelected = true;
                    assetItem.BringIntoView();
                    Window.GetWindow(this)?.Activate();
                }));
        }

        private void OpenReferenceSelector()
        {
            var selectionWindow = new BankPickerDialog { Owner = Window.GetWindow(this) };

            if (selectionWindow.ShowDialog() == true)
            {
                var selectedBanks = selectionWindow.SelectedEntries.ToList();
                if (selectedBanks.Count == 0) return;

                FrostyTaskWindow.Show("Loading Reference Banks", "", (task) =>
                {
                    int progress = 0;
                    foreach (var entry in selectedBanks)
                    {
                        task.Update($"Caching {entry.Name}...", ((double)progress / selectedBanks.Count) * 100.0);
                        try
                        {
                            EbxAsset asset = App.AssetManager.GetEbx(entry);
                            dynamic antStateAsset = asset.RootObject;
                            Stream s = null;
                            int bundleId = entry.Bundles.Count > 0 ? entry.Bundles[0] : 0;

                            if (antStateAsset.StreamingGuid == Guid.Empty)
                            {
                                var res = App.AssetManager.GetResEntry(entry.Name);
                                if (res != null) s = App.AssetManager.GetRes(res);
                            }
                            else
                            {
                                var chunk = App.AssetManager.GetChunkEntry(antStateAsset.StreamingGuid);
                                if (chunk != null) { bundleId = chunk.Bundles[0]; s = App.AssetManager.GetChunk(chunk); }
                            }

                            if (s != null)
                            {
                                using (var reader = new NativeReader(s))
                                    _ = new Bank(reader, bundleId);
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger.LogError($"[AntStateEditor] Failed to load reference bank {entry.Name}: {ex.Message}");
                        }
                        progress++;
                    }
                });

                App.Logger.Log($"[AntStateEditor] Successfully loaded {selectedBanks.Count} reference banks into cache.");
            }
        }
    }
    internal class EditAnimAssetsWindow : FrostyWindow
    {
        private readonly List<AntStateAssetEditor.AnimAssetEntry> _entries;
        private readonly List<TextBox> _boxes = new List<TextBox>();

        public EditAnimAssetsWindow(List<AntStateAssetEditor.AnimAssetEntry> entries)
        {
            _entries = entries;
            Title = "  Edit Anim Assets";
            Width = 520;
            MinHeight = 150;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var outer = new StackPanel { Margin = new Thickness(10) };

            foreach (var e in entries)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lbl = new TextBlock
                {
                    Text = $"Track {e.TrackIndex}  Anim {e.AnimIndex}:",
                    Foreground = new SolidColorBrush(Color.FromRgb(187, 187, 187)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                };
                Grid.SetColumn(lbl, 0);
                row.Children.Add(lbl);

                var tb = new TextBox
                {
                    Text = e.CurrentGuid,
                    Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 206, 155)),
                    FontSize = 11,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                    CaretBrush = Brushes.White,
                    Padding = new Thickness(3, 2, 3, 2),
                };
                Grid.SetColumn(tb, 1);
                row.Children.Add(tb);
                _boxes.Add(tb);
                outer.Children.Add(row);
            }

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var save = new Button { Content = "Save", Width = 100, Height = 26, Margin = new Thickness(0, 0, 12, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, Height = 26 };
            save.Click += (s, e) => Commit();
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnRow.Children.Add(save);
            btnRow.Children.Add(cancel);
            outer.Children.Add(btnRow);

            Content = outer;
        }

        private void Commit()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                string text = _boxes[i].Text.Trim();
                if (text == _entries[i].CurrentGuid) continue;

                if (Guid.TryParse(text, out Guid g))
                {
                    _entries[i].NewKey = AntStateAssetEditor.GuidToKey(g);
                    _entries[i].WasEdited = true;
                }
                else if (ulong.TryParse(text,
                    System.Globalization.NumberStyles.HexNumber, null, out ulong h))
                {
                    _entries[i].NewKey = h;
                    _entries[i].WasEdited = true;
                }
            }
            DialogResult = true;
            Close();
        }
    }
}