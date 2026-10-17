using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fills a card gallery panel YOU already built with every card in the game.
///
/// SETUP
///  1. Add this script to your gallery panel object (the one that gets shown/hidden).
///     It fills the grid every time the panel is switched on, so your existing open button keeps working.
///  2. Drag your ScrollRect's "Content" object into 'Content'. (If left empty, the script looks for a ScrollRect
///     in the children and uses its content.)
///  3. Optional: drag a card tile prefab into 'Card Tile Prefab'. The prefab can contain:
///        - an Image  -> shows the card picture (an Image named "Picture" or "Card" is preferred, else the first Image)
///        - a TMP_Text -> shows the card name
///        - a Button on the root (added automatically if missing) -> click opens the big view
///     With no prefab, a plain tile is built from code.
///  4. Optional: tab buttons, tier filter buttons, a count label, a close button and an inspect (big card) panel.
///     Leave any of them empty and that feature is simply skipped.
///  5. No DeckManager in the scene? Drag ActionDeck.csv / SupportDeck.csv into the TextAsset slots.
///
/// If Content has no layout group, a GridLayoutGroup + ContentSizeFitter are added so it scrolls properly.
/// Sprites come from Resources/CardSprites/Action|Support/{id} (same as the hand).
/// </summary>
public class CardGalleryFiller : MonoBehaviour
{
    [Header("Required (or auto-found from a child ScrollRect)")]
    public RectTransform content;

    [Header("Optional: your tile prefab")]
    public GameObject cardTilePrefab;

    [Header("Optional: data source if no DeckManager in scene")]
    public DeckManager deckManager;
    public TextAsset actionDeckCsv;
    public TextAsset supportDeckCsv;

    [Header("Optional: tabs")]
    public Button actionTabButton;
    public Button supportTabButton;

    [Header("Optional: tier filter")]
    public Button filterAllButton;
    public Button filterLegendaryButton;
    public Button filterRareButton;
    public Button filterCommonButton;

    [Header("Optional: extras")]
    public TMP_Text countText;
    public Button closeButton;

    [Header("Optional: big card view")]
    public GameObject inspectRoot;
    public Image inspectImage;
    public TMP_Text inspectText;

    [Header("Grid (only used if Content has no layout group)")]
    public int columns = 5;
    public Vector2 cellSize = new Vector2(200f, 350f);
    public Vector2 spacing = new Vector2(24f, 24f);

    [Header("Card name text")]
    [Tooltip("Font size of the name under each card. Raise this to make names bigger.")]
    public float nameFontSize = 32f;
    [Tooltip("Height of the name strip under the card (default tiles only). Raise it if big names get cut off.")]
    public float nameHeight = 60f;
    [Tooltip("Also force this size on text inside your own tile prefab.")]
    public bool applyToPrefabText = true;
    [Tooltip("Drag a TMP Font Asset here to use that font for the card names (and the big-card text). Empty = TMP default.")]
    public TMP_FontAsset nameFont;

    [Header("Look")]
    public Color tabOn = new Color(0.95f, 0.78f, 0.3f, 1f);
    public Color tabOff = new Color(0.6f, 0.6f, 0.6f, 1f);
    [Header("Optional: tab sprites (swap instead of tint)")]
    public Sprite tabOnSprite;   // the lit-up / selected tab picture
    public Sprite tabOffSprite;  // the dark / unselected tab picture
    public bool useLabelColors = false;
    public Color labelOnColor = Color.black;
    public Color labelOffColor = Color.black;

    public Color legendaryColor = new Color(0.98f, 0.76f, 0.25f, 1f);
    public Color rareColor = new Color(0.72f, 0.45f, 0.95f, 1f);
    public Color commonColor = new Color(0.4f, 0.65f, 0.95f, 1f);

    private bool showAction = true;
    private string tierFilter = "All";
    private bool wired;
    private List<ActionCardData> uniqueAction = new List<ActionCardData>();
    private List<SupportCardData> uniqueSupport = new List<SupportCardData>();

