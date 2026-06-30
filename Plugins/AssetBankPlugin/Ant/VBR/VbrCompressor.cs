using System;
using System.Collections.Generic;
using Assimp;
using Frosty.Core;

namespace AssetBankPlugin.Ant.VBR
{
    internal static class VbrCompressor
    {
        private static readonly float[,] DctFwd;

        private static readonly float[,] DctInv = new float[8, 8]
        {
            {  0.12500000f,  0.24519631f,  0.23096988f,  0.20786740f,  0.17677669f,  0.13889255f,  0.09567086f,  0.04877256f },
            {  0.12500000f,  0.20786740f,  0.09567086f, -0.04877258f, -0.17677669f, -0.24519633f, -0.23096988f, -0.13889250f },
            {  0.12500000f,  0.13889255f, -0.09567088f, -0.24519633f, -0.17677666f,  0.04877260f,  0.23096989f,  0.20786734f },
            {  0.12500000f,  0.04877256f, -0.23096991f, -0.13889250f,  0.17677675f,  0.20786734f, -0.09567098f, -0.24519630f },
            {  0.12500000f, -0.04877258f, -0.23096988f,  0.13889261f,  0.17677669f, -0.20786744f, -0.09567074f,  0.24519636f },
            {  0.12500000f, -0.13889259f, -0.09567078f,  0.24519631f, -0.17677681f, -0.04877255f,  0.23096983f, -0.20786740f },
            {  0.12500000f, -0.20786741f,  0.09567090f,  0.04877252f, -0.17677663f,  0.24519633f, -0.23096994f,  0.13889278f },
            {  0.12500000f, -0.24519633f,  0.23096989f, -0.20786744f,  0.17677671f, -0.13889271f,  0.09567098f, -0.04877289f },
        };

        private const int NumQTables = 256;

        static VbrCompressor()
        {
            DctFwd = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int k = 0; k < 8; k++)
                    DctFwd[j, k] = (float)Math.Cos(Math.PI * (j + 0.5) * k / 8.0);
        }

