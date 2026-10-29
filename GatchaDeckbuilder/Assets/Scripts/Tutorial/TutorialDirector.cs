using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the tutorial: one scripted match against a Training Dummy, played on the real game systems (pulls, pity,
/// the 50/50, card rules, elemental reactions, Support effects). Each lesson teaches one idea, and the player has to
/// do the thing before the tutorial moves on.
///
///   Round 1  Lesson 1  Tokens and pulling     Lesson 2  Your first duel
///   Round 2  Lesson 3  Pity and the 50/50     Lesson 4  Combos and elements
///   Round 3  Lesson 5  Support cards
///   Round 4  Lesson 6  Practice round (real odds, real timer, normal AI)
///
/// The pulls and the Dummy's cards are scripted through hooks on GachaManager, EndTurnManager and DrawTimerManager,
/// so every player sees the same lesson. While a lesson runs, only the highlighted thing can be clicked and only the
/// cards Paimon asks for can be played. All of Paimon's lines are in RunTutorial below.
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    private const int LessonCount = 6;

    // Only one tutorial may run. A second director in the scene would draw a second overlay that eats the clicks.
    private static TutorialDirector running;

    [Header("Scenes")]
    [SerializeField] private string menuSceneName = "Menu";
    [SerializeField] private string aiSceneName = "AIMode";

    private GachaManager gacha;
    private GachaUI gachaUI;
    private EndTurnManager endTurn;
    private DrawTimerManager timer;
    private PlayerHandManager hand;
    private TutorialOverlay overlay;
    private Sprite portrait;

    // Cards GachaManager hands out on the next pulls, per deck. Empty = roll normally.
    private readonly Queue<int> scriptedActionPulls = new Queue<int>();
    private readonly Queue<int> scriptedSupportPulls = new Queue<int>();

    // What the Dummy draws and plays each round (network ids). Rounds missing here use the normal AI.
    private readonly Dictionary<int, int[]> dummyDraws = new Dictionary<int, int[]>
    {
        { 1, new[] { TutorialCards.Action(TutorialCards.AnemoSamachurl) } },
        { 2, new[] { TutorialCards.Action(TutorialCards.Azhdaha), TutorialCards.Action(TutorialCards.GeoRifthound) } },
        { 3, new[] { TutorialCards.Action(TutorialCards.MountainKing), TutorialCards.Action(TutorialCards.WingedDendroshroom) } },
    };

    private readonly Dictionary<int, int[]> dummyPlays = new Dictionary<int, int[]>
    {
        { 1, new[] { TutorialCards.Action(TutorialCards.AnemoSamachurl) } },
        { 2, new[] { TutorialCards.Action(TutorialCards.Azhdaha), TutorialCards.Action(TutorialCards.GeoRifthound) } },
        { 3, new[] { TutorialCards.Action(TutorialCards.MountainKing), TutorialCards.Action(TutorialCards.WingedDendroshroom) } },
    };

    // The cards Paimon has asked for this round (network ids). While it holds any, only exactly these can be played.
    private readonly List<int> plannedPlay = new List<int>();

    // What has happened so far. The lesson steps wait on these.
    private bool continuePressed;
    private int drawPhasesOpened;
    private int pullsResolved;
    private int invalidPlays;
    private int turnsSubmitted;
    private int roundsResolved;
    private TurnResultData lastRound;

    private bool featuredShown;
    private bool guaranteeArmed;
    private bool scriptedPullInProgress;

    private Coroutine lessons;
    private bool reachedPractice;
    private bool finished;

    // ===================== setup =====================

    private void Awake()
    {
        if (running != null && running != this)
        {
            Debug.LogWarning($"[Tutorial] '{name}' is a second TutorialDirector in the scene; only the first one runs. Delete the extra one.");
            enabled = false;
            Destroy(this);
            return;
        }
        running = this;

        gacha = FindFirstObjectByType<GachaManager>();
        gachaUI = FindFirstObjectByType<GachaUI>();
        endTurn = FindFirstObjectByType<EndTurnManager>();
        timer = FindFirstObjectByType<DrawTimerManager>();
        hand = FindFirstObjectByType<PlayerHandManager>();

        if (gacha == null || gachaUI == null || endTurn == null || timer == null || hand == null)
        {
            Debug.LogError("[Tutorial] The scene is missing a GachaManager, GachaUI, EndTurnManager, DrawTimerManager or PlayerHandManager. " +
                           "The Tutorial scene should be a copy of the AIMode scene.");
            enabled = false;
            return;
        }

        if (!gacha.isOfflineAIRival || !endTurn.isOfflineAIRival)
            Debug.LogWarning("[Tutorial] GachaManager and EndTurnManager should have Is Offline AI Rival ticked in the Tutorial scene.");

        // Hold everything before Round 1 starts; the welcome comes first. Awake runs before any Start, so this is in time.
        timer.HoldRoundStart = true;
        timer.HoldTimer = true;

        gacha.ScriptedPullCardId = NextScriptedPull;
        endTurn.ScriptedAIDraw = round => dummyDraws.TryGetValue(round, out int[] cards) ? cards : null;
        endTurn.ScriptedAITurn = round => dummyPlays.TryGetValue(round, out int[] cards) ? cards : null;
        endTurn.ExtraPlayRule = CheckPlannedPlay;

        gacha.OnMyPullResolved += HandlePullResolved;
        endTurn.OnInvalidPlay += HandleInvalidPlay;
        endTurn.OnTurnSubmitted += HandleTurnSubmitted;
        endTurn.OnRoundResolved += HandleRoundResolved;
        timer.OnDrawPhaseChanged += HandleDrawPhaseChanged;

        portrait = MakePortrait(PlayerHandManager.GetCardSpriteCached(TutorialCards.Paimon, false));

        var overlayObject = new GameObject("TutorialOverlay", typeof(RectTransform));
        overlay = overlayObject.AddComponent<TutorialOverlay>();
        overlay.Build(gachaUI.tokenText != null ? gachaUI.tokenText.font : null, portrait, hand.GetComponentInParent<Canvas>());

        overlay.ContinueClicked += () => continuePressed = true;
        overlay.SkipClicked += () => LoadScene(menuSceneName);
        overlay.MenuClicked += () => LoadScene(menuSceneName);
        overlay.PlayAIClicked += () => LoadScene(aiSceneName);
    }

    private void Start()
    {
        lessons = StartCoroutine(RunTutorial());
    }

    // The match can end at any time (a forfeit from the pause menu, or the last round). Either way, wrap up.
    private void Update()
    {
        if (finished || !endTurn.IsMatchOver) return;

        finished = true;
        if (lessons != null) StopCoroutine(lessons);
        endTurn.HoldAfterRound = false;
        StartCoroutine(Finish());
    }

    private void OnDestroy()
    {
        if (running == this) running = null;

        if (gacha != null)
        {
            gacha.ScriptedPullCardId = null;
            gacha.OnMyPullResolved -= HandlePullResolved;
        }
        if (endTurn != null)
        {
            endTurn.ScriptedAIDraw = null;
            endTurn.ScriptedAITurn = null;
            endTurn.ExtraPlayRule = null;
            endTurn.OnInvalidPlay -= HandleInvalidPlay;
            endTurn.OnTurnSubmitted -= HandleTurnSubmitted;
            endTurn.OnRoundResolved -= HandleRoundResolved;
        }
        if (timer != null) timer.OnDrawPhaseChanged -= HandleDrawPhaseChanged;
        if (portrait != null) Destroy(portrait);
    }

    // ===================== the lessons =====================

    private IEnumerator RunTutorial()
    {
        // ---------- Welcome ----------
        overlay.SetHeader("WELCOME", "How to play");
        yield return Say("Hi, I'm Paimon! Let's learn the game by playing one quick match against a Training Dummy.", null, "Let's go");
        yield return Say("Win by bringing the Dummy's HP to 0, or by having more HP than it after 4 rounds.", OpponentHP);

        timer.HoldRoundStart = false;
        yield return WaitForDrawPhase(1);

        // ---------- Round 1: pulling ----------
        overlay.SetLesson(1, LessonCount, "Tokens and pulling");
        yield return Say("These are your tokens. You get 10 for the whole match, and every pull costs 1.", Tokens);
        yield return Pull(DeckType.Action, TutorialCards.Dvalin, "Spend a token: pull a card from the Action deck.", "Click the Action deck");
        yield return Say("Dvalin! The number is its power, and the crossed swords mean Attack. Purple means it's Rare.",
                         () => HandCard(TutorialCards.Dvalin, false));
        yield return Pull(DeckType.Action, TutorialCards.TaintedHydroPhantasm, "Pull one more.", "Click the Action deck");
        yield return Say("Blue means Common. The shield means Defense: Attack hurts the Dummy, Defense blocks its hits.",
                         () => HandCard(TutorialCards.TaintedHydroPhantasm, false));
        yield return Say("The Dummy pulled too. You only see the backs of its cards until you both lock in.", OpponentCards);
        timer.EndDrawPhaseNow();

        // ---------- Round 1: the duel ----------
        overlay.SetLesson(2, LessonCount, "Your first duel");
        yield return Select(TutorialCards.Dvalin, false, "Time to fight. Choose Dvalin.", "Click Dvalin");
        yield return PlayTurn("Press End Turn to lock it in.", "Both sides reveal their cards at the same time.");
        TurnResultData r = lastRound;
        yield return Say($"Your {r.playerAttack} Attack hit its {r.opponentDefense} Defense, so {r.netDamageToOpponent} damage got through. " +
                         $"The Dummy is down to {r.finalOpponentHP} HP.", OpponentHP);
        yield return Say("Played cards come back to your hand next round, so a good pull keeps paying off.");
        endTurn.HoldAfterRound = false;
        yield return WaitForDrawPhase(2);

        // ---------- Round 2: pity and the 50/50 ----------
        overlay.SetLesson(3, LessonCount, "Pity and the 50/50");
        gacha.SetFeaturedCards(TutorialCards.RaidenShogun, TutorialCards.JadeChamber);
        featuredShown = true;
        RefreshChips();
        yield return Say("Every match has one featured Legendary. This time it's Raiden Shogun.", () => overlay.FeaturedChip);
        yield return Say("Each pull has a 10% Legendary chance. This pity counter counts your pulls since your last Legendary.", ActionPity);
        yield return Pull(DeckType.Action, TutorialCards.FavoniusSword, "Pull again.", "Click the Action deck");
        yield return Pull(DeckType.Action, TutorialCards.PyroWhopperflower, "Pull once more and watch the counter.", "Click the Action deck");
        yield return Say("4 of 4: pity is full! Your next pull is a guaranteed Legendary.", ActionPity);
        yield return Pull(DeckType.Action, TutorialCards.Zhongli, "Pull your guaranteed Legendary!", "Click the Action deck");
        yield return Say("Gold means Legendary! But it's Zhongli, not Raiden, so you lost the 50/50.", () => HandCard(TutorialCards.Zhongli, false));
        yield return Say("That's not wasted: your next Legendary is now guaranteed to be Raiden. Pity and the 50/50 are your safety nets.",
                         () => overlay.GuaranteeChip);
        timer.EndDrawPhaseNow();

        // ---------- Round 2: combos and elements ----------
        overlay.SetLesson(4, LessonCount, "Combos and elements");
        yield return Select(TutorialCards.FavoniusSword, false, "Favonius Sword is a Weapon. Choose it.", "Click Favonius Sword");
        int refusedBefore = invalidPlays;
        yield return DoAction("Now try to play it on its own.", "Press End Turn", EndTurnButton, () => invalidPlays > refusedBefore);
        yield return Say("Blocked! A Weapon needs a Character played with it. Artefacts need a card of their own element.");
        yield return Select(TutorialCards.Zhongli, false, "Zhongli is a Character. Choose him to wield the sword.", "Click Zhongli");
        yield return Say("The icon in the bottom corner is a card's element. Zhongli is Geo. Two different elements played together react for a bonus.",
                         () => HandCard(TutorialCards.Zhongli, false));
        yield return Select(TutorialCards.PyroWhopperflower, false, "Add Pyro Whopperflower: Geo + Pyro makes Crystallize, +1 Defense.",
                            "Click Pyro Whopperflower");
        yield return PlayTurn("Lock it in.", "Watch for the Crystallize pop-up.");
        r = lastRound;
        string blocked = r.netDamageToPlayer == 0 ? "blocked its whole attack" : $"let {r.netDamageToPlayer} through";
        yield return Say($"Attack {r.playerAttack} against its {r.opponentDefense} Defense: {r.netDamageToOpponent} damage. " +
                         $"Your {r.playerDefense} Defense {blocked}.", OpponentHP);
        endTurn.HoldAfterRound = false;
        yield return WaitForDrawPhase(3);

        // ---------- Round 3: Support cards ----------
        overlay.SetLesson(5, LessonCount, "Support cards");
        yield return Say("Support cards come from this second deck. They bend the rules, and the deck has its own pity counter.", SupportDeck);
        yield return Pull(DeckType.Support, TutorialCards.TreasureSeekingSeelie, "Pull a Support card.", "Click the Support deck");
        yield return Say("Treasure-Seeking Seelie doubles your attack. The hourglass means it's used up after one play.",
                         () => HandCard(TutorialCards.TreasureSeekingSeelie, true));
        yield return Pull(DeckType.Support, TutorialCards.WangshuInn, "Pull one more.", "Click the Support deck");
        yield return Say("Wangshu Inn takes 1 off the Dummy's attack. The loop icon means it comes back every round.",
                         () => HandCard(TutorialCards.WangshuInn, true));
        timer.EndDrawPhaseNow();

        yield return Select(TutorialCards.Dvalin, false, "Supports power up your Action cards, so pick an attacker first. Choose Dvalin.", "Click Dvalin");
        yield return Select(TutorialCards.TreasureSeekingSeelie, true, "Add Treasure-Seeking Seelie.", "Click Treasure-Seeking Seelie");
        yield return Select(TutorialCards.WangshuInn, true, "And Wangshu Inn.", "Click Wangshu Inn");
        yield return PlayTurn("Lock it in.", "Supports take effect before the damage is counted.");
        r = lastRound;
        yield return Say($"Seelie doubled your attack to {r.playerAttack}! Wangshu Inn cut the Dummy's attack to {r.opponentAttack}, " +
                         $"so you took {r.netDamageToPlayer}.", OpponentHP);

        timer.HoldRoundStart = true;
        endTurn.HoldAfterRound = false;
        yield return Watch(null, () => false, 1.2f);
        yield return Say("Seelie is gone and Wangshu Inn is back in your hand. Some Supports even change your next pulls, like extra Legendary luck.",
                         HandArea);
        timer.HoldRoundStart = false;
        yield return WaitForDrawPhase(4);

        // ---------- Round 4: practice ----------
        overlay.SetLesson(6, LessonCount, "Practice round");
        reachedPractice = true;
        yield return Say("The last round is all yours. Real odds now, and the draw timer is running, so pull fast!", null, "Start");
        timer.HoldTimer = false;
        overlay.SetShade(false);
        overlay.ClearSpotlight();
        overlay.HideBubble();
        overlay.ShowCaption("Pull while the timer runs. When it ends, pick your cards and press End Turn.");
        // Update() takes over when the match ends.
    }

    private IEnumerator Finish()
    {
        overlay.SetShade(false);
        overlay.SetInputBlocked(false);
        overlay.HideBubble();
        overlay.HideCaption();
        yield return new WaitForSecondsRealtime(1.5f);

        if (reachedPractice)
            overlay.ShowFinish("Tutorial complete!", "You know tokens, pity, the 50/50, combos and Support cards. Ready for a real match?");
        else
            overlay.ShowFinish("Tutorial ended", "You can replay it any time from the Main Menu.");
    }

    // ===================== step types =====================

    /// <summary>Paimon talks; the player reads and presses the button. The spotlight is look-only.</summary>
    private IEnumerator Say(string text, Func<RectTransform> spotlight = null, string button = "Next")
    {
        overlay.SetInputBlocked(false);
        overlay.HideCaption();
        overlay.SetShade(true);
        overlay.SetHoleClickable(false);
        if (spotlight != null) overlay.Spotlight(spotlight);
        else overlay.ClearSpotlight();

        continuePressed = false;
        overlay.ShowBubble(text, button, null);
        yield return new WaitUntil(() => continuePressed);
    }

    /// <summary>The player must click the highlighted thing; everything else is blocked until done() is true.</summary>
    private IEnumerator DoAction(string text, string hint, Func<RectTransform> spotlight, Func<bool> done)
    {
        overlay.SetInputBlocked(false);
        overlay.HideCaption();
        overlay.SetShade(true);
        overlay.Spotlight(spotlight);
        overlay.SetHoleClickable(true);
        overlay.ShowBubble(text, null, hint);
        yield return new WaitUntil(done);
        overlay.SetHoleClickable(false);
    }

    /// <summary>
    /// Paimon steps aside while the game plays out (a card reveal, a round, the countdown). Nothing can be clicked
    /// meanwhile, so the scripted moment can't be thrown off. Ends when done() is true, or after maxSeconds if given.
    /// </summary>
    private IEnumerator Watch(string caption, Func<bool> done, float maxSeconds = 0f)
    {
        overlay.SetShade(false);
        overlay.ClearSpotlight();
        overlay.HideBubble();
        overlay.SetInputBlocked(true);
        if (caption != null) overlay.ShowCaption(caption);
        else overlay.HideCaption();

        float started = Time.unscaledTime;
        while (!done() && (maxSeconds <= 0f || Time.unscaledTime - started < maxSeconds))
            yield return null;

        overlay.HideCaption();
    }

    private IEnumerator Pull(DeckType deck, int cardId, string text, string hint)
    {
        (deck == DeckType.Action ? scriptedActionPulls : scriptedSupportPulls).Enqueue(cardId);

        int pullsBefore = pullsResolved;
        int cardsBefore = HandCount();
        scriptedPullInProgress = true;
        yield return DoAction(text, hint, deck == DeckType.Action ? ActionDeck : SupportDeck, () => pullsResolved > pullsBefore);

        // Let the card's reveal play without the shade over it. The chips update after it, so they don't spoil the reveal.
        yield return Watch(null, () => HandCount() > cardsBefore);
        yield return Watch(null, () => false, 0.4f);
        scriptedPullInProgress = false;
        RefreshChips();
    }

    /// <summary>
    /// The player picks one card. Cards in a fan overlap, so any other card that gets picked by accident is put back
    /// straight away: only the cards Paimon asked for stay selected.
    /// </summary>
    private IEnumerator Select(int cardId, bool isSupport, string text, string hint)
    {
        plannedPlay.Add(CardNetId.Encode(cardId, isSupport));

        yield return DoAction(text, hint, () => HandCard(cardId, isSupport), () =>
        {
            DeselectUnplannedCards();
            BalatroCardController card = FindHandCard(cardId, isSupport);
            if (card == null) Debug.LogWarning($"[Tutorial] Card {cardId} is not in the hand; skipping this step.");
            return card == null || card.IsSelected;
        });
    }

    /// <summary>Press End Turn, then watch the reveal. The played cards stay on the table until HoldAfterRound is released.</summary>
    private IEnumerator PlayTurn(string text, string watchCaption)
    {
        endTurn.HoldAfterRound = true;
        int submittedBefore = turnsSubmitted;
        int roundsBefore = roundsResolved;

        yield return DoAction(text, "Press End Turn", EndTurnButton, () => turnsSubmitted > submittedBefore);
        plannedPlay.Clear();
        yield return Watch(watchCaption, () => roundsResolved > roundsBefore);
    }

    private IEnumerator WaitForDrawPhase(int count)
    {
        yield return Watch(null, () => drawPhasesOpened >= count);
    }

    // ===================== what to highlight =====================

    private RectTransform Tokens() => gachaUI.tokenText != null ? gachaUI.tokenText.rectTransform : null;
    private RectTransform ActionPity() => gachaUI.actionPityText != null ? gachaUI.actionPityText.rectTransform : null;
    private RectTransform ActionDeck() => gachaUI.pullActionButton != null ? (RectTransform)gachaUI.pullActionButton.transform : null;
    private RectTransform SupportDeck() => gachaUI.pullSupportButton != null ? (RectTransform)gachaUI.pullSupportButton.transform : null;
    private RectTransform OpponentCards() => gachaUI.OpponentCardContainer as RectTransform;
    private RectTransform EndTurnButton() => endTurn.EndTurnButton != null ? (RectTransform)endTurn.EndTurnButton.transform : null;
    private RectTransform OpponentHP() => endTurn.OpponentHPText != null ? endTurn.OpponentHPText.rectTransform : null;
    private RectTransform HandArea() => hand.HandArea;

    private RectTransform HandCard(int cardId, bool isSupport)
    {
        BalatroCardController card = FindHandCard(cardId, isSupport);
        return card != null ? card.Rect : null;
    }

    private BalatroCardController FindHandCard(int cardId, bool isSupport)
    {
        foreach (BalatroCardController card in HandCards())
        {
            if (card.CardId == cardId && (card.Category == CardCategory.Support) == isSupport) return card;
        }
        return null;
    }

    private int HandCount() => HandCards().Count;

    private List<BalatroCardController> HandCards()
    {
        var cards = new List<BalatroCardController>();
        if (hand.HandArea == null) return cards;
        foreach (Transform child in hand.HandArea)
        {
            BalatroCardController card = child.GetComponent<BalatroCardController>();
            if (card != null) cards.Add(card);
        }
        return cards;
    }

    private void DeselectUnplannedCards()
    {
        foreach (BalatroCardController card in HandCards())
        {
            if (card.IsSelected && !plannedPlay.Contains(CardNetId.Encode(card.CardId, card.Category == CardCategory.Support)))
                card.ForceDeselect();
        }
    }

    // ===================== listening to the game =====================

    private int NextScriptedPull(DeckType deck)
    {
        Queue<int> queue = deck == DeckType.Action ? scriptedActionPulls : scriptedSupportPulls;
        return queue.Count > 0 ? queue.Dequeue() : 0;
    }

    /// <summary>While Paimon has asked for cards, End Turn only accepts exactly those.</summary>
    private string CheckPlannedPlay(int[] selected)
    {
        if (plannedPlay.Count == 0) return null;

        var expected = new List<int>(plannedPlay);
        foreach (int netId in selected)
        {
            if (!expected.Remove(netId)) return "Play just the cards Paimon picked.";
        }
        return expected.Count == 0 ? null : "Play just the cards Paimon picked.";
    }

    private void HandlePullResolved(PullResult result)
    {
        pullsResolved++;
        if (result != null && result.Deck == DeckType.Action && result.Was5050Roll)
        {
            guaranteeArmed = !result.Won5050;
            if (!scriptedPullInProgress) RefreshChips();
        }
    }

    private void HandleInvalidPlay(string message) => invalidPlays++;
    private void HandleTurnSubmitted(int[] cards) => turnsSubmitted++;

    private void HandleRoundResolved(TurnResultData result)
    {
        lastRound = result;
        roundsResolved++;
    }

    private void HandleDrawPhaseChanged(bool isDrawPhase)
    {
        if (isDrawPhase) drawPhasesOpened++;
    }

    private void RefreshChips()
    {
        if (!featuredShown) return;
        overlay.ShowChips("Featured Legendary: Raiden Shogun",
                          guaranteeArmed ? "Next Legendary: guaranteed Raiden" : null);
    }

    /// <summary>Paimon's face from the top of her card, without the effect text at the bottom.</summary>
    private static Sprite MakePortrait(Sprite card)
    {
        if (card == null) return null;
        Rect r = card.rect;
        var face = new Rect(r.x, r.y + r.height * 0.38f, r.width, r.height * 0.58f);
        return Sprite.Create(card.texture, face, new Vector2(0.5f, 0.5f), card.pixelsPerUnit);
    }

    private static void LoadScene(string sceneName)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }
}
