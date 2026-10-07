using UnityEngine;

namespace Fieldmate.Twin
{
    /// <summary>
    /// The gauge's digital window (#88): three seven-segment digits and a decimal point in one small mesh, drawn with the
    /// machine's occludable shader so real furniture hides it like the rest of the gauge (floating text would not be).
    /// Built once; a new value only moves the vertices of segments that switch, into one reused array. The colour comes
    /// from the status, through a property block (base and emission, so it reads as lit on the dark window).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SegmentReadout : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private float digitHeight = 0.013f;
        [SerializeField] private float digitWidth = 0.0075f;
        [SerializeField] private float stroke = 0.0016f;
        [SerializeField] private float pitch = 0.0115f;

        private const int Quads = SevenSegment.Digits * SevenSegment.Segments + 1; // + the decimal point
        private readonly byte[] masks = new byte[SevenSegment.Digits];
        private Vector3[] lit;
        private Vector3[] vertices;
        private Mesh mesh;
        private MaterialPropertyBlock block;
        private int shownTenths = -1;

        /// <summary>The value on display in tenths (68 for "6.8"); -1 before the first.</summary>
        public int ShownTenths => shownTenths;

        public Color Colour { get; private set; }

        /// <summary>Whether segment <paramref name="segment"/> of digit <paramref name="digit"/> (0 tens, 1 ones, 2 tenths) is lit.</summary>
        public bool IsLit(int digit, int segment) => SevenSegment.IsLit(masks[digit], segment);

        public void Show(float value)
        {
            Build();
            var tenths = SevenSegment.Tenths(value, masks);
            if (tenths == shownTenths)
            {
                return;
            }

            shownTenths = tenths;
            for (var d = 0; d < SevenSegment.Digits; d++)
            {
                for (var s = 0; s < SevenSegment.Segments; s++)
                {
                    var q = (d * SevenSegment.Segments + s) * 4;
                    var on = SevenSegment.IsLit(masks[d], s);
                    for (var v = 0; v < 4; v++)
                    {
                        vertices[q + v] = on ? lit[q + v] : lit[q]; // a dark segment collapses to a point
                    }
                }
            }

            mesh.SetVertices(vertices);
        }

        public void SetColour(Color colour)
        {
            Build();
            Colour = colour;
            var renderer = GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour * 0.35f);
            block.SetColor(EmissionColorId, colour);
            renderer.SetPropertyBlock(block);
        }

        private void Awake() => Build();

        // The front faces +Z and the user looks along -Z, so their right is -X; quads wind clockwise as they see them.
        private void Build()
        {
            if (mesh != null)
            {
                return;
            }

            block = new MaterialPropertyBlock();
            lit = new Vector3[Quads * 4];
            var triangles = new int[Quads * 6];
            var h = digitHeight * 0.5f;
            var w = digitWidth * 0.5f;
            var t = stroke * 0.5f;
            var gap = stroke * 0.6f;
            for (var d = 0; d < SevenSegment.Digits; d++)
            {
                var x = (d - 1) * pitch;
                Quad(d, 0, new Vector2(x, h - t), new Vector2(w - gap, t)); // a
                Quad(d, 1, new Vector2(x + w - t, h * 0.5f), new Vector2(t, h * 0.5f - gap)); // b
                Quad(d, 2, new Vector2(x + w - t, -h * 0.5f), new Vector2(t, h * 0.5f - gap)); // c
                Quad(d, 3, new Vector2(x, -h + t), new Vector2(w - gap, t)); // d
                Quad(d, 4, new Vector2(x - w + t, -h * 0.5f), new Vector2(t, h * 0.5f - gap)); // e
                Quad(d, 5, new Vector2(x - w + t, h * 0.5f), new Vector2(t, h * 0.5f - gap)); // f
                Quad(d, 6, new Vector2(x, 0f), new Vector2(w - gap, t)); // g
            }

            QuadAt(Quads - 1, new Vector2(pitch * 0.5f, -h + t), new Vector2(t * 1.1f, t * 1.1f)); // decimal point

            for (var q = 0; q < Quads; q++)
            {
                triangles[q * 6] = q * 4;
                triangles[q * 6 + 1] = q * 4 + 1;
                triangles[q * 6 + 2] = q * 4 + 2;
                triangles[q * 6 + 3] = q * 4;
                triangles[q * 6 + 4] = q * 4 + 2;
                triangles[q * 6 + 5] = q * 4 + 3;
            }

            vertices = (Vector3[])lit.Clone();
            var normals = new Vector3[lit.Length];
            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.forward;
            }

            mesh = new Mesh { name = "Segment readout" };
            mesh.MarkDynamic();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(pitch * 4f, digitHeight * 1.5f, 0.01f));
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void Quad(int digit, int segment, Vector2 centre, Vector2 half) =>
            QuadAt(digit * SevenSegment.Segments + segment, centre, half);

        // Centre and half size in the user's view (x right, y up).
        private void QuadAt(int quad, Vector2 centre, Vector2 half)
        {
            var i = quad * 4;
            lit[i] = Local(centre + new Vector2(-half.x, -half.y));
            lit[i + 1] = Local(centre + new Vector2(-half.x, half.y));
            lit[i + 2] = Local(centre + new Vector2(half.x, half.y));
            lit[i + 3] = Local(centre + new Vector2(half.x, -half.y));
        }

        private static Vector3 Local(Vector2 view) => new(-view.x, view.y, 0f);

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
    }
}