        public static VbrAnimationAsset Compress(
            Scene scene,
            VbrAnimationAsset template,
            bool bigEndian,
            float maxRotErrPct = 0.42f,
            float maxTransErrPct = 0.42f,
            float maxTrajErrPct = 0.42f,
            float constThresh = 0.00001f)
        {
            int totalQ = template.QuaternionCount + template.ConstQuaternionCount;
            int totalV = template.Vector3Count + template.ConstVector3Count;
            int totalF = template.NumFloat + template.ConstFloatCount;

            ExtractFromScene(scene, template, totalQ, totalV, totalF,
                out float[][][] quats,    
                out float[][][] vecs,     
                out float[][] floats,   
                out int frameCount,
                out bool cycle);

            int totalCh = totalQ + totalV + totalF;

            bool[] constCh = MarkConstantChannels(
                quats, vecs, floats, frameCount, totalQ, totalV, totalF, constThresh);

            int cc = 0;
            for (int i = 0; i < totalCh; i++) if (constCh[i]) cc++;
            if (totalCh > 0 && (float)cc / totalCh < 0.1f)
                for (int i = 0; i < totalCh; i++) constCh[i] = false;

            int cQ = 0, cV = 0, cF = 0;
            for (int i = 0; i < totalQ; i++) if (constCh[i]) cQ++;
            for (int i = 0; i < totalV; i++) if (constCh[totalQ + i]) cV++;
            for (int i = 0; i < totalF; i++) if (constCh[totalQ + totalV + i]) cF++;
            int aQ = totalQ - cQ;
            int aV = totalV - cV;
            int aF = totalF - cF;

            int stride = aQ * 4 + aV * 3 + aF;
            int alignedStride = (stride + 3) & ~3;

            bool sepTraj = aV > 1 && !constCh[totalQ];

            float[] cData = SaveConstantChannels(
                quats, vecs, floats, constCh, totalQ, totalV, totalF, frameCount);

            ComputeMinMax(quats, vecs, floats, constCh, frameCount,
                totalQ, totalV, totalF, aQ, aV, aF,
                out float[] qMinCh, out float[] qMaxCh,
                out float[] vMinCh, out float[] vMaxCh,
                out float[] fMinCh, out float[] fMaxCh);

            float quatMin = 0f, quatMax = 0f;
            float v3Min = 0f, v3Max = 0f;
            float fltMin = 0f, fltMax = 0f;
            float tjMin = 0f, tjMax = 0f;

            int cdi = 0;
            for (int i = 0; i < cQ * 4; i++, cdi++) { quatMin = Math.Min(quatMin, cData[cdi]); quatMax = Math.Max(quatMax, cData[cdi]); }
            for (int i = 0; i < cV * 3; i++, cdi++) { v3Min = Math.Min(v3Min, cData[cdi]); v3Max = Math.Max(v3Max, cData[cdi]); }
            for (int i = 0; i < cF; i++, cdi++) { fltMin = Math.Min(fltMin, cData[cdi]); fltMax = Math.Max(fltMax, cData[cdi]); }

            for (int i = 0; i < aQ; i++) { quatMin = Math.Min(quatMin, qMinCh[i]); quatMax = Math.Max(quatMax, qMaxCh[i]); }

            if (aV > 0)
            {
                if (sepTraj) { tjMin = vMinCh[0]; tjMax = vMaxCh[0]; }
                else { v3Min = Math.Min(v3Min, vMinCh[0]); v3Max = Math.Max(v3Max, vMaxCh[0]); }
            }
            for (int i = 1; i < aV; i++) { v3Min = Math.Min(v3Min, vMinCh[i]); v3Max = Math.Max(v3Max, vMaxCh[i]); }

            for (int i = 0; i < aF; i++) { fltMin = Math.Min(fltMin, fMinCh[i]); fltMax = Math.Max(fltMax, fMaxCh[i]); }

            if (!sepTraj) { tjMin = v3Min; tjMax = v3Max; }

            for (int i = 0; i < aQ; i++) { qMinCh[i] = quatMin; qMaxCh[i] = quatMax; }
            for (int i = 0; i < aV; i++)
            {
                bool isTraj = i == 0 && sepTraj;
                vMinCh[i] = isTraj ? tjMin : v3Min;
                vMaxCh[i] = isTraj ? tjMax : v3Max;
            }
            for (int i = 0; i < aF; i++) { fMinCh[i] = fltMin; fMaxCh[i] = fltMax; }

            float[] errBlock = BuildErrorBlock(stride, aQ, aV, sepTraj,
                maxRotErrPct / 100f,
                maxTransErrPct / 100f,
                maxTrajErrPct / 100f);

            NormalizeConstantData(cData, cQ, cV, cF,
                quatMin, quatMax, v3Min, v3Max, fltMin, fltMax);

            float[] constPalette = BuildConstantPalette(cData, cQ, cV, cF, template.Name ?? "");

            float[] norm = NormalizeAnimated(quats, vecs, floats, constCh, frameCount,
                totalQ, totalV, totalF, aQ, aV, aF, alignedStride,
                qMinCh, qMaxCh, vMinCh, vMaxCh, fMinCh, fMaxCh);

            int frameBlocks = (frameCount + 7) >> 3;

            if (stride == 0)
            {
                byte[] dataBlob0 = BitPackHeaderOnly(cData, constCh, constPalette,
                    totalQ, totalV, totalF, cQ, cV, cF, frameCount, cycle,
                    out ushort kts0, out ushort ccms0, out bool noChMap0, out bool noKT0);

                return BuildResult(template, frameCount, aQ, aV, aF, cQ, cV, cF,
                    quatMin, quatMax, tjMin, tjMax, v3Min, v3Max, fltMin, fltMax, 0f,
                    (ushort)constPalette.Length, kts0, ccms0, noKT0, noChMap0,
                    false, cycle, sepTraj, totalQ,
                    constPalette, Array.Empty<ushort>(), dataBlob0);
            }

            float[] dctData = ForwardDCT(norm, frameCount, alignedStride, frameBlocks);

            GetDctMinMax(dctData, frameBlocks, stride, alignedStride,
                out float dctMin, out float dctMax);
            float dct = Math.Max(Math.Abs(dctMin), Math.Abs(dctMax));
            if (dct == 0f) dct = 1f;

            float[,] qTables = InitQuantizationTables(dctMin, dctMax);

            int numShorts = stride * 8 + 3;       
            short[] intermediate = new short[frameBlocks * numShorts];

            for (int fb = 0; fb < frameBlocks; fb++)
            {
                DetermineQuantTables(fb, frameCount, dctData, norm, qTables, errBlock,
                    stride, alignedStride, aQ, aV, sepTraj,
                    out int rt, out int tjt, out int tat);

                intermediate[fb * numShorts + 0] = (short)rt;
                intermediate[fb * numShorts + 1] = (short)tjt;
                intermediate[fb * numShorts + 2] = (short)tat;

                QuantizeBlock(fb, frameCount, dctData, qTables, intermediate,
                    stride, alignedStride, aQ, aV, sepTraj, rt, tjt, tat, numShorts);
            }

            int[] numBits = ComputeNumBits(intermediate, stride, frameBlocks, numShorts,
                out bool fastBit);

            byte[] data = BitPackAll(constCh, cData, constPalette, intermediate, numBits,
                frameCount, frameBlocks,
                totalQ, totalV, totalF, aQ, aV, aF,
                stride, alignedStride, sepTraj, cycle,
                bigEndian, numShorts,
                out ushort[] frameBlockSizes,
                out ushort keyTimeSize, out ushort constChanMapSize,
                out bool noKeyTimes, out bool noChannelMap);

            return BuildResult(template, frameCount, aQ, aV, aF, cQ, cV, cF,
                quatMin, quatMax, tjMin, tjMax, v3Min, v3Max, fltMin, fltMax, dct,
                (ushort)constPalette.Length, keyTimeSize, constChanMapSize,
                noKeyTimes, noChannelMap, fastBit, cycle, sepTraj, totalQ,
                constPalette, frameBlockSizes, data);
        }

