using System.Collections;
using UnityEngine;

public class CardUI : MonoBehaviour
{
    private RectTransform rectTransform;
    private BalatroCardController balatroController;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        balatroController = GetComponent<BalatroCardController>();
    }

    public IEnumerator AnimateToHand(Vector3 targetLocalPos, Quaternion targetLocalRot, Vector3 targetScale, float duration)
    {
        Vector3 startPos = rectTransform.localPosition;
        Quaternion startRot = rectTransform.localRotation;
        Vector3 startScale = rectTransform.localScale;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

            rectTransform.localPosition = Vector3.Lerp(startPos, targetLocalPos, t);
            rectTransform.localRotation = Quaternion.Slerp(startRot, targetLocalRot, t);
            rectTransform.localScale = Vector3.Lerp(startScale, targetScale, t);

            yield return null;
        }

        rectTransform.localPosition = targetLocalPos;
        rectTransform.localRotation = targetLocalRot;
        rectTransform.localScale = targetScale;

        // Save base transform for hover/selection math
        if (balatroController != null)
        {
            balatroController.SaveBaseTransform();
        }
    }
}