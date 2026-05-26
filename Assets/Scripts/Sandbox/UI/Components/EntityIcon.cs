using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class EntityIcon : MonoBehaviour
    {
        [SerializeField] private Image _entityBackground;
        [SerializeField] private Image _entityIcon;

        [SerializeField] private Color _itemColour;
        [SerializeField] private Sprite _currencySprite;

        private const string LogChannel = "[EntityIcon]";

        public void SetEntity(Entity entity)
        {
            if (entity == null)
            {
                Debug.LogError($"{LogChannel} Failed setup, Entity is null!");
                return;
            }

            //Set icon — clear any prefab-baked tint so pixel-art icons render at natural colours.
            if (_entityIcon != null)
            {
                Sprite resource = Resources.Load<Sprite>(entity.Icon);
                if (resource != null)
                {
                    _entityIcon.sprite = resource;
                    _entityIcon.color = Color.white;
                    _entityIcon.preserveAspect = true;
                }
                else
                {
                    Debug.LogError($"{LogChannel} Failed to find icon for entity at path {entity.Icon}!");
                }
            }

            //Set Colour — same rationale as EntityWidget: icon backplate uniformly white so
            //each entity's pixel-art icon reads at its natural colours.
            if (_entityBackground != null)
            {
                _entityBackground.color = Color.white;
            }
        }

        public void SetItem(Item itemDef)
        {
            if (itemDef == null)
            {
                Debug.LogError($"{LogChannel} Failed setup, Item is null!");
                return;
            }

            //Set icon — clear any prefab-baked tint so pixel-art icons render at natural colours.
            if (_entityIcon != null)
            {
                Sprite resource = Resources.Load<Sprite>(itemDef.Icon);
                if (resource != null)
                {
                    _entityIcon.sprite = resource;
                    _entityIcon.color = Color.white;
                    _entityIcon.preserveAspect = true;
                }
                else
                {
                    Debug.LogError($"{LogChannel} Failed to find icon for item at path {itemDef.Icon}!");
                }
            }

            //Set Colour
            if (_entityBackground != null)
            {
                _entityBackground.color = _itemColour;
            }
        }

        public void SetCurrency()
        {
            //Set icon — clear any prefab-baked tint so the currency sprite renders natural.
            if (_entityIcon != null && _currencySprite != null)
            {
                _entityIcon.sprite = _currencySprite;
                _entityIcon.color = Color.white;
                _entityIcon.preserveAspect = true;
            }

            //Set Colour
            if (_entityBackground != null)
            {
                _entityBackground.color = _itemColour;
            }
        }
    }
}

