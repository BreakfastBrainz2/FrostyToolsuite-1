using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;
using Frosty.Controls;
using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers.Entries;
using FrostySdk.Resources;

namespace CustomizationCreatorPlugin.Windows
{
    /// <summary>
    /// Interaction logic for AddWeaponWindow.xaml
    /// </summary>
    public partial class AddCustomizationWindow : FrostyDockableWindow
    {
        private string mBlueprintDirectory = "";
        private string mWeaponName = "";

        public AddCustomizationWindow()
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;
        }

        private void varBPDirTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            mBlueprintDirectory = varBPDirTextBox.Text.TrimEnd('/');
        }

        private void varWepNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            mWeaponName = varWepNameTextBox.Text;
        }

        private string VerifyFileName(string inFilename)
        {
            string filename = inFilename;
            if (filename.Contains("//"))
            {
                filename = filename.Replace("//", "/");
            }

            if (filename.Contains("\\"))
            {
                filename = filename.Replace("\\", "/");
            }

            return filename;
        }

        private void createButton_Click(object sender, RoutedEventArgs e)
        {
            EbxAssetEntry entry = App.AssetManager.GetEbxEntry("Win32/weapons/ai_allstarjump_bpb");

            string baseName = entry.Name.Substring(0, entry.Name.LastIndexOf('/') + 1);
            uint wepHash = (uint)Utils.HashString(mWeaponName + DateTime.Now.ToFileTime());
            string newName = $"{baseName}{wepHash}/{mWeaponName}_bpb";
            VerifyFileName(newName);
            EbxAssetEntry newBpb = CreateAsset(newName, TypeLibrary.GetType("PVZCharacterWeaponBlueprintBundle"));
            newName = newName.ToLowerInvariant().Replace("win32/", string.Empty);

            // Create the new bundle
            string bundleName = "win32/" + newName;
            BundleEntry newBundleEntry = App.AssetManager.AddBundle(bundleName, BundleType.BlueprintBundle, App.AssetManager.GetBundleEntry(entry.Bundles[0]).SuperBundleId);
            int bundleId = App.AssetManager.GetBundleId(newBundleEntry);

            newBpb.AddedBundles.Add(bundleId);

            dynamic bpbRoot = App.AssetManager.GetEbx(entry).RootObject;
            // Get the AntStateAsset to dupe
            EbxAssetEntry antStateAsset = App.AssetManager.GetEbxEntry(bpbRoot.AntStateAssets[0].External.FileGuid);

            // Get the original AntStateAsset and streaming chunk
            dynamic antStateRoot = App.AssetManager.GetEbx(antStateAsset).RootObject;
            Guid streamingGuid = antStateRoot.StreamingGuid;

            // Dupe the AntStateAsset
            string weaponName = newName.Split('/').Last().Replace("_bpb", string.Empty);
            string antStateName = $"animations/antanimations/gameplay/weapons/{wepHash}/{weaponName}/{weaponName}__3p_win32_antstate";

            EbxAssetEntry newAntState = CreateAsset(antStateName, TypeLibrary.GetType("AntStateAsset"));
            newAntState.AddedBundles.Clear();
            newAntState.AddedBundles.Add(bundleId);
            dynamic newAntRoot = App.AssetManager.GetEbx(newAntState).RootObject;

            // Dupe the streaming chunk
            // This is always an empty assetbank, but it's required
            bool needsStreamingChunk = antStateRoot.ChunkSize > 0;
            if (needsStreamingChunk)
            {
                ChunkAssetEntry chunk = App.AssetManager.GetChunkEntry(streamingGuid);
                ChunkAssetEntry newChunk = DuplicateChunk(chunk);

                newChunk.AddedBundles.Clear();
                newChunk.AddedBundles.Add(bundleId);

                // Set the chunk data to the duped chunk data
                newAntRoot.StreamingGuid = newChunk.Id;
                newAntRoot.ChunkSize = (int)App.AssetManager.GetChunk(newChunk).Length;
                App.AssetManager.ModifyEbx(newAntState.Name, App.AssetManager.GetEbx(newAntState));
            }

            //App.Logger.Log("Successfully created new weapon blueprint bundle {0}. \nAfter creating the weapon blueprint for this bundle, open {1} and assign it to Blueprint.", bundleName, newBpb.DisplayName);
            string weaponBPAssetName = $"{mBlueprintDirectory}/{mWeaponName}";
            Type weaponBPType = TypeLibrary.GetType("PVZCharacterWeaponBlueprint");

            EbxAssetEntry newWeaponBP = CreateAsset(weaponBPAssetName, weaponBPType);
            newWeaponBP.AddToBundle(bundleId);

            // Update the new BPB
            EbxAsset newBpbAsset = App.AssetManager.GetEbx(newBpb);
            dynamic newBpbRoot = newBpbAsset.RootObject;

            PointerRef newAntPr = new PointerRef(new EbxImportReference
            {
                ClassGuid = App.AssetManager.GetEbx(newAntState).RootInstanceGuid,
                FileGuid = newAntState.Guid
            });

            PointerRef newWepBPPr = new PointerRef(new EbxImportReference
            {
                ClassGuid = App.AssetManager.GetEbx(newWeaponBP).RootInstanceGuid,
                FileGuid = newWeaponBP.Guid
            });

            newBpbRoot.Blueprint = newWepBPPr;
            newBpbRoot.AntStateAssets.Add(newAntPr);
            newBpbAsset.AddDependency(newWeaponBP.Guid);
            newBpbAsset.AddDependency(newAntState.Guid);
            App.AssetManager.ModifyEbx(newBpb.Name, newBpbAsset);

            // Create the UnlockAsset
            EbxAssetEntry baseUnlock = App.AssetManager.GetEbxEntry("Gameplay/Weapons/AI/Zombie/AllStar/AI_AllStarJump/U_AI_AllStarJump");
            EbxAssetEntry newUnlock = DuplicateAsset(baseUnlock, $"{mBlueprintDirectory}/U_{mWeaponName}", false);

            EbxAsset newUnlockAsset = App.AssetManager.GetEbx(newUnlock);
            dynamic newUnlockRoot = newUnlockAsset.RootObject;

            //newUnlockRoot.Identifier = (uint)Utils.HashString($"{newUnlock.Name}{newUnlock.Guid}", true);
            //newUnlockRoot.WeaponIdentifier = (uint)Utils.HashString(newWeaponBP.Name);
            newUnlockRoot.WeaponBlueprintBundleReference.Name = bundleName.Replace("win32/", string.Empty);
            //newUnlockRoot.DebugUnlockId = newUnlock.Name.Split('/').Last();
            App.AssetManager.ModifyEbx(newUnlock.Name, newUnlockAsset);

            App.Logger.Log("Successfully created the weapon {0} and bundle {1}.", newWeaponBP.Name, bundleName);
            Close();
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private EbxAssetEntry CreateAsset(string newName, Type newType)
        {
            EbxAsset newAsset = null;

            newAsset = new EbxAsset(TypeLibrary.CreateObject(newType.Name));

            newAsset.SetFileGuid(Guid.NewGuid());

            dynamic obj = newAsset.RootObject;
            obj.Name = newName;

            AssetClassGuid guid = new AssetClassGuid(Utils.GenerateDeterministicGuid(newAsset.Objects, (Type)obj.GetType(), newAsset.FileGuid), -1);
            obj.SetInstanceGuid(guid);

            EbxAssetEntry newEntry = App.AssetManager.AddEbx(newName, newAsset);

            newEntry.ModifiedEntry.DependentAssets.AddRange(newAsset.Dependencies);

            return newEntry;
        }

        private EbxAssetEntry DuplicateAsset(EbxAssetEntry entry, string newName, bool createNew, Type newType = null)
        {
            EbxAsset asset = App.AssetManager.GetEbx(entry);
            EbxAsset newAsset = null;

            if (createNew)
            {
                newAsset = new EbxAsset(TypeLibrary.CreateObject(newType.Name));
            }
            else
            {
                using (EbxBaseWriter writer = EbxBaseWriter.CreateWriter(new MemoryStream(), EbxWriteFlags.DoNotSort | EbxWriteFlags.IncludeTransient))
                {
                    writer.WriteAsset(asset);
                    byte[] buf = writer.ToByteArray();
                    using (EbxReader reader = EbxReader.CreateReader(new MemoryStream(buf)))
                        newAsset = reader.ReadAsset<EbxAsset>();
                }
            }

            newAsset.SetFileGuid(Guid.NewGuid());

            dynamic obj = newAsset.RootObject;
            obj.Name = newName;

            AssetClassGuid guid = new AssetClassGuid(Utils.GenerateDeterministicGuid(newAsset.Objects, (Type)obj.GetType(), newAsset.FileGuid), -1);
            obj.SetInstanceGuid(guid);

            EbxAssetEntry newEntry = App.AssetManager.AddEbx(newName, newAsset);

            newEntry.AddedBundles.AddRange(entry.EnumerateBundles());
            newEntry.ModifiedEntry.DependentAssets.AddRange(newAsset.Dependencies);

            return newEntry;
        }

        private ResAssetEntry DuplicateRes(ResAssetEntry entry, string name, ResourceType resType)
        {
            if (App.AssetManager.GetResEntry(name) == null)
            {
                ResAssetEntry newEntry;
                using (NativeReader reader = new NativeReader(App.AssetManager.GetRes(entry)))
                {
                    newEntry = App.AssetManager.AddRes(name, resType, entry.ResMeta, reader.ReadToEnd(), entry.EnumerateBundles().ToArray());
                }
                return newEntry;
            }
            else
            // A resource with this name already exists
            {
                return null;
            }
        }

        private ChunkAssetEntry DuplicateChunk(ChunkAssetEntry entry, Texture texture = null)
        {
            byte[] random = new byte[16];
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            // Create a unique GUID for the new chunk
            while (true)
            {
                rng.GetBytes(random);

                random[15] |= 1;

                if (App.AssetManager.GetChunkEntry(new Guid(random)) == null)
                {
                    break;
                }
            }
            Guid newGuid;
            using (NativeReader reader = new NativeReader(App.AssetManager.GetChunk(entry)))
            {
                newGuid = App.AssetManager.AddChunk(reader.ReadToEnd(), new Guid(random), texture, entry.EnumerateBundles().ToArray());
            }

            ChunkAssetEntry newEntry = App.AssetManager.GetChunkEntry(newGuid);

            return newEntry;
        }
    }
}
