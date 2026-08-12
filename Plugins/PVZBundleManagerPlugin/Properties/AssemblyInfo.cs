using System.Reflection;
using System.Runtime.InteropServices;
using Frosty.Core.Attributes;
using FrostySdk;
using PvZBundleManagerPlugin.Extensions.DECMEs;
using PvZBundleManagerPlugin.Extensions.MEs;
using PvZBundleManagerPlugin.Extensions.StartupActions;

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components. If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("177574de-1b32-4c3e-b1e7-e439649ee6ea")]

// Plugin metadata
[assembly: PluginAuthor("BreakfastBrainz2")]
[assembly: PluginDisplayName("PvZ Bundle Manager")]
[assembly: PluginVersion("1.0.1.0")]

// Plugin contents
[assembly: RegisterDataExplorerContextMenu(typeof(ManageBundles))]
[assembly: RegisterStartupAction(typeof(BundleManagerInitialization))]
//[assembly: RegisterMenuExtension(typeof(OptimizeBundles))]
[assembly: RegisterMenuExtension(typeof(ManualAdd))]