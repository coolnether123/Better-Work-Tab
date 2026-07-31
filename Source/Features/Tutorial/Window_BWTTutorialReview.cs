using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal sealed class Window_BWTTutorialReview : Window
    {
        private Vector2 scrollPosition;
        private bool dirty;

        public override Vector2 InitialSize => new Vector2(900f, 760f);

        public Window_BWTTutorialReview()
        {
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                Close();
                return;
            }

            BWTTutorialFeedbackStore.Ensure(settings);
            Text.Font = GameFont.Medium;
            string title = "BWT_Tutorial_Review_Title".Translate();
            float titleHeight = Mathf.Max(32f, Text.CalcHeight(title, inRect.width));
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleHeight), title);
            Text.Font = GameFont.Small;
            string introduction = "BWT_Tutorial_Review_Body".Translate();
            float introductionY = inRect.y + titleHeight + 2f;
            float introductionHeight = Mathf.Max(42f, Text.CalcHeight(introduction, inRect.width));
            Widgets.Label(
                new Rect(inRect.x, introductionY, inRect.width, introductionHeight),
                introduction);

            Rect footer = new Rect(inRect.x, inRect.yMax - 42f, inRect.width, 42f);
            float viewportY = introductionY + introductionHeight + 6f;
            Rect viewport = new Rect(
                inRect.x,
                viewportY,
                inRect.width,
                Mathf.Max(1f, footer.y - viewportY - 10f));
            float contentHeight = CalculateContentHeight(settings);
            Rect view = new Rect(0f, 0f, viewport.width - 18f, contentHeight);
            Widgets.BeginScrollView(viewport, ref scrollPosition, view);
            DrawReview(view, settings);
            Widgets.EndScrollView();

            float buttonWidth = (footer.width - 24f) / 4f;
            if (Widgets.ButtonText(new Rect(footer.x, footer.y + 5f, buttonWidth, 34f),
                    "BWT_Tutorial_CopyDiscord".Translate()))
            {
                CopyDiscordForSmokeTest();
                Messages.Message("BWT_Tutorial_DiscordCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            if (Widgets.ButtonText(new Rect(footer.x + buttonWidth + 8f, footer.y + 5f, buttonWidth, 34f),
                    "BWT_Tutorial_CopyFull".Translate()))
            {
                CopyFullForSmokeTest();
                Messages.Message("BWT_Tutorial_FullCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            if (Widgets.ButtonText(new Rect(footer.x + (buttonWidth + 8f) * 2f, footer.y + 5f, buttonWidth, 34f),
                    "BWT_Tutorial_ClearFeedback".Translate()))
            {
                ClearFeedbackForSmokeTest();
            }
            if (Widgets.ButtonText(new Rect(footer.x + (buttonWidth + 8f) * 3f, footer.y + 5f, buttonWidth, 34f),
                    "Close".Translate()))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            if (dirty)
            {
                BetterWorkTabMod.Settings?.Write();
            }
            base.PostClose();
        }

        internal void PopulateFeedbackForSmokeTest()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            int index = 0;
            foreach (BWTTutorialLessonDefinition lesson in BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse))
            {
                BWTTutorialLessonFeedback response = BWTTutorialFeedbackStore.GetOrCreate(settings, lesson.Id);
                response.lessonValue = (BWTTutorialLessonFeedbackValue)(index % 3 + 1);
                response.behaviorValue = (BWTTutorialBehaviorFeedbackValue)(index % 3 + 1);
                response.note = index % 2 == 0 ? "Smoke-test note " + lesson.Id : string.Empty;
                index++;
            }
            settings.tutorialOverallFeedback = "Automated beta tutorial feedback smoke test.";
            dirty = true;
            settings.Write();
        }

        internal string CopyDiscordForSmokeTest()
        {
            string report = BWTTutorialReportFormatter.FormatDiscord(BetterWorkTabMod.Settings);
            GUIUtility.systemCopyBuffer = report;
            return report;
        }

        internal string CopyFullForSmokeTest()
        {
            string report = BWTTutorialReportFormatter.FormatFull(BetterWorkTabMod.Settings);
            GUIUtility.systemCopyBuffer = report;
            return report;
        }

        internal void ClearFeedbackForSmokeTest()
        {
            BWTTutorialFeedbackStore.Clear(BetterWorkTabMod.Settings);
            dirty = false;
        }

        private void DrawReview(Rect view, BetterWorkTabSettings settings)
        {
            float y = 0f;
            foreach (BWTTutorialLessonDefinition lesson in BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse))
            {
                bool completed = settings.completedTutorialLessonIds.Contains(lesson.Id);
                bool skipped = settings.skippedTutorialLessonIds.Contains(lesson.Id);
                string status = completed
                    ? "BWT_Tutorial_StatusCompleted".Translate()
                    : skipped ? "BWT_Tutorial_StatusSkipped".Translate() : "BWT_Tutorial_StatusIncomplete".Translate();
                BWTTutorialLessonFeedback response = BWTTutorialFeedbackStore.GetOrCreate(settings, lesson.Id);

                Rect card = new Rect(0f, y, view.width, 174f);
                Widgets.DrawBoxSolid(card, new Color(0.09f, 0.10f, 0.11f, 0.7f));
                Widgets.DrawBox(card, 1);
                Rect inner = card.ContractedBy(10f);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(inner.x, inner.y, inner.width - 150f, 26f),
                    (lesson.FeedbackLabelKey.Translate() + "  [" + lesson.VersionIntroduced + "]"));
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(inner.xMax - 145f, inner.y, 145f, 26f), status);
                Text.Anchor = TextAnchor.UpperLeft;

                float choiceY = inner.y + 31f;
                DrawChoiceRow(
                    new Rect(inner.x, choiceY, inner.width, 28f),
                    "BWT_Tutorial_LessonValuePrompt".Translate(),
                    new[]
                    {
                        "BWT_Tutorial_FeedbackKeep".Translate().ToString(),
                        "BWT_Tutorial_FeedbackRevise".Translate().ToString(),
                        "BWT_Tutorial_FeedbackNoLesson".Translate().ToString()
                    },
                    (int)response.lessonValue - 1,
                    selected =>
                    {
                        response.lessonValue = (BWTTutorialLessonFeedbackValue)(selected + 1);
                        dirty = true;
                    });
                DrawChoiceRow(
                    new Rect(inner.x, choiceY + 34f, inner.width, 28f),
                    "BWT_Tutorial_BehaviorPrompt".Translate(),
                    new[] { "Yes".Translate().ToString(), "No".Translate().ToString(), "BWT_Tutorial_NotSure".Translate().ToString() },
                    (int)response.behaviorValue - 1,
                    selected =>
                    {
                        response.behaviorValue = (BWTTutorialBehaviorFeedbackValue)(selected + 1);
                        dirty = true;
                    });

                Widgets.Label(new Rect(inner.x, choiceY + 70f, 110f, 28f), "BWT_Tutorial_OptionalNote".Translate());
                string note = Widgets.TextField(new Rect(inner.x + 116f, choiceY + 68f, inner.width - 116f, 30f), response.note ?? string.Empty);
                if (!string.Equals(note, response.note, StringComparison.Ordinal))
                {
                    response.note = note;
                    dirty = true;
                }

                y += card.height + 10f;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y + 4f, view.width, 30f), "BWT_Tutorial_OverallFeedback".Translate());
            Text.Font = GameFont.Small;
            string overall = Widgets.TextArea(new Rect(0f, y + 38f, view.width, 110f), settings.tutorialOverallFeedback ?? string.Empty);
            if (!string.Equals(overall, settings.tutorialOverallFeedback, StringComparison.Ordinal))
            {
                settings.tutorialOverallFeedback = overall;
                dirty = true;
            }
        }

        private static void DrawChoiceRow(Rect rect, string prompt, string[] labels, int selected, Action<int> onSelect)
        {
            const float promptWidth = 190f;
            Widgets.Label(new Rect(rect.x, rect.y + 4f, promptWidth, rect.height), prompt);
            float width = (rect.width - promptWidth - 12f) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect button = new Rect(rect.x + promptWidth + 12f + i * width, rect.y, width - 6f, rect.height);
                Color old = GUI.color;
                if (selected == i) GUI.color = new Color(1f, 0.84f, 0.35f);
                if (Widgets.ButtonText(button, labels[i])) onSelect(i);
                GUI.color = old;
            }
        }

        private static float CalculateContentHeight(BetterWorkTabSettings settings)
        {
            int count = BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse).Count();
            return count * 184f + 170f;
        }
    }
}
