using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FrostySdk;
using FrostySdk.Deobfuscators;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;

namespace FrostyCmd
{
    internal static class WriterExtensions
    {
        public static void WriteObfuscatedString(this NativeWriter writer, string str)
        {
            byte[] b = Encoding.UTF8.GetBytes(str);
            byte[] key = new byte[] { 0x46, 0x52, 0x4F, 0x53, 0x54, 0x59 };

            writer.Write7BitEncodedInt(b.Length);
            for (int i = 0; i < b.Length; i++)
            {
                byte bv = (byte)(b[i] ^ key[i % key.Length]);
                writer.Write(bv);
            }
        }
    }

    public class ProfileFlags
    {
        byte MustAddChunks;
        byte EbxVersion;
        byte RequiresKey;
        byte ReadOnly;
        byte ContainsEAC;

        public ProfileFlags(byte mustAddChunks, byte ebxVersion, byte requiresKey, byte readOnly = 0, byte containsEAC = 0)
        {
            MustAddChunks = mustAddChunks;
            EbxVersion = ebxVersion;
            RequiresKey = requiresKey;
            ReadOnly = readOnly;
            ContainsEAC = containsEAC;
        }

        public void Write(NativeWriter writer)
        {
            writer.Write(MustAddChunks);
            writer.Write(EbxVersion);
            writer.Write(RequiresKey);
            writer.Write(ReadOnly);
            writer.Write(ContainsEAC);
        }
    }

    public class ProfileCreator
    {
        Dictionary<string, byte[]> blobs = new Dictionary<string, byte[]>();

        #region -- Helper Functions --

        private byte[] CreateSources(params string[] paths)
        {
            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {
                writer.Write(paths.Length);
                foreach (string path in paths)
                {
                    string[] arr = path.Split(';');
                    byte subPaths = (byte)(bool.Parse(arr[1]) ? 1 : 0);

                    writer.WriteObfuscatedString(arr[0]);
                    writer.Write(subPaths);
                }

                return writer.ToByteArray();
            }
        }

        static byte[] CreateBanner(string banner)
        {
            FileInfo fi = new FileInfo("Banners/" + banner + ".png");
            byte[] buffer = null;

            if (fi.Exists)
            {
                using (NativeReader reader = new NativeReader(new FileStream("Banners/" + banner + ".png", FileMode.Open, FileAccess.Read)))
                    buffer = reader.ReadToEnd();
            }
            else
            {
                buffer = new byte[] { };
            }

            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {
                writer.Write(buffer.Length);
                writer.Write(buffer);
                return writer.ToByteArray();
            }
        }

        #endregion

        #region -- Profiles --
        private void CreatePVZ1Profile()
        {
            string key = "PVZ.Main_Win64_Retail";
            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {
                writer.WriteObfuscatedString("Plants vs Zombies™ Garden Warfare");
                writer.Write((int)ProfileVersion.PlantsVsZombiesGardenWarfare);
                writer.WriteObfuscatedString("pvz1");
                writer.WriteObfuscatedString(typeof(PVZDeobfuscator).Name);
                writer.WriteObfuscatedString(AssetManager.GetLoaderName("LegacyAssetLoader"));
                writer.Write(CreateSources("Update\\Patch\\Data;false", "Update;true", "Data;false"));
                writer.WriteObfuscatedString("PVZ1SDK");
                writer.Write(CreateBanner("PVZ"));
                writer.WriteObfuscatedString("_pvz/Shaders/_System/BaseShaders/default_textures/white");
                writer.WriteObfuscatedString("_pvz/Shaders/_System/BaseShaders/default_textures/normal_default");
                writer.WriteObfuscatedString("_pvz/Shaders/_System/BaseShaders/default_textures/white");
                writer.WriteObfuscatedString("_pvz/Shaders/_System/BaseShaders/default_textures/white");
                writer.Write(0); // shared bundle names
                writer.Write(0); // ignored res types

                // Flags (MustAddChunks, EbxVersion, RequiresKey)
                ProfileFlags pf = new ProfileFlags(0, 2, 0, 1);
                pf.Write(writer);

                blobs.Add(key, writer.ToByteArray());
            }
        }

