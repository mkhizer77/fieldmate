using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.UI;

/// <summary>
/// Builders for the runtime UI: world canvases, rounded cards, labels, chips. Everything is built once at Awake from
/// <see cref="Theme"/>; nothing here runs per frame. The rounded-corner sprite is generated once and shared, so all
/// cards in a canvas batch into one draw.
/// </summary>
public static class UiKit
{
    private const int SpriteSize = 96;
    private const float SpriteRadius = 32f;
    private static Sprite rounded;

    /// <summary>A 9-sliced rounded rectangle (anti-aliased), shared by every card, pill and chip.</summary>
    public static Sprite Rounded
    {
        get
        {
            if (rounded != null)
            {
                return rounded;
            }

            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false) { name = "UiKit Rounded", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[SpriteSize * SpriteSize];
            var inner = SpriteSize * 0.5f - SpriteRadius;
            for (var y = 0; y < SpriteSize; y++)
            {
                for (var x = 0; x < SpriteSize; x++)
                {
                    // Signed distance to a rounded box centred in the texture; 1 px of anti-aliasing at the edge.
                    var dx = Mathf.Max(Mathf.Abs(x + 0.5f - SpriteSize * 0.5f) - inner, 0f);
                    var dy = Mathf.Max(Mathf.Abs(y + 0.5f - SpriteSize * 0.5f) - inner, 0f);
                    var distance = Mathf.Sqrt(dx * dx + dy * dy) - SpriteRadius;
                    var alpha = Mathf.Clamp01(0.5f - distance);
                    pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var border = SpriteRadius + 8f;
            // PPU 100 matches the Canvas reference PPU, so one sprite pixel is one canvas unit (mm) in a sliced Image.
            rounded = Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            rounded.name = "UiKit Rounded";
            return rounded;
        }
    }

    /// <summary>Turns <paramref name="go"/> into a world-space canvas of the given size in millimetres.</summary>
    public static Canvas WorldCanvas(GameObject go, float widthMm, float heightMm, float unitsPerMm = 1f)
    {
        var canvas = go.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = go.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(widthMm * unitsPerMm, heightMm * unitsPerMm);
        rect.localScale = Vector3.one * (0.001f / unitsPerMm);
        if (go.GetComponent<CanvasGroup>() == null)
        {
            go.AddComponent<CanvasGroup>();
        }

        return canvas;
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    /// <summary>A rounded fill (sliced) with an optional hairline stroke behind it.</summary>
    public static Image Card(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color fill, bool stroke = false,
        Vector2 offsetMin = default, Vector2 offsetMax = default)
    {
        if (stroke)
        {
            var outline = Rect($"{name} Stroke", parent, anchorMin, anchorMax, offsetMin, offsetMax).gameObject.AddComponent<Image>();
            Style(outline, Theme.Stroke);
            parent = outline.transform;
            anchorMin = Vector2.zero;
            anchorMax = Vector2.one;
            offsetMin = new Vector2(Theme.HairLine, Theme.HairLine);
            offsetMax = new Vector2(-Theme.HairLine, -Theme.HairLine);
        }

        var image = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax).gameObject.AddComponent<Image>();
        Style(image, fill);
        return image;
    }

    /// <summary>A plain rectangle (no rounding): accent bars, stems, dividers.</summary>
    public static Image Bar(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color color, Vector2 offsetMin = default, Vector2 offsetMax = default)
    {
        var image = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TMP_Text Label(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool semiBold = false, Vector2 offsetMin = default, Vector2 offsetMax = default)
    {
        var text = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = semiBold ? Theme.SemiBold : Theme.Regular;
        text.fontSize = size;
        text.color = color;
        text.alignment = align;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
        text.extraPadding = true; // crisper edges at oblique viewing angles
        return text;
    }

    /// <summary>Small uppercase tracking label: section names, step counters.</summary>
    public static TMP_Text Eyebrow(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var text = Label(name, parent, anchorMin, anchorMax, Theme.Eyebrow, color, align, semiBold: true);
        text.characterSpacing = 6f;
        text.fontStyle = FontStyles.UpperCase;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true; // long procedure names shrink a little before they get an ellipsis
        text.fontSizeMin = Theme.Eyebrow - 5f;
        text.fontSizeMax = Theme.Eyebrow;
        return text;
    }

    /// <summary>A rounded pill with centred eyebrow text; returns the text so callers can recolour both.</summary>
    public static TMP_Text Chip(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color fill, Color textColor, out Image pill)
    {
        pill = Card(name, parent, anchorMin, anchorMax, fill);
        var text = Eyebrow($"{name} Text", pill.transform, Vector2.zero, Vector2.one, textColor, TextAlignmentOptions.Center);
        text.characterSpacing = 4f;
        return text;
    }

    /// <summary>Yaw-only billboard: keeps the panel upright and turned to the viewer.</summary>
    public static void FaceAway(Transform panel, Vector3 viewerPosition)
    {
        var away = panel.position - viewerPosition;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-4f)
        {
            panel.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }

    private static void Style(Image image, Color color)
    {
        image.sprite = Rounded;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = SpriteRadius / Theme.Radius; // corner radius in canvas units
        image.color = color;
        image.raycastTarget = false;
    }
}
