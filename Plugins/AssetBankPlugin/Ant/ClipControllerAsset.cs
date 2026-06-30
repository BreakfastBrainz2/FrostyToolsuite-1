using AssetBankPlugin.Enums;
using AssetBankPlugin.Extensions;
using FrostySdk;
using System;
using System.Collections.Generic;

namespace AssetBankPlugin.Ant
{
    public class ClipControllerAsset : AntAsset
    {
        public override string Name { get; set; }
        public override Guid ID { get; set; }

        public Guid[] Anims { get; set; }
        public Guid Anim { get; set; }
        public Guid DofCodecAnim { get; set; }
        public Guid Target { get; set; }
        public float NumTicks { get; set; }
        public float TickOffset { get; set; }
        public float FPS { get; set; }
        public float TimeScale { get; set; }
        public float Distance { get; set; }
        public int TrajectoryAnimIndex { get; set; }
        public int Modes { get; set; }
        public Guid TagCollectionSet { get; set; }

        public override void SetData(Dictionary<string, object> data)
        {
            ParseBasicData(data);

            if (data.TryGetValue("Target", out object t)) Target = SafeGuid(t);
            if (data.TryGetValue("Anims", out object anms)) Anims = ConvertArray<Guid>(anms);
            if (data.TryGetValue("Anim", out object anim)) Anim = SafeGuid(anim);
            if (data.TryGetValue("DofCodecAnim", out object d)) DofCodecAnim = SafeGuid(d);

            if (data.TryGetValue("NumTicks", out object nt)) NumTicks = Convert.ToSingle(nt);
            if (data.TryGetValue("TickOffset", out object to)) TickOffset = Convert.ToSingle(to);
            if (data.TryGetValue("FPS", out object fps)) FPS = Convert.ToSingle(fps);
            if (data.TryGetValue("TimeScale", out object ts)) TimeScale = Convert.ToSingle(ts);
            if (data.TryGetValue("FPSScale", out object fpss)) TimeScale = Convert.ToSingle(fpss);
            if (data.TryGetValue("Distance", out object dist)) Distance = Convert.ToSingle(dist);
            if (data.TryGetValue("TrajectoryAnimIndex", out object tai)) TrajectoryAnimIndex = Convert.ToInt32(tai);
            if (data.TryGetValue("DeltaTrajectory", out object dt)) TrajectoryAnimIndex = Convert.ToInt32(dt);
            if (data.TryGetValue("Modes", out object m)) Modes = Convert.ToInt32(m);

            if (data.TryGetValue("TagCollectionSet", out object tcs)) TagCollectionSet = SafeGuid(tcs);
            if (TagCollectionSet == Guid.Empty && data.TryGetValue("TagCollectionSetAsset", out object tcsa))
            {
                TagCollectionSet = SafeGuid(tcsa);
            }
        }
    }
}