        private void CreatePVZ2Profile()
        {
            string key = "GW2.Main_Win64_Retail";
            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {
                writer.WriteObfuscatedString("Plants vs Zombies™ Garden Warfare 2");
                writer.Write((int)ProfileVersion.PlantsVsZombiesGardenWarfare2);
                writer.WriteObfuscatedString("pvz2");
                writer.WriteObfuscatedString(typeof(PVZDeobfuscator).Name);
                writer.WriteObfuscatedString(AssetManager.GetLoaderName("LegacyAssetLoader"));
                writer.Write(CreateSources("Update\\Patch\\Data;false", "Update;true", "Data;false"));
                writer.WriteObfuscatedString("PVZ2SDK");
                writer.Write(CreateBanner("PVZ2"));
                writer.WriteObfuscatedString("art/Shaders/_System/BaseShaders/default_textures/white");
                writer.WriteObfuscatedString("art/Shaders/_System/BaseShaders/default_textures/normal_default");
                writer.WriteObfuscatedString("art/Shaders/_System/BaseShaders/default_textures/white");
                writer.WriteObfuscatedString("art/Shaders/_System/BaseShaders/default_textures/white");
                writer.Write(0); // shared bundle names
                writer.Write(0); // ignored res types

                // Flags (MustAddChunks, EbxVersion, RequiresKey)
                ProfileFlags pf = new ProfileFlags(0, 2, 0);
                pf.Write(writer);

                blobs.Add(key, writer.ToByteArray());
            }
        }

        private void CreatePVZ3Profile()
        {
            string key = "PVZBattleforNeighborville";
            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {
                writer.WriteObfuscatedString("Plants vs Zombies: Battle for Neighborville™");
                writer.Write((int)ProfileVersion.PlantsVsZombiesBattleforNeighborville);
                writer.WriteObfuscatedString("PVZ3");
                writer.WriteObfuscatedString(typeof(NullDeobfuscator).Name);
                writer.WriteObfuscatedString(AssetManager.GetLoaderName("CasAssetLoader"));
                writer.Write(CreateSources("Patch;false", "Update;true", "Data;false"));
                writer.WriteObfuscatedString("PVZ3SDK");
                writer.Write(CreateBanner("PVZ3"));
                writer.WriteObfuscatedString("GW2_art/Shaders/_System/BaseShaders/default_textures/white");
                writer.WriteObfuscatedString("GW2_art/Shaders/_System/BaseShaders/default_textures/normal_default");
                writer.WriteObfuscatedString("GW2_art/Shaders/_System/BaseShaders/default_textures/white");
                writer.WriteObfuscatedString("GW2_art/Shaders/_System/BaseShaders/default_textures/white");
                writer.Write(0); // shared bundle names
                writer.Write(0); // ignored res types

                // Flags (MustAddChunks, EbxVersion, RequiresKey)
                ProfileFlags pf = new ProfileFlags(0, 5, 1, 1, 1);
                pf.Write(writer);

                blobs.Add(key, writer.ToByteArray());
            }
        }
        #endregion

        public ProfileCreator()
        {
        }

        public void CreateProfiles()
        {
            CreatePVZ1Profile();
            CreatePVZ2Profile();
            CreatePVZ3Profile();

            using (NativeWriter writer = new NativeWriter(new FileStream(@"..\..\..\..\FrostySdk\Profiles.bin", FileMode.Create)))
            {
                writer.Write(blobs.Count);

                long offset = 0;
                foreach (string key in blobs.Keys)
                {
                    writer.WriteObfuscatedString(key);
                    writer.Write(offset);
                    offset += blobs[key].Length;
                }

                foreach (byte[] blob in blobs.Values)
                    writer.Write(blob);
            }
        }
    }
}
