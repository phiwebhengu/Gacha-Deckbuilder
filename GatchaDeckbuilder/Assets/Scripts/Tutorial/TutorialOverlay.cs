using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutorial's screen layer, built in code so it needs no prefab: a dark shade with a spotlight hole that follows
/// its target, Paimon's speech bubble, a tutorial column on the left (lesson header, short caption, skip button),
/// the featured / 50-50 chips above the Action deck, and the finish panel.
/// Clicks pass through the spotlight hole only when the player is meant to click it; otherwise input is blocked
/// while the tutorial is in charge. TutorialDirector decides what to show; this script only draws it.
///
/// Positions match the game's 1920x1080 layout: the tutorial column sits between Round text and the Support deck,
/// the chips sit above the Action deck.
/// </summary>
public class TutorialOverlay : MonoBehaviour
{
    public event Action ContinueClicked;
    public event Action SkipClicked;
    public event Action PlayAIClicked;
    public event Action MenuClicked;

    private static readonly Color ShadeColor = new Color(0f, 0f, 0f, 0.8f);
    private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.15f, 0.97f);
    private static readonly Color Gold = new Color(1f, 0.8f, 0.35f, 1f);
    private static readonly Color Accent = new Color(0.45f, 0.85f, 1f, 1f);
    private static readonly Color Good = new Color(0.45f, 0.9f, 0.55f, 1f);
    private static readonly Color TextColor = new Color(0.95f, 0.95f, 0.97f, 1f);
    private static readonly Color DarkLabel = new Color(0.1f, 0.08f, 0.02f, 1f);

    private const float SpotlightPadding = 14f;
    private const float FrameThickness = 4f;
    private const float BorderThickness = 3f;
    private const float EdgeMargin = 16f;

    private TMP_FontAsset font;
    private Texture2D roundedTexture;
    private Sprite roundedSprite;

    private Canvas canvas;
    private RectTransform root;
    private readonly RectTransform[] shades = new RectTransform[4];
    private readonly Image[] frameBars = new Image[4];
    private RectTransform holeBlocker;
    private RectTransform inputBlocker;

    private RectTransform bubble;
    private TextMeshProUGUI bubbleText;
    private TextMeshProUGUI hintText;
    private Button continueButton;
    private TextMeshProUGUI continueLabel;

    private RectTransform lessonCard;
    private TextMeshProUGUI lessonNumberText;
    private TextMeshProUGUI lessonTitleText;
    private RectTransform caption;
    private TextMeshProUGUI captionText;

    private RectTransform featuredChip;
    private TextMeshProUGUI featuredText;
    private RectTransform guaranteeChip;
    private TextMeshProUGUI guaranteeText;

    private Button skipButton;
    private TextMeshProUGUI skipLabel;
    private bool skipArmed;

    private RectTransform finishPanel;
    private TextMeshProUGUI finishTitle;
    private TextMeshProUGUI finishText;

    private Func<RectTransform> spotlightTarget;

    // The bubble picks its side (top or bottom) once per spotlight. Deciding every frame made it flip back and forth
    // when a hovered button near the middle of the screen grew a little.
    private bool bubbleSideChosen;
    private bool bubbleOnTop;
    private bool shadeOn;
    private bool holeClickable;
    private bool inputBlocked;

    public RectTransform FeaturedChip => featuredChip;
    public RectTransform GuaranteeChip => guaranteeChip;

    // ===================== setup =====================

    /// <param name="gameCanvas">The game's canvas. The overlay copies its scaling so both line up at any resolution.</param>
    public void Build(TMP_FontAsset fontAsset, Sprite guidePortrait, Canvas gameCanvas)
    {
        font = fontAsset != null ? fontAsset : TMP_Settings.defaultFontAsset;
        roundedTexture = MakeRoundedTexture(48, 16);
        roundedSprite = Sprite.Create(roundedTexture, new Rect(0, 0, 48, 48), new Vector2(0.5f, 0.5f), 100f, 0,
                                      SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));

        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 500;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        CanvasScaler gameScaler = gameCanvas != null ? gameCanvas.rootCanvas.GetComponent<CanvasScaler>() : null;
        if (gameScaler != null)
        {
            scaler.uiScaleMode = gameScaler.uiScaleMode;
            scaler.scaleFactor = gameScaler.scaleFactor;
            scaler.referenceResolution = gameScaler.referenceResolution;
            scaler.screenMatchMode = gameScaler.screenMatchMode;
            scaler.matchWidthOrHeight = gameScaler.matchWidthOrHeight;
        }

        gameObject.AddComponent<GraphicRaycaster>();
        root = (RectTransform)transform;

        for (int i = 0; i < shades.Length; i++)
        {
            Image shade = NewImage("Shade" + i, root, ShadeColor, false);
            shades[i] = shade.rectTransform;
            CenterAnchor(shades[i]);
        }

        for (int i = 0; i < frameBars.Length; i++)
        {
            frameBars[i] = NewImage("Frame" + i, root, Gold, false);
            frameBars[i].raycastTarget = false;
            CenterAnchor(frameBars[i].rectTransform);
        }

        // Invisible, but they catch clicks: one covers the spotlight hole on read-only steps,
        // the other covers the whole screen while the game plays out a scripted moment.
        holeBlocker = NewImage("HoleBlocker", root, Color.clear, false).rectTransform;
        CenterAnchor(holeBlocker);
        inputBlocker = NewImage("InputBlocker", root, Color.clear, false).rectTransform;
        Stretch(inputBlocker, 0f, 0f);

        BuildTutorialColumn();
        BuildChips();
        BuildBubble(guidePortrait);
        BuildFinishPanel();

        SetShade(false);
        SetInputBlocked(false);
        ClearSpotlight();
        HideBubble();
        HideCaption();
        ShowChips(null, null);
        finishPanel.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (roundedSprite != null) Destroy(roundedSprite);
        if (roundedTexture != null) Destroy(roundedTexture);
    }

    /// <summary>The lesson card (lesson number, title, Skip) and the caption under it, between Round text and the Support deck.</summary>
    private void BuildTutorialColumn()
    {
        lessonCard = NewBorderedPanel("LessonCard", root, Gold);
        lessonCard.GetComponent<Image>().raycastTarget = false;
        PlaceTopLeft(lessonCard, new Vector2(24f, -244f), new Vector2(480f, 148f));

        lessonNumberText = NewText("LessonNumber", lessonCard, "", 22f, Gold, TextAlignmentOptions.TopLeft);
        lessonNumberText.fontStyle = FontStyles.Bold;
        PlaceTopLeft(lessonNumberText.rectTransform, new Vector2(20f, -14f), new Vector2(440f, 28f));

        lessonTitleText = NewText("LessonTitle", lessonCard, "", 36f, TextColor, TextAlignmentOptions.TopLeft);
        lessonTitleText.fontStyle = FontStyles.Bold;
        lessonTitleText.enableAutoSizing = true;
        lessonTitleText.fontSizeMin = 26f;
        lessonTitleText.fontSizeMax = 36f;
        PlaceTopLeft(lessonTitleText.rectTransform, new Vector2(20f, -42f), new Vector2(440f, 46f));

        skipButton = NewButton("SkipButton", lessonCard, "Skip tutorial", 22f, new Color(1f, 1f, 1f, 0.16f), TextColor, out skipLabel);
        PlaceTopLeft((RectTransform)skipButton.transform, new Vector2(20f, -94f), new Vector2(220f, 42f));
        skipButton.onClick.AddListener(OnSkipPressed);

        caption = NewBorderedPanel("Caption", root, Accent);
        PlaceTopLeft(caption, new Vector2(24f, -404f), new Vector2(600f, 64f));
        caption.GetComponent<Image>().raycastTarget = false;
        captionText = NewText("Text", caption, "", 24f, TextColor, TextAlignmentOptions.MidlineLeft);
        captionText.enableAutoSizing = true;
        captionText.fontSizeMin = 18f;
        captionText.fontSizeMax = 24f;
        Stretch(captionText.rectTransform, 18f, 6f);
    }

    private void BuildChips()
    {
        featuredChip = NewBorderedPanel("FeaturedChip", root, Gold);
        PlaceCentre(featuredChip, new Vector2(600f, 166f), new Vector2(460f, 52f));
        featuredChip.GetComponent<Image>().raycastTarget = false;
        featuredText = NewText("Text", featuredChip, "", 24f, Gold, TextAlignmentOptions.Center);
        Stretch(featuredText.rectTransform, 14f, 4f);

        guaranteeChip = NewBorderedPanel("GuaranteeChip", root, Good);
        PlaceCentre(guaranteeChip, new Vector2(600f, 106f), new Vector2(460f, 52f));
        guaranteeChip.GetComponent<Image>().raycastTarget = false;
        guaranteeText = NewText("Text", guaranteeChip, "", 22f, Good, TextAlignmentOptions.Center);
        Stretch(guaranteeText.rectTransform, 14f, 4f);
    }

    private void BuildBubble(Sprite guidePortrait)
    {
        bubble = NewBorderedPanel("Bubble", root, Gold);
        bubble.anchorMin = bubble.anchorMax = bubble.pivot = new Vector2(0.5f, 0.5f);
        bubble.sizeDelta = new Vector2(880f, 250f);

        Image portrait = NewImage("Portrait", bubble, Color.white, false);
        portrait.sprite = guidePortrait;
        portrait.preserveAspect = true;
        portrait.raycastTarget = false;
        portrait.enabled = guidePortrait != null;
        RectTransform portraitRect = portrait.rectTransform;
        portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0f, 0.5f);
        portraitRect.anchoredPosition = new Vector2(22f, 0f);
        portraitRect.sizeDelta = new Vector2(140f, 210f);

        TextMeshProUGUI nameText = NewText("Name", bubble, "Paimon", 26f, Gold, TextAlignmentOptions.TopLeft);
        nameText.fontStyle = FontStyles.Bold;
        PlaceTopLeft(nameText.rectTransform, new Vector2(186f, -20f), new Vector2(400f, 34f));

        bubbleText = NewText("Body", bubble, "", 31f, TextColor, TextAlignmentOptions.TopLeft);
        bubbleText.enableAutoSizing = true;
        bubbleText.fontSizeMin = 22f;
        bubbleText.fontSizeMax = 31f;
        RectTransform bodyRect = bubbleText.rectTransform;
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(186f, 70f);
        bodyRect.offsetMax = new Vector2(-28f, -58f);

        hintText = NewText("Hint", bubble, "", 25f, Accent, TextAlignmentOptions.BottomLeft);
        hintText.fontStyle = FontStyles.Bold;
        RectTransform hintRect = hintText.rectTransform;
        hintRect.anchorMin = Vector2.zero;
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = Vector2.zero;
        hintRect.offsetMin = new Vector2(186f, 20f);
        hintRect.offsetMax = new Vector2(-230f, 60f);

        continueButton = NewButton("ContinueButton", bubble, "Next", 26f, Gold, DarkLabel, out continueLabel);
        RectTransform buttonRect = (RectTransform)continueButton.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(1f, 0f);
        buttonRect.anchoredPosition = new Vector2(-22f, 18f);
        buttonRect.sizeDelta = new Vector2(190f, 56f);
        continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
    }

    /// <summary>A strip at the top of the screen, so the game's own result summary stays readable under it.</summary>
    private void BuildFinishPanel()
    {
        finishPanel = NewBorderedPanel("FinishPanel", root, Gold);
        finishPanel.anchorMin = finishPanel.anchorMax = finishPanel.pivot = new Vector2(0.5f, 1f);
        finishPanel.anchoredPosition = new Vector2(0f, -EdgeMargin);
        finishPanel.sizeDelta = new Vector2(1100f, 170f);

        finishTitle = NewText("Title", finishPanel, "", 38f, Gold, TextAlignmentOptions.TopLeft);
        finishTitle.fontStyle = FontStyles.Bold;
        PlaceTopLeft(finishTitle.rectTransform, new Vector2(32f, -22f), new Vector2(760f, 48f));

        finishText = NewText("Body", finishPanel, "", 26f, TextColor, TextAlignmentOptions.TopLeft);
        finishText.enableAutoSizing = true;
        finishText.fontSizeMin = 20f;
        finishText.fontSizeMax = 26f;
        PlaceTopLeft(finishText.rectTransform, new Vector2(32f, -76f), new Vector2(760f, 76f));

        Button playButton = NewButton("PlayAIButton", finishPanel, "Play vs AI", 26f, Gold, DarkLabel, out _);
        PlaceTopRight((RectTransform)playButton.transform, new Vector2(-24f, -20f), new Vector2(250f, 60f));
        playButton.onClick.AddListener(() => PlayAIClicked?.Invoke());

        Button menuButton = NewButton("MenuButton", finishPanel, "Main Menu", 26f, new Color(1f, 1f, 1f, 0.15f), TextColor, out _);
        PlaceTopRight((RectTransform)menuButton.transform, new Vector2(-24f, -90f), new Vector2(250f, 60f));
        menuButton.onClick.AddListener(() => MenuClicked?.Invoke());
    }

    // ===================== what the director calls =====================

    public void SetLesson(int number, int total, string title) => SetHeader($"LESSON {number} OF {total}", title);

    public void SetHeader(string small, string title)
    {
        lessonNumberText.text = small;
        lessonTitleText.text = title;
    }

    /// <summary>Shows the bubble. A button label adds a button the player must press; no label means the player must act.</summary>
    public void ShowBubble(string text, string buttonLabel, string hint)
    {
        bubble.gameObject.SetActive(true);
        bubbleText.text = text;

        bool hasHint = !string.IsNullOrEmpty(hint);
        hintText.gameObject.SetActive(hasHint);
        hintText.text = hasHint ? hint : "";

        bool hasButton = !string.IsNullOrEmpty(buttonLabel);
        continueButton.gameObject.SetActive(hasButton);
        if (hasButton) continueLabel.text = buttonLabel;

        PlaceBubble();
    }

    public void HideBubble() => bubble.gameObject.SetActive(false);

    /// <summary>A one-line note in the tutorial column, for moments when the game plays out and Paimon steps aside.</summary>
    public void ShowCaption(string text)
    {
        caption.gameObject.SetActive(!string.IsNullOrEmpty(text));
        captionText.text = text ?? "";
    }

    public void HideCaption() => caption.gameObject.SetActive(false);

    /// <summary>Dark shade on or off. With a spotlight target the shade leaves a hole around it.</summary>
    public void SetShade(bool on)
    {
        shadeOn = on;
        RefreshShade();
    }

    /// <summary>Blocks every click on the game (the tutorial's own buttons still work), without darkening the screen.</summary>
    public void SetInputBlocked(bool blocked)
    {
        inputBlocked = blocked;
        inputBlocker.gameObject.SetActive(blocked);
    }

    public void Spotlight(Func<RectTransform> target)
    {
        spotlightTarget = target;
        bubbleSideChosen = false;
        RefreshShade();
        PlaceBubble();
    }

    public void ClearSpotlight()
    {
        spotlightTarget = null;
        bubbleSideChosen = false;
        RefreshShade();
    }

    /// <summary>Whether clicks inside the spotlight reach the game (true while the player must click the highlighted thing).</summary>
    public void SetHoleClickable(bool clickable)
    {
        holeClickable = clickable;
        RefreshShade();
    }

    public void ShowChips(string featured, string guarantee)
    {
        featuredChip.gameObject.SetActive(!string.IsNullOrEmpty(featured));
        featuredText.text = featured ?? "";
        guaranteeChip.gameObject.SetActive(!string.IsNullOrEmpty(guarantee));
        guaranteeText.text = guarantee ?? "";
    }

    public void ShowFinish(string title, string text)
    {
        SetShade(false);
        SetInputBlocked(false);
        ClearSpotlight();
        HideBubble();
        HideCaption();
        ShowChips(null, null);
        lessonCard.gameObject.SetActive(false);
        finishTitle.text = title;
        finishText.text = text;
        finishPanel.gameObject.SetActive(true);
    }

    private void OnSkipPressed()
    {
        // Two clicks, so a stray click doesn't throw the player out of the tutorial.
        if (!skipArmed)
        {
            skipArmed = true;
            skipLabel.text = "Click again to skip";
            CancelInvoke(nameof(DisarmSkip));
            Invoke(nameof(DisarmSkip), 3f);
            return;
        }
        SkipClicked?.Invoke();
    }

    private void DisarmSkip()
    {
        skipArmed = false;
        skipLabel.text = "Skip tutorial";
    }

    // ===================== every frame: follow the spotlight target =====================

    private void LateUpdate()
    {
        // Hide while the game is paused, so the pause menu stays usable.
        bool paused = Time.timeScale == 0f;
        if (canvas.enabled == paused) canvas.enabled = !paused;
        if (paused) return;

        RefreshShade();
        if (bubble.gameObject.activeSelf) PlaceBubble();

        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f);
        foreach (Image bar in frameBars)
        {
            Color c = bar.color;
            c.a = pulse;
            bar.color = c;
        }
    }

    private void RefreshShade()
    {
        if (root == null) return;

        Vector2 half = root.rect.size * 0.5f;
        bool hasHole = TryGetSpotlightRect(out Rect hole);

        if (!shadeOn)
        {
            foreach (RectTransform s in shades) s.gameObject.SetActive(false);
        }
        else if (!hasHole)
        {
            SetBox(shades[0], -half.x, -half.y, half.x, half.y);
            for (int i = 0; i < shades.Length; i++) shades[i].gameObject.SetActive(i == 0);
        }
        else
        {
            SetBox(shades[0], -half.x, hole.yMax, half.x, half.y);          // above
            SetBox(shades[1], -half.x, -half.y, half.x, hole.yMin);         // below
            SetBox(shades[2], -half.x, hole.yMin, hole.xMin, hole.yMax);    // left
            SetBox(shades[3], hole.xMax, hole.yMin, half.x, hole.yMax);     // right
            foreach (RectTransform s in shades) s.gameObject.SetActive(true);
        }

        bool showFrame = hasHole && shadeOn;
        foreach (Image bar in frameBars) bar.gameObject.SetActive(showFrame);
        holeBlocker.gameObject.SetActive(showFrame && !holeClickable && !inputBlocked);
        if (!showFrame) return;

        SetBox(holeBlocker, hole.xMin, hole.yMin, hole.xMax, hole.yMax);

        float t = FrameThickness;
        SetBox(frameBars[0].rectTransform, hole.xMin - t, hole.yMax, hole.xMax + t, hole.yMax + t);
        SetBox(frameBars[1].rectTransform, hole.xMin - t, hole.yMin - t, hole.xMax + t, hole.yMin);
        SetBox(frameBars[2].rectTransform, hole.xMin - t, hole.yMin, hole.xMin, hole.yMax);
        SetBox(frameBars[3].rectTransform, hole.xMax, hole.yMin, hole.xMax + t, hole.yMax);
    }

    /// <summary>
    /// The spotlight target's screen area, in this canvas's local units, padded a little.
    /// A container with no size of its own (the hand, the opponent's card backs) uses the area its children cover.
    /// </summary>
    private bool TryGetSpotlightRect(out Rect local)
    {
        local = default;
        RectTransform target = spotlightTarget != null ? spotlightTarget() : null;
        if (target == null || !target.gameObject.activeInHierarchy) return false;

        Camera cam = null;
        Canvas targetCanvas = target.GetComponentInParent<Canvas>();
        if (targetCanvas != null && targetCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = targetCanvas.rootCanvas.worldCamera;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        bool found = false;

        if (target.rect.width >= 1f && target.rect.height >= 1f)
        {
            found = AddCorners(target, cam, ref min, ref max);
        }
        else
        {
            foreach (Transform child in target)
            {
                if (child is RectTransform childRect && child.gameObject.activeInHierarchy)
                    found |= AddCorners(childRect, cam, ref min, ref max);
            }
        }

        if (!found) return false;

        min -= Vector2.one * SpotlightPadding;
        max += Vector2.one * SpotlightPadding;
        local = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private bool AddCorners(RectTransform rect, Camera cam, ref Vector2 min, ref Vector2 max)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corner);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 point);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return true;
    }

    /// <summary>The bubble sits on the opposite half of the screen from the spotlight, or a little below centre without one.</summary>
    private void PlaceBubble()
    {
        if (bubble == null) return;

        float halfHeight = root.rect.height * 0.5f;
        float halfBubble = bubble.sizeDelta.y * 0.5f;
        float y = -halfHeight * 0.18f;

        if (shadeOn && !bubbleSideChosen && TryGetSpotlightRect(out Rect hole))
        {
            bubbleOnTop = hole.center.y < 0f;
            bubbleSideChosen = true;
        }

        if (shadeOn && bubbleSideChosen)
            y = bubbleOnTop ? halfHeight - EdgeMargin - halfBubble : -halfHeight + 60f + halfBubble;

        bubble.anchoredPosition = new Vector2(0f, y);
    }

    // ===================== small UI builders =====================

    private static void SetBox(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchoredPosition = new Vector2(xMin, yMin);
        rt.sizeDelta = new Vector2(Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
    }

    private static void CenterAnchor(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = Vector2.zero;
    }

    private static void PlaceTopLeft(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void PlaceTopRight(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void PlaceCentre(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt, float padX, float padY)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    private Image NewImage(string objectName, Transform parent, Color color, bool rounded)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        if (rounded)
        {
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    /// <summary>A coloured rounded panel with the dark fill inset inside it, so the colour shows as a thin border.</summary>
    private RectTransform NewBorderedPanel(string objectName, Transform parent, Color borderColor)
    {
        Image border = NewImage(objectName, parent, borderColor, true);
        border.raycastTarget = true;

        Image fill = NewImage("Fill", border.transform, PanelColor, true);
        fill.raycastTarget = false;
        Stretch(fill.rectTransform, BorderThickness, BorderThickness);

        return border.rectTransform;
    }

    private TextMeshProUGUI NewText(string objectName, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }

    private Button NewButton(string objectName, Transform parent, string label, float size, Color background, Color labelColor, out TextMeshProUGUI labelText)
    {
        Image image = NewImage(objectName, parent, background, true);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        labelText = NewText("Label", image.transform, label, size, labelColor, TextAlignmentOptions.Center);
        labelText.fontStyle = FontStyles.Bold;
        Stretch(labelText.rectTransform, 8f, 4f);
        return button;
    }

    /// <summary>A white square with soft rounded corners, used sliced so panels of any size keep the same corners.</summary>
    private static Texture2D MakeRoundedTexture(int size, int radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f)));
            }
        }
        texture.Apply();
        return texture;
    }
}