        private static void ExtractFromScene(
            Scene scene, VbrAnimationAsset template,
            int totalQ, int totalV, int totalF,
            out float[][][] quats, out float[][][] vecs, out float[][] floats,
            out int frameCount, out bool cycle)
        {
            float fps = template.FPS > 0f ? template.FPS : 30f;

            Animation anim = (scene.AnimationCount > 0) ? scene.Animations[0] : null;

            double durationSec = 0.0;
            if (anim != null && anim.TicksPerSecond > 0.0)
                durationSec = anim.DurationInTicks / anim.TicksPerSecond;
            frameCount = Math.Max(2, (int)Math.Ceiling(durationSec * fps) + 1);

            cycle = (template.Flags & 1) != 0;

            var animMap = new Dictionary<string, NodeAnimationChannel>(StringComparer.OrdinalIgnoreCase);
            if (anim != null)
                foreach (var ch in anim.NodeAnimationChannels)
                    animMap[ch.NodeName] = ch;

            App.Logger.Log("[DBG] FBX anim node count=" + animMap.Count + " fps=" + fps + " frames=" + (int)Math.Max(2, (anim != null && anim.TicksPerSecond > 0.0 ? (int)Math.Ceiling(anim.DurationInTicks / anim.TicksPerSecond * fps) + 1 : 2)));
            int dbgNodeLimit = 0;
            foreach (var dbgKey in animMap.Keys) { App.Logger.Log("[DBG] FBX node: '" + dbgKey + "'"); if (++dbgNodeLimit >= 8) break; }

            List<string> quatNames = new List<string>();
            List<string> vecNames = new List<string>();
            List<string> floatNames = new List<string>();

            if (template.OrderedChannels != null)
            {
                foreach (var ch in template.OrderedChannels)
                {
                    string raw = ch.Name ?? "";
                    string name = raw.Replace(".q", "").Replace(".t", "").Replace(".s", "").Trim();
                    switch (ch.Type)
                    {
                        case BoneChannelType.Rotation: quatNames.Add(name); break;
                        case BoneChannelType.Position:
                        case BoneChannelType.Scale: vecNames.Add(name); break;
                        default: floatNames.Add(name); break;
                    }
                }
            }

            App.Logger.Log("[DBG] quatNames=" + quatNames.Count + " vecNames=" + vecNames.Count + " floatNames=" + floatNames.Count);
            for (int dbgI = 0; dbgI < Math.Min(4, quatNames.Count); dbgI++)
                App.Logger.Log("[DBG] quat[" + dbgI + "]='" + quatNames[dbgI] + "' hit=" + (FindNode(animMap, quatNames[dbgI]) != null));
            for (int dbgI = 0; dbgI < Math.Min(4, vecNames.Count); dbgI++)
                App.Logger.Log("[DBG] vec[" + dbgI + "]='" + vecNames[dbgI] + "' hit=" + (FindNode(animMap, vecNames[dbgI]) != null));

            while (quatNames.Count < totalQ) quatNames.Add("");
            while (vecNames.Count < totalV) vecNames.Add("");
            while (floatNames.Count < totalF) floatNames.Add("");

            quats = new float[totalQ][][];
            vecs = new float[totalV][][];
            floats = new float[totalF][];

            for (int i = 0; i < totalQ; i++)
            {
                quats[i] = new float[frameCount][];
                var nodeCh = FindNode(animMap, quatNames[i]);
                for (int f = 0; f < frameCount; f++)
                    quats[i][f] = SampleQuat(nodeCh, (double)f / fps, anim);
            }

            for (int i = 0; i < totalV; i++)
            {
                vecs[i] = new float[frameCount][];
                var nodeCh = FindNode(animMap, vecNames[i]);
                for (int f = 0; f < frameCount; f++)
                    vecs[i][f] = SampleVec3(nodeCh, (double)f / fps, anim, i == 0);
            }

            for (int i = 0; i < totalF; i++)
            {
                floats[i] = new float[frameCount];
                for (int f = 0; f < frameCount; f++) floats[i][f] = 0f;
            }
        }

        private static NodeAnimationChannel FindNode(
            Dictionary<string, NodeAnimationChannel> map, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            NodeAnimationChannel ch;
            if (map.TryGetValue(name, out ch)) return ch;
            if (name.Length > 2 && map.TryGetValue(name.Substring(0, name.Length - 2), out ch)) return ch;
            if (name.Length > 1 && map.TryGetValue(name.Substring(0, name.Length - 1), out ch)) return ch;
            return null;
        }

        private static float[] SampleQuat(NodeAnimationChannel nodeCh, double t, Animation anim)
        {
            if (nodeCh == null || nodeCh.RotationKeyCount == 0)
                return new float[] { 0f, 0f, 0f, 1f };

            double ticks = t * (anim != null && anim.TicksPerSecond > 0.0
                ? anim.TicksPerSecond : 1.0);

            var keys = nodeCh.RotationKeys;
            if (keys.Count == 1)
                return QuatToArray(keys[0].Value);

            int lo = keys.Count - 2;
            for (int k = 0; k < keys.Count - 1; k++)
            {
                if (ticks <= keys[k + 1].Time) { lo = k; break; }
            }

            double span = keys[lo + 1].Time - keys[lo].Time;
            float alpha = span > 0.0 ? (float)((ticks - keys[lo].Time) / span) : 0f;
            alpha = Math.Max(0f, Math.Min(1f, alpha));

            Assimp.Quaternion a = keys[lo].Value;
            Assimp.Quaternion b = keys[lo + 1].Value;
            Assimp.Quaternion q = Assimp.Quaternion.Slerp(a, b, alpha);
            return QuatToArray(q);
        }

        private static float[] SampleVec3(NodeAnimationChannel nodeCh, double t, Animation anim, bool isRoot)
        {
            if (nodeCh == null || nodeCh.PositionKeyCount == 0)
                return new float[] { 0f, 0f, 0f };

            double ticks = t * (anim != null && anim.TicksPerSecond > 0.0
                ? anim.TicksPerSecond : 1.0);

            var keys = nodeCh.PositionKeys;
            if (keys.Count == 1)
                return Vec3ToArray(keys[0].Value);

            int lo = keys.Count - 2;
            for (int k = 0; k < keys.Count - 1; k++)
            {
                if (ticks <= keys[k + 1].Time) { lo = k; break; }
            }

            double span = keys[lo + 1].Time - keys[lo].Time;
            float alpha = span > 0.0 ? (float)((ticks - keys[lo].Time) / span) : 0f;
            alpha = Math.Max(0f, Math.Min(1f, alpha));

            var va = keys[lo].Value;
            var vb = keys[lo + 1].Value;
            return new float[]
            {
                va.X + (vb.X - va.X) * alpha,
                va.Y + (vb.Y - va.Y) * alpha,
                va.Z + (vb.Z - va.Z) * alpha,
            };
        }

        private static float[] QuatToArray(Assimp.Quaternion q)
            => new float[] { q.X, q.Y, q.Z, q.W };

        private static float[] Vec3ToArray(Vector3D v)
            => new float[] { v.X, v.Y, v.Z };

        private static bool[] MarkConstantChannels(
            float[][][] quats, float[][][] vecs, float[][] floats,
            int frameCount, int totalQ, int totalV, int totalF, float threshold)
        {
            bool[] c = new bool[totalQ + totalV + totalF];
            int idx = 0;

            for (int i = 0; i < totalQ; i++, idx++)
            {
                if (frameCount < 2) { c[idx] = true; continue; }
                float[] ref4 = quats[i][0];
                float s0 = 0f, s1 = 0f, s2 = 0f, s3 = 0f;
                for (int f = 1; f < frameCount; f++)
                {
                    float[] cur = quats[i][f];
                    s0 += Math.Abs(ref4[0] - cur[0]);
                    s1 += Math.Abs(ref4[1] - cur[1]);
                    s2 += Math.Abs(ref4[2] - cur[2]);
                    s3 += Math.Abs(ref4[3] - cur[3]);
                }
                c[idx] = s0 <= threshold && s1 <= threshold && s2 <= threshold && s3 <= threshold;
            }

            for (int i = 0; i < totalV; i++, idx++)
            {
                if (frameCount < 2) { c[idx] = true; continue; }
                float[] ref3 = vecs[i][0];
                float s0 = 0f, s1 = 0f, s2 = 0f;
                for (int f = 1; f < frameCount; f++)
                {
                    float[] cur = vecs[i][f];
                    s0 += Math.Abs(ref3[0] - cur[0]);
                    s1 += Math.Abs(ref3[1] - cur[1]);
                    s2 += Math.Abs(ref3[2] - cur[2]);
                }
                c[idx] = s0 <= threshold && s1 <= threshold && s2 <= threshold;
            }

            for (int i = 0; i < totalF; i++, idx++)
            {
                if (frameCount < 2) { c[idx] = true; continue; }
                float sum = 0f;
                float ref1 = floats[i][0];
                for (int f = 1; f < frameCount; f++)
                    sum += Math.Abs(ref1 - floats[i][f]);
                c[idx] = sum <= threshold;
            }

            return c;
        }

        private static float[] SaveConstantChannels(
            float[][][] quats, float[][][] vecs, float[][] floats,
            bool[] constCh, int totalQ, int totalV, int totalF, int frameCount)
        {
            int size = 0;
            for (int i = 0; i < totalQ; i++) if (constCh[i]) size += 4;
            for (int i = 0; i < totalV; i++) if (constCh[totalQ + i]) size += 3;
            for (int i = 0; i < totalF; i++) if (constCh[totalQ + totalV + i]) size++;

            float[] data = new float[size];
            int pos = 0;

            for (int i = 0; i < totalQ; i++)
                if (constCh[i]) { float[] q = quats[i][0]; data[pos++] = q[0]; data[pos++] = q[1]; data[pos++] = q[2]; data[pos++] = q[3]; }
            for (int i = 0; i < totalV; i++)
                if (constCh[totalQ + i]) { float[] v = vecs[i][0]; data[pos++] = v[0]; data[pos++] = v[1]; data[pos++] = v[2]; }
            for (int i = 0; i < totalF; i++)
                if (constCh[totalQ + totalV + i]) data[pos++] = floats[i][0];

            return data;
        }

        private static void ComputeMinMax(
            float[][][] quats, float[][][] vecs, float[][] floats,
            bool[] constCh, int frameCount,
            int totalQ, int totalV, int totalF, int aQ, int aV, int aF,
            out float[] qMin, out float[] qMax,
            out float[] vMin, out float[] vMax,
            out float[] fMin, out float[] fMax)
        {
            qMin = new float[aQ]; qMax = new float[aQ];
            vMin = new float[aV]; vMax = new float[aV];
            fMin = new float[aF]; fMax = new float[aF];

            int qi = 0;
            for (int i = 0; i < totalQ; i++)
            {
                if (constCh[i]) continue;
                float mn = quats[i][0][0], mx = quats[i][0][0];
                for (int f = 0; f < frameCount; f++)
                {
                    float[] q = quats[i][f];
                    for (int c = 0; c < 4; c++) { if (q[c] < mn) mn = q[c]; if (q[c] > mx) mx = q[c]; }
                }
                qMin[qi] = mn; qMax[qi] = mx; qi++;
            }

            int vi = 0;
            for (int i = 0; i < totalV; i++)
            {
                if (constCh[totalQ + i]) continue;
                float mn = vecs[i][0][0], mx = vecs[i][0][0];
                for (int f = 0; f < frameCount; f++)
                {
                    float[] v = vecs[i][f];
                    for (int c = 0; c < 3; c++) { if (v[c] < mn) mn = v[c]; if (v[c] > mx) mx = v[c]; }
                }
                vMin[vi] = mn; vMax[vi] = mx; vi++;
            }

            int fi = 0;
            for (int i = 0; i < totalF; i++)
            {
                if (constCh[totalQ + totalV + i]) continue;
                float mn = floats[i][0], mx = floats[i][0];
                for (int f = 1; f < frameCount; f++) { if (floats[i][f] < mn) mn = floats[i][f]; if (floats[i][f] > mx) mx = floats[i][f]; }
                fMin[fi] = mn; fMax[fi] = mx; fi++;
            }
        }

        private static float[] BuildErrorBlock(
            int stride, int aQ, int aV, bool sepTraj,
            float rotErr, float transErr, float trajErr)
        {
            float[] e = new float[stride];
            int pos = 0;

            for (int i = 0; i < aQ * 4; i++) e[pos++] = rotErr;

            if (aV > 0)
            {
                if (sepTraj)
                {
                    for (int i = 0; i < 3; i++) e[pos++] = trajErr;     
                    if (pos < stride) e[pos++] = transErr;                 
                }
                int transStart = sepTraj ? aQ * 4 + 4 : aQ * 4;
                for (int p = transStart; p < stride; p++) e[p] = transErr;
                if (!sepTraj) pos = stride;   
            }

            return e;
        }

        private static void NormalizeConstantData(
            float[] cData, int cQ, int cV, int cF,
            float quatMin, float quatMax,
            float v3Min, float v3Max,
            float fltMin, float fltMax)
        {
            float qRng = (quatMax - quatMin) != 0f ? 1f / (quatMax - quatMin) : 1f;
            float vRng = (v3Max - v3Min) != 0f ? 1f / (v3Max - v3Min) : 1f;
            float fRng = (fltMax - fltMin) != 0f ? 1f / (fltMax - fltMin) : 1f;

            int pos = 0;
            for (int i = 0; i < cQ * 4; i++, pos++) cData[pos] = (cData[pos] - quatMin) * qRng;
            for (int i = 0; i < cV * 3; i++, pos++) cData[pos] = (cData[pos] - v3Min) * vRng;
            for (int i = 0; i < cF; i++, pos++) cData[pos] = (cData[pos] - fltMin) * fRng;
        }

