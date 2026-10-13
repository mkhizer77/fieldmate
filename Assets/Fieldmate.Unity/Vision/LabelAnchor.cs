using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Vision
{
    /// <summary>
    /// Pins "What's this?" answers in the room (design.md §5.5 step 4): a card above the hit point with a stem down to it,
    /// held for <see cref="Seconds"/> and then faded. A small pool of cards is built once and reused, oldest first; asking
    /// about the same part again moves its card instead of stacking a second. Hidden cards cost no draw calls; no
    /// per-frame allocations.
    /// </summary>
    public sealed class LabelAnchor : MonoBehaviour
    {
        public const float Seconds = 20f;
        public const int PoolSize = 3;
        private const float FadeSeconds = 0.6f;
        private const float CanvasScale = 0.0006f;
        private const float WidthUnits = 440f;
        private const float HeightUnits = 150f;
        private const float RiseMetres = 0.14f;

        [SerializeField] private Transform head;

        private Card[] cards;
        private int shownCount; // orders cards shown within the same frame

        public Transform Head
        {
            get => head;
            set => head = value;
        }

        /// <summary>Cards currently showing (tests, debug).</summary>
        public int ShownCount
        {
            get
            {
                var count = 0;
                foreach (var card in Cards)
                {
                    count += card.Shown ? 1 : 0;
                }

                return count;
            }
        }

        private Card[] Cards => cards ??= Build();

        /// <summary>Pins <paramref name="answer"/> at <paramref name="point"/> (world) for <see cref="Seconds"/>.</summary>
        public void Show(FusedAnswer answer, Vector3 point)
        {
            if (answer == null || answer.Source == AnswerSource.None)
            {
                return;
            }

            var card = Pick(answer.PartId);
            card.PartId = answer.PartId;
            card.Point = point;
            card.ShownAt = Time.time;
            card.Order = ++shownCount;
            card.Shown = true;
            card.Title.text = answer.Label;
            card.Help.text = answer.Help;
            var (chip, colour) = answer.Source == AnswerSource.ModelData ? ("Model data", Theme.Neutral)
                : answer.IsUnsure ? ("Not sure", Theme.Warning)
                : ("Camera", Theme.Accent);
            card.Chip.text = chip;
            card.ChipFill.color = colour;
            card.Root.position = point + Vector3.up * RiseMetres;
            Face(card);
            card.Canvas.enabled = true;
            card.Group.alpha = 1f;
        }

        /// <summary>The text of the card pinned for <paramref name="partId"/> (null for a non-part answer), or null.</summary>
        public string TitleFor(string partId)
        {
            foreach (var card in Cards)
            {
                if (card.Shown && card.PartId == partId)
                {
                    return card.Title.text;
                }
            }

            return null;
        }

        /// <summary>Where the card for <paramref name="partId"/> points, for tests and the debug overlay.</summary>
        public bool TryGetPoint(string partId, out Vector3 point)
        {
            foreach (var card in Cards)
            {
                if (card.Shown && card.PartId == partId)
                {
                    point = card.Point;
                    return true;
                }
            }

            point = default;
            return false;
        }

        public void HideAll()
        {
            foreach (var card in Cards)
            {
                Hide(card);
            }
        }

        private void LateUpdate()
        {
            if (cards == null)
            {
                return;
            }

            var now = Time.time;
            foreach (var card in cards)
            {
                if (!card.Shown)
                {
                    continue;
                }

                var age = now - card.ShownAt;
                if (age >= Seconds)
                {
                    Hide(card);
                    continue;
                }

                card.Group.alpha = Mathf.Clamp01((Seconds - age) / FadeSeconds);
                Face(card);
            }
        }

        // The card already showing this part, else a free one, else the oldest.
        private Card Pick(string partId)
        {
            Card free = null, oldest = null;
            foreach (var card in Cards)
            {
                if (card.Shown && partId != null && card.PartId == partId)
                {
                    return card;
                }

                if (!card.Shown)
                {
                    free ??= card;
                }
                else if (oldest == null || card.Order < oldest.Order)
                {
                    oldest = card;
                }
            }

            return free ?? oldest;
        }

        private void Face(Card card)
        {
            var viewer = head != null ? head : Camera.main != null ? Camera.main.transform : null;
            if (viewer != null)
            {
                UiKit.FaceAway(card.Root, viewer.position);
            }
        }

        private static void Hide(Card card)
        {
            card.Shown = false;
            card.Group.alpha = 0f;
            card.Canvas.enabled = false;
        }

        private Card[] Build()
        {
            var built = new Card[PoolSize];
            for (var i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"Vision Label {i + 1}", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var canvas = UiKit.WorldCanvas(go, WidthUnits * CanvasScale * 1000f, HeightUnits * CanvasScale * 1000f, 1f / (CanvasScale * 1000f));
                var root = go.transform;

                // Stem from the card down to the pinned point, in canvas units.
                var stem = RiseMetres / CanvasScale - HeightUnits * 0.5f;
                UiKit.Bar("Stem", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Theme.Accent, new Vector2(-1.5f, -stem), new Vector2(1.5f, 0f));
                UiKit.Card("Dot", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Theme.Accent, false,
                    new Vector2(-7f, -stem - 7f), new Vector2(7f, -stem + 7f));

                UiKit.Card("Card", root, Vector2.zero, Vector2.one, Theme.Surface, stroke: true);
                var chip = UiKit.Chip("Source", root, new Vector2(0f, 1f), new Vector2(0f, 1f), Theme.Accent, Theme.TextOnAccent, out var pill);
                pill.rectTransform.offsetMin = new Vector2(18f, -46f);
                pill.rectTransform.offsetMax = new Vector2(170f, -16f);
                var title = UiKit.Label("Title", root, new Vector2(0f, 0.38f), new Vector2(1f, 0.72f), 34f, Theme.TextPrimary,
                    TextAlignmentOptions.MidlineLeft, semiBold: true, new Vector2(18f, 0f), new Vector2(-18f, 0f));
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.overflowMode = TextOverflowModes.Ellipsis;
                var help = UiKit.Label("Help", root, new Vector2(0f, 0f), new Vector2(1f, 0.4f), 22f, Theme.TextSecondary,
                    TextAlignmentOptions.TopLeft, false, new Vector2(18f, 10f), new Vector2(-18f, 0f));
                title.text = string.Empty;
                help.text = string.Empty;
                chip.text = string.Empty;

                var group = go.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                canvas.enabled = false;
                built[i] = new Card { Root = root, Canvas = canvas, Group = group, Title = title, Help = help, Chip = chip, ChipFill = pill };
            }

            return built;
        }

        private sealed class Card
        {
            public Transform Root;
            public Canvas Canvas;
            public CanvasGroup Group;
            public TMP_Text Title;
            public TMP_Text Help;
            public TMP_Text Chip;
            public Image ChipFill;
            public string PartId;
            public Vector3 Point;
            public float ShownAt;
            public int Order;
            public bool Shown;
        }
    }
}
