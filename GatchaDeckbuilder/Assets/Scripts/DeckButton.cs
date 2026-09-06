using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum DeckType
{
    Action,
    Support
}

public class DeckButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Deck Config")]
    [SerializeField] private DeckType deckType;
    [SerializeField] private RectTransform deckTransform;

    [Header("Visual Tuning")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.08f, 1.08f, 1f);
    [SerializeField] private float scaleSpeed = 12f;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private TokenSelectionManager tokenManager;

    public DeckType Type => deckType;
    public RectTransform DeckTransform => deckTransform != null ? deckTransform : GetComponent<RectTransform>();

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;
        tokenManager = FindObjectOfType<TokenSelectionManager>();
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSpeed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetScale = hoverScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (tokenManager != null)
        {
            tokenManager.OnDeckSelected(this);
        }
    }
}