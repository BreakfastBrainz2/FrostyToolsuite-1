using Assimp;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.IO;
using MeshSetPlugin.Resources;
using SharpGen.Runtime.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Windows.Media.Media3D;

namespace MeshSetPlugin;

public class MeshExportParams
{
    public dynamic MeshAsset;
    public string Filename;
    public MeshExportScale Scale;
    public bool FlattenHierarchy;
    public bool ExportSingleLod;
    public bool ExportNonRenderable;
    public dynamic SkeletonAsset;
    public MeshSet[] MeshSets;
}

public struct BoneEntry
{
    public Node Node;
    public Matrix4x4 InverseTransform;
}

public class MeshExporter
{
    private FrostyTaskWindow m_task;
    private Scene m_scene;
    private MeshExportParams m_params;
    private int m_meshCount = 0;
    private int m_boneCount = 0;
    private List<BoneEntry> m_boneEntries = new();
    private float m_unitScale = 1.0f;

    public MeshExporter(FrostyTaskWindow task)
    {
        m_task = task;
    }

    public void Export(MeshExportParams exportParams)
    {
        m_scene = new Scene();
        m_params = exportParams;

        m_scene.RootNode = new Node(m_params.MeshSets[0].Name);
        // at least one material is required or the fbx exporter will crash
        m_scene.Materials.Add(new());

        m_unitScale = m_params.Scale switch
        {
            MeshExportScale.Millimeters => 0.1f,
            MeshExportScale.Centimeters => 1.0f,
            MeshExportScale.Meters => 100.0f,
            MeshExportScale.Kilometers => 100000.0f,
            _ => 1.0f
        };
        m_scene.Metadata.Add("UnitScaleFactor", new() { Data = m_unitScale, DataType = MetaDataType.Float });

        try
        {
            if (m_params.MeshSets[0].Type == MeshType.MeshType_Skinned)
                CreateSkeleton();

            foreach (var set in exportParams.MeshSets)
            {
                foreach (var lod in set.Lods)
                {
                    CreateMesh(lod);

                    if (exportParams.ExportSingleLod)
                        break;
                }
            }

            using var exporter = new AssimpContext();
            exporter.ExportFile(m_scene, m_params.Filename, "fbx", PostProcessSteps.ValidateDataStructure);
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"Failed to export mesh: {ex.Message}");
        }
    }

    private void CreateSkeleton()
    {
        var skeletonAsset = m_params.SkeletonAsset;
        m_boneCount = skeletonAsset.BoneNames.Count;

        for (int boneIdx = 0; boneIdx < m_boneCount; boneIdx++)
        {
            dynamic pose = skeletonAsset.LocalPose;

            Matrix4x4 boneMatrix = new Matrix4x4(
                pose[boneIdx].right.x, pose[boneIdx].right.y, pose[boneIdx].right.z, 0.0f,
                pose[boneIdx].up.x, pose[boneIdx].up.y, pose[boneIdx].up.z, 0.0f,
                pose[boneIdx].forward.x, pose[boneIdx].forward.y, pose[boneIdx].forward.z, 0.0f,
                pose[boneIdx].trans.x, pose[boneIdx].trans.y, pose[boneIdx].trans.z, 1.0f
            );

            var boneNode = new Node(skeletonAsset.BoneNames[boneIdx])
            {
                Transform = Matrix4x4.Transpose(boneMatrix)
            };

            Node? parent = null;

            int parentIdx = skeletonAsset.Hierarchy[boneIdx];
            if(parentIdx != -1)
            {
                parent = m_boneEntries[parentIdx].Node;
            }

            (parent ?? m_scene.RootNode).Children.Add(boneNode);

            Matrix4x4.Invert(boneMatrix, out var inverseBoneMtx);
            var inverseGlobalMtx = parent is null
                ? inverseBoneMtx
                : m_boneEntries[parentIdx].InverseTransform * inverseBoneMtx;

            m_boneEntries.Add(new() { Node = boneNode, InverseTransform = inverseGlobalMtx });
        }
    }

    public void CreateMesh(MeshSetLod lod)
    {
        Stream chunkStream = (lod.ChunkId != Guid.Empty)
                ? App.AssetManager.GetChunk(App.AssetManager.GetChunkEntry(lod.ChunkId))
                : new MemoryStream(lod.InlineData);

        using DataStream stream = new(chunkStream);

        foreach(var subset in lod.Sections)
        {
            if (!m_params.ExportNonRenderable && !lod.IsSectionRenderable(subset))
                continue;

            int indexSize = lod.IndexUnitSize / 8;

            stream.Position = subset.VertexOffset;
            byte[] vertData = stream.ReadBytes((int)(subset.VertexCount * subset.VertexStride));

            stream.Position = lod.VertexBufferSize + (subset.StartIndex * indexSize);
            byte[] indexData = stream.ReadBytes((int)(subset.PrimitiveCount * indexSize * 3));

            var subsetNode = new Node($"{subset.Name}:lod{lod.ShortName.Last()}");
            subsetNode.MeshIndices.Add(m_meshCount++);

            m_scene.RootNode.Children.Add(subsetNode);
            m_scene.Meshes.Add(CreateMeshSubset(lod, subset, vertData, indexData));
        }
    }

    public Bone GetMeshBone(Mesh mesh, int boneIndex)
    {
        string boneName = m_boneEntries[boneIndex].Node.Name;
        Bone? bone = mesh.Bones.Find(x => x.Name == boneName);
        if(bone is null)
        {
            bone = new Bone()
            {
                Name = boneName,
                OffsetMatrix = Matrix4x4.Transpose(m_boneEntries[boneIndex].InverseTransform)
            };
            mesh.Bones.Add(bone);
        }

        return bone;
    }

    public Mesh CreateMeshSubset(MeshSetLod lod, MeshSetSection subset, byte[] vertData, byte[] indexData)
    {
        var mesh = new Mesh(subset.Name, Assimp.PrimitiveType.Triangle);
        mesh.Vertices.EnsureCapacity((int)subset.VertexCount);

        ref var geomDesc = ref subset.GeometryDeclDesc[0];

        // first pass, setup the mesh channels (uv channels, vert col channels)
        int boneElemCount = 0;
        for (int i = 0; i < geomDesc.ElementCount; i++)
        {
            ref var element = ref geomDesc.Elements[i];

            if (element.Usage >= VertexElementUsage.TexCoord0 && element.Usage <= VertexElementUsage.TexCoord7)
            {
                int uvIndex = element.Usage - VertexElementUsage.TexCoord0;
                int componentCount = element.Format >= VertexElementFormat.Half
                    ? element.Format - VertexElementFormat.Half
                    : element.Format - VertexElementFormat.Float;

                componentCount++;

                mesh.TextureCoordinateChannels[uvIndex] = new();
                mesh.UVComponentCount[uvIndex] = componentCount;
            }
            else if (element.Usage >= VertexElementUsage.Color0 && element.Usage <= VertexElementUsage.Color1)
            {
                int colIndex = element.Usage - VertexElementUsage.Color0;

                mesh.VertexColorChannels[colIndex] = new();
            }
            else if (element.Usage >= VertexElementUsage.BoneIndices && element.Usage <= VertexElementUsage.BoneIndices2)
                boneElemCount += 4;
        }

        // second pass, read the vertex data
        VertexDataReader vertReader = new(vertData, 0);

        ushort[] boneIndices = Array.Empty<ushort>();
        float[] boneWeights = Array.Empty<float>();

        if(lod.Type == MeshType.MeshType_Skinned)
        {
            boneIndices = new ushort[subset.VertexCount * boneElemCount];
            boneWeights = new float[subset.VertexCount * boneElemCount];
        }

        for (int elemIt = 0; elemIt < geomDesc.ElementCount; elemIt++)
        {
            ref var element = ref geomDesc.Elements[elemIt];
            if (element.Offset == byte.MaxValue || element.Usage == VertexElementUsage.Unknown)
                continue;

            ref var stream = ref geomDesc.Streams[element.StreamIndex];

            for (int vertIt = 0; vertIt < subset.VertexCount; vertIt++)
            {
                switch (element.Usage)
                {
                    case VertexElementUsage.Pos:
                    {
                        mesh.Vertices.Add(vertReader.ReadVec3(element.Format));
                        break;
                    }
                    case VertexElementUsage.Normal:
                    {
                        mesh.Normals.Add(vertReader.ReadVec3(element.Format));
                        break;
                    }
                    case VertexElementUsage.BinormalSign:
                    {
                        mesh.Tangents.Add(vertReader.ReadVec3(element.Format));
                        break;
                    }
                    // bone elements are stored consecutively
                    case VertexElementUsage.BoneIndices:
                    case VertexElementUsage.BoneIndices2:
                    {
                        // element layout is: [x, y, z, w] + another 4 elements if subset has more than 4 bones per vertex
                        int baseIndex = vertIt * boneElemCount;
                        int boneOffset = 4 * (element.Usage - VertexElementUsage.BoneIndices);

                        vertReader.ReadBoneIndices(element.Format, boneIndices, baseIndex + boneOffset);
                        break;
                    }
                    case VertexElementUsage.BoneWeights:
                    case VertexElementUsage.BoneWeights2:
                    {
                        int baseIndex = vertIt * boneElemCount;
                        int boneOffset = 4 * (element.Usage - VertexElementUsage.BoneWeights2);

                        vertReader.ReadBoneWeights(element.Format, boneWeights, baseIndex + boneOffset);
                        break;
                    }
                    case VertexElementUsage.TexCoord0:
                    case VertexElementUsage.TexCoord1:
                    case VertexElementUsage.TexCoord2:
                    case VertexElementUsage.TexCoord3:
                    case VertexElementUsage.TexCoord4:
                    case VertexElementUsage.TexCoord5:
                    case VertexElementUsage.TexCoord6:
                    case VertexElementUsage.TexCoord7:
                    {
                        int uvIndex = element.Usage - VertexElementUsage.TexCoord0;
                        mesh.TextureCoordinateChannels[uvIndex].Add(new Vector3(vertReader.ReadVec2(element.Format), 0.0f));
                        break;
                    }
                    case VertexElementUsage.Color0:
                    case VertexElementUsage.Color1:
                    {
                        int colIndex = element.Usage - VertexElementUsage.Color0;
                        mesh.VertexColorChannels[colIndex].Add(vertReader.ReadVec4(element.Format));
                        break;
                    }
                    case VertexElementUsage.RadiosityTexCoord: break; // runtime only i think
                    case VertexElementUsage.DisplacementMapTexCoord: break; // same with this?
                    default: throw new InvalidOperationException($"Unhandled element usage: {element.Usage}");
                }
                vertReader.Offset += stream.VertexStride;
            }
        }

        for (int vertIdx = 0; vertIdx < subset.VertexCount; vertIdx++)
        {
            for (int weightIdx = 0; weightIdx < boneElemCount; weightIdx++)
            {
                int index = vertIdx * boneElemCount + weightIdx;
                ushort boneIndex = boneIndices[index];
                float boneWeight = boneWeights[index];

                if ((boneIndex & 0x8000) != 0)
                    continue; // skipping proc bones for now

                if (boneWeight == 0.0f)
                    continue;

                int subIndex = subset.BoneList[boneIndex];
                var bone = GetMeshBone(mesh, subIndex);

                bone.VertexWeights.Add(new(vertIdx, boneWeight));
            }
        }

        Debug.Assert(subset.PrimitiveType == Resources.PrimitiveType.PrimitiveType_TriangleList, "Subset isn't made of triangles, WTF?");

        int indexSizeBytes = lod.IndexUnitSize / 8;
        int indexStride = indexSizeBytes * 3;

        bool shortIndices = indexSizeBytes == 2;

        int indexOffset = 0;
        for (int i = 0; i < subset.PrimitiveCount; i++)
        {
            int indice1;
            int indice2;
            int indice3;

            if(shortIndices)
            {
                indice1 = BitConverter.ToInt16(indexData, indexOffset);
                indice2 = BitConverter.ToInt16(indexData, indexOffset + 2);
                indice3 = BitConverter.ToInt16(indexData, indexOffset + 4);
            }
            else
            {
                indice1 = BitConverter.ToInt32(indexData, indexOffset);
                indice2 = BitConverter.ToInt32(indexData, indexOffset + 4);
                indice3 = BitConverter.ToInt32(indexData, indexOffset + 8);
            }

            var face = new Face();

            face.Indices.Add(indice1);
            face.Indices.Add(indice2);
            face.Indices.Add(indice3);

            mesh.Faces.Add(face);

            indexOffset += indexStride;
        }

        return mesh;
    }
}
