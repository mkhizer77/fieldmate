using UnityEngine;

namespace Fieldmate.Assistant;

/// <summary>
/// Surfaces of revolution for the hologram figure (#69): a profile of (radius, height) points turned about the Y axis,
/// with an elliptical cross-section (<c>depth</c> scales Z) and smooth normals so the fresnel rim reads as a silhouette.
/// Built once at start-up; no per-frame work.
/// </summary>
public static class LatheMesh
{
    public static Mesh Build(string name, Vector2[] profile, int segments, float depth = 1f)
    {
        var rings = profile.Length;
        var columns = segments + 1; // seam duplicated so every ring closes cleanly
        var vertices = new Vector3[rings * columns];
        var normals = new Vector3[vertices.Length];
        for (var r = 0; r < rings; r++)
        {
            // Profile tangent → outward normal in the (radius, height) plane.
            var prev = profile[Mathf.Max(0, r - 1)];
            var next = profile[Mathf.Min(rings - 1, r + 1)];
            var tangent = (next - prev).normalized;
            var outward = new Vector2(tangent.y, -tangent.x);
            for (var s = 0; s < columns; s++)
            {
                var a = s * Mathf.PI * 2f / segments;
                var cos = Mathf.Cos(a);
                var sin = Mathf.Sin(a);
                var i = r * columns + s;
                vertices[i] = new Vector3(cos * profile[r].x, profile[r].y, sin * profile[r].x * depth);
                // Normal of the scaled ellipse: scale the radial part by 1/depth in Z.
                normals[i] = new Vector3(cos * outward.x, outward.y, sin * outward.x / Mathf.Max(depth, 1e-3f)).normalized;
            }
        }

        var triangles = new int[(rings - 1) * segments * 6];
        var t = 0;
        for (var r = 0; r < rings - 1; r++)
        {
            for (var s = 0; s < segments; s++)
            {
                var a = r * columns + s;
                var b = a + columns;
                // Clockwise seen from outside (Unity's front face), for profiles listed bottom to top.
                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = a + 1;
                triangles[t++] = b;
                triangles[t++] = b + 1;
                triangles[t++] = a + 1;
            }
        }

        var mesh = new Mesh { name = name, vertices = vertices, normals = normals, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>A flat strip along an arc (a lip, a brow), facing +Z, <paramref name="width"/> wide and bowed by <paramref name="bow"/>.</summary>
    public static Mesh Arc(string name, float width, float bow, float thickness, int segments = 12)
    {
        var vertices = new Vector3[(segments + 1) * 2];
        var normals = new Vector3[vertices.Length];
        for (var i = 0; i <= segments; i++)
        {
            var u = (float)i / segments * 2f - 1f;
            var y = bow * (1f - u * u);
            var taper = thickness * (0.35f + 0.65f * (1f - u * u));
            vertices[i * 2] = new Vector3(u * width * 0.5f, y + taper * 0.5f, 0f);
            vertices[i * 2 + 1] = new Vector3(u * width * 0.5f, y - taper * 0.5f, 0f);
            normals[i * 2] = Vector3.forward;
            normals[i * 2 + 1] = Vector3.forward;
        }

        var triangles = new int[segments * 6];
        for (var i = 0; i < segments; i++)
        {
            var a = i * 2;
            triangles[i * 6] = a;
            triangles[i * 6 + 1] = a + 1;
            triangles[i * 6 + 2] = a + 2;
            triangles[i * 6 + 3] = a + 2;
            triangles[i * 6 + 4] = a + 1;
            triangles[i * 6 + 5] = a + 3;
        }

        var mesh = new Mesh { name = name, vertices = vertices, normals = normals, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }
}