        private static float[] NormalizeAnimated(
            float[][][] quats, float[][][] vecs, float[][] floats,
            bool[] constCh, int frameCount,
            int totalQ, int totalV, int totalF,
            int aQ, int aV, int aF, int alignedStride,
            float[] qMinCh, float[] qMaxCh,
            float[] vMinCh, float[] vMaxCh,
            float[] fMinCh, float[] fMaxCh)
        {
            float[] norm = new float[frameCount * alignedStride];

            int qi = 0;
            for (int i = 0; i < totalQ; i++)
            {
                if (constCh[i]) continue;
                float mn = qMinCh[qi], rng = (qMaxCh[qi] - mn) != 0f ? 1f / (qMaxCh[qi] - mn) : 1f;
                for (int f = 0; f < frameCount; f++)
                {
                    float[] q = quats[i][f];
                    int base_ = f * alignedStride + qi * 4;
                    norm[base_ + 0] = (q[0] - mn) * rng;
                    norm[base_ + 1] = (q[1] - mn) * rng;
                    norm[base_ + 2] = (q[2] - mn) * rng;
                    norm[base_ + 3] = (q[3] - mn) * rng;
                }
                qi++;
            }

            int vi = 0;
            for (int i = 0; i < totalV; i++)
            {
                if (constCh[totalQ + i]) continue;
                float mn = vMinCh[vi], rng = (vMaxCh[vi] - mn) != 0f ? 1f / (vMaxCh[vi] - mn) : 1f;
                for (int f = 0; f < frameCount; f++)
                {
                    float[] v = vecs[i][f];
                    int base_ = f * alignedStride + aQ * 4 + vi * 3;
                    norm[base_ + 0] = (v[0] - mn) * rng;
                    norm[base_ + 1] = (v[1] - mn) * rng;
                    norm[base_ + 2] = (v[2] - mn) * rng;
                }
                vi++;
            }

            int fi = 0;
            for (int i = 0; i < totalF; i++)
            {
                if (constCh[totalQ + totalV + i]) continue;
                float mn = fMinCh[fi], rng = (fMaxCh[fi] - mn) != 0f ? 1f / (fMaxCh[fi] - mn) : 1f;
                for (int f = 0; f < frameCount; f++)
                    norm[f * alignedStride + aQ * 4 + aV * 3 + fi] = (floats[i][f] - mn) * rng;
                fi++;
            }

            return norm;
        }

        private static float[] ForwardDCT(
            float[] norm, int frameCount, int alignedStride, int frameBlocks)
        {
            float[] dct = new float[frameBlocks * 8 * alignedStride];

            for (int fb = 0; fb < frameBlocks; fb++)
            {
                int startFrame;
                if (frameCount < 8) startFrame = 0;
                else if (fb * 8 + 8 <= frameCount) startFrame = fb * 8;
                else startFrame = frameCount - 8;

                float[] input = new float[8];
                for (int c = 0; c < alignedStride; c++)
                {
                    for (int j = 0; j < 8; j++)
                    {
                        int frameIdx = (frameCount < 8)
                            ? (j < frameCount ? j : frameCount - 1)
                            : startFrame + j;
                        input[j] = norm[frameIdx * alignedStride + c] - 0.5f;
                    }

                    for (int k = 0; k < 8; k++)
                    {
                        float sum = 0f;
                        for (int j = 0; j < 8; j++) sum += input[j] * DctFwd[j, k];
                        dct[(fb * 8 + k) * alignedStride + c] = sum;
                    }
                }
            }

            return dct;
        }

        private static void GetDctMinMax(
            float[] dctData, int frameBlocks, int stride, int alignedStride,
            out float dctMin, out float dctMax)
        {
            dctMin = float.MaxValue; dctMax = float.MinValue;
            for (int fb = 0; fb < frameBlocks; fb++)
                for (int k = 0; k < 8; k++)
                    for (int c = 0; c < stride; c++)
                    {
                        float v = dctData[(fb * 8 + k) * alignedStride + c];
                        if (v < dctMin) dctMin = v;
                        if (v > dctMax) dctMax = v;
                    }

            if (dctMin == float.MaxValue) { dctMin = 0f; dctMax = 0f; }
        }

        private static float[,] InitQuantizationTables(float dctMin, float dctMax)
        {
            float scalar = Math.Max(Math.Abs(dctMin), Math.Abs(dctMax));
            if (scalar == 0f) scalar = 1f;

            float[,] t = new float[NumQTables, 8];
            for (int i = 0; i < NumQTables; i++)
            {
                float scale = (i + 1) * 0.2f;
                for (int k = 0; k < 8; k++)
                {
                    double ln = Math.Log(k + 2);
                    t[i, k] = (float)((1.0 + scalar * scale * ln) * scalar / 32768.0);
                }
            }
            return t;
        }

        private static void DetermineQuantTables(
            int fb, int frameCount, float[] dctData, float[] norm,
            float[,] qTables, float[] errBlock,
            int stride, int alignedStride, int aQ, int aV, bool sepTraj,
            out int rotTable, out int trjTable, out int traTable)
        {
            rotTable = FindBestQuantTable(fb, frameCount, dctData, norm, qTables, errBlock,
                stride, alignedStride, 0, aQ * 4);

            int transCompOffset;
            int transCompCount;

            if (sepTraj)
            {
                trjTable = FindBestQuantTable(fb, frameCount, dctData, norm, qTables, errBlock,
                    stride, alignedStride, aQ * 4, 4);

                transCompOffset = (aQ + 1) * 4;
                transCompCount = stride - transCompOffset;
                traTable = transCompCount > 0
                    ? FindBestQuantTable(fb, frameCount, dctData, norm, qTables, errBlock,
                        stride, alignedStride, transCompOffset, transCompCount)
                    : trjTable;
            }
            else
            {
                transCompOffset = aQ * 4;
                transCompCount = stride - transCompOffset;
                trjTable = traTable = transCompCount > 0
                    ? FindBestQuantTable(fb, frameCount, dctData, norm, qTables, errBlock,
                        stride, alignedStride, transCompOffset, transCompCount)
                    : 0;
            }
        }

