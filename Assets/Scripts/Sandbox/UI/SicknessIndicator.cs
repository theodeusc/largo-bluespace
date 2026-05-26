using UnityEngine;
using TMPro;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class SicknessIndicator : MonoBehaviour
    {
        [SerializeField] private TMP_Text _sicknessText;

        [Header("Colours")]
        [SerializeField] private Color _currentColour;
        [SerializeField] private Color _maxColour;

        public void UpdateSicknessCounter(int currentSickness, int maxSickness)
        {
            if (_sicknessText != null)
            {
                string currentStr = string.Format($"<color=#{ColorUtility.ToHtmlStringRGB(_currentColour)}>{currentSickness}</color>");
                string maxStr = string.Format($"<color=#{ColorUtility.ToHtmlStringRGB(_maxColour)}>/{maxSickness}</color>");

                _sicknessText.text = currentStr + maxStr;
            }
        }
    }
}