    // ------------------------------------------------------------------------------------------------

    private void Awake() { Wire(); }

    private void OnEnable()
    {
        Wire();
        if (inspectRoot != null) inspectRoot.SetActive(false);
        LoadCards();
        Refresh();
    }

    private void Wire()
    {
        if (wired) return;
        wired = true;

        if (content == null)
        {
            var sr = GetComponentInChildren<ScrollRect>(true);
            if (sr != null) content = sr.content;
        }
        if (content == null)
            Debug.LogError("[CardGalleryFiller] Assign 'Content' (your ScrollRect's content object).", this);
        else
            EnsureLayout();

        if (actionTabButton != null) actionTabButton.onClick.AddListener(() => { showAction = true; Refresh(); });
        if (supportTabButton != null) supportTabButton.onClick.AddListener(() => { showAction = false; Refresh(); });
        Hook(filterAllButton, "All");
        Hook(filterLegendaryButton, "Legendary");
        Hook(filterRareButton, "Rare");
        Hook(filterCommonButton, "Common");
        if (closeButton != null) closeButton.onClick.AddListener(() => gameObject.SetActive(false));

        if (inspectRoot != null)
        {
            // Click the big view to dismiss it.
            var btn = inspectRoot.GetComponent<Button>();
            if (btn == null)
            {
                if (inspectRoot.GetComponent<Graphic>() == null)
                    inspectRoot.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
                btn = inspectRoot.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
            }
            btn.onClick.AddListener(() => inspectRoot.SetActive(false));
            if (inspectImage != null) inspectImage.raycastTarget = false;
            if (inspectText != null) inspectText.raycastTarget = false;
        }
    }

    private void Hook(Button b, string tier)
    {
        if (b != null) b.onClick.AddListener(() => { tierFilter = tier; Refresh(); });
    }

    private void EnsureLayout()
    {
        if (content.GetComponent<LayoutGroup>() == null)
        {
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.padding = new RectOffset(24, 24, 24, 24);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, columns);
        }
        if (content.GetComponent<ContentSizeFitter>() == null)
        {
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Data
    // ------------------------------------------------------------------------------------------------

    private void LoadCards()
    {
        List<ActionCardData> actions = null;
        List<SupportCardData> supports = null;

        if (deckManager == null) deckManager = FindFirstObjectByType<DeckManager>();
        if (deckManager != null)
        {
            actions = deckManager.loadedActionCards;
            supports = deckManager.loadedSupportCards;
        }
        if ((actions == null || actions.Count == 0) && actionDeckCsv != null)
            actions = CardLoader.LoadActionDeck(actionDeckCsv);
        if ((supports == null || supports.Count == 0) && supportDeckCsv != null)
            supports = CardLoader.LoadSupportDeck(supportDeckCsv);

        // Loaded lists have one entry per copy; the gallery wants one per distinct card.
        uniqueAction = new List<ActionCardData>();
        if (actions != null)
        {
            var seen = new HashSet<int>();
            foreach (var c in actions) if (c != null && seen.Add(c.Id)) uniqueAction.Add(c);
            uniqueAction.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
        uniqueSupport = new List<SupportCardData>();
        if (supports != null)
        {
            var seen = new HashSet<int>();
            foreach (var c in supports) if (c != null && seen.Add(c.Id)) uniqueSupport.Add(c);
            uniqueSupport.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        if (uniqueAction.Count == 0 && uniqueSupport.Count == 0)
            Debug.LogWarning("[CardGalleryFiller] No cards found. Add a DeckManager to the scene or assign the two CSV files.", this);
    }

    private bool TierMatches(string tier)
    {
        if (tierFilter == "All") return true;
        return string.Equals((tier ?? "").Trim(), tierFilter, System.StringComparison.OrdinalIgnoreCase);
    }

    private Color TierColor(string tier)
    {
        string t = (tier ?? "").Trim();
        if (t.Equals("Legendary", System.StringComparison.OrdinalIgnoreCase)) return legendaryColor;
        if (t.Equals("Rare", System.StringComparison.OrdinalIgnoreCase)) return rareColor;
        return commonColor;
    }

    // ------------------------------------------------------------------------------------------------
    // Filling the grid
    // ------------------------------------------------------------------------------------------------

    private void Refresh()
    {
        if (content == null) return;

        Tint(actionTabButton, showAction);
        Tint(supportTabButton, !showAction);
        Tint(filterAllButton, tierFilter == "All");
        Tint(filterLegendaryButton, tierFilter == "Legendary");
        Tint(filterRareButton, tierFilter == "Rare");
        Tint(filterCommonButton, tierFilter == "Common");

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        int shown = 0;
        if (showAction)
        {
            foreach (var c in uniqueAction)
            {
                if (!TierMatches(c.Tier)) continue;
                var d = c;
                AddTile(d.Id, true, d.Name, d.Tier, d.Role + " " + d.Value, () => ShowAction(d));
                shown++;
            }
        }
        else
        {
            foreach (var c in uniqueSupport)
            {
                if (!TierMatches(c.Tier)) continue;
                var d = c;
                AddTile(d.Id, false, d.Name, d.Tier, d.EffectType.ToString(), () => ShowSupport(d));
                shown++;
            }
        }

        if (countText != null) countText.text = shown + (shown == 1 ? " card" : " cards");

        var scroll = content.GetComponentInParent<ScrollRect>();
        if (scroll != null)
        {
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
        }
    }

    private void Tint(Button b, bool on)
    {
        if (b == null) return;
        var g = b.targetGraphic != null ? b.targetGraphic : b.GetComponent<Graphic>();
        if (g == null) return;

        // Sprite swap (for tabs like "selected = lit up / raised"). Falls back to a colour tint if no sprites are set.
        var img = g as Image;
        if (img != null && tabOnSprite != null && tabOffSprite != null)
        {
            img.sprite = on ? tabOnSprite : tabOffSprite;
            img.color = Color.white;
        }
        else
        {
            g.color = on ? tabOn : tabOff;
        }

        // Selected tab's label gets the dark/light colour too, if you set them.
        var label = b.GetComponentInChildren<TMP_Text>(true);
        if (label != null && useLabelColors) label.color = on ? labelOnColor : labelOffColor;
    }

    private void AddTile(int id, bool isAction, string cardName, string tier, string fallback, UnityEngine.Events.UnityAction onClick)
    {
        Sprite sprite = PlayerHandManager.GetCardSpriteCached(id, isAction);
        GameObject tile;

        if (cardTilePrefab != null)
        {
            tile = Instantiate(cardTilePrefab, content);
            tile.name = "Card_" + id;
            tile.SetActive(true);

            Image pic = FindPicture(tile);
            if (pic != null)
            {
                if (sprite != null) { pic.sprite = sprite; pic.color = Color.white; pic.preserveAspect = true; }
                else pic.color = TierColor(tier) * 0.6f;
            }
            var label = tile.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = (sprite == null) ? cardName + "\n" + fallback : cardName;
                if (applyToPrefabText)
                {
                    label.enableAutoSizing = false;
                    label.fontSize = nameFontSize;
                    if (nameFont != null) label.font = nameFont;
                }
            }
        }
        else
        {
            tile = BuildDefaultTile(id, sprite, cardName, tier, fallback);
        }

        var btn = tile.GetComponent<Button>();
        if (btn == null)
        {
            btn = tile.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            if (tile.GetComponent<Graphic>() == null)
            {
                var hit = tile.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                btn.targetGraphic = hit;
            }
        }
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);
    }

    private static Image FindPicture(GameObject tile)
    {
        Image first = null;
        foreach (var img in tile.GetComponentsInChildren<Image>(true))
        {
            string n = img.gameObject.name.ToLowerInvariant();
            if (n.Contains("picture") || n.Contains("card") || n.Contains("art") || n.Contains("image")) return img;
            if (first == null && img.gameObject != tile) first = img;
        }
        return first != null ? first : tile.GetComponent<Image>();
    }

    private GameObject BuildDefaultTile(int id, Sprite sprite, string cardName, string tier, string fallback)
    {
        var tile = new GameObject("Card_" + id, typeof(RectTransform));
        tile.transform.SetParent(content, false);

        var pic = new GameObject("Picture", typeof(RectTransform));
        pic.transform.SetParent(tile.transform, false);
        var prt = pic.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(0, nameHeight + 4f); prt.offsetMax = Vector2.zero;
        var img = pic.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = true;
        if (sprite != null) { img.sprite = sprite; img.color = Color.white; }
        else
        {
            img.color = TierColor(tier) * 0.6f;
            var fb = NewText("Fallback", pic.transform, cardName + "\n\n" + fallback, 22);
            if (nameFont != null) fb.font = nameFont;
            var frt = fb.rectTransform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(8, 8); frt.offsetMax = new Vector2(-8, -8);
            fb.enableAutoSizing = true; fb.fontSizeMin = 12; fb.fontSizeMax = 22;
        }

        var label = NewText("Name", tile.transform, cardName, nameFontSize);
        label.color = TierColor(tier);
        if (nameFont != null) label.font = nameFont;
        label.fontStyle = FontStyles.Bold;
        // Shrinks only for very long names, never below ~70% of the chosen size.
        label.enableAutoSizing = true;
        label.fontSizeMax = nameFontSize;
        label.fontSizeMin = nameFontSize * 0.7f;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0);
        lrt.pivot = new Vector2(0.5f, 0);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(0, nameHeight);
        return tile;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white; t.raycastTarget = false;
        return t;
    }

    // ------------------------------------------------------------------------------------------------
    // Big card view
    // ------------------------------------------------------------------------------------------------

    private void ShowAction(ActionCardData c)
    {
        string weapon = string.IsNullOrWhiteSpace(c.WeaponType) ? "" : "\nWeapon type: " + c.WeaponType;
        Inspect(PlayerHandManager.GetCardSpriteCached(c.Id, true),
            "<b>" + c.Name + "</b>\n\nTier: " + c.Tier + "\nCategory: " + c.Category + weapon +
            "\nRole: " + c.Role + "\nValue: " + c.Value + "\n\nCopies in the deck: " + c.Copies, c.Tier);
    }

    private void ShowSupport(SupportCardData c)
    {
        string cat = string.IsNullOrWhiteSpace(c.Category) ? "" : "\nCategory: " + c.Category;
        string type = c.EffectType.ToString() == "Forever"
            ? "Forever (comes back to your hand after it is played)"
            : "Immediate (used up when played)";
        Inspect(PlayerHandManager.GetCardSpriteCached(c.Id, false),
            "<b>" + c.Name + "</b>\n\nTier: " + c.Tier + cat + "\nType: " + type +
            "\n\n" + c.Effect + "\n\nCopies in the deck: " + c.Copies, c.Tier);
    }

    private void Inspect(Sprite sprite, string body, string tier)
    {
        if (inspectRoot == null) return; // no big view set up: tiles just show the picture and name
        if (inspectImage != null)
        {
            inspectImage.sprite = sprite;
            inspectImage.preserveAspect = true;
            inspectImage.color = sprite != null ? Color.white : TierColor(tier) * 0.6f;
        }
        if (inspectText != null)
        {
            if (nameFont != null) inspectText.font = nameFont;
            inspectText.text = body;
        }
        inspectRoot.SetActive(true);
        inspectRoot.transform.SetAsLastSibling();
    }
}