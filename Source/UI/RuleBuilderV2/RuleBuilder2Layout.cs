using Spine.UI.Layout;
using Spine.UI.Animation;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2LayoutMetrics
    {
        internal float HeaderHeight = 46f;
        internal float HeaderBodyGap = 8f;
        internal float OuterPadding = 10f;
        internal float InnerPadding = 8f;
        internal float Gap = 8f;
        internal float SmallGap = 6f;
        internal float ButtonHeight = 30f;
        internal float CompactButtonHeight = 24f;
        internal float TextFieldHeight = 28f;
        internal float DashboardActionsHeight = 76f;
        internal float RuleRowHeight = 30f;
        internal float RuleRowStride = 34f;
        internal float ConditionRowHeight = 52f;
        internal float ConditionRowStride = 58f;
        internal float RuleNameHeight = 36f;
        internal float RuleSentenceHeight = 48f;
        internal float ConfirmHeight = 70f;
        internal float SectionTitleHeight = 28f;
        internal float TargetSelectedHeight = 118f;
        internal float TargetPickerHeight = 238f;
        internal float ConditionsPanelHeight = 315f;
        internal float ActionPanelHeight = 230f;
        internal float PreviewPanelHeight = 300f;
        internal float CollapsedSectionHeight = 44f;
        internal float SurfaceAnimationSeconds = 0.16f;
        internal float SectionAnimationSeconds = 0.18f;
        internal float PreferredWindowWidth = 1180f;
        internal float PreferredWindowHeight = 760f;
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
            Rect title = new Rect(rect.x + 12f, rect.y, 220f, rect.height);
            Rect name = new Rect(rect.x + 245f, rect.y + 9f, 270f, metrics.TextFieldHeight);
            Rect enabled = new Rect(name.xMax + 12f, rect.y + 10f, 110f, 24f);
            Rect done = new Rect(rect.xMax - 90f, rect.y + 8f, 80f, metrics.ButtonHeight);
            Rect apply = new Rect(done.x - 92f, done.y, 84f, metrics.ButtonHeight);
            Rect preview = new Rect(apply.x - 92f, done.y, 84f, metrics.ButtonHeight);
            Rect status = new Rect(preview.x - 280f, rect.y + 12f, 270f, 24f);
            return new RuleBuilder2HeaderRects(title, name, enabled, preview, apply, done, status);
        }

        internal RuleBuilder2DashboardRects Dashboard(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect title = new Rect(inner.x, inner.y, inner.width, 28f);
            Rect actionsCard = new Rect(inner.x, title.yMax + metrics.SmallGap, inner.width, metrics.DashboardActionsHeight);
            Rect actionsInner = actionsCard.ContractedBy(metrics.InnerPadding);
            Rect[] actions = SpineRectLayout.Horizontal(
                new Rect(actionsInner.x, actionsInner.y, Mathf.Max(0f, actionsInner.width - 78f), metrics.ButtonHeight),
                metrics.Gap,
                SpineLayoutTrack.Fixed(170f),
                SpineLayoutTrack.Fixed(250f),
                SpineLayoutTrack.Fixed(150f),
                SpineLayoutTrack.Flex(1f, 1f));
            Rect more = new Rect(actionsInner.xMax - 70f, actionsInner.y, 70f, metrics.ButtonHeight);
            Rect description = new Rect(actionsInner.x, actionsInner.y + 40f, actionsInner.width, 24f);
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
                description,
                content);
        }

        internal RuleBuilder2RulesTableRects RulesTable(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.InnerPadding);
            Rect header = new Rect(inner.x, inner.y, inner.width, 26f);
            Rect list = new Rect(inner.x, inner.y + 30f, inner.width, Mathf.Max(0f, inner.height - 30f));
            return new RuleBuilder2RulesTableRects(inner, header, list);
        }

        internal RuleBuilder2RuleTableHeaderRects RulesTableHeader(Rect rect)
        {
            Rect enabled = new Rect(rect.x + 8f, rect.y + 4f, 60f, 22f);
            Rect target = new Rect(rect.x + 72f, rect.y + 4f, 190f, 22f);
            Rect conditions = new Rect(rect.x + 270f, rect.y + 4f, 220f, 22f);
            Rect action = new Rect(rect.x + 500f, rect.y + 4f, Mathf.Max(1f, rect.width - 650f), 22f);
            Rect edit = new Rect(rect.xMax - 130f, rect.y + 4f, 120f, 22f);
            return new RuleBuilder2RuleTableHeaderRects(enabled, target, conditions, action, edit);
        }

        internal RuleBuilder2RuleTableRowRects RuleTableRow(Rect row)
        {
            Rect enabled = new Rect(row.x + 8f, row.y + 5f, 24f, 24f);
            Rect target = new Rect(row.x + 44f, row.y + 5f, 210f, 22f);
            Rect conditions = new Rect(row.x + 260f, row.y + 5f, 230f, 22f);
            Rect action = new Rect(row.x + 500f, row.y + 5f, Mathf.Max(1f, row.width - 640f), 22f);
            Rect edit = new Rect(row.xMax - 128f, row.y + 3f, 58f, 24f);
            Rect delete = new Rect(edit.xMax + 8f, edit.y, 58f, 24f);
            Rect select = new Rect(row.x, row.y, Mathf.Max(0f, edit.x - row.x - 4f), row.height);
            return new RuleBuilder2RuleTableRowRects(enabled, target, conditions, action, edit, delete, select);
        }

        internal RuleBuilder2DraftQueueRects DraftQueue(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect title = new Rect(inner.x, inner.y, inner.width, 30f);
            Rect side = new Rect(inner.x, inner.y + 38f, 220f, Mathf.Max(0f, inner.height - 38f));
            Rect main = new Rect(side.xMax + 12f, side.y, Mathf.Max(0f, inner.width - side.width - 12f), side.height);
            return new RuleBuilder2DraftQueueRects(inner, title, side, main);
        }

        internal RuleBuilder2DraftReviewRects DraftReview(Rect rect)
        {
            Rect inner = rect.ContractedBy(12f);
            Rect title = new Rect(inner.x, inner.y, inner.width, 30f);
            Rect notes = new Rect(inner.x, inner.y + 34f, inner.width, 44f);
            Rect sentence = new Rect(inner.x, inner.y + 84f, inner.width, 42f);
            Rect preview = new Rect(inner.x, sentence.yMax + 10f, inner.width, Mathf.Max(0f, inner.height - 176f));
            Rect reject = new Rect(inner.xMax - 250f, inner.yMax - 34f, 74f, metrics.ButtonHeight);
            Rect edit = new Rect(reject.xMax + metrics.Gap, reject.y, 74f, metrics.ButtonHeight);
            Rect accept = new Rect(edit.xMax + metrics.Gap, reject.y, 86f, metrics.ButtonHeight);
            return new RuleBuilder2DraftReviewRects(inner, title, notes, sentence, preview, reject, edit, accept);
        }

        internal float EditorViewHeight(float activePanelHeight)
        {
            return metrics.RuleNameHeight + metrics.Gap + metrics.RuleSentenceHeight + 10f + activePanelHeight + metrics.Gap + metrics.ConfirmHeight + 24f;
        }

        internal float EditorSectionExpandedHeight(RuleBuilder2EditorSection section, bool targetHasSelection)
        {
            switch (section)
            {
                case RuleBuilder2EditorSection.Target:
                    return targetHasSelection ? metrics.TargetSelectedHeight : metrics.TargetPickerHeight;
                case RuleBuilder2EditorSection.Conditions:
                    return metrics.ConditionsPanelHeight;
                case RuleBuilder2EditorSection.Action:
                    return metrics.ActionPanelHeight;
                case RuleBuilder2EditorSection.Preview:
                    return metrics.PreviewPanelHeight;
                default:
                    return metrics.TargetPickerHeight;
            }
        }

        internal float AnimatedEditorSectionHeight(RuleBuilder2EditorSection section, bool targetHasSelection, float openProgress)
        {
            float expandedHeight = EditorSectionExpandedHeight(section, targetHasSelection);
            return Mathf.Lerp(metrics.CollapsedSectionHeight, expandedHeight, SpineEasing.SmoothStep01(openProgress));
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
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            float y = inner.y + metrics.SectionTitleHeight;
            Rect searchOrSummary = new Rect(inner.x, y, inner.width, metrics.TextFieldHeight);
            if (hasTarget)
            {
                Rect summary = new Rect(inner.x, y, Mathf.Max(0f, inner.width - 120f), 26f);
                Rect change = new Rect(inner.xMax - 110f, y - 2f, 100f, metrics.TextFieldHeight);
                Rect hint = new Rect(inner.x, y + 34f, Mathf.Max(0f, inner.width - 150f), 42f);
                Rect next = new Rect(inner.xMax - 140f, y + 36f, 130f, metrics.TextFieldHeight);
                return new RuleBuilder2TargetSectionRects(inner, summary, change, hint, next, Rect.zero, Rect.zero);
            }

            Rect list = new Rect(inner.x, searchOrSummary.yMax + metrics.SmallGap, inner.width, Mathf.Max(0f, inner.yMax - searchOrSummary.yMax - metrics.SmallGap));
            return new RuleBuilder2TargetSectionRects(inner, Rect.zero, Rect.zero, Rect.zero, Rect.zero, searchOrSummary, list);
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
            Rect label = new Rect(rect.x + 34f, rect.y + 6f, Mathf.Max(1f, rect.width - 128f), 22f);
            Rect editor = new Rect(rect.x + 34f, rect.y + 28f, Mathf.Max(1f, rect.width - 128f), 20f);
            Rect up = new Rect(rect.xMax - 86f, rect.y + 6f, 24f, 22f);
            Rect down = new Rect(rect.xMax - 58f, rect.y + 6f, 24f, 22f);
            Rect remove = new Rect(rect.xMax - 30f, rect.y + 6f, 24f, 22f);
            return new RuleBuilder2ConditionRowRects(checkbox, label, editor, up, down, remove);
        }

        internal RuleBuilder2ActionSectionRects ActionSection(Rect rect)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            float y = inner.y + 30f;
            Rect kind = new Rect(inner.x, y, 210f, metrics.TextFieldHeight);
            Rect priority = new Rect(kind.xMax + 14f, y, 150f, metrics.TextFieldHeight);
            Rect next = new Rect(inner.xMax - 130f, y, 120f, metrics.TextFieldHeight);
            Rect body = new Rect(inner.x, y + 38f, inner.width, 72f);
            return new RuleBuilder2ActionSectionRects(inner, kind, priority, next, body);
        }

        internal RuleBuilder2PreviewSectionRects PreviewSection(Rect rect, bool showMatchedPanel)
        {
            Rect inner = rect.ContractedBy(metrics.OuterPadding);
            Rect run = new Rect(inner.x, inner.y + metrics.SectionTitleHeight, 120f, metrics.TextFieldHeight);
            Rect matched = showMatchedPanel
                ? new Rect(inner.x + 130f, run.y, Mathf.Max(0f, inner.width - 130f), 72f)
                : Rect.zero;
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

        internal Rect GridCell(Rect rect, int index, float minWidth, float height, float gap)
        {
            float slotWidth = Mathf.Max(minWidth, (rect.width - gap * 2f) / 4f);
            int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width + gap) / slotWidth));
            int column = index % columns;
            int row = index / columns;
            return new Rect(
                rect.x + column * slotWidth,
                rect.y + row * (height + gap),
                Mathf.Max(1f, slotWidth - gap),
                height);
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
        internal RuleBuilder2HeaderRects(Rect title, Rect name, Rect enabled, Rect preview, Rect apply, Rect done, Rect status)
        {
            Title = title; Name = name; Enabled = enabled; Preview = preview; Apply = apply; Done = done; Status = status;
        }
        internal Rect Title { get; }
        internal Rect Name { get; }
        internal Rect Enabled { get; }
        internal Rect Preview { get; }
        internal Rect Apply { get; }
        internal Rect Done { get; }
        internal Rect Status { get; }
    }

    internal readonly struct RuleBuilder2DashboardRects
    {
        internal RuleBuilder2DashboardRects(Rect inner, Rect title, Rect actionsCard, Rect actionsInner, Rect addRule, Rect generateDraft, Rect import, Rect more, Rect description, Rect content)
        {
            Inner = inner; Title = title; ActionsCard = actionsCard; ActionsInner = actionsInner; AddRule = addRule; GenerateDraft = generateDraft; Import = import; More = more; Description = description; Content = content;
        }
        internal Rect Inner { get; }
        internal Rect Title { get; }
        internal Rect ActionsCard { get; }
        internal Rect ActionsInner { get; }
        internal Rect AddRule { get; }
        internal Rect GenerateDraft { get; }
        internal Rect Import { get; }
        internal Rect More { get; }
        internal Rect Description { get; }
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
        internal RuleBuilder2DraftQueueRects(Rect inner, Rect title, Rect side, Rect main) { Inner = inner; Title = title; Side = side; Main = main; }
        internal Rect Inner { get; }
        internal Rect Title { get; }
        internal Rect Side { get; }
        internal Rect Main { get; }
    }

    internal readonly struct RuleBuilder2DraftReviewRects
    {
        internal RuleBuilder2DraftReviewRects(Rect inner, Rect title, Rect notes, Rect sentence, Rect preview, Rect reject, Rect edit, Rect accept)
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
        internal RuleBuilder2ActionSectionRects(Rect inner, Rect kind, Rect priority, Rect next, Rect body)
        {
            Inner = inner; Kind = kind; Priority = priority; Next = next; Body = body;
        }
        internal Rect Inner { get; }
        internal Rect Kind { get; }
        internal Rect Priority { get; }
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
