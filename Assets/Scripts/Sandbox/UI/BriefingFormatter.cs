using System.Text;
using System.Text.RegularExpressions;
using Glitchers.EcoKnow.Sandbox.Data;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Composes the briefing description text shown inside ObjectivesModal when a
    // <scenario>_briefing.json sidecar is available. Falls back to the scenario's
    // own Description when no briefing has been loaded, preserving prior behaviour
    // for scenarios without a briefing file.
    public static class BriefingFormatter
    {
        private static readonly Regex PascalCaseSplit = new Regex(@"(?<!^)(?=[A-Z])");

        public static string Compose(Scenario scenario, BriefingData briefing)
        {
            string fallback = scenario != null ? scenario.Description : null;
            if (briefing == null) return fallback;

            StringBuilder sb = new StringBuilder();

            AppendParagraph(sb, briefing.situation);

            // gameplay_summary uses raw HTML on the briefings site; on the in-game
            // modal it is rendered through the markdown converter so authors can
            // stay in one syntax.
            AppendParagraph(sb, briefing.gameplay_summary);

            if (briefing.info_card != null && !string.IsNullOrEmpty(briefing.info_card.body))
            {
                AppendHeading(sb, briefing.info_card.title);
                AppendParagraph(sb, briefing.info_card.body);
            }

            AppendParagraph(sb, briefing.actions_intro);

            if (briefing.objectives != null && briefing.objectives.Count > 0)
            {
                AppendHeading(sb, "To Meet Objectives");
                foreach (BriefingData.Objective o in briefing.objectives)
                {
                    if (o == null) continue;
                    AppendBullet(sb, o.title, o.description, o.goal);
                }
            }

            if (briefing.entity_descriptions != null && briefing.entity_descriptions.Count > 0)
            {
                AppendHeading(sb, "Key Entities");
                foreach (var pair in briefing.entity_descriptions)
                {
                    AppendBullet(sb, Humanise(pair.Key), pair.Value, null);
                }
            }

            if (briefing.watch_out != null && briefing.watch_out.Count > 0)
            {
                AppendHeading(sb, "Watch Out For");
                foreach (BriefingData.WatchOut w in briefing.watch_out)
                {
                    if (w == null) continue;
                    string heading = !string.IsNullOrEmpty(w.name) ? w.name : Humanise(w.entity);
                    AppendBullet(sb, heading, w.description, null);
                }
            }

            if (!string.IsNullOrEmpty(briefing.why_this_matters))
            {
                AppendHeading(sb, "Why This Matters");
                AppendParagraph(sb, briefing.why_this_matters);
            }

            string composed = sb.ToString().TrimEnd();
            return string.IsNullOrEmpty(composed) ? fallback : composed;
        }

        private static void AppendParagraph(StringBuilder sb, string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return;
            sb.Append(MarkdownToRichText.Convert(markdown.Trim()));
            sb.Append("\n\n");
        }

        private static void AppendHeading(StringBuilder sb, string heading)
        {
            if (string.IsNullOrWhiteSpace(heading)) return;
            sb.Append("<b>");
            sb.Append(MarkdownToRichText.Convert(heading.Trim()));
            sb.Append("</b>\n");
        }

        private static void AppendBullet(StringBuilder sb, string title, string body, string goal)
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(title);
            bool hasBody = !string.IsNullOrWhiteSpace(body);
            bool hasGoal = !string.IsNullOrWhiteSpace(goal);
            if (!hasTitle && !hasBody && !hasGoal) return;

            sb.Append("• ");
            if (hasTitle)
            {
                sb.Append("<b>");
                sb.Append(MarkdownToRichText.Convert(title.Trim()));
                sb.Append("</b>");
                if (hasBody || hasGoal) sb.Append(" — ");
            }
            if (hasBody) sb.Append(MarkdownToRichText.Convert(body.Trim()));
            if (hasGoal)
            {
                if (hasBody) sb.Append(' ');
                sb.Append("<i>");
                sb.Append(MarkdownToRichText.Convert(goal.Trim()));
                sb.Append("</i>");
            }
            sb.Append('\n');
        }

        private static string Humanise(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            return PascalCaseSplit.Replace(id, " ");
        }
    }
}
