using Fieldmate.Twin;
using UnityEditor;
using UnityEngine;

namespace Fieldmate.Editor;

/// <summary>
/// Bakes the pressure gauge's printed dial (#88): <see cref="DialPainter"/> artwork saved as a PNG, and a flat disc
/// mesh whose UVs map it as seen from the front. Deterministic, so a rebuild only rewrites what changed.
/// </summary>
public static class GaugeArt
{
    public const string Dir = "Assets/_Project/Placeholders/Gauge";
    public const string DialTexturePath = Dir + "/PressureDial.png";
    public const string DiscMeshPath = Dir + "/DialDisc.asset";
    public const int TextureSize = 1024;
    private const int DiscSegments = 96;

    public static Texture2D DialTexture()
    {
        ProjectSetup.EnsureFolder(Dir);
        var pixels = DialPainter.Paint(TextureSize, GaugeScale.Pressure, GaugeBand.Pressure(), majorStep: 1f, minorStep: 0.5f,
            numeralStep: 2f, unit: "bar");
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        texture.LoadRawTextureData(pixels);
        texture.Apply();
        var png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        var existing = System.IO.File.Exists(DialTexturePath) ? System.IO.File.ReadAllBytes(DialTexturePath) : null;
        if (existing == null || !System.Linq.Enumerable.SequenceEqual(existing, png))
        {
            System.IO.File.WriteAllBytes(DialTexturePath, png);
            AssetDatabase.ImportAsset(DialTexturePath, ImportAssetOptions.ForceSynchronousImport);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(DialTexturePath);
        if (importer.mipmapEnabled != true || importer.anisoLevel != 8 || importer.wrapMode != TextureWrapMode.Clamp
            || importer.textureCompression != TextureImporterCompression.CompressedHQ || importer.maxTextureSize != TextureSize)
        {
            importer.mipmapEnabled = true; // read at a slant and from a few metres
            importer.anisoLevel = 8;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // thin ticks blur under the default ASTC
            importer.maxTextureSize = TextureSize;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(DialTexturePath);
    }

    /// <summary>
    /// A unit-radius disc in the XY plane facing +Z, the skid's front. The user looks at it along -Z, so their right is
    /// -X: u runs along -X, and the triangles wind clockwise as they see them (Unity's front face).
    /// </summary>
    public static Mesh DiscMesh()
    {
        ProjectSetup.EnsureFolder(Dir);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(DiscMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "DialDisc" };
            AssetDatabase.CreateAsset(mesh, DiscMeshPath);
        }

        var vertices = new Vector3[DiscSegments + 2];
        var uvs = new Vector2[vertices.Length];
        var normals = new Vector3[vertices.Length];
        var triangles = new int[DiscSegments * 3];
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (var i = 0; i <= DiscSegments; i++)
        {
            var a = 2f * Mathf.PI * i / DiscSegments; // clockwise from twelve as the user sees it
            var screen = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            vertices[i + 1] = new Vector3(-screen.x, screen.y, 0f);
            uvs[i + 1] = new Vector2(0.5f + 0.5f * screen.x, 0.5f + 0.5f * screen.y);
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.forward;
        }

        for (var i = 0; i < DiscSegments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }
}
