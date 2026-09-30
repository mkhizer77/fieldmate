using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.UI
{
    /// <summary>A segmented progress bar: one rounded segment per step, filled up to the current one.</summary>
    public sealed class ProgressSegments : MonoBehaviour
    {
        private const float GapUnits = 6f;
        private readonly List<Image> segments = new();

        public int Total { get; private set; }
        public int Done { get; private set; }

        /// <summary>Shows <paramref name="done"/> of <paramref name="total"/> filled; <paramref name="current"/> marks the active one.</summary>
        public void Set(int done, int total, bool current = true)
        {
            Total = Mathf.Max(0, total);
            Done = Mathf.Clamp(done, 0, Total);
            while (segments.Count < Total)
            {
                segments.Add(UiKit.Card($"Segment {segments.Count + 1}", transform, Vector2.zero, Vector2.one, Theme.SurfaceRaised));
            }

            for (var i = 0; i < segments.Count; i++)
            {
                var active = i < Total;
                segments[i].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                var rect = segments[i].rectTransform;
                rect.anchorMin = new Vector2((float)i / Total, 0f);
                rect.anchorMax = new Vector2((float)(i + 1) / Total, 1f);
                rect.offsetMin = new Vector2(i == 0 ? 0f : GapUnits * 0.5f, 0f);
                rect.offsetMax = new Vector2(i == Total - 1 ? 0f : -GapUnits * 0.5f, 0f);
                segments[i].color = i < Done ? Theme.Accent : i == Done && current ? Theme.AccentSoft : Theme.SurfaceRaised;
            }
        }

        public void Clear() => Set(0, 0);
    }
}
