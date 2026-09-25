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

        /// <summary>The tagged part hit by <paramref name="ray"/>, if any.</summary>
        public PartInfo PartAlong(Ray ray, float maxDistance = 5f)
        {
            if (!Physics.Raycast(ray, out var hit, maxDistance))
            {
                return null;
            }

            var tag = hit.collider.GetComponentInParent<PartTag>();
            return tag != null && Catalog.TryGet(tag.PartId, out var part) ? part : null;
        }
    }
}