        private static int FindBestQuantTable(
            int fb, int frameCount, float[] dctData, float[] norm,
            float[,] qTables, float[] errBlock,
            int stride, int alignedStride, int compOffset, int compCount)
        {
            if (compCount <= 0) return 0;

            int first = 0, d = NumQTables;
            while (d > 0)
            {
                int d2 = d >> 1;
                int mid = first + d2;
                if (TestQuantizationTable(fb, frameCount, dctData, norm, qTables, errBlock,
                        stride, alignedStride, compOffset, compCount, mid))
                {
                    first = mid + 1;
                    d -= d2 + 1;
                }
                else d = d2;
            }

            if (first > 0) first--;        
            return first;
        }

        private static bool TestQuantizationTable(
            int fb, int frameCount, float[] dctData, float[] norm,
            float[,] qTables, float[] errBlock,
            int stride, int alignedStride, int compOffset, int compCount, int tableIdx)
        {
            int startFrame;
            if (frameCount < 8) startFrame = 0;
            else if (fb * 8 + 8 <= frameCount) startFrame = fb * 8;
            else startFrame = frameCount - 8;

            int itr = Math.Min(8, frameCount);
            int compEnd = Math.Min(compOffset + compCount, stride);

            for (int c = compOffset; c < compEnd; c++)
            {
                float[] dequant = new float[8];
                for (int k = 0; k < 8; k++)
                {
                    float qt = qTables[tableIdx, k];
                    short q = (short)Math.Round(dctData[(fb * 8 + k) * alignedStride + c] / qt);
                    dequant[k] = q * qt;
                }

                for (int j = 0; j < itr; j++)
                {
                    float recon = 0.5f;
                    for (int k = 0; k < 8; k++) recon += dequant[k] * DctInv[j, k];
                    float original = norm[(startFrame + j) * alignedStride + c];
                    if (Math.Abs(recon - original) > errBlock[c]) return false;
                }
            }

            return true;
        }

        private static void QuantizeBlock(
            int fb, int frameCount, float[] dctData, float[,] qTables,
            short[] intermediate, int stride, int alignedStride,
            int aQ, int aV, bool sepTraj,
            int rotTable, int trjTable, int traTable, int numShorts)
        {
            int trajEnd = sepTraj ? aQ * 4 + 4 : -1;

            for (int c = 0; c < stride; c++)
            {
                int tableIdx;
                if (c < aQ * 4) tableIdx = rotTable;
                else if (sepTraj && c < trajEnd) tableIdx = trjTable;
                else tableIdx = traTable;

                for (int k = 0; k < 8; k++)
                {
                    float qt = qTables[tableIdx, k];
                    float dct = dctData[(fb * 8 + k) * alignedStride + c];
                    intermediate[fb * numShorts + c * 8 + k + 3] = (short)Math.Round(dct / qt);
                }
            }
        }

        private static int[] ComputeNumBits(
            short[] intermediate, int stride, int frameBlocks, int numShorts,
            out bool fastBitDecoder)
        {
            int[] maxAbs = new int[stride * 8];

            for (int fb = 0; fb < frameBlocks; fb++)
                for (int c = 0; c < stride; c++)
                    for (int k = 0; k < 8; k++)
                    {
                        int v = Math.Abs((int)intermediate[fb * numShorts + c * 8 + k + 3]);
                        int idx = c * 8 + k;
                        if (v > maxAbs[idx]) maxAbs[idx] = v;
                    }

            int[] numBits = new int[stride * 8];
            int maxBitsPerBlock = 0;

            for (int c = 0; c < stride; c++)
            {
                int bitsThisChannel = 0;
                for (int k = 0; k < 8; k++)
                {
                    int idx = c * 8 + k;
                    int mxAb = maxAbs[idx];
                    int nb = 0;
                    if (mxAb > 0)
                    {
                        bitsThisChannel += 2;    
                        nb = (int)Math.Ceiling(Math.Log(mxAb + 0.5) / Math.Log(2.0));
                        bitsThisChannel += nb;
                    }
                    numBits[idx] = nb;
                }
                if (bitsThisChannel > maxBitsPerBlock) maxBitsPerBlock = bitsThisChannel;
            }

            fastBitDecoder = maxBitsPerBlock <= 128;    
            return numBits;
        }

        private static float[] BuildConstantPalette(
            float[] cData, int cQ, int cV, int cF, string clipName)
        {
            int chCnt = cQ * 4 + cV * 3 + cF;
            if (chCnt == 0) return Array.Empty<float>();

            const float Epsilon = 4f * 1.1920929e-7f;    
            var exactPalette = new List<float>();
            bool overflow = false;

            for (int i = 0; i < chCnt; i++)
            {
                float v = cData[i];
                bool found = false;
                for (int k = 0; k < exactPalette.Count; k++)
                {
                    if (Math.Abs(v - exactPalette[k]) <= Epsilon) { found = true; break; }
                }
                if (!found)
                {
                    exactPalette.Add(v);
                    if (exactPalette.Count >= 4096) { overflow = true; break; }
                }
            }

            if (!overflow) return exactPalette.ToArray();

            const int PaletteSize = 4096;
            float[] uniformPalette = new float[PaletteSize];
            for (int i = 0; i < PaletteSize; i++) uniformPalette[i] = (float)i / (PaletteSize - 1);

            bool[] used = new bool[PaletteSize];
            for (int i = 0; i < chCnt; i++)
                used[FindClosestPaletteIndex(cData[i], uniformPalette)] = true;

            var result = new List<float>();
            for (int i = 0; i < PaletteSize; i++)
                if (used[i]) result.Add(uniformPalette[i]);
            return result.ToArray();
        }

