using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // Using Unity's new Input System

public class BalatroCardController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Hover Visual Settings")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.15f, 1.15f, 1f);
    [SerializeField] private float hoverLiftAmount = 30f; // Pixels to move upward on hover
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Image cardFrameOrOutline;

    [Header("Balatro Tilt Settings")]
    [SerializeField] private float maxTiltAngle = 12f; // Degrees of rotation toward cursor

    [Header("Animation Tuning")]
    [SerializeField] private float lerpSpeed = 12f;

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

    private void Start()
    {
        SaveBaseTransform();
    }

    public void SaveBaseTransform()
    {
        baseLocalPosition = rectTransform.localPosition;
        baseLocalRotation = rectTransform.localRotation;
        targetLocalPosition = baseLocalPosition;
        targetLocalRotation = baseLocalRotation;
        targetScale = Vector3.one;
    }

    private void Update()
    {
        // Calculate dynamic cursor tilt using active pointer coordinates
        if (isHovered && !isSelected)
        {
            CalculateCursorTilt();
        }

        // Smoothly lerp position, rotation, and scale
        rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetLocalPosition, Time.deltaTime * lerpSpeed);
        rectTransform.localRotation = Quaternion.Slerp(rectTransform.localRotation, targetLocalRotation, Time.deltaTime * lerpSpeed);
        rectTransform.localScale = Vector3.Lerp(rectTransform.localScale, targetScale, Time.deltaTime * lerpSpeed);
    }

    private void CalculateCursorTilt()
    {
        // Get mouse/pointer position safely with the new Input System
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
            // Normalize relative to card dimensions (-0.5 to 0.5)
            float normalizedX = Mathf.Clamp(localMousePos.x / rectTransform.rect.width, -0.5f, 0.5f);
            float normalizedY = Mathf.Clamp(localMousePos.y / rectTransform.rect.height, -0.5f, 0.5f);

            // Pitch and yaw angles
            float tiltX = -normalizedY * maxTiltAngle;
            float tiltY = normalizedX * maxTiltAngle;

            targetLocalRotation = baseLocalRotation * Quaternion.Euler(tiltX, tiltY, 0f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;

        if (!isSelected)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * hoverLiftAmount);
            targetScale = hoverScale;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = highlightColor;
            }

            transform.SetAsLastSibling();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;

        if (!isSelected)
        {
            targetLocalPosition = baseLocalPosition;
            targetLocalRotation = baseLocalRotation;
            targetScale = Vector3.one;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = originalColor;
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        SetSelected(!isSelected);
    }

    public void SetSelected(bool select)
    {
        isSelected = select;

        if (isSelected)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * hoverLiftAmount);
            targetScale = hoverScale;
            targetLocalRotation = baseLocalRotation;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = highlightColor;
            }

            transform.SetAsLastSibling();
            Debug.Log($"[Card System] Selected Card: {gameObject.name}");
        }
        else
        {
            targetLocalPosition = baseLocalPosition;
            targetLocalRotation = baseLocalRotation;
            targetScale = Vector3.one;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = originalColor;
            }
        }
    }
}