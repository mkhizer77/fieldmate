using System.Collections.Generic;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// The machine's Core models in the scene: manual, part catalog, retriever, telemetry simulation, fault model and the
    /// procedure runner, plus a registry of tagged scene parts. Telemetry steps every frame without allocating.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MachineServices : MonoBehaviour
    {
        [SerializeField] private TextAsset manualJson;

        [Tooltip("Demo story: the pump starts with a stuck relief cartridge (pressure alarm).")]
        [SerializeField] private bool startWithOverpressure = true;

        private readonly Dictionary<string, PartTag> parts = new();

        public MachineManual Manual { get; private set; }
        public PartCatalog Catalog { get; private set; }
        public ManualRetriever Retriever { get; private set; }
        public TelemetryModel Telemetry { get; private set; }
        public ProcedureRunner Runner { get; private set; }
        public IReadOnlyDictionary<string, PartTag> Parts => parts;

        private static readonly RaycastHit[] RayHits = new RaycastHit[16];

        /// <summary>Scene clock for procedure events (seconds since load).</summary>
        public double Now => Time.timeAsDouble;

        private void Awake()
        {
            Manual = MachineManual.Parse(manualJson.text);
            Catalog = PartCatalog.FromManual(Manual);
            Retriever = new ManualRetriever(Manual);

            var faults = FaultModel.CreateDefault();
            if (startWithOverpressure)
            {
                faults.Inject(FaultModel.Overpressure);
            }

            Telemetry = new TelemetryModel(faults);
            Runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
            Runner.SetInitialState("main_breaker", "on");
            Runner.SetInitialState("inlet_valve", "open");
            Runner.SetInitialState("pump_cover", "fitted");

            RefreshParts();
        }

        private void Update() => Telemetry.Step(Time.deltaTime);

        /// <summary>Re-scans the scene for <see cref="PartTag"/>s (call after placing or replacing the machine).</summary>
        public void RefreshParts()
        {
            parts.Clear();
            foreach (var tag in FindObjectsByType<PartTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!string.IsNullOrEmpty(tag.PartId))
                {
                    parts[tag.PartId] = tag;
                }
            }
        }

        public bool TryGetPart(string partId, out PartTag part) => parts.TryGetValue(partId ?? string.Empty, out part);

        /// <summary>
        /// The nearest tagged machine part along <paramref name="ray"/>, if any. Trigger volumes (grab spheres, sockets) and
        /// untagged colliders such as the room-scan mesh are skipped: on device they sat in front of parts and gaze never
        /// landed on the relief valve. No allocations.
        /// </summary>
        public PartInfo PartAlong(Ray ray, float maxDistance = 5f)
        {
            var count = Physics.RaycastNonAlloc(ray, RayHits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            PartTag nearest = null;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                if (RayHits[i].distance < nearestDistance && RayHits[i].collider.GetComponentInParent<PartTag>() is { } tag)
                {
                    nearest = tag;
                    nearestDistance = RayHits[i].distance;
                }
            }

            return nearest != null && Catalog.TryGet(nearest.PartId, out var part) ? part : null;
        }
    }
}
