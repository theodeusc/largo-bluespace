using System.Text.RegularExpressions;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Converts the limited markdown used by EcoKnow briefing YAML
    // (`**bold**` and `*italic*`) into TextMeshPro rich-text tags.
    // Bold is processed before italic so `**x**` never matches as `*<i>x</i>*`.
    public static class MarkdownToRichText
    {
        private static readonly Regex BoldPattern = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Singleline);
        private static readonly Regex ItalicPattern = new Regex(@"\*(.+?)\*", RegexOptions.Singleline);

        public static string Convert(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return markdown;
            string result = BoldPattern.Replace(markdown, "<b>$1</b>");
            result = ItalicPattern.Replace(result, "<i>$1</i>");
            return result;
        }
    }
}
