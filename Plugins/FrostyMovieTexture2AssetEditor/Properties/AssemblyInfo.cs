using Frosty.Core.Attributes;
using System.Runtime.InteropServices;
using System.Windows;
using MovieTexture2AssetEditorPlugin;

[assembly: ComVisible(false)]
[assembly: Guid("4b612468-9b6a-4304-88a5-055c3575eb3d")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]

[assembly: PluginDisplayName("MovieTexture2Asset Editor Utils")]
[assembly: PluginAuthor("AdamRaichu")]
[assembly: PluginVersion("1.0.1.0")]

// Register your menu extension or editor
[assembly: RegisterAssetDefinition("MovieTexture2Asset", typeof(MovieTexture2AssetDefition))] // Register the asset definition
[assembly: RegisterAssetDefinition("MovieTextureAsset", typeof(MovieTexture2AssetDefition))]
