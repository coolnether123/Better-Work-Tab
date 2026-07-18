using Spine.UI.Layout;
using Spine.UI.Animation;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2LayoutMetrics
    {
        internal float HeaderHeight = 40f;
        internal float HeaderBodyGap = 6f;
        internal float OuterPadding = 10f;
        internal float InnerPadding = 8f;
        internal float Gap = 8f;
        internal float SmallGap = 6f;
        internal float ButtonHeight = 30f;
        internal float CompactButtonHeight = 24f;
        internal float TextFieldHeight = 28f;
        internal float DashboardActionsHeight = 46f;
        internal float LeftPaneWidth = 300f;
        internal float RuleRowHeight = 60f;
        internal float RuleRowStride = 64f;
        internal float ConditionRowHeight = 58f;
        internal float ConditionRowStride = 64f;
        internal float RuleNameHeight = 36f;
        internal float RuleSentenceHeight = 48f;
        internal float ConfirmHeight = 70f;
        internal float SectionTitleHeight = 30f;
        internal float TargetSelectedHeight = 46f;
        internal float TargetPickerHeight = 238f;
        internal float ConditionsPanelHeight = 315f;
        internal float ActionPanelHeight = 230f;
        internal float PreviewPanelHeight = 300f;
        internal float CollapsedSectionHeight = 44f;
        internal float SurfaceAnimationSeconds = 0.16f;
        internal float SectionAnimationSeconds = 0.18f;
        internal float WindowOpenAnimationSeconds = 0.22f;
        internal float PreferredWindowWidth = 1180f;
        internal float PreferredWindowHeight = 760f;
        internal float MinimumWindowWidth = 940f;
        internal float MinimumWindowHeight = 620f;
        internal float ScreenMargin = 40f;
    }

    internal sealed class RuleBuilder2Layout
    {
        private readonly RuleBuilder2LayoutMetrics metrics;

        internal RuleBuilder2Layout(RuleBuilder2LayoutMetrics metrics = null)
        {
            this.metrics = metrics ?? new RuleBuilder2LayoutMetrics();
        }

        internal RuleBuilder2LayoutMetrics Metrics => metrics;

        internal RuleBuilder2WindowRects Window(Rect rect)
        {
            Rect header = new Rect(rect.x, rect.y, rect.width, metrics.HeaderHeight);
            Rect body = new Rect(
                rect.x,
                header.yMax + metrics.HeaderBodyGap,
                rect.width,
                Mathf.Max(0f, rect.yMax - header.yMax - metrics.HeaderBodyGap));
            return new RuleBuilder2WindowRects(header, body);
        }

        internal RuleBuilder2HeaderRects Header(Rect rect)
        {
            Rect title = new Rect(rect.x + 12f, rect.y, 190f, rect.height);
            Rect name = new Rect(rect.x + 206f, rect.y + 6f, 270f, metrics.TextFieldHeight);
            Rect tools = new Rect(name.xMax + 4f, name.y, 34f, metrics.TextFieldHeight);
            Rect enabled = new Rect(tools.xMax + 12f, rect.y + 8f, 110f, 24f);
            Rect done = new Rect(rect.xMax - 88f, rect.y + 5f, 78f, metrics.ButtonHeight);
            Rect apply = new Rect(done.x - 92f, done.y, 84f, metrics.ButtonHeight);
            Rect preview = UnityCompat.ZeroRect;
            Rect status = new Rect(enabled.xMax + 10f, rect.y + 8f, Mathf.Max(0f, apply.x - enabled.xMax - 20f), 24f);
            return new RuleBuilder2HeaderRects(title, name, tools, enabled, preview, apply, done, status);
        }

        internal RuleBuilder2MasterDetailRects MasterDetail(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect left = new Rect(inner.x, inner.y, Mathf.Min(metrics.LeftPaneWidth, inner.width * 0.38f), inner.height);
            Rect right = new Rect(left.xMax + metrics.Gap, inner.y, Mathf.Max(0f, inner.xMax - left.xMax - metrics.Gap), inner.height);
            return new RuleBuilder2MasterDetailRects(inner, left, right);
        }

        internal RuleBuilder2LeftPaneRects LeftPane(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.InnerPadding);
            Rect generateDraft = new Rect(inner.x, inner.y, inner.width, metrics.ButtonHeight);
            Rect addRule = new Rect(inner.x, inner.yMax - metrics.ButtonHeight, inner.width, metrics.ButtonHeight);
            Rect review = new Rect(inner.x, addRule.y - metrics.ButtonHeight - metrics.SmallGap, inner.width, metrics.ButtonHeight);
            Rect list = new Rect(inner.x, generateDraft.yMax + metrics.Gap, inner.width, Mathf.Max(0f, review.y - generateDraft.yMax - metrics.Gap - metrics.SmallGap));
            return new RuleBuilder2LeftPaneRects(inner, generateDraft, list, review, addRule);
        }

        internal RuleBuilder2RuleListRowRects RuleListRow(Rect row)
        {
            Rect enabled = new Rect(row.x + 6f, row.y + 8f, 24f, 24f);
            Rect badgeArea = new Rect(row.xMax - 116f, row.y + 7f, 108f, 24f);
            Rect target = new Rect(row.x + 36f, row.y + 6f, Mathf.Max(1f, badgeArea.x - row.x - 42f), 24f);
            Rect summary = new Rect(row.x + 36f, row.y + 32f, Mathf.Max(1f, row.width - 44f), 24f);
            Rect select = new Rect(row.x, row.y, row.width, row.height);
            return new RuleBuilder2RuleListRowRects(enabled, target, summary, badgeArea, select);
        }

        internal RuleBuilder2DashboardRects Dashboard(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect title = UnityCompat.ZeroRect;
            Rect actionsCard = new Rect(inner.x, inner.y, inner.width, metrics.DashboardActionsHeight);
            Rect actionsInner = actionsCard.ContractedBy(metrics.InnerPadding);
            Rect[] actions = SpineRectLayout.Horizontal(
                new Rect(actionsInner.x, actionsInner.y, Mathf.Max(0f, actionsInner.width - 78f), metrics.ButtonHeight),
                metrics.Gap,
                SpineLayoutTrack.Fixed(160f),
                SpineLayoutTrack.Fixed(230f),
                SpineLayoutTrack.Fixed(110f),
                SpineLayoutTrack.Flex(1f, 1f));
            Rect more = new Rect(actionsInner.xMax - 70f, actionsInner.y, 70f, metrics.ButtonHeight);
            Rect overview = UnityCompat.ZeroRect;
            Rect content = new Rect(inner.x, actionsCard.yMax + 10f, inner.width, Mathf.Max(0f, inner.yMax - actionsCard.yMax - 10f));

            return new RuleBuilder2DashboardRects(
                inner,
                title,
                actionsCard,
                actionsInner,
                actions[0],
                actions[1],
                actions[2],
                more,
                overview,
                content);
        }

        internal RuleBuilder2RulesTableRects RulesTable(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.InnerPadding);
            Rect header = new Rect(inner.x, inner.y, inner.width, 28f);
            Rect list = new Rect(inner.x, inner.y + 32f, inner.width, Mathf.Max(0f, inner.height - 32f));
            return new RuleBuilder2RulesTableRects(inner, header, list);
        }

        internal RuleBuilder2RuleTableHeaderRects RulesTableHeader(Rect rect)
        {
            Rect enabled = new Rect(rect.x + 8f, rect.y + 3f, 60f, 24f);
            Rect target = new Rect(rect.x + 72f, rect.y + 3f, 190f, 24f);
            Rect conditions = new Rect(rect.x + 270f, rect.y + 3f, 220f, 24f);
            Rect action = new Rect(rect.x + 500f, rect.y + 3f, Mathf.Max(1f, rect.width - 650f), 24f);
            Rect edit = new Rect(rect.xMax - 130f, rect.y + 3f, 120f, 24f);
            return new RuleBuilder2RuleTableHeaderRects(enabled, target, conditions, action, edit);
        }

        internal RuleBuilder2RuleTableRowRects RuleTableRow(Rect row)
        {
            Rect enabled = new Rect(row.x + 8f, row.y + 5f, 24f, 24f);
            Rect target = new Rect(row.x + 44f, row.y + 5f, 210f, 24f);
            Rect conditions = new Rect(row.x + 260f, row.y + 5f, 230f, 24f);
            Rect action = new Rect(row.x + 500f, row.y + 5f, Mathf.Max(1f, row.width - 640f), 24f);
            Rect edit = new Rect(row.xMax - 128f, row.y + 3f, 58f, 24f);
            Rect delete = new Rect(edit.xMax + 8f, edit.y, 58f, 24f);
            Rect select = new Rect(row.x, row.y, Mathf.Max(0f, edit.x - row.x - 4f), row.height);
            return new RuleBuilder2RuleTableRowRects(enabled, target, conditions, action, edit, delete, select);
        }

        internal RuleBuilder2DraftQueueRects DraftQueue(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect title = new Rect(inner.x, inner.y, inner.width, 28f);
            Rect subtitle = new Rect(inner.x, title.yMax + 2f, inner.width, 22f);
            Rect side = new Rect(inner.x, inner.y + 58f, 220f, Mathf.Max(0f, inner.height - 58f));
            Rect main = new Rect(side.xMax + 12f, side.y, Mathf.Max(0f, inner.width - side.width - 12f), side.height);
            return new RuleBuilder2DraftQueueRects(inner, title, subtitle, side, main);
        }

        internal RuleBuilder2GeneratedDraftPanelRects GeneratedDraftPanel(Rect rect, float sentenceHeight)
        {
            Rect inner = rect.ContractedBy(12f);
            Rect title = new Rect(inner.x, inner.y, inner.width, 30f);
            Rect notes = new Rect(inner.x, inner.y + 34f, inner.width, 44f);
            Rect sentence = new Rect(inner.x, inner.y + 84f, inner.width, Mathf.Max(30f, sentenceHeight));
            Rect preview = new Rect(inner.x, sentence.yMax + 10f, inner.width, Mathf.Max(0f, inner.yMax - 44f - sentence.yMax - 10f));
            float buttonWidth = Mathf.Min(86f, Mathf.Max(64f, (inner.width - metrics.Gap * 2f) / 3f));
            float buttonRowWidth = buttonWidth * 3f + metrics.Gap * 2f;
            float buttonX = Mathf.Max(inner.x, inner.xMax - buttonRowWidth);
            Rect reject = new Rect(buttonX, inner.yMax - 34f, buttonWidth, metrics.ButtonHeight);
            Rect edit = new Rect(reject.xMax + metrics.Gap, reject.y, buttonWidth, metrics.ButtonHeight);
            Rect accept = new Rect(edit.xMax + metrics.Gap, reject.y, buttonWidth, metrics.ButtonHeight);
            return new RuleBuilder2GeneratedDraftPanelRects(inner, title, notes, sentence, preview, reject, edit, accept);
        }

        internal float EditorViewHeight(float activePanelHeight)
        {
            return metrics.RuleNameHeight + metrics.Gap + metrics.RuleSentenceHeight + 10f + activePanelHeight + metrics.Gap + metrics.ConfirmHeight + 24f;
        }

        internal RuleBuilder2EditorRects Editor(Rect view, float activePanelHeight)
        {
            float y = view.y;
            Rect name = new Rect(view.x, y, view.width, metrics.RuleNameHeight);
            y += metrics.RuleNameHeight + metrics.Gap;
            Rect sentence = new Rect(view.x, y, view.width, metrics.RuleSentenceHeight);
            y += metrics.RuleSentenceHeight + 10f;
            Rect panel = new Rect(view.x, y, view.width, activePanelHeight);
            y += activePanelHeight + metrics.Gap;
            Rect confirm = new Rect(view.x, y, view.width, metrics.ConfirmHeight);
            return new RuleBuilder2EditorRects(name, sentence, panel, confirm);
        }

        internal RuleBuilder2RuleNameRects RuleName(Rect rect)
        {
            Rect title = new Rect(rect.x, rect.y, 160f, rect.height);
            Rect name = new Rect(rect.x + 170f, rect.y + 3f, 320f, metrics.TextFieldHeight);
            Rect dashboard = new Rect(rect.xMax - 150f, rect.y + 3f, 150f, metrics.TextFieldHeight);
            return new RuleBuilder2RuleNameRects(title, name, dashboard);
        }

        internal RuleBuilder2SentenceRects Sentence(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.InnerPadding);
            float gap = metrics.SmallGap;
            const float setLabelWidth = 34f;
            const float whenLabelWidth = 44f;
            const float thenLabelWidth = 38f;
            float tokenWidth = Mathf.Max(1f, inner.width - setLabelWidth - whenLabelWidth - thenLabelWidth - gap * 4f);
            float targetWidth = tokenWidth * 0.24f;
            float conditionsWidth = tokenWidth * 0.29f;
            float actionWidth = tokenWidth * 0.32f;
            float previewWidth = tokenWidth - targetWidth - conditionsWidth - actionWidth;

            float x = inner.x;
            Rect setLabel = new Rect(x, inner.y, setLabelWidth, inner.height);
            x += setLabelWidth + gap;
            Rect target = TokenRect(x, inner, targetWidth);
            x += targetWidth + gap;
            Rect whenLabel = new Rect(x, inner.y, whenLabelWidth, inner.height);
            x += whenLabelWidth + gap;
            Rect conditions = TokenRect(x, inner, conditionsWidth);
            x += conditionsWidth + gap;
            Rect thenLabel = new Rect(x, inner.y, thenLabelWidth, inner.height);
            x += thenLabelWidth + gap;
            Rect action = TokenRect(x, inner, actionWidth);
            x += actionWidth + gap;
            Rect preview = TokenRect(x, inner, previewWidth);
            return new RuleBuilder2SentenceRects(setLabel, target, whenLabel, conditions, thenLabel, action, preview);
        }

        internal RuleBuilder2TargetSectionRects TargetSection(Rect rect, bool hasTarget)
        {
            Rect inner = hasTarget ? rect.ContractedBy(8f) : rect.ContractedBy(metrics.OuterPadding);
            float y = inner.y + metrics.SectionTitleHeight;
            Rect searchOrSummary = new Rect(inner.x, y, inner.width, metrics.TextFieldHeight);
            if (hasTarget)
            {
                float titleWidth = 58f;
                Rect change = new Rect(inner.xMax - 100f, inner.y + 1f, 100f, metrics.TextFieldHeight);
                Rect summary = new Rect(inner.x + titleWidth + metrics.SmallGap, inner.y + 2f, Mathf.Max(0f, change.x - inner.x - titleWidth - metrics.SmallGap * 2f), 26f);
                Rect hint = UnityCompat.ZeroRect;
                Rect next = UnityCompat.ZeroRect;
                return new RuleBuilder2TargetSectionRects(inner, summary, change, hint, next, UnityCompat.ZeroRect, UnityCompat.ZeroRect);
            }

            Rect list = new Rect(inner.x, searchOrSummary.yMax + metrics.SmallGap, inner.width, Mathf.Max(0f, inner.yMax - searchOrSummary.yMax - metrics.SmallGap));
            return new RuleBuilder2TargetSectionRects(inner, UnityCompat.ZeroRect, UnityCompat.ZeroRect, UnityCompat.ZeroRect, UnityCompat.ZeroRect, searchOrSummary, list);
        }

        internal RuleBuilder2ConditionsSectionRects ConditionsSection(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect search = new Rect(inner.x, inner.y + metrics.SectionTitleHeight, inner.width, metrics.TextFieldHeight);
            Rect next = new Rect(inner.xMax - 120f, search.y, 120f, metrics.TextFieldHeight);
            Rect content = new Rect(inner.x, search.yMax + metrics.Gap, inner.width, Mathf.Max(0f, inner.yMax - search.yMax - metrics.Gap));
            Rect active = new Rect(content.x, content.y, content.width * 0.52f - 6f, content.height);
            Rect picker = new Rect(active.xMax + 12f, content.y, content.width * 0.48f - 6f, content.height);
            return new RuleBuilder2ConditionsSectionRects(inner, search, next, active, picker);
        }

        internal RuleBuilder2ConditionRowRects ConditionRow(Rect rect)
        {
            Rect checkbox = new Rect(rect.x + 6f, rect.y + 7f, 24f, 24f);
            Rect label = new Rect(rect.x + 34f, rect.y + 5f, Mathf.Max(1f, rect.width - 128f), 24f);
            Rect editor = new Rect(rect.x + 34f, rect.y + 31f, Mathf.Max(1f, rect.width - 128f), 24f);
            Rect up = new Rect(rect.xMax - 86f, rect.y + 6f, 24f, 24f);
            Rect down = new Rect(rect.xMax - 58f, rect.y + 6f, 24f, 24f);
            Rect remove = new Rect(rect.xMax - 30f, rect.y + 6f, 24f, 24f);
            return new RuleBuilder2ConditionRowRects(checkbox, label, editor, up, down, remove);
        }

        internal RuleBuilder2ActionSectionRects ActionSection(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect modes = new Rect(inner.x, inner.y + metrics.SectionTitleHeight, inner.width, metrics.ButtonHeight);
            Rect priority = new Rect(inner.x, modes.yMax + metrics.Gap, 150f, metrics.TextFieldHeight);
            Rect scope = new Rect(priority.xMax + 12f, priority.y + 2f, Mathf.Max(0f, inner.width - priority.width - 154f), 24f);
            Rect next = new Rect(inner.xMax - 130f, priority.y, 120f, metrics.TextFieldHeight);
            Rect body = new Rect(inner.x, priority.yMax + 10f, inner.width, Mathf.Max(0f, inner.yMax - priority.yMax - 10f));
            return new RuleBuilder2ActionSectionRects(inner, modes, priority, scope, next, body);
        }

        internal RuleBuilder2PreviewSectionRects PreviewSection(Rect rect, bool showMatchedPanel)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect run = new Rect(inner.x, inner.y + metrics.SectionTitleHeight, 120f, metrics.TextFieldHeight);
            Rect matched = showMatchedPanel
                ? new Rect(inner.x + 130f, run.y, Mathf.Max(0f, inner.width - 130f), 72f)
                : UnityCompat.ZeroRect;
            Rect hint = new Rect(inner.x, inner.y + 66f, inner.width, 40f);
            Rect list = new Rect(inner.x, inner.y + 104f, inner.width, Mathf.Max(0f, inner.height - 108f));
            return new RuleBuilder2PreviewSectionRects(inner, run, matched, hint, list);
        }

        internal RuleBuilder2ConfirmRects Confirm(Rect rect)
        {
            Rect confirm = new Rect(rect.x, rect.y + 12f, 190f, 34f);
            Rect done = new Rect(confirm.xMax + 10f, confirm.y, 110f, 34f);
            return new RuleBuilder2ConfirmRects(confirm, done);
        }

        private static Rect TokenRect(float x, Rect inner, float width)
        {
            return new Rect(x, inner.y + 2f, Mathf.Max(1f, width), Mathf.Max(1f, inner.height - 4f));
        }
    }

    internal readonly struct RuleBuilder2WindowRects
    {
        internal RuleBuilder2WindowRects(Rect header, Rect body) { Header = header; Body = body; }
        internal Rect Header { get; }
        internal Rect Body { get; }
    }

    internal readonly struct RuleBuilder2HeaderRects
    {
        internal RuleBuilder2HeaderRects(Rect title, Rect name, Rect tools, Rect enabled, Rect preview, Rect apply, Rect done, Rect status)
        {
            Title = title; Name = name; Tools = tools; Enabled = enabled; Preview = preview; Apply = apply; Done = done; Status = status;
        }
        internal Rect Title { get; }
        internal Rect Name { get; }
        internal Rect Tools { get; }
        internal Rect Enabled { get; }
        internal Rect Preview { get; }
        internal Rect Apply { get; }
        internal Rect Done { get; }
        internal Rect Status { get; }
    }

    internal readonly struct RuleBuilder2MasterDetailRects
    {
        internal RuleBuilder2MasterDetailRects(Rect inner, Rect left, Rect right)
        {
            Inner = inner; Left = left; Right = right;
        }
        internal Rect Inner { get; }
        internal Rect Left { get; }
        internal Rect Right { get; }
    }

    internal readonly struct RuleBuilder2LeftPaneRects
    {
        internal RuleBuilder2LeftPaneRects(Rect inner, Rect generateDraft, Rect list, Rect review, Rect addRule)
        {
            Inner = inner; GenerateDraft = generateDraft; List = list; Review = review; AddRule = addRule;
        }
        internal Rect Inner { get; }
        internal Rect GenerateDraft { get; }
        internal Rect List { get; }
        internal Rect Review { get; }
        internal Rect AddRule { get; }
    }

    internal readonly struct RuleBuilder2RuleListRowRects
    {
        internal RuleBuilder2RuleListRowRects(Rect enabled, Rect target, Rect summary, Rect badges, Rect select)
        {
            Enabled = enabled; Target = target; Summary = summary; Badges = badges; Select = select;
        }
        internal Rect Enabled { get; }
        internal Rect Target { get; }
        internal Rect Summary { get; }
        internal Rect Badges { get; }
        internal Rect Select { get; }
    }

    internal readonly struct RuleBuilder2DashboardRects
    {
        internal RuleBuilder2DashboardRects(Rect inner, Rect title, Rect actionsCard, Rect actionsInner, Rect addRule, Rect generateDraft, Rect import, Rect more, Rect overview, Rect content)
        {
            Inner = inner; Title = title; ActionsCard = actionsCard; ActionsInner = actionsInner; AddRule = addRule; GenerateDraft = generateDraft; Import = import; More = more; Overview = overview; Content = content;
        }
        internal Rect Inner { get; }
        internal Rect Title { get; }
        internal Rect ActionsCard { get; }
        internal Rect ActionsInner { get; }
        internal Rect AddRule { get; }
        internal Rect GenerateDraft { get; }
        internal Rect Import { get; }
        internal Rect More { get; }
        internal Rect Overview { get; }
        internal Rect Content { get; }
    }

    internal readonly struct RuleBuilder2RulesTableRects
    {
        internal RuleBuilder2RulesTableRects(Rect inner, Rect header, Rect list) { Inner = inner; Header = header; List = list; }
        internal Rect Inner { get; }
        internal Rect Header { get; }
        internal Rect List { get; }
    }

    internal readonly struct RuleBuilder2RuleTableRowRects
    {
        internal RuleBuilder2RuleTableRowRects(Rect enabled, Rect target, Rect conditions, Rect action, Rect edit, Rect delete, Rect select)
        {
            Enabled = enabled; Target = target; Conditions = conditions; Action = action; Edit = edit; Delete = delete; Select = select;
        }
        internal Rect Enabled { get; }
        internal Rect Target { get; }
        internal Rect Conditions { get; }
        internal Rect Action { get; }
        internal Rect Edit { get; }
        internal Rect Delete { get; }
        internal Rect Select { get; }
    }

    internal readonly struct RuleBuilder2RuleTableHeaderRects
    {
        internal RuleBuilder2RuleTableHeaderRects(Rect enabled, Rect target, Rect conditions, Rect action, Rect edit)
        {
            Enabled = enabled; Target = target; Conditions = conditions; Action = action; Edit = edit;
        }
        internal Rect Enabled { get; }
        internal Rect Target { get; }
        internal Rect Conditions { get; }
        internal Rect Action { get; }
        internal Rect Edit { get; }
    }

    internal readonly struct RuleBuilder2DraftQueueRects
    {
        internal RuleBuilder2DraftQueueRects(Rect inner, Rect title, Rect subtitle, Rect side, Rect main)
        {
            Inner = inner; Title = title; Subtitle = subtitle; Side = side; Main = main;
        }
        internal Rect Inner { get; }
        internal Rect Title { get; }
        internal Rect Subtitle { get; }
        internal Rect Side { get; }
        internal Rect Main { get; }
    }

    internal readonly struct RuleBuilder2GeneratedDraftPanelRects
    {
        internal RuleBuilder2GeneratedDraftPanelRects(Rect inner, Rect title, Rect notes, Rect sentence, Rect preview, Rect reject, Rect edit, Rect accept)
        {
            Inner = inner; Title = title; Notes = notes; Sentence = sentence; Preview = preview; Reject = reject; Edit = edit; Accept = accept;
        }
        internal Rect Inner { get; }
        internal Rect Title { get; }
        internal Rect Notes { get; }
        internal Rect Sentence { get; }
        internal Rect Preview { get; }
        internal Rect Reject { get; }
        internal Rect Edit { get; }
        internal Rect Accept { get; }
    }

    internal readonly struct RuleBuilder2EditorRects
    {
        internal RuleBuilder2EditorRects(Rect name, Rect sentence, Rect panel, Rect confirm)
        {
            Name = name; Sentence = sentence; Panel = panel; Confirm = confirm;
        }
        internal Rect Name { get; }
        internal Rect Sentence { get; }
        internal Rect Panel { get; }
        internal Rect Confirm { get; }
    }

    internal readonly struct RuleBuilder2RuleNameRects
    {
        internal RuleBuilder2RuleNameRects(Rect title, Rect name, Rect dashboard)
        {
            Title = title; Name = name; Dashboard = dashboard;
        }
        internal Rect Title { get; }
        internal Rect Name { get; }
        internal Rect Dashboard { get; }
    }

    internal readonly struct RuleBuilder2SentenceRects
    {
        internal RuleBuilder2SentenceRects(Rect setLabel, Rect target, Rect whenLabel, Rect conditions, Rect thenLabel, Rect action, Rect preview)
        {
            SetLabel = setLabel; Target = target; WhenLabel = whenLabel; Conditions = conditions; ThenLabel = thenLabel; Action = action; Preview = preview;
        }
        internal Rect SetLabel { get; }
        internal Rect Target { get; }
        internal Rect WhenLabel { get; }
        internal Rect Conditions { get; }
        internal Rect ThenLabel { get; }
        internal Rect Action { get; }
        internal Rect Preview { get; }
    }

    internal readonly struct RuleBuilder2TargetSectionRects
    {
        internal RuleBuilder2TargetSectionRects(Rect inner, Rect summary, Rect change, Rect hint, Rect next, Rect search, Rect list)
        {
            Inner = inner; Summary = summary; Change = change; Hint = hint; Next = next; Search = search; List = list;
        }
        internal Rect Inner { get; }
        internal Rect Summary { get; }
        internal Rect Change { get; }
        internal Rect Hint { get; }
        internal Rect Next { get; }
        internal Rect Search { get; }
        internal Rect List { get; }
    }

    internal readonly struct RuleBuilder2ConditionsSectionRects
    {
        internal RuleBuilder2ConditionsSectionRects(Rect inner, Rect search, Rect next, Rect active, Rect picker)
        {
            Inner = inner; Search = search; Next = next; Active = active; Picker = picker;
        }
        internal Rect Inner { get; }
        internal Rect Search { get; }
        internal Rect Next { get; }
        internal Rect Active { get; }
        internal Rect Picker { get; }
    }

    internal readonly struct RuleBuilder2ConditionRowRects
    {
        internal RuleBuilder2ConditionRowRects(Rect checkbox, Rect label, Rect editor, Rect up, Rect down, Rect remove)
        {
            Checkbox = checkbox; Label = label; Editor = editor; Up = up; Down = down; Remove = remove;
        }
        internal Rect Checkbox { get; }
        internal Rect Label { get; }
        internal Rect Editor { get; }
        internal Rect Up { get; }
        internal Rect Down { get; }
        internal Rect Remove { get; }
    }

    internal readonly struct RuleBuilder2ActionSectionRects
    {
        internal RuleBuilder2ActionSectionRects(Rect inner, Rect modes, Rect priority, Rect scope, Rect next, Rect body)
        {
            Inner = inner; Modes = modes; Priority = priority; Scope = scope; Next = next; Body = body;
        }
        internal Rect Inner { get; }
        internal Rect Modes { get; }
        internal Rect Priority { get; }
        internal Rect Scope { get; }
        internal Rect Next { get; }
        internal Rect Body { get; }
    }

    internal readonly struct RuleBuilder2PreviewSectionRects
    {
        internal RuleBuilder2PreviewSectionRects(Rect inner, Rect run, Rect matched, Rect hint, Rect list)
        {
            Inner = inner; Run = run; Matched = matched; Hint = hint; List = list;
        }
        internal Rect Inner { get; }
        internal Rect Run { get; }
        internal Rect Matched { get; }
        internal Rect Hint { get; }
        internal Rect List { get; }
    }

    internal readonly struct RuleBuilder2ConfirmRects
    {
        internal RuleBuilder2ConfirmRects(Rect confirm, Rect done) { Confirm = confirm; Done = done; }
        internal Rect Confirm { get; }
        internal Rect Done { get; }
    }
}
