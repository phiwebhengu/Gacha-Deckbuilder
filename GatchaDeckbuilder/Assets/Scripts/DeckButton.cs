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

    [Header("Target Managers")]
    [Tooltip("Drag the corresponding TokenSelectionManager here in the Inspector")]
    [SerializeField] private TokenSelectionManager tokenManager;

    [Tooltip("Drag the DrawTimerManager here to restrict drawing window.")]
    [SerializeField] private DrawTimerManager timerManager; // <-- Added reference

    [Header("Visual Tuning")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.08f, 1.08f, 1f);
    [SerializeField] private float scaleSpeed = 12f;

    private Vector3 originalScale;
    private Vector3 targetScale;

    public DeckType Type => deckType;
    public RectTransform DeckTransform => deckTransform != null ? deckTransform : GetComponent<RectTransform>();

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;

        // Fallback searches if unassigned
        if (tokenManager == null)
        {
            tokenManager = GetComponentInParent<TokenSelectionManager>();
            if (tokenManager == null) tokenManager = FindObjectOfType<TokenSelectionManager>();
        }

        if (timerManager == null)
        {
            timerManager = FindObjectOfType<DrawTimerManager>();
        }
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSpeed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Only trigger hover animation if draw phase is active
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
        // Block clicks if draw phase is not currently running
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