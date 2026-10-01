using System.Collections.Generic;
using System.Text;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// The mate's speech bubble (#69, #71 device test): a small message toast on the mate's left with a tail pointing at
    /// it. It shows the line being spoken, word by word in step with the voice (at reading pace without one), the state
    /// while it listens or thinks, and fades out when the mate has been quiet for a while. Below it, a detail card appears
    /// only when there is something to read (setup instructions, a manual section, a step, the log); above it, the
    /// offline banner. The full recent transcript is kept for tests and the model, not drawn. Forwards the assistant state
    /// to the <see cref="HologramMate"/>. Built once with the UI kit; turned to face the user.
    /// </summary>
    public sealed class AssistantPanel : MonoBehaviour
    {
        private const int MaxLines = 5;
        private const float WidthMm = 330f;
        private const float HeightMm = 330f;

        /// <summary>Drawn at 0.6 of the kit's size: a 20 cm bubble that suits the small mate.</summary>
        private const float CaptionScale = 0.6f;

        /// <summary>From the mate's head to the bubble's centre, to the user's left, in metres.</summary>
        private const float BesideMate = 0.17f;

        /// <summary>Speaking pace of the voice in characters a second, and reading pace without one.</summary>
        private const float SpokenCps = 15f;
        private const float ReadCps = 28f;

        /// <summary>How long to wait for the voice before revealing at reading pace.</summary>
        private const float VoiceWaitSeconds = 1.5f;

        /// <summary>Quiet this long after the last line, the bubble fades out.</summary>
        private const float LingerSeconds = 6f;

        [SerializeField] private Transform head;
        [SerializeField] private Transform anchor;
        [SerializeField] private Vector3 anchorOffset = new(1.15f, 1.45f, 0.25f);
        [SerializeField] private HologramMate mate;
        [SerializeField] private bool followMate;

        private readonly Queue<string> lines = new();
        private string transcript = string.Empty;
        private TMP_Text stateText;
        private TMP_Text messageText;
        private TMP_Text detailEyebrow;
        private TMP_Text detailText;
        private TMP_Text bannerText;
        private GameObject banner;
        private GameObject detail;
        private CanvasGroup toast;
        private StatePulse pulse;
        private AssistantState state;

        private bool revealing;
        private float lineArrived;
        private float playStarted = -1f;
        private bool wasPlaying;
        private bool voiced;
        private float lastActivity = -100f;

        public string DetailText => detailText != null && detail.activeSelf ? detailText.text : string.Empty;
        public string TranscriptText => transcript;
        public string StateLabel => stateText != null ? stateText.text : string.Empty;
        public bool IsPulsing => pulse != null && pulse.IsPulsing;
        public string BannerText => banner != null && banner.activeSelf ? bannerText.text : string.Empty;

        /// <summary>The line in the bubble and how much of it is showing.</summary>
        public string MessageText => messageText != null ? messageText.text : string.Empty;
        public int VisibleCharacters => messageText != null ? Mathf.Min(messageText.maxVisibleCharacters, messageText.text.Length) : 0;
        public float ToastAlpha => toast != null ? toast.alpha : 0f;

        private void Awake() => Build();

        public HologramMate Mate => mate;

        public void SetHead(Transform headTransform)
        {
            head = headTransform;
            if (mate != null)
            {
                mate.SetHead(headTransform);
            }
        }

        /// <summary>The figure that speaks these lines; it follows the same state.</summary>
        public void SetMate(HologramMate figure) => mate = figure;

        /// <summary>Keeps the bubble beside the mate's head, on the user's left, wherever the mate is put (#71).</summary>
        public void FollowMate(HologramMate figure)
        {
            mate = figure;
            followMate = true;
        }

        /// <summary>A setup prompt in the detail card (#71): what to do now.</summary>
        public void ShowSetup(string eyebrow, string text) => ShowDetail(eyebrow, text);

        public void SetState(AssistantState next, string hint)
        {
            state = next;
            var (label, color, busy) = next switch
            {
                AssistantState.Listening => ("Listening…", Theme.Success, true),
                AssistantState.Transcribing => ("Transcribing…", Theme.Warning, true),
                AssistantState.Thinking => ("Thinking…", Theme.Warning, true),
                AssistantState.Speaking => ("Fieldmate", Theme.Accent, true),
                _ => (hint, Theme.TextMuted, false),
            };
            if (mate != null)
            {
                mate.SetState(next);
            }

            stateText.text = label;
            stateText.color = busy ? Theme.TextPrimary : Theme.TextSecondary;
            pulse.Set(color, busy);
            if (busy)
            {
                lastActivity = Time.time;
            }
        }

        public void Add(TranscriptEntry entry)
        {
            var line = entry.Kind switch
            {
                TranscriptKind.User => $"<color={Theme.SecondaryHex}>You</color>   {entry.Text}",
                TranscriptKind.Assistant => $"<color={Theme.AccentHex}>Fieldmate</color>   {entry.Text}",
                TranscriptKind.Tool => $"<size={Theme.Eyebrow}><color={Theme.MutedHex}>→ {entry.Text}</color></size>",
                TranscriptKind.Error => $"<color={Theme.DangerHex}>{entry.Text}</color>",
                _ => $"<i><color={Theme.SecondaryHex}>{entry.Text}</color></i>",
            };
            lines.Enqueue(line);
            while (lines.Count > MaxLines)
            {
                lines.Dequeue();
            }

            transcript = string.Join("\n", lines);

            // The bubble shows one line: what Fieldmate is saying (revealed as it is spoken), what the user said, or an error.
            switch (entry.Kind)
            {
                case TranscriptKind.Assistant:
                    messageText.text = entry.Text;
                    messageText.color = Theme.TextPrimary;
                    messageText.maxVisibleCharacters = 0;
                    revealing = true;
                    lineArrived = Time.time;
                    voiced = mate != null && mate.IsSpeechPlaying;
                    break;
                case TranscriptKind.User:
                case TranscriptKind.Error:
                    messageText.text = entry.Text;
                    messageText.color = entry.Kind == TranscriptKind.Error ? Theme.Danger : Theme.TextSecondary;
                    messageText.maxVisibleCharacters = int.MaxValue;
                    revealing = false;
                    break;
                default:
                    return; // tool calls stay out of the bubble
            }

            lastActivity = Time.time;
        }

        public void ShowBanner(string text)
        {
            bannerText.text = text ?? string.Empty;
            banner.SetActive(!string.IsNullOrEmpty(text));
        }

        public void ShowSection(ManualSection section) => ShowDetail("From the manual", $"<b>[{section.Id}] {section.Title}</b>\n{section.Text}");

        public void ShowStep(ProcedureDefinition procedure, int number)
        {
            var step = procedure.Steps[number - 1];
            ShowDetail("Procedure", $"<b>{procedure.Title}</b>\nStep {number} of {procedure.Steps.Count}: {step.Title}");
        }

        public void ShowNotes(IReadOnlyList<string> notes)
        {
            var sb = new StringBuilder("<b>Maintenance log</b>");
            foreach (var note in notes)
            {
                sb.Append("\n•  ").Append(note);
            }

            ShowDetail("Maintenance log", sb.ToString());
        }

        /// <summary>Pins the bubble beside the machine instead of the mate (no mate in the scene).</summary>
        public void Anchor(Transform machine, Vector3 localOffset)
        {
            anchor = machine;
            anchorOffset = localOffset;
        }

        /// <summary>Hides the detail card (setup finished, nothing opened).</summary>
        public void HideDetail() => detail.SetActive(false);

        private void ShowDetail(string eyebrow, string text)
        {
            detailEyebrow.text = eyebrow;
            detailText.text = text;
            detail.SetActive(!string.IsNullOrEmpty(text));
        }

        private void Update()
        {
            var now = Time.time;
            var playing = mate != null && mate.IsSpeechPlaying;
            if (playing && !wasPlaying)
            {
                playStarted = now;
                voiced = true;
            }

            wasPlaying = playing;
            if (playing)
            {
                lastActivity = now;
            }

            if (revealing)
            {
                var total = messageText.text.Length;
                int shown;
                if (voiced && playing)
                {
                    // In step with the voice: from when this speech started playing (it may have started before the text arrived).
                    shown = Mathf.FloorToInt((now - Mathf.Min(playStarted, now)) * SpokenCps);
                }
                else if (voiced)
                {
                    shown = total; // the voice has finished: the whole line
                }
                else if (now - lineArrived < VoiceWaitSeconds)
                {
                    shown = 0; // the voice is on its way
                }
                else
                {
                    shown = Mathf.FloorToInt((now - lineArrived - VoiceWaitSeconds) * ReadCps); // no voice: reading pace
                }

                shown = WholeWords(messageText.text, Mathf.Clamp(shown, 0, total));
                messageText.maxVisibleCharacters = shown;
                if (shown >= total)
                {
                    revealing = false;
                    lastActivity = now;
                }
            }

            var busy = revealing || playing || state != AssistantState.Idle;
            var target = busy || now - lastActivity < LingerSeconds ? 1f : 0f;
            toast.alpha = Mathf.MoveTowards(toast.alpha, target, Time.deltaTime / 0.35f);
        }

        // Reveal a word at a time: round up to the end of the word in progress.
        private static int WholeWords(string text, int count)
        {
            while (count > 0 && count < text.Length && text[count] != ' ')
            {
                count++;
            }

            return count;
        }

        private void LateUpdate()
        {
            if (followMate && mate != null && mate.HeadPivot != null && head != null)
            {
                var face = mate.HeadPivot.position;
                var toMate = face - head.position;
                toMate.y = 0f;
                var right = toMate.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, toMate.normalized) : Vector3.right;
                // The bubble's centre level with the mouth; the canvas centre sits lower because the detail card hangs below.
                transform.position = face - right * BesideMate + Vector3.down * 0.055f;
            }
            else if (anchor != null)
            {
                transform.position = anchor.TransformPoint(anchorOffset);
            }

            if (head != null)
            {
                UiKit.FaceAway(transform, head.position);
            }
        }

        private void Build()
        {
            UiKit.WorldCanvas(gameObject, WidthMm, HeightMm);
            transform.localScale *= CaptionScale;
            var pad = new Vector2(Theme.Gap + 8f, 0f);

            // Speech bubble, top half, with a tail on its right edge pointing at the mate.
            var toastRect = UiKit.Rect("Toast", transform, new Vector2(0f, 0.53f), Vector2.one);
            toast = toastRect.gameObject.AddComponent<CanvasGroup>();
            toast.alpha = 0f;
            var tail = UiKit.Card("Tail", toastRect, new Vector2(1f, 0.42f), new Vector2(1f, 0.42f), Theme.Surface, false,
                new Vector2(-16f, -16f), new Vector2(16f, 16f));
            tail.type = Image.Type.Simple;
            tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var card = UiKit.Card("Bubble", toastRect, Vector2.zero, Vector2.one, Theme.Surface, stroke: true).transform;

            var dot = UiKit.Rect("StateDot", card, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Theme.Gap + 8f, -34f), new Vector2(Theme.Gap + 24f, -18f)).gameObject;
            var dotImage = dot.AddComponent<Image>();
            dotImage.sprite = UiKit.Rounded;
            dotImage.type = Image.Type.Simple;
            dotImage.material = UiKit.ImageOverlay;
            dotImage.raycastTarget = false;
            pulse = dot.AddComponent<StatePulse>();
            stateText = UiKit.Label("State", card, new Vector2(0f, 1f), new Vector2(1f, 1f), Theme.Caption, Theme.TextSecondary,
                TextAlignmentOptions.MidlineLeft, semiBold: true, new Vector2(Theme.Gap + 34f, -44f), new Vector2(-Theme.Gap, -8f));
            stateText.textWrappingMode = TextWrappingModes.NoWrap;
            stateText.overflowMode = TextOverflowModes.Ellipsis;

            // The line: newest words at the bottom, older ones scroll off the top inside the mask.
            var viewport = UiKit.Rect("MessageViewport", card, Vector2.zero, Vector2.one, new Vector2(pad.x, 12f), new Vector2(-pad.x, -48f));
            viewport.gameObject.AddComponent<RectMask2D>();
            messageText = UiKit.Label("Message", viewport, Vector2.zero, Vector2.one, Theme.Body - 2f, Theme.TextPrimary, TextAlignmentOptions.BottomLeft);
            messageText.overflowMode = TextOverflowModes.Overflow;
            messageText.lineSpacing = 2f;
            messageText.text = string.Empty;

            // Detail card below the bubble, only when there is something to read.
            var detailCard = UiKit.Card("DetailBox", transform, Vector2.zero, new Vector2(1f, 0.5f), Theme.Surface, stroke: true);
            detail = detailCard.transform.parent.gameObject; // the stroke wraps the card
            var inset = new Vector2(Theme.Gap + 8f, 0f);
            detailEyebrow = UiKit.Eyebrow("DetailEyebrow", detailCard.transform, new Vector2(0f, 0.78f), new Vector2(1f, 0.95f), Theme.TextMuted);
            detailEyebrow.rectTransform.offsetMin = inset;
            detailEyebrow.text = string.Empty;
            detailText = UiKit.Label("Detail", detailCard.transform, new Vector2(0f, 0.05f), new Vector2(1f, 0.78f), Theme.Caption, Theme.TextSecondary,
                TextAlignmentOptions.TopLeft, semiBold: false, inset, -inset);
            detailText.text = string.Empty;
            detail.SetActive(false);

            // Offline toast above the bubble.
            banner = UiKit.Card("Banner", transform, new Vector2(0f, 1.02f), new Vector2(1f, 1.16f), new Color(0.55f, 0.15f, 0.12f, 0.95f)).gameObject;
            bannerText = UiKit.Label("BannerText", banner.transform, Vector2.zero, Vector2.one, Theme.Caption, Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, semiBold: true, new Vector2(Theme.Gap + 8f, 0f), new Vector2(-Theme.Gap, 0f));
            banner.SetActive(false);
        }
    }
}
