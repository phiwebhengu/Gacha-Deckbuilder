using UnityEngine;
using UnityEngine.UI;

public class CardVisual : MonoBehaviour
{
    public Image cardImage;

    public ActionCardData ActionData { get; private set; }
    public SupportCardData SupportData { get; private set; }

    public void Setup(ActionCardData data, Sprite sprite)
    {
        ActionData = data;
        SupportData = null;
        if (cardImage != null) cardImage.sprite = sprite;
    }

    public void Setup(SupportCardData data, Sprite sprite)
    {
        SupportData = data;
        ActionData = null;
        if (cardImage != null) cardImage.sprite = sprite;
    }
}