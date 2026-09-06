using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class BalatroCardController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Hover Visual Settings")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.15f, 1.15f, 1f);
    [SerializeField] private float hoverLiftAmount = 30f; // Pixels lifted while hovering
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Image cardFrameOrOutline;

    [Header("Selection Visual Settings")]
    [Tooltip("Height above the resting hand position when locked in as selected")]
    [SerializeField] private float selectedLiftAmount = 60f;
    [SerializeField] private Vector3 selectedScale = new Vector3(1.15f, 1.15f, 1f);

    [Header("Balatro Tilt Settings")]
    [SerializeField] private float maxTiltAngle = 12f;

    [Header("Animation Tuning")]
    [SerializeField] private float lerpSpeed = 12f;

    [Header("Scale Settings")]
    [Tooltip("Base scale of the card while resting in hand (unselected)")]
    [SerializeField] private Vector3 restingScale = new Vector3(0.7f, 0.7f, 1f);

    // Internal State Tracking
    private bool isHovered = false;
    private bool isSelected = false;

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 targetLocalPosition;
    private Quaternion targetLocalRotation;
    private Vector3 targetScale = Vector3.one;

    private Color originalColor = Color.white;
    private RectTransform rectTransform;
    private Canvas parentCanvas;

    public bool IsSelected => isSelected;

    public Vector3 RestingScale => restingScale;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (cardFrameOrOutline == null)
        {
            cardFrameOrOutline = GetComponent<Image>();
        }

        if (cardFrameOrOutline != null)
        {
            originalColor = cardFrameOrOutline.color;
        }
    }

    // Call this whenever the Hand Manager updates the fan layout positions
    public void SaveBaseTransform()
    {
        baseLocalPosition = rectTransform.localPosition;
        baseLocalRotation = rectTransform.localRotation;

        UpdateTargetVisuals();
    }

    private void Update()
    {
        // Dynamic Balatro tilt only occurs when hovering and NOT locked in selection
        if (isHovered && !isSelected)
        {
            CalculateCursorTilt();
        }

        // Interpolate position, rotation, and scale smoothly
        rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetLocalPosition, Time.deltaTime * lerpSpeed);
        rectTransform.localRotation = Quaternion.Slerp(rectTransform.localRotation, targetLocalRotation, Time.deltaTime * lerpSpeed);
        rectTransform.localScale = Vector3.Lerp(rectTransform.localScale, targetScale, Time.deltaTime * lerpSpeed);
    }

    private void CalculateCursorTilt()
    {
        Vector2 mousePos = Vector2.zero;

        if (Mouse.current != null)
        {
            mousePos = Mouse.current.position.ReadValue();
        }
        else if (Pointer.current != null)
        {
            mousePos = Pointer.current.position.ReadValue();
        }
        else
        {
            return;
        }

        Camera uiCamera = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? parentCanvas.worldCamera : null;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, mousePos, uiCamera, out Vector2 localMousePos))
        {
            float normalizedX = Mathf.Clamp(localMousePos.x / rectTransform.rect.width, -0.5f, 0.5f);
            float normalizedY = Mathf.Clamp(localMousePos.y / rectTransform.rect.height, -0.5f, 0.5f);

            float tiltX = -normalizedY * maxTiltAngle;
            float tiltY = normalizedX * maxTiltAngle;

            targetLocalRotation = baseLocalRotation * Quaternion.Euler(tiltX, tiltY, 0f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        UpdateTargetVisuals();

        if (!isSelected)
        {
            transform.SetAsLastSibling();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        UpdateTargetVisuals();
    }

    // Direct interface click handler
    public void OnPointerClick(PointerEventData eventData)
    {
        ToggleSelection();
    }

    // Exposed parameterless method for standard UI Button OnClick() events
    public void OnCardClicked()
    {
        ToggleSelection();
    }

    public void ToggleSelection()
    {
        isSelected = !isSelected;
        UpdateTargetVisuals();

        if (isSelected)
        {
            transform.SetAsLastSibling();
            Debug.Log($"[Card System] Selected: {gameObject.name}");
        }
        else
        {
            Debug.Log($"[Card System] Deselected: {gameObject.name}");
        }

        // Notify End Turn system to update button visibility
        EndTurnManager endTurnMgr = FindObjectOfType<EndTurnManager>();
        if (endTurnMgr != null)
        {
            endTurnMgr.UpdateEndTurnButtonVisibility();
        }
    }

    // Central state machine that determines target transforms based on (isSelected, isHovered)
    private void UpdateTargetVisuals()
    {
        if (isSelected)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * selectedLiftAmount);
            targetLocalRotation = baseLocalRotation;
            targetScale = selectedScale;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = highlightColor;
            }
        }
        else if (isHovered)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * hoverLiftAmount);
            targetScale = hoverScale;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = highlightColor;
            }
        }
        else
        {
            // Resets back to resting scale instead of Vector3.one
            targetLocalPosition = baseLocalPosition;
            targetLocalRotation = baseLocalRotation;
            targetScale = restingScale;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = originalColor;
            }
        }
    }
}