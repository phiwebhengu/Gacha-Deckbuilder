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
    [SerializeField] private DeckType deckType = DeckType.Action;
    [SerializeField] private RectTransform deckTransform;

    [Header("Deck Category Settings")]
    [SerializeField] private bool isSupportDeck = false;

    [Header("Target Managers")]
    [SerializeField] private TokenSelectionManager tokenManager;
    [SerializeField] private DrawTimerManager timerManager;

    [Header("Visual Tuning")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.08f, 1.08f, 1f);
    [SerializeField] private float scaleSpeed = 12f;

    private Vector3 originalScale;
    private Vector3 targetScale;

    public DeckType Type => deckType;
    public bool IsSupportDeck => isSupportDeck || deckType == DeckType.Support;
    public RectTransform DeckTransform => deckTransform != null ? deckTransform : GetComponent<RectTransform>();

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;

        if (deckType == DeckType.Support)
        {
            isSupportDeck = true;
        }

        if (tokenManager == null)
        {
            tokenManager = GetComponentInParent<TokenSelectionManager>();
            if (tokenManager == null) tokenManager = FindFirstObjectByType<TokenSelectionManager>();
        }

        if (timerManager == null)
        {
            timerManager = FindFirstObjectByType<DrawTimerManager>();
        }
    }

    private void OnValidate()
    {
        if (deckType == DeckType.Support)
        {
            isSupportDeck = true;
        }
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSpeed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (timerManager != null && timerManager.IsDrawPhaseActive)
        {
            targetScale = hoverScale;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (timerManager != null && !timerManager.IsDrawPhaseActive)
        {
            Debug.LogWarning("[DeckButton] Draw attempted outside active draw window!");
            return;
        }

        if (tokenManager != null)
        {
            tokenManager.OnDeckSelected(this);
        }
    }
}