        private static int FindClosestPaletteIndex(float value, float[] palette)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                float d = Math.Abs(palette[i] - value);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private static byte[] BitPackHeaderOnly(
            float[] cData, bool[] constCh, float[] constPalette,
            int totalQ, int totalV, int totalF,
            int cQ, int cV, int cF,
            int frameCount, bool cycle,
            out ushort keyTimeSize, out ushort constChanMapSize,
            out bool noChannelMap, out bool noKeyTimes)
        {
            var writer = new BitWriter();
            keyTimeSize = 0;
            noKeyTimes = true;
            WriteConstantChannelHeader(writer, cData, constCh, constPalette,
                totalQ, totalV, totalF, cQ, cV, cF,
                out constChanMapSize, out noChannelMap);
            return writer.ToArray();
        }

        private static byte[] BitPackAll(
            bool[] constCh, float[] cData, float[] constPalette,
            short[] intermediate, int[] numBits,
            int frameCount, int frameBlocks,
            int totalQ, int totalV, int totalF,
            int aQ, int aV, int aF,
            int stride, int alignedStride,
            bool sepTraj, bool cycle, bool bigEndian, int numShorts,
            out ushort[] frameBlockSizes,
            out ushort keyTimeSize, out ushort constChanMapSize,
            out bool noKeyTimes, out bool noChannelMap)
        {
            var writer = new BitWriter();
            keyTimeSize = 0;
            noKeyTimes = true;
            WriteConstantChannelHeader(writer, cData, constCh, constPalette,
                totalQ, totalV, totalF,
                totalQ - aQ, totalV - aV, totalF - aF,    
                out constChanMapSize, out noChannelMap);

            for (int c = 0; c < stride; c++)
                for (int k = 0; k < 8; k++)
                    writer.WriteBits((uint)numBits[c * 8 + k], 4);
            writer.Flush();

            int headerBytes = writer.ByteCount;
            int[] fbStart = new int[frameBlocks];
            frameBlockSizes = new ushort[frameBlocks];

            for (int fb = 0; fb < frameBlocks; fb++)
            {
                fbStart[fb] = writer.ByteCount;
                writer.ResetBitCount();

                writer.WriteBits((uint)(byte)intermediate[fb * numShorts + 0], 8);
                writer.WriteBits((uint)(byte)intermediate[fb * numShorts + 1], 8);
                writer.WriteBits((uint)(byte)intermediate[fb * numShorts + 2], 8);

                for (int c = 0; c < stride; c++)
                {
                    for (int k = 0; k < 8; k++)
                        if (numBits[c * 8 + k] > 0)
                            writer.WriteBits(
                                intermediate[fb * numShorts + c * 8 + k + 3] != 0 ? 1u : 0u, 1);

                    for (int k = 0; k < 8; k++)
                    {
                        int nb = numBits[c * 8 + k];
                        if (nb > 0)
                        {
                            short v = intermediate[fb * numShorts + c * 8 + k + 3];
                            if (v != 0) writer.WriteBits(v > 0 ? 1u : 0u, 1);
                        }
                    }

                    for (int k = 0; k < 8; k++)
                    {
                        int nb = numBits[c * 8 + k];
                        if (nb > 0)
                        {
                            short v = intermediate[fb * numShorts + c * 8 + k + 3];
                            if (v != 0) writer.WriteBits((uint)Math.Abs((int)v), nb);
                        }
                    }
                }

                writer.Flush();
                frameBlockSizes[fb] = (ushort)(writer.ByteCount - fbStart[fb]);
            }

            byte[] result = writer.ToArray();

            if (!bigEndian)
            {
                int cQ_cnt = totalQ - aQ;
                int cV_cnt = totalV - aV;
                int cF_cnt = totalF - aF;
                int constDofCnt = cQ_cnt * 4 + cV_cnt * 3 + cF_cnt;
                bool use8Bit = constPalette.Length <= 256;

                int buggyOffset = 0 + constDofCnt + constChanMapSize + stride * 4 + 0;

                int leSwapBase = use8Bit ? headerBytes : buggyOffset;

                int curPos = leSwapBase;
                for (int fb = 0; fb < frameBlocks; fb++)
                {
                    int sz = frameBlockSizes[fb];
                    if (sz > 3)
                    {
                        int lo = curPos + 3, hi = curPos + sz - 1;
                        while (lo < hi && lo >= 0 && hi < result.Length)
                        {
                            byte tmp = result[lo]; result[lo] = result[hi]; result[hi] = tmp;
                            lo++; hi--;
                        }
                    }
                    curPos += sz;
                }
            }

            return result;
        }

        private static void WriteConstantChannelHeader(
            BitWriter writer,
            float[] cData, bool[] constCh, float[] constPalette,
            int totalQ, int totalV, int totalF,
            int cQ, int cV, int cF,
            out ushort constChanMapSize, out bool noChannelMap)
        {
            int chCnt = cQ * 4 + cV * 3 + cF;
            bool use8Bit = constPalette.Length <= 256;
            int idxBits = use8Bit ? 8 : 16;

            for (int i = 0; i < chCnt; i++)
                writer.WriteBits((uint)FindClosestPaletteIndex(cData[i], constPalette), idxBits);
            writer.Flush();

            int totalCh = totalQ + totalV + totalF;
            noChannelMap = chCnt == 0;
            constChanMapSize = 0;

            if (!noChannelMap)
            {
                bool curVal = false;      
                int curCnt = 0;

                for (int i = 0; i < totalCh;)
                {
                    if (constCh[i] == curVal && curCnt < 255)
                    {
                        curCnt++; i++;
                    }
                    else
                    {
                        writer.WriteBits((uint)curCnt, 8);
                        constChanMapSize++;
                        curCnt = 0;
                        curVal = !curVal;
                    }
                }
                if (curCnt > 0) { writer.WriteBits((uint)curCnt, 8); constChanMapSize++; }
                writer.Flush();
            }
        }

