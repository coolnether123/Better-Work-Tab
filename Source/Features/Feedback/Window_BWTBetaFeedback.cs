using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Tutorial;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Feedback
{
    /// <summary>
    /// The 2.0 beta feedback portal.
    ///
    /// It began as an end-of-tutorial review, which meant the only thing a
    /// tester could tell us about was the tutorial — the least important part of
    /// the release. The tutorial is now one section among several.
    ///
    /// The layout is a title band, a tab strip, and a single framed panel. Rows
    /// inside the panel are separated by hairlines instead of being boxed
    /// individually, so a page of eleven questions reads as one list rather than
    /// eleven competing dialogs.
    /// </summary>
    internal sealed class Window_BWTBetaFeedback : Window
    {
        private enum Section
        {
            Features,
            Problems,
            Tutorial,
            Build,
            Preview
        }

        private const float HeaderHeight = 50f;
        private const float FooterHeight = 40f;
        private const float PanelPadding = 10f;

        // Where the portal was left. Someone who closed it half way down the
        // problem list to go and reproduce something is coming back to that
        // spot, not to the top of the first tab, and making them navigate there
        // again is a tax on exactly the people filing the most detail.
        //
        // Static rather than saved: it is a view position, not a preference, and
        // a fresh session has no place to return to yet.
        private static Section lastSection = Section.Features;
        private static readonly Dictionary<Section, Vector2> lastScrollPositions = new Dictionary<Section, Vector2>();

        private Section section;
        private string previewText;
        private bool dirty;

        // The environment block reflects over every setting and walks the mod
        // list. That is cheap once and ruinous sixty times a second, and it is a
        // snapshot of the build being reported on, so it is captured when the
        // portal opens and left alone.
        private List<BWTBetaEnvironmentReport.Line> environment;
        private List<string> activeMods;
        private List<string> changedSettings;

        public override Vector2 InitialSize => new Vector2(940f, 800f);

        public Window_BWTBetaFeedback()
        {
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            forcePause = false;

            // The remembered tab may no longer exist — the tutorial one is only
            // offered once there is tutorial history, and clearing the form
            // takes it away again. Returning to a tab that is not in the strip
            // would draw its contents under no selected tab at all.
            section = IsAvailable(lastSection) ? lastSection : Section.Features;
        }

        private static bool IsAvailable(Section candidate)
        {
            return candidate != Section.Tutorial || HasTutorialHistory();
        }

        public override void DoWindowContents(Rect inRect)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                Close();
                return;
            }

            BWTBetaFeedbackStore.Ensure(settings);
            BWTTutorialFeedbackStore.Ensure(settings);

            // Clearing the form takes the tutorial history, and with it the
            // tutorial tab, out from under a window that is already open on it.
            if (!IsAvailable(section))
            {
                Select(Section.Features);
            }

            Rect header = new Rect(inRect.x, inRect.y, inRect.width - 26f, HeaderHeight);
            if (BWTFeedbackWidgets.DrawHeaderBand(
                    header,
                    "BWT_Beta_Title".Translate(),
                    "BWT_Beta_Subtitle".Translate(),
                    "BWT_Beta_CopyDiscord".Translate(),
                    BWTBetaFeedbackButton.Icon))
            {
                CopyDiscordForSmokeTest();
                Messages.Message("BWT_Tutorial_DiscordCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }

            Rect footer = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);
            float panelTop = header.yMax + 10f + TabDrawer.TabHeight;
            Rect panel = new Rect(inRect.x, panelTop, inRect.width, Mathf.Max(1f, footer.y - panelTop - 10f));
            DrawSectionTabs(panel);
            Widgets.DrawMenuSection(panel);

            Rect content = panel.ContractedBy(PanelPadding);
            if (section == Section.Preview)
            {
                DrawPreview(content, settings);
            }
            else
            {
                DrawScrolledSection(content, settings);
            }

            DrawFooter(footer, settings);
        }

        public override void PostClose()
        {
            if (dirty)
            {
                BetterWorkTabMod.Settings?.Write();
            }

            base.PostClose();
        }

        /// <summary>Draws the tab strip that sits immediately above <paramref name="panel"/>.</summary>
        private void DrawSectionTabs(Rect panel)
        {
            var tabs = new List<TabRecord>
            {
                new TabRecord("BWT_Beta_Tab_Features".Translate(), () => Select(Section.Features), section == Section.Features),
                new TabRecord(ProblemsTabLabel(), () => Select(Section.Problems), section == Section.Problems)
            };

            if (HasTutorialHistory())
            {
                tabs.Add(new TabRecord("BWT_Beta_Tab_Tutorial".Translate(), () => Select(Section.Tutorial), section == Section.Tutorial));
            }

            tabs.Add(new TabRecord("BWT_Beta_Tab_Build".Translate(), () => Select(Section.Build), section == Section.Build));
            tabs.Add(new TabRecord("BWT_Beta_Tab_Preview".Translate(), () => Select(Section.Preview), section == Section.Preview));
            TabDrawer.DrawTabs(panel, tabs, panel.width / tabs.Count);
        }

        private void Select(Section next)
        {
            section = next;
            lastSection = next;
            previewText = null;
        }

        private string ProblemsTabLabel()
        {
            int count = BetterWorkTabMod.Settings?.betaProblemReports?.Count(problem => problem.HasResponse) ?? 0;
            return count == 0
                ? "BWT_Beta_Tab_Problems".Translate().ToString()
                : "BWT_Beta_Tab_ProblemsCount".Translate(count).ToString();
        }

        private static bool HasTutorialHistory()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return false;
            }

            return settings.completedTutorialLessonIds.Count > 0 ||
                   settings.skippedTutorialLessonIds.Count > 0 ||
                   settings.tutorialLessonFeedback.Any(item => item.HasResponse);
        }

        private void DrawScrolledSection(Rect content, BetterWorkTabSettings settings)
        {
            float contentHeight;
            switch (section)
            {
                case Section.Features: contentHeight = FeaturesHeight(settings); break;
                case Section.Problems: contentHeight = ProblemsHeight(settings); break;
                case Section.Tutorial: contentHeight = TutorialHeight(settings); break;
                default: contentHeight = BuildHeight(settings); break;
            }

            Vector2 scroll = lastScrollPositions.TryGetValue(section, out Vector2 stored) ? stored : Vector2.zero;
            Rect view = new Rect(0f, 0f, content.width - 18f, Mathf.Max(content.height, contentHeight));
            Widgets.BeginScrollView(content, ref scroll, view);
            lastScrollPositions[section] = scroll;

            bool closeRequested = false;
            switch (section)
            {
                case Section.Features:
                    DrawFeatures(view, settings);
                    break;
                case Section.Problems:
                    DrawProblems(view, settings);
                    break;
                case Section.Tutorial:
                    closeRequested = DrawTutorial(view, settings);
                    break;
                case Section.Build:
                    DrawBuild(view, settings);
                    break;
            }

            Widgets.EndScrollView();
            if (closeRequested)
            {
                Close();
            }
        }

        // -- How is 2.0 going -------------------------------------------------

        private static readonly BWTFeatureVerdict[] Verdicts =
        {
            BWTFeatureVerdict.Great,
            BWTFeatureVerdict.Works,
            BWTFeatureVerdict.Rough,
            BWTFeatureVerdict.Broken,
            BWTFeatureVerdict.NotUsed
        };

        private const float VerdictStripWidth = 340f;
        private const float OverallBoxHeight = 156f;

        private static bool WantsNote(BWTFeatureRating rating)
        {
            // Works is the one answer that explains itself; asking "what
            // happened?" under a row somebody just confirmed as fine is a prompt
            // with no answer. Every other answer has something worth hearing,
            // including "Didn't use" — why a tester skipped a feature is a
            // finding about the feature.
            return rating.reviewed && rating.verdict != BWTFeatureVerdict.Works;
        }

        /// <summary>
        /// The question the note box asks. "Didn't use" is asking about an
        /// absence, so "what happened?" would be the wrong question.
        /// </summary>
        private static string NotePlaceholder(BWTFeatureRating rating)
        {
            return rating.verdict == BWTFeatureVerdict.NotUsed
                ? "BWT_Beta_NotePlaceholderNotUsed".Translate()
                : "BWT_Beta_NotePlaceholder".Translate();
        }

        private static float FeatureRowHeight(BWTFeatureRating rating)
        {
            return WantsNote(rating)
                ? BWTFeedbackWidgets.TallRowHeight + 30f
                : BWTFeedbackWidgets.TallRowHeight;
        }

        private static float FeaturesHeight(BetterWorkTabSettings settings)
        {
            float height = BWTFeedbackWidgets.SectionHeadingHeight + OverallBoxHeight + 20f;
            foreach (BWTBetaFeature feature in BWTBetaFeatureCatalog.Relevant)
            {
                height += FeatureRowHeight(BWTBetaFeedbackStore.GetOrCreateRating(settings, feature.Id));
            }

            return height;
        }

        private void DrawFeatures(Rect view, BetterWorkTabSettings settings)
        {
            float y = 0f;
            BWTFeedbackWidgets.SectionHeading(
                new Rect(0f, y, view.width, BWTFeedbackWidgets.SectionHeadingHeight),
                "BWT_Beta_Heading_Features".Translate(),
                "BWT_Beta_Heading_FeaturesHint".Translate());
            y += BWTFeedbackWidgets.SectionHeadingHeight;

            foreach (BWTBetaFeature feature in BWTBetaFeatureCatalog.Relevant)
            {
                BWTFeatureRating rating = BWTBetaFeedbackStore.GetOrCreateRating(settings, feature.Id);
                float height = FeatureRowHeight(rating);
                Rect row = new Rect(0f, y, view.width, height);
                Widgets.DrawHighlightIfMouseover(row);

                Rect label = new Rect(row.x + 6f, row.y + 3f, row.width - VerdictStripWidth - 20f, 22f);
                Widgets.Label(label, feature.LabelKey.Translate());
                Text.Font = GameFont.Tiny;
                GUI.color = BWTFeedbackWidgets.Dim;
                Widgets.Label(new Rect(label.x, row.y + 22f, label.width, 20f), feature.HintKey.Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                int selected = Array.IndexOf(Verdicts, rating.verdict);
                int chosen = BWTFeedbackWidgets.DrawSegmented(
                    new Rect(row.xMax - VerdictStripWidth - 6f, row.y + 9f, VerdictStripWidth, 26f),
                    VerdictLabels,
                    selected,
                    out bool clicked,
                    allowDeselect: false,
                    confirmed: rating.reviewed);

                // Any click is the tester speaking, including clicking the
                // pre-selected Works — that is how someone confirms the default
                // rather than merely leaving it alone. It has to be an actual
                // click: the control returns the current selection on every
                // quiet frame too.
                if (clicked && chosen >= 0)
                {
                    BWTFeatureVerdict previous = rating.verdict;
                    rating.verdict = Verdicts[chosen];
                    rating.reviewed = true;

                    // A note answers the question it was asked under. Works asks
                    // nothing, and "Didn't use" asks a different question from
                    // the rest, so crossing either boundary drops the text
                    // rather than filing an answer against the wrong question.
                    // Moving between Great, Rough and Broken keeps it: the
                    // question has not changed.
                    if (!WantsNote(rating) ||
                        (previous == BWTFeatureVerdict.NotUsed) != (rating.verdict == BWTFeatureVerdict.NotUsed))
                    {
                        rating.note = string.Empty;
                    }

                    dirty = true;
                }

                if (WantsNote(rating))
                {
                    Rect noteRect = new Rect(row.x + 6f, row.y + BWTFeedbackWidgets.TallRowHeight - 2f, row.width - 12f, 26f);
                    string note = BWTFeedbackWidgets.TextFieldWithPlaceholder(
                        noteRect, rating.note, NotePlaceholder(rating));
                    if (!string.Equals(note, rating.note, StringComparison.Ordinal))
                    {
                        rating.note = note;
                        dirty = true;
                    }
                }

                BWTFeedbackWidgets.HairlineUnder(row);
                y += height;
            }

            // The ratings are a row of buttons and will never carry the one thing
            // a tester actually wanted to say, so the open box follows them
            // rather than hiding behind another tab.
            y += 10f;
            BWTFeedbackWidgets.SectionHeading(
                new Rect(0f, y, view.width, BWTFeedbackWidgets.SectionHeadingHeight),
                "BWT_Beta_Overall".Translate());
            y += BWTFeedbackWidgets.SectionHeadingHeight + 6f;

            Rect overallRect = new Rect(0f, y, view.width, OverallBoxHeight - 40f);
            string overall = BWTFeedbackWidgets.TextFieldWithPlaceholder(
                overallRect, settings.betaOverallFeedback, "BWT_Beta_OverallPlaceholder".Translate(), true);
            if (!string.Equals(overall, settings.betaOverallFeedback, StringComparison.Ordinal))
            {
                settings.betaOverallFeedback = overall;
                dirty = true;
            }
        }

        private static string[] verdictLabels;

        private static string[] VerdictLabels =>
            verdictLabels ??= Verdicts.Select(verdict => ("BWT_Beta_Verdict_" + verdict).Translate().ToString()).ToArray();

        // -- Report a problem -------------------------------------------------

        private static readonly BWTProblemSeverity[] Severities =
        {
            BWTProblemSeverity.Cosmetic,
            BWTProblemSeverity.Annoying,
            BWTProblemSeverity.Blocking,
            BWTProblemSeverity.Crash
        };

        private static string[] severityLabels;

        private static string[] SeverityLabels =>
            severityLabels ??= Severities.Select(severity => ("BWT_Beta_Severity_" + severity).Translate().ToString()).ToArray();

        private const float ProblemBlockHeight = 128f;

        private static float ProblemsHeight(BetterWorkTabSettings settings)
        {
            return BWTFeedbackWidgets.SectionHeadingHeight + 6f +
                   (settings.betaProblemReports.Count * (ProblemBlockHeight + 6f)) + 60f;
        }

        private void DrawProblems(Rect view, BetterWorkTabSettings settings)
        {
            float y = 0f;
            bool full = settings.betaProblemReports.Count >= BWTBetaFeedbackStore.MaxProblemReports;
            BWTFeedbackWidgets.SectionHeading(
                new Rect(0f, y, view.width - 190f, BWTFeedbackWidgets.SectionHeadingHeight),
                "BWT_Beta_Heading_Problems".Translate(),
                full ? "BWT_Beta_ProblemLimit".Translate(BWTBetaFeedbackStore.MaxProblemReports).ToString() : null);

            if (BWTFeedbackWidgets.MiniButton(new Rect(view.width - 184f, y - 2f, 184f, 26f), "BWT_Beta_AddProblem".Translate()) &&
                !full)
            {
                BWTBetaFeedbackStore.AddProblem(settings);
                dirty = true;
            }

            y += BWTFeedbackWidgets.SectionHeadingHeight + 6f;

            BWTProblemReport removing = null;
            for (int i = 0; i < settings.betaProblemReports.Count; i++)
            {
                BWTProblemReport problem = settings.betaProblemReports[i];
                Rect block = new Rect(0f, y, view.width, ProblemBlockHeight);

                Rect head = new Rect(block.x + 6f, block.y + 4f, block.width - 12f, 26f);
                Text.Font = GameFont.Tiny;
                GUI.color = BWTFeedbackWidgets.Dim;
                Widgets.Label(new Rect(head.x, head.y + 4f, 20f, 20f), (i + 1).ToString());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                if (BWTFeedbackWidgets.MiniButton(
                        new Rect(head.x + 22f, head.y, 210f, 26f),
                        BWTBetaFeatureCatalog.AreaLabel(problem.areaId)))
                {
                    Find.WindowStack.Add(new FloatMenu(AreaOptions(problem)));
                }

                int selectedSeverity = Array.IndexOf(Severities, problem.severity);
                int chosenSeverity = BWTFeedbackWidgets.DrawSegmented(
                    new Rect(head.x + 240f, head.y, Mathf.Min(300f, head.xMax - head.x - 240f - 30f), 26f),
                    SeverityLabels,
                    selectedSeverity);
                if (chosenSeverity != selectedSeverity)
                {
                    problem.severity = chosenSeverity < 0 ? BWTProblemSeverity.Unset : Severities[chosenSeverity];
                    dirty = true;
                }

                if (Widgets.ButtonImage(new Rect(head.xMax - 22f, head.y + 2f, 22f, 22f), TexButton.Delete))
                {
                    removing = problem;
                }

                Rect textRect = new Rect(block.x + 6f, head.yMax + 4f, block.width - 12f, block.height - head.height - 16f);
                string text = BWTFeedbackWidgets.TextFieldWithPlaceholder(
                    textRect, problem.text, "BWT_Beta_ProblemPlaceholder".Translate(), true);
                if (!string.Equals(text, problem.text, StringComparison.Ordinal))
                {
                    problem.text = text;
                    dirty = true;
                }

                BWTFeedbackWidgets.HairlineUnder(block);
                y += block.height + 6f;
            }

            if (removing != null)
            {
                BWTBetaFeedbackStore.RemoveProblem(settings, removing);
                dirty = true;
            }

            if (settings.betaProblemReports.Count == 0)
            {
                BWTFeedbackWidgets.Placeholder(new Rect(0f, y + 12f, view.width, 44f), "BWT_Beta_NoProblems".Translate());
            }
        }

        private List<FloatMenuOption> AreaOptions(BWTProblemReport problem)
        {
            var options = BWTBetaFeatureCatalog.Relevant
                .Select(feature => new FloatMenuOption(feature.LabelKey.Translate(), () =>
                {
                    problem.areaId = feature.Id;
                    dirty = true;
                }))
                .ToList();
            options.Add(new FloatMenuOption("BWT_Beta_Area_Other".Translate(), () =>
            {
                problem.areaId = BWTBetaFeatureCatalog.OtherAreaId;
                dirty = true;
            }));
            return options;
        }

        // -- Tutorial ---------------------------------------------------------

        private const float LessonRowHeight = 108f;

        private static float TutorialHeight(BetterWorkTabSettings settings)
        {
            return BWTFeedbackWidgets.SectionHeadingHeight +
                   (BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse).Count() * LessonRowHeight);
        }

        private static string[] lessonValueLabels;
        private static string[] behaviorLabels;

        private bool DrawTutorial(Rect view, BetterWorkTabSettings settings)
        {
            lessonValueLabels ??= new[]
            {
                "BWT_Tutorial_FeedbackKeep".Translate().ToString(),
                "BWT_Tutorial_FeedbackRevise".Translate().ToString(),
                "BWT_Tutorial_FeedbackNoLesson".Translate().ToString()
            };
            behaviorLabels ??= new[]
            {
                "Yes".Translate().ToString(),
                "No".Translate().ToString(),
                "BWT_Tutorial_NotSure".Translate().ToString()
            };

            float y = 0f;
            BWTFeedbackWidgets.SectionHeading(
                new Rect(0f, y, view.width, BWTFeedbackWidgets.SectionHeadingHeight),
                "BWT_Beta_Heading_Tutorial".Translate());
            y += BWTFeedbackWidgets.SectionHeadingHeight;

            foreach (BWTTutorialLessonDefinition lesson in BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse))
            {
                bool completed = settings.completedTutorialLessonIds.Contains(lesson.Id);
                bool skipped = settings.skippedTutorialLessonIds.Contains(lesson.Id);
                string status = completed
                    ? "BWT_Tutorial_StatusCompleted".Translate()
                    : skipped ? "BWT_Tutorial_StatusSkipped".Translate() : "BWT_Tutorial_StatusIncomplete".Translate();
                BWTTutorialLessonFeedback response = BWTTutorialFeedbackStore.GetOrCreate(settings, lesson.Id);

                Rect row = new Rect(0f, y, view.width, LessonRowHeight);
                Widgets.DrawHighlightIfMouseover(row);

                Widgets.Label(new Rect(row.x + 6f, row.y + 4f, row.width * 0.5f, 24f),
                    lesson.FeedbackLabelKey.Translate());
                Text.Font = GameFont.Tiny;
                GUI.color = BWTFeedbackWidgets.Dim;
                Widgets.Label(new Rect(row.x + 6f, row.y + 24f, row.width * 0.5f, 20f),
                    lesson.VersionIntroduced + " · " + status);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                if (BWTFeedbackWidgets.MiniButton(new Rect(row.xMax - 146f, row.y + 6f, 140f, 26f),
                        "BWT_Tutorial_GoToLesson".Translate()))
                {
                    return BWTGeneralTutorial.OpenLessonFromReview(lesson.Id);
                }

                float promptWidth = 190f;
                float stripWidth = Mathf.Min(340f, row.width - promptWidth - 24f);
                Rect valuePrompt = new Rect(row.x + 6f, row.y + 48f, promptWidth, 26f);
                Text.Font = GameFont.Tiny;
                GUI.color = BWTFeedbackWidgets.Dim;
                Widgets.Label(new Rect(valuePrompt.x, valuePrompt.y + 4f, promptWidth, 22f),
                    "BWT_Tutorial_LessonValuePrompt".Translate());
                Widgets.Label(new Rect(valuePrompt.x, valuePrompt.y + 32f, promptWidth, 22f),
                    "BWT_Tutorial_BehaviorPrompt".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                int lessonSelected = (int)response.lessonValue - 1;
                int lessonChosen = BWTFeedbackWidgets.DrawSegmented(
                    new Rect(row.x + promptWidth + 12f, valuePrompt.y, stripWidth, 24f),
                    lessonValueLabels,
                    lessonSelected);
                if (lessonChosen != lessonSelected)
                {
                    response.lessonValue = lessonChosen < 0
                        ? BWTTutorialLessonFeedbackValue.Unanswered
                        : (BWTTutorialLessonFeedbackValue)(lessonChosen + 1);
                    dirty = true;
                }

                int behaviorSelected = (int)response.behaviorValue - 1;
                int behaviorChosen = BWTFeedbackWidgets.DrawSegmented(
                    new Rect(row.x + promptWidth + 12f, valuePrompt.y + 28f, stripWidth, 24f),
                    behaviorLabels,
                    behaviorSelected);
                if (behaviorChosen != behaviorSelected)
                {
                    response.behaviorValue = behaviorChosen < 0
                        ? BWTTutorialBehaviorFeedbackValue.Unanswered
                        : (BWTTutorialBehaviorFeedbackValue)(behaviorChosen + 1);
                    dirty = true;
                }

                Rect noteRect = new Rect(row.x + promptWidth + stripWidth + 22f, valuePrompt.y,
                    Mathf.Max(60f, row.xMax - (row.x + promptWidth + stripWidth + 28f)), 52f);
                string note = BWTFeedbackWidgets.TextFieldWithPlaceholder(
                    noteRect, response.note, "BWT_Tutorial_OptionalNote".Translate(), true);
                if (!string.Equals(note, response.note, StringComparison.Ordinal))
                {
                    response.note = note;
                    dirty = true;
                }

                BWTFeedbackWidgets.HairlineUnder(row);
                y += LessonRowHeight;
            }

            return false;
        }

        // -- This build -------------------------------------------------------

        private List<BWTBetaEnvironmentReport.Line> Environment =>
            environment ??= BWTBetaEnvironmentReport.Summary();

        private List<string> ActiveMods => activeMods ??= BWTBetaEnvironmentReport.ActiveMods();

        private List<string> ChangedSettings =>
            changedSettings ??= BWTBetaEnvironmentReport.ChangedSettings(BetterWorkTabMod.Settings);

        private float BuildHeight(BetterWorkTabSettings settings)
        {
            return (BWTFeedbackWidgets.SectionHeadingHeight * 3f) +
                   ((Environment.Count + 1) * BWTFeedbackWidgets.RowHeight) +
                   (ActiveMods.Count * BWTFeedbackWidgets.RowHeight) +
                   (Mathf.Max(1, ChangedSettings.Count) * BWTFeedbackWidgets.RowHeight) + 40f;
        }

        private void DrawBuild(Rect view, BetterWorkTabSettings settings)
        {
            float y = 0f;
            BWTFeedbackWidgets.SectionHeading(
                new Rect(0f, y, view.width, BWTFeedbackWidgets.SectionHeadingHeight),
                "BWT_Beta_Heading_Build".Translate());
            y += BWTFeedbackWidgets.SectionHeadingHeight;

            // The handle belongs to the report rather than to any one answer, so
            // it sits with the other things the report says about the tester.
            Rect handleRow = new Rect(0f, y, view.width, BWTFeedbackWidgets.RowHeight);
            Widgets.Label(new Rect(handleRow.x + 6f, handleRow.y + 7f, handleRow.width * 0.42f, 24f),
                "BWT_Beta_Handle".Translate());
            string handle = BWTFeedbackWidgets.TextFieldWithPlaceholder(
                new Rect(handleRow.x + (handleRow.width * 0.42f), handleRow.y + 4f, handleRow.width * 0.58f - 6f, 26f),
                settings.betaTesterHandle,
                "BWT_Beta_HandlePlaceholder".Translate());
            if (!string.Equals(handle, settings.betaTesterHandle, StringComparison.Ordinal))
            {
                settings.betaTesterHandle = handle;
                dirty = true;
            }

            BWTFeedbackWidgets.HairlineUnder(handleRow);
            y += BWTFeedbackWidgets.RowHeight;

            foreach (BWTBetaEnvironmentReport.Line line in Environment)
            {
                BWTFeedbackWidgets.FactRow(
                    new Rect(0f, y, view.width, BWTFeedbackWidgets.RowHeight),
                    line.Label,
                    line.Value,
                    line.Label + ": " + line.Value);
                y += BWTFeedbackWidgets.RowHeight;
            }

            y += 10f;
            BWTFeedbackWidgets.SectionHeading(
                new Rect(0f, y, view.width, BWTFeedbackWidgets.SectionHeadingHeight),
                "BWT_Beta_Heading_Mods".Translate(),
                "BWT_Beta_Env_ModsValue".Translate(ActiveMods.Count).ToString());
            y += BWTFeedbackWidgets.SectionHeadingHeight;

            for (int i = 0; i < ActiveMods.Count; i++)
            {
                BWTFeedbackWidgets.ListRow(
                    new Rect(0f, y, view.width, BWTFeedbackWidgets.RowHeight),
                    (i + 1).ToString(),
                    ActiveMods[i],
                    null,
                    null);
                y += BWTFeedbackWidgets.RowHeight;
            }

            y += 10f;
            BWTFeedbackWidgets.SectionHeading(
                new Rect(0f, y, view.width, BWTFeedbackWidgets.SectionHeadingHeight),
                "BWT_Beta_Heading_Settings".Translate(),
                "BWT_Beta_Env_ChangedValue".Translate(ChangedSettings.Count).ToString());
            y += BWTFeedbackWidgets.SectionHeadingHeight;

            if (ChangedSettings.Count == 0)
            {
                BWTFeedbackWidgets.Placeholder(
                    new Rect(0f, y + 6f, view.width, BWTFeedbackWidgets.RowHeight),
                    "BWT_Beta_SettingsAllDefault".Translate());
                return;
            }

            foreach (string entry in ChangedSettings)
            {
                int split = entry.IndexOf('=');
                string name = split > 0 ? entry.Substring(0, split) : entry;
                string value = split > 0 ? entry.Substring(split + 1) : string.Empty;
                BWTFeedbackWidgets.FactRow(new Rect(0f, y, view.width, BWTFeedbackWidgets.RowHeight), name, value);
                y += BWTFeedbackWidgets.RowHeight;
            }
        }

        // -- What gets copied -------------------------------------------------

        private static Vector2 previewScrollPosition;

        /// <summary>
        /// Shows exactly what the Copy buttons put on the clipboard. A report
        /// that lists somebody's mods should be readable before it is sent, not
        /// after.
        /// </summary>
        private void DrawPreview(Rect content, BetterWorkTabSettings settings)
        {
            previewText ??= BWTBetaReportFormatter.FormatFull(settings);

            Text.Font = GameFont.Tiny;
            float height = Mathf.Max(content.height, Text.CalcHeight(previewText, content.width - 24f) + 12f);
            Rect view = new Rect(0f, 0f, content.width - 18f, height);
            Widgets.BeginScrollView(content, ref previewScrollPosition, view);
            Widgets.Label(new Rect(4f, 4f, view.width - 8f, height), previewText);
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }

        // -- Footer -----------------------------------------------------------

        private void DrawFooter(Rect footer, BetterWorkTabSettings settings)
        {
            if (Widgets.ButtonText(new Rect(footer.x, footer.y + 4f, 220f, 32f), "BWT_Beta_CopyFull".Translate()))
            {
                CopyFullForSmokeTest();
                Messages.Message("BWT_Tutorial_FullCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }

            if (Widgets.ButtonText(new Rect(footer.xMax - 340f, footer.y + 4f, 160f, 32f), "BWT_Beta_Clear".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "BWT_Beta_ClearConfirm".Translate(),
                    () =>
                    {
                        ClearFeedbackForSmokeTest();
                        settings.Write();
                    },
                    true));
            }

            if (Widgets.ButtonText(new Rect(footer.xMax - 170f, footer.y + 4f, 170f, 32f), "Close".Translate()))
            {
                Close();
            }
        }

        // -- Smoke-test surface -----------------------------------------------

        internal void PopulateFeedbackForSmokeTest()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            int index = 0;
            foreach (BWTTutorialLessonDefinition lesson in BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse))
            {
                BWTTutorialLessonFeedback response = BWTTutorialFeedbackStore.GetOrCreate(settings, lesson.Id);
                response.lessonValue = (BWTTutorialLessonFeedbackValue)((index % 3) + 1);
                response.behaviorValue = (BWTTutorialBehaviorFeedbackValue)((index % 3) + 1);
                response.note = index % 2 == 0 ? "Smoke-test note " + lesson.Id : string.Empty;
                index++;
            }

            index = 0;
            foreach (BWTBetaFeature feature in BWTBetaFeatureCatalog.Relevant)
            {
                BWTFeatureRating rating = BWTBetaFeedbackStore.GetOrCreateRating(settings, feature.Id);
                rating.verdict = Verdicts[index % Verdicts.Length];
                rating.reviewed = true;
                rating.note = index % 3 == 0 ? "Smoke-test note " + feature.Id : string.Empty;
                index++;
            }

            BWTProblemReport problem = BWTBetaFeedbackStore.AddProblem(settings);
            if (problem != null)
            {
                problem.areaId = "worktab";
                problem.severity = BWTProblemSeverity.Annoying;
                problem.text = "Smoke-test problem report.";
            }

            settings.betaOverallFeedback = "Automated 2.0 beta feedback smoke test.";
            settings.tutorialOverallFeedback = settings.betaOverallFeedback;
            dirty = true;
            settings.Write();
        }

        internal string CopyDiscordForSmokeTest()
        {
            string report = BWTBetaReportFormatter.FormatDiscord(BetterWorkTabMod.Settings);
            GUIUtility.systemCopyBuffer = report;
            return report;
        }

        internal string CopyFullForSmokeTest()
        {
            string report = BWTBetaReportFormatter.FormatFull(BetterWorkTabMod.Settings);
            GUIUtility.systemCopyBuffer = report;
            return report;
        }

        internal void ClearFeedbackForSmokeTest()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            BWTTutorialFeedbackStore.Clear(settings);
            BWTBetaFeedbackStore.Clear(settings);
            previewText = null;
            dirty = false;
        }
    }
}
