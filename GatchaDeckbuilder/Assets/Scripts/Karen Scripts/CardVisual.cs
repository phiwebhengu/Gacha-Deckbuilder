using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardVisual : MonoBehaviour
{
    public Image cardImage;

    public void Setup(ActionCardData data, Sprite sprite)
    {
        if (cardImage != null) cardImage.sprite = sprite;
    }

    // You can add an overloaded Setup method for SupportCardData as well
    public void Setup(SupportCardData data, Sprite sprite)
    {
        if (cardImage != null) cardImage.sprite = sprite;
    }
}
