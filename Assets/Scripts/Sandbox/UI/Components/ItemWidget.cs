using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class ItemWidget : MonoBehaviour
    {
        [Header("Item Info")]
        [SerializeField] private TMP_Text _itemTitle;
        [SerializeField] private Image _itemIcon;
        [SerializeField] private Image _currencyIcon;
        [SerializeField] private TMP_Text _quantityText;
        [SerializeField] private Image _quantityPanelBackground;

        [Header("Colours")]
        [SerializeField] private Color _validColour;
        [SerializeField] private Color _invalidColour;

        private const string LogChannel = "[ItemWidget]";

        public void Awake()
        {
            SetValid(true);
            ShowItemIcon();
        }

        public void SetTitle(string title)
        {
            if (_itemTitle != null)
            {
                _itemTitle.text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title.ToString().ToLower());
                ShowTitle(true);
            }
        }

        public void ShowTitle(bool visible)
        {
            if (_itemTitle != null)
            {
                _itemTitle.gameObject.SetActive(visible);
            }
        }

        public void SetIcon(string spritePath)
        {
            if (spritePath == null)
            {
                //Debug.LogError($"{LogChannel} Spritepath is null, aborting icon setup");
                return;
            }

            if (spritePath.Equals(PlayerInventory.CurrencyID))
            {
                ShowCurrencyIcon();
            }
            else
            {
                ShowItemIcon();
                if ((_itemIcon != null) && !string.IsNullOrEmpty(spritePath))
                {
                    Sprite resource = Resources.Load<Sprite>(spritePath);
                    if (resource != null)
                    {
                        _itemIcon.sprite = resource;
                        _itemIcon.color = Color.white;
                    }
                    else
                    {
                        Debug.LogError($"{LogChannel} Failed to find icon for item at path {spritePath}!");
                    }
                }
            }
        }

        private void ShowCurrencyIcon()
        {
            if (_itemIcon != null)
            {
                _itemIcon.gameObject.SetActive(false);
            }
            if (_currencyIcon != null)
            {
                _currencyIcon.gameObject.SetActive(true);
            }
        }

        private void ShowItemIcon()
        {
            if (_itemIcon != null)
            {
                _itemIcon.gameObject.SetActive(true);
            }
            if (_currencyIcon != null)
            {
                _currencyIcon.gameObject.SetActive(false);
            }
        }

        public void SetQuantity(int quantity, bool forceShowSign = false)
        {
            if (_quantityText != null)
            {
                string quantityStr = FormatQuantity(quantity);
                if ((forceShowSign) && (quantity > 0))
                {
                    quantityStr = "+ " + quantityStr;
                }

                _quantityText.text = quantityStr;
            }
        }

        private string FormatQuantity(int quantity)
        {
            if (quantity >= 1000000)
            {
                float roundedQuantity = Mathf.Floor(((float)quantity / 100000f) * 10f) / 10f;
                return roundedQuantity.ToString("0.#") + "M";
            }
            else if (quantity >= 1000)
            {
                float roundedQuantity = Mathf.Floor(((float)quantity / 1000f) * 10f) / 10f;
                return roundedQuantity.ToString("0.#") + "k";
            }

            return quantity.ToString();
        }

        public void SetValid(bool value)
        {
            if (_quantityPanelBackground != null)
            {
                _quantityPanelBackground.color = value == true ? _validColour : _invalidColour;
            }

            if (_itemTitle != null)
            {
                _itemTitle.color = value == true ? _validColour : _invalidColour;
            }
        }
    }
}