        private static VbrAnimationAsset BuildResult(
            VbrAnimationAsset template,
            int frameCount,
            int aQ, int aV, int aF,
            int cQ, int cV, int cF,
            float quatMin, float quatMax,
            float tjMin, float tjMax,
            float v3Min, float v3Max,
            float fltMin, float fltMax,
            float dct,
            ushort constPaletteSize,
            ushort keyTimeSize,
            ushort constChanMapSize,
            bool noKeyTimes,
            bool noChannelMap,
            bool fastBitDecoder,
            bool cycle,
            bool sepTraj,
            int totalQ,
            float[] constPalette,
            ushort[] frameBlockSizes,
            byte[] data)
        {
            ushort flags = 0;
            if (cycle) flags |= 1;    
            if (noKeyTimes) flags |= 2;    
            if (fastBitDecoder) flags |= 4;    
            if (noChannelMap) flags |= 8;    
            if (totalQ < template.QuaternionCount + template.ConstQuaternionCount + template.Vector3Count + template.ConstVector3Count
                && cQ + cV + cF > 0 && aV == 0)
                flags |= 128;

            var r = new VbrAnimationAsset();
            r.AssetType = template.AssetType;
            r.Bank = template.Bank;
            r.OrderedChannels = template.OrderedChannels;
            r.Channels = template.Channels;
            r.ChannelToDofAsset = template.ChannelToDofAsset;
            r.DofSetList = template.DofSetList;
            r.AnimId = template.AnimId;
            r.CodecType = template.CodecType;
            r.TrimOffset = template.TrimOffset;
            r.Additive = template.Additive;
            r.FPS = template.FPS;
            r.EndFrame = (ushort)(frameCount - 1);
            r.NumKeys = (ushort)frameCount;
            r.QuaternionCount = (ushort)aQ;
            r.Vector3Count = (ushort)aV;
            r.NumFloat = (ushort)aF;
            r.ConstQuaternionCount = (ushort)cQ;
            r.ConstVector3Count = (ushort)cV;
            r.ConstFloatCount = (ushort)cF;
            r.QuatMin = quatMin; r.QuatMax = quatMax;
            r.TrajMin = tjMin; r.TrajMax = tjMax;
            r.Vec3Min = v3Min; r.Vec3Max = v3Max;
            r.FloatMin = fltMin; r.FloatMax = fltMax;
            r.Dct = dct;
            r.KeyTimeSize = keyTimeSize;
            r.ConstChanMapSize = constChanMapSize;
            r.ConstPaletteSize = constPaletteSize;
            r.VectorOffsetScale = 0f;
            r.FloatOffsetScale = 0f;
            r.VectorOffsetSize = 0;
            r.FloatOffsetSize = 0;
            r.Flags = flags;
            r.ConstantPalette = constPalette;
            r.FrameBlockSizes = frameBlockSizes;
            r.Data = data;

            var rd = template.RawData != null
                ? new Dictionary<string, object>(template.RawData)
                : new Dictionary<string, object>();

            rd["QuaternionCount"] = (ushort)aQ;
            rd["Vector3Count"] = (ushort)aV;
            rd["FloatCount"] = (ushort)aF;
            rd["ConstQuaternionCount"] = (ushort)cQ;
            rd["ConstVector3Count"] = (ushort)cV;
            rd["ConstFloatCount"] = (ushort)cF;
            rd["NumKeys"] = (ushort)frameCount;
            rd["EndFrame"] = (ushort)(frameCount - 1);
            rd["KeyTimeSize"] = keyTimeSize;
            rd["ConstChanMapSize"] = constChanMapSize;
            rd["ConstPaletteSize"] = constPaletteSize;
            rd["VectorOffsetSize"] = (ushort)0;
            rd["FloatOffsetSize"] = (ushort)0;
            rd["Flags"] = flags;
            rd["QuatMin"] = quatMin; rd["QuatMax"] = quatMax;
            rd["TrajMin"] = tjMin; rd["TrajMax"] = tjMax;
            rd["Vec3Min"] = v3Min; rd["Vec3Max"] = v3Max;
            rd["FloatMin"] = fltMin; rd["FloatMax"] = fltMax;
            rd["Dct"] = dct;
            rd["VectorOffsetScale"] = 0f;
            rd["FloatOffsetScale"] = 0f;
            rd["ConstantPalette"] = constPalette;
            rd["FrameBlockSizes"] = frameBlockSizes;
            rd["Data"] = data;

            r.RawData = rd;
            return r;
        }

        private sealed class BitWriter
        {
            private readonly List<byte> _buf = new List<byte>(4096);
            private byte _bucket;
            private int _stored;
            private int _byteCountAtReset;

            public int ByteCount => _buf.Count;

            public void ResetBitCount() => _byteCountAtReset = _buf.Count;

            public int BitsSinceReset => (_buf.Count - _byteCountAtReset) * 8;

            public void WriteBits(uint value, int bits)
            {
                while (bits > 0)
                {
                    int left = 8 - _stored;
                    int take = bits < left ? bits : left;
                    uint mask = (1u << take) - 1u;
                    _bucket |= (byte)((value & mask) << _stored);
                    value >>= take;
                    _stored += take;
                    bits -= take;
                    if (_stored == 8) FlushInner();
                }
            }

            public void Flush() { if (_stored != 0) FlushInner(); }

            private void FlushInner()
            {
                _buf.Add(_bucket);
                _bucket = 0;
                _stored = 0;
            }

            public byte[] ToArray() => _buf.ToArray();
        }
    }
}