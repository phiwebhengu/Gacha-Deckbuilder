using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class CardSpriteMapping
{
    public int CardId;
    public Sprite CardSprite;
}

public class CardAssetRegistry : MonoBehaviour
{
    public static CardAssetRegistry Instance { get; private set; }

    [Header("Drag all card sprites into these lists in the Inspector")]
    public List<CardSpriteMapping> actionCardSprites;
    public List<CardSpriteMapping> supportCardSprites;

    private Dictionary<int, Sprite> actionDict;
    private Dictionary<int, Sprite> supportDict;

    void Awake()
    {
        Instance = this;
        BuildDictionaries();
    }

    void BuildDictionaries()
    {
        actionDict = new Dictionary<int, Sprite>();
        foreach (var mapping in actionCardSprites)
        {
            if (!actionDict.ContainsKey(mapping.CardId))
                actionDict.Add(mapping.CardId, mapping.CardSprite);
        }

        supportDict = new Dictionary<int, Sprite>();
        foreach (var mapping in supportCardSprites)
        {
            if (!supportDict.ContainsKey(mapping.CardId))
                supportDict.Add(mapping.CardId, mapping.CardSprite);
        }
    }

    public Sprite GetActionSprite(int id)
    {
        actionDict.TryGetValue(id, out Sprite sprite);
        return sprite;
    }

    public Sprite GetSupportSprite(int id)
    {
        supportDict.TryGetValue(id, out Sprite sprite);
        return sprite;
    }
}
