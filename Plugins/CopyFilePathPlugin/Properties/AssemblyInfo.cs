using CopyFilePathPlugin;
using Frosty.Core.Attributes;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: ComVisible(false)]
[assembly: Guid("4b612468-9b6a-4304-88a5-055c3575eb3e")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]

[assembly: PluginDisplayName("Copy File Path")]
[assembly: PluginAuthor("AdamRaichu")]
[assembly: PluginVersion("1.0.0.1")]

[assembly: RegisterDataExplorerContextMenu(typeof(CopyFilePathMenuItem))]