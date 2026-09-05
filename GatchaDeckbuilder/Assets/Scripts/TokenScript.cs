using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TokenButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Token Config")]
    public int tokenIndex; // 1-based index (e.g., 1 to 9)

    [Header("Visual Settings")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.15f, 1.15f, 1f);
    [SerializeField] private Color highlightColor = Color.yellow;

    [Tooltip("Speed of the smooth scaling transition (higher = faster)")]
    [SerializeField] private float scaleSpeed = 12f;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private Color originalColor;
    private Image buttonImage;
    private TokenSelectionManager manager;

    private void Awake()
    {
        buttonImage = GetComponent<Image>();
        originalScale = transform.localScale;
        targetScale = originalScale;

        if (buttonImage != null)
        {
            originalColor = buttonImage.color;
        }
    }

    private void Update()
    {
        // Smoothly interpolate towards the target scale over time
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSpeed);
    }

    public void Setup(int index, TokenSelectionManager parentManager)
    {
        tokenIndex = index;
        manager = parentManager;
    }

    public void Highlight(bool enable)
    {
        // Color updates instantly, scale updates smoothly via targetScale
        if (buttonImage != null)
        {
            buttonImage.color = enable ? highlightColor : originalColor;
        }

        targetScale = enable ? hoverScale : originalScale;
    }

    // Triggered when mouse hovers over this button
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (manager != null)
        {
            manager.HoverTokensUpTo(tokenIndex);
        }
    }

    // Triggered when mouse leaves this button
    public void OnPointerExit(PointerEventData eventData)
    {
        if (manager != null)
        {
            manager.ClearHover();
        }
    }

    // Triggered when mouse clicks this button
    public void OnPointerClick(PointerEventData eventData)
    {
        if (manager != null)
        {
            manager.SelectTokens(tokenIndex);
        }
    }
}