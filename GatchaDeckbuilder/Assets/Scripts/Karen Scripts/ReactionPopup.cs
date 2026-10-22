using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// A short floating text such as "Overload +1 ATK". It pops in above a text on the HUD, drifts upwards, fades out
/// and removes itself. Started with StartCoroutine(ReactionPopup.Routine(...)) from EndTurnManager.
///
/// If a 'template' TMP text is given (the one you styled in the Inspector), every pop-up is a COPY of it, so it keeps
/// your font, size and layout. Without a template, a plain text is created from code.
/// </summary>
public static class ReactionPopup
{
    public static IEnumerator Routine(
        TextMeshProUGUI anchor, string message, Color color, TMP_FontAsset font,
        float delay = 0f, float fontSize = 40f, float lifetime = 1.3f, float rise = 80f,
        TextMeshProUGUI template = null)
    {
        if (anchor == null)
        {
            Debug.LogWarning("[ReactionPopup] '" + message + "' not shown: the Attack/Defense text it should appear above is not assigned on EndTurnManager.");
            yield break;
        }
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (anchor == null || anchor.canvas == null)
        {
            Debug.LogWarning("[ReactionPopup] '" + message + "' not shown: the anchor text is not inside a Canvas.");
            yield break;
        }

        Canvas root = anchor.canvas.rootCanvas;
        float scale = Mathf.Max(0.01f, root.scaleFactor);

        GameObject go;
        TextMeshProUGUI text;

        if (template != null)
        {
            // Copy of the text you set up in the Inspector (font, size, anchors and everything else are kept).
            go = Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = "ReactionPopup";
            go.SetActive(true);
            text = go.GetComponent<TextMeshProUGUI>();
            go.transform.SetAsLastSibling();
        }
        else
        {
            go = new GameObject("ReactionPopup", typeof(RectTransform));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root.transform, false);
            go.transform.SetAsLastSibling();

            var sub = go.AddComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = 1000;

            text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = new Vector2(700f, 80f);
            if (font != null) text.font = font;
        }

        text.text = message;
        text.color = color;
        text.raycastTarget = false;

        var rt = text.rectTransform;
        Vector3 baseScale = rt.localScale;

        // With a template the pop-up stays exactly where the template is. Without one it floats above the anchor.
        bool stayInPlace = template != null;
        Vector3 basePos = stayInPlace ? rt.position : anchor.transform.position + Vector3.up * (70f * scale);
        if (!stayInPlace) rt.position = basePos;

        Debug.Log("[ReactionPopup] showing '" + message + "' above " + anchor.name);

        try
        {
            float elapsed = 0f;
            while (elapsed < lifetime)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / lifetime);

                // drift upwards, fast at first and then slowing down
                float ease = 1f - (1f - p) * (1f - p);
                if (!stayInPlace) rt.position = basePos + Vector3.up * (rise * scale * ease);

                // quick "pop": small -> a bit too big -> normal size
                float s = p < 0.12f ? Mathf.Lerp(0.6f, 1.2f, p / 0.12f)
                        : p < 0.24f ? Mathf.Lerp(1.2f, 1f, (p - 0.12f) / 0.12f)
                        : 1f;
                rt.localScale = baseScale * s;

                // stay solid, then fade out over the last 30%
                Color c = color;
                c.a = p < 0.7f ? color.a : color.a * (1f - (p - 0.7f) / 0.3f);
                text.color = c;

                yield return null;
            }
        }
        finally
        {
            if (go != null) Object.Destroy(go);
        }
    }
}