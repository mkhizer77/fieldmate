using Fieldmate.Markers;
using UnityEngine;

namespace Fieldmate.Twin
{
    /// <summary>
    /// What a machine tag stands for (#27): the machine it identifies, how the tag is fixed to it, and the manual its
    /// twin runs on. Loaded by tag id through Addressables (address <c>machines/tag-NN</c>), so new machines ship as
    /// content without code changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Fieldmate/Machine Definition")]
    public sealed class MachineDefinition : ScriptableObject
    {
        public const string AddressPrefix = "machines/tag-";

        [SerializeField] private int tagId;
        [Tooltip("Must equal the manual's machine name: the scan checks it is the machine this app runs.")]
        [SerializeField] private string machineName;
        [Tooltip("What the mate calls it out loud, e.g. \"the pump skid\".")]
        [SerializeField] private string spokenName;
        [SerializeField] private TextAsset manual;
        [Tooltip("Printed side of the tag's black square, metres.")]
        [SerializeField] private float tagSide = 0.2f;
        [SerializeField] private TagMountKind mount = TagMountKind.Floor;
        [Tooltip("From the tag to the machine's origin, in the machine's frame (x right, y up, z forward), metres.")]
        [SerializeField] private Vector3 offset;
        [SerializeField] private float yawDegrees;

        public int TagId => tagId;
        public string MachineName => machineName;
        public string SpokenName => string.IsNullOrEmpty(spokenName) ? machineName : spokenName;
        public TextAsset Manual => manual;
        public float TagSide => tagSide;
        public TagMountKind Mount => mount;
        public Vector3 Offset => offset;
        public float YawDegrees => yawDegrees;

        public static string AddressFor(int tag) => $"{AddressPrefix}{tag:00}";

        public void Configure(int tag, string name, string spoken, TextAsset manualJson, float side, TagMountKind kind, Vector3 fromTag, float yaw)
        {
            tagId = tag;
            machineName = name;
            spokenName = spoken;
            manual = manualJson;
            tagSide = side;
            mount = kind;
            offset = fromTag;
            yawDegrees = yaw;
        }
    }
}
