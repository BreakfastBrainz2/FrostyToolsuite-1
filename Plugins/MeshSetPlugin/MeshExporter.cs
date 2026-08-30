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

public class MeshExporter
{
    private FrostyTaskWindow m_task;
    private Scene m_scene;
    private MeshExportParams m_params;
    private int m_meshCount = 0;

    public MeshExporter(FrostyTaskWindow task)
    {
        m_task = task;
    }

    public void ExportGLB(MeshExportParams exportParams)
    {
        m_scene = new Scene();
        m_params = exportParams;

        m_scene.RootNode = new Node(m_params.MeshSets[0].Name);

        try
        {
            foreach (var set in exportParams.MeshSets)
            {
                foreach (var lod in set.Lods)
                {
                    CreateMesh(lod);

                    if (exportParams.ExportSingleLod)
                        continue;
                }
            }

            using var exporter = new AssimpContext();
            exporter.ExportFile(m_scene, m_params.Filename, "glb2");
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"Failed to export mesh: {ex.Message}");
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

    public Mesh CreateMeshSubset(MeshSetLod lod, MeshSetSection subset, byte[] vertData, byte[] indexData)
    {
        var glbMesh = new Mesh(subset.Name, Assimp.PrimitiveType.Triangle);
        glbMesh.Vertices.EnsureCapacity((int)subset.VertexCount);

        ref var geomDesc = ref subset.GeometryDeclDesc[0];

        // first pass, setup the mesh channels (uv channels, vert col channels)
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

                glbMesh.TextureCoordinateChannels[uvIndex] = new();
                glbMesh.UVComponentCount[uvIndex] = componentCount;
            }
            else if(element.Usage >= VertexElementUsage.Color0 && element.Usage <= VertexElementUsage.Color1)
            {
                int colIndex = element.Usage - VertexElementUsage.Color0;

                glbMesh.VertexColorChannels[colIndex] = new();
            }
        }

        // second pass, read the vertex data
        VertexDataReader vertReader = new(vertData, 0);
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
                        glbMesh.Vertices.Add(vertReader.ReadVec3(element.Format));
                        break;
                    }
                    case VertexElementUsage.Normal:
                    {
                        glbMesh.Normals.Add(vertReader.ReadVec3(element.Format));
                        break;
                    }
                    case VertexElementUsage.BinormalSign:
                    {
                        glbMesh.Tangents.Add(vertReader.ReadVec3(element.Format));
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
                        glbMesh.TextureCoordinateChannels[uvIndex].Add(new Vector3(vertReader.ReadVec2(element.Format), 0.0f));
                        break;
                    }
                    case VertexElementUsage.Color0:
                    case VertexElementUsage.Color1:
                    {
                        int colIndex = element.Usage - VertexElementUsage.Color0;
                        glbMesh.VertexColorChannels[colIndex].Add(vertReader.ReadVec4(element.Format));
                        break;
                    }
                    case VertexElementUsage.RadiosityTexCoord: break; // unused apparently
                    case VertexElementUsage.DisplacementMapTexCoord: break; // unused apparently
                    default: throw new InvalidOperationException($"Unhandled element usage: {element.Usage}");
                }
                vertReader.Offset += stream.VertexStride;
            }
        }

        Debug.Assert(subset.PrimitiveType == Resources.PrimitiveType.PrimitiveType_TriangleList, "Subset isn't made of triangles, WTF?");

        int indexOffset = 0;
        int indexSizeBytes = lod.IndexUnitSize / 8;
        int indexStride = indexSizeBytes * 3;
        int numIndices = (int)(subset.PrimitiveCount * 3);

        bool shortIndices = indexSizeBytes == 2;

        for (int i = 0; i < numIndices; i += 3)
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

            glbMesh.Faces.Add(face);

            indexOffset += indexSizeBytes * 3;
        }

        return glbMesh;
    }
}
