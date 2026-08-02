using System.Collections.Generic;
using Better_Work_Tab.Features.Tutorial;
using RimWorld;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal enum RuleBuilder2TutorialStep
    {
        Welcome,
        BlankRuleset,
        RuleDeck,
        Target,
        SubWorkTarget,
        Condition,
        Action,
        Schedule,
        Preview,
        Confirm,
        MatchedConditions,
        ReplaySettings,
        Complete
    }

    internal sealed class RuleBuilder2TutorialController
    {
        private const string SuggestionsHintSettingId = "bwt.ruleBuilder2.suggestionsHint.v1";
        private readonly TutorialTextViewport bodyViewport = new TutorialTextViewport();
        private readonly TutorialOverlayStyle cardStyle = new TutorialOverlayStyle();
        private bool suggestionsHintRequested;

        private enum HintKind
        {
            None,
            FirstOpen,
            Suggestions
        }

        internal bool IsActive => GetActiveHint() != HintKind.None;

        internal RuleBuilder2TutorialStep ActiveStep =>
            GetActiveHint() == HintKind.Suggestions
                ? RuleBuilder2TutorialStep.RuleDeck
                : GetFirstOpenActive()
                    ? RuleBuilder2TutorialStep.Welcome
                    : RuleBuilder2TutorialStep.Complete;

        internal void RequestSuggestionsHint()
        {
            if (!HasSeenSuggestionsHint())
            {
                suggestionsHintRequested = true;
            }
        }

        internal bool TryHandleInput(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects, Event evt)
        {
            HintKind hint = GetActiveHint();
            if (hint == HintKind.None || evt == null)
            {
                return false;
            }

            Rect card = GetCardRect(bounds, focusRects, hint);
            bool overCard = card.Contains(evt.mousePosition);
            if (overCard && bodyViewport.TryHandleScroll(GetBodyRect(card), GetBody(hint), evt))
            {
                return true;
            }

            if (evt.type == EventType.KeyDown &&
                (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                Dismiss(hint);
                evt.Use();
                return true;
            }

            if (!overCard)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (TryGetButtonAt(card, hint, evt.mousePosition, out TutorialButton button))
                {
                    if (button == TutorialButton.Settings)
                    {
                        OpenSettings();
                    }
                    else
                    {
                        Dismiss(hint);
                    }
                }

                evt.Use();
                return true;
            }

            return evt.type == EventType.MouseDrag || evt.type == EventType.ScrollWheel;
        }

        internal void Draw(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects)
        {
            HintKind hint = GetActiveHint();
            if (hint == HintKind.None)
            {
                return;
            }

            Rect card = GetCardRect(bounds, focusRects, hint);
            DrawDimOutside(card, bounds);
            DrawCard(card, hint);
            BWTTutorialGestureDemo.DrawExternal(
                "rule-builder-" + hint,
                GetDismissButtonRect(card, hint),
                BWTTutorialGestureDemo.GestureKind.LeftClick,
                T("BWT_Tutorial_Gesture_Continue"));
        }

        internal void ObserveTargetSelected(bool subWork)
        {
        }

        internal void ObserveConditionAdded()
        {
        }

        internal void ObserveActionEdited()
        {
        }

        internal void ObservePreview()
        {
        }

        internal void ObserveConfirmed()
        {
        }

        internal void Reset()
        {
            if (BetterWorkTabMod.Settings == null)
            {
                return;
            }

            BetterWorkTabMod.Settings.showRuleBuilder2Tutorial = true;
            SetStep(RuleBuilder2TutorialStep.Welcome);
        }

        internal void ResetOverlayAnimation()
        {
            bodyViewport.Reset();
        }

        private HintKind GetActiveHint()
        {
            if (GetFirstOpenActive())
            {
                return HintKind.FirstOpen;
            }

            return suggestionsHintRequested && !HasSeenSuggestionsHint()
                ? HintKind.Suggestions
                : HintKind.None;
        }

        private static bool GetFirstOpenActive()
        {
            return BetterWorkTabMod.Settings?.showRuleBuilder2Tutorial == true &&
                   CurrentStep != RuleBuilder2TutorialStep.Complete;
        }

        private static RuleBuilder2TutorialStep CurrentStep
        {
            get
            {
                int raw = BetterWorkTabMod.Settings?.ruleBuilder2TutorialStep ?? 0;
                return System.Enum.IsDefined(typeof(RuleBuilder2TutorialStep), raw)
                    ? (RuleBuilder2TutorialStep)raw
                    : RuleBuilder2TutorialStep.Welcome;
            }
        }

        private static Rect GetCardRect(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects, HintKind hint)
        {
            RuleBuilder2TutorialStep key = hint == HintKind.Suggestions
                ? RuleBuilder2TutorialStep.RuleDeck
                : RuleBuilder2TutorialStep.Welcome;
            if (focusRects != null &&
                focusRects.TryGetValue(key, out Rect card) &&
                card.width > 0f &&
                card.height > 0f)
            {
                card.height = CalculateCardHeight(card.width, hint);
                return ClampCard(card, bounds.ContractedBy(12f));
            }

            float width = 430f;
            float height = CalculateCardHeight(width, hint);
            Rect fallback = new Rect(
                bounds.center.x - width / 2f,
                bounds.center.y - height / 2f,
                width,
                height);
            return ClampCard(fallback, bounds.ContractedBy(12f));
        }

        private static float CalculateCardHeight(float width, HintKind hint)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float innerWidth = Mathf.Max(1f, width - 36f);
            float bodyHeight = Mathf.Ceil(Text.CalcHeight(GetBody(hint), innerWidth));
            Text.Font = previousFont;
            return 128f + Mathf.Max(40f, bodyHeight);
        }

        private static Rect ClampCard(Rect card, Rect bounds)
        {
            float x = Mathf.Clamp(card.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - card.width));
            float y = Mathf.Clamp(card.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - card.height));
            return new Rect(x, y, card.width, card.height);
        }

        private static void DrawDimOutside(Rect card, Rect bounds)
        {
            Color previous = GUI.color;
            Color dim = new Color(0f, 0f, 0f, 0.14f);
            GUI.color = dim;
            Widgets.DrawBoxSolid(new Rect(bounds.xMin, bounds.yMin, bounds.width, Mathf.Max(0f, card.yMin - bounds.yMin)), dim);
            Widgets.DrawBoxSolid(new Rect(bounds.xMin, card.yMax, bounds.width, Mathf.Max(0f, bounds.yMax - card.yMax)), dim);
            Widgets.DrawBoxSolid(new Rect(bounds.xMin, card.yMin, Mathf.Max(0f, card.xMin - bounds.xMin), card.height), dim);
            Widgets.DrawBoxSolid(new Rect(card.xMax, card.yMin, Mathf.Max(0f, bounds.xMax - card.xMax), card.height), dim);
            GUI.color = previous;
        }

        private void DrawCard(Rect rect, HintKind hint)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;

            TutorialCardRenderer.Draw(rect, cardStyle);

            Rect inner = rect.ContractedBy(18f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f), GetTitle(hint));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            bodyViewport.Draw(GetBodyRect(rect), GetBody(hint));

            Rect dismiss = GetDismissButtonRect(rect, hint);
            if (Widgets.ButtonText(dismiss, T("BWT_RuleBuilder2_Tutorial_GotIt")))
            {
                // Input is handled in TryHandleInput; this supports keyboard-driven repaint safety.
            }

            if (hint == HintKind.FirstOpen)
            {
                Rect settings = GetSettingsButtonRect(rect);
                if (Widgets.ButtonText(settings, T("BWT_RuleBuilder2_Tutorial_Settings")))
                {
                    // Input is handled in TryHandleInput; this supports keyboard-driven repaint safety.
                }
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private static bool TryGetButtonAt(Rect card, HintKind hint, Vector2 mousePosition, out TutorialButton button)
        {
            if (GetDismissButtonRect(card, hint).Contains(mousePosition))
            {
                button = TutorialButton.Dismiss;
                return true;
            }

            if (hint == HintKind.FirstOpen && GetSettingsButtonRect(card).Contains(mousePosition))
            {
                button = TutorialButton.Settings;
                return true;
            }

            button = TutorialButton.None;
            return false;
        }

        private static Rect GetDismissButtonRect(Rect card, HintKind hint)
        {
            Rect inner = card.ContractedBy(18f);
            float width = hint == HintKind.FirstOpen ? 130f : 110f;
            return new Rect(inner.xMax - width, inner.yMax - 32f, width, 32f);
        }

        private static Rect GetSettingsButtonRect(Rect card)
        {
            Rect inner = card.ContractedBy(18f);
            return new Rect(inner.x, inner.yMax - 32f, 150f, 32f);
        }

        private static Rect GetBodyRect(Rect card)
        {
            Rect inner = card.ContractedBy(18f);
            float y = inner.y + 38f;
            float buttonTop = inner.yMax - 32f;
            return new Rect(inner.x, y, inner.width, Mathf.Max(1f, buttonTop - y - 10f));
        }

        private static string GetTitle(HintKind hint)
        {
            return hint == HintKind.Suggestions
                ? T("BWT_RuleBuilder2_Tutorial_SuggestionsTitle")
                : T("BWT_RuleBuilder2_Tutorial_WelcomeTitle");
        }

        private static string GetBody(HintKind hint)
        {
            return hint == HintKind.Suggestions
                ? T("BWT_RuleBuilder2_Tutorial_SuggestionsBody")
                : T("BWT_RuleBuilder2_Tutorial_WelcomeBody");
        }

        private void Dismiss(HintKind hint)
        {
            if (hint == HintKind.Suggestions)
            {
                BetterWorkTabMod.Settings?.RecordViewedSetting(SuggestionsHintSettingId);
                suggestionsHintRequested = false;
                BetterWorkTabMod.Settings?.Write();
                return;
            }

            SetStep(RuleBuilder2TutorialStep.Complete);
            if (BetterWorkTabMod.Settings != null)
            {
                BetterWorkTabMod.Settings.showRuleBuilder2Tutorial = false;
                BetterWorkTabMod.Settings.Write();
            }
            BWTGeneralTutorial.NotifyRuleBuilderTutorialCompleted();
        }

        private static bool HasSeenSuggestionsHint()
        {
            return BetterWorkTabMod.Settings?.HasViewedSetting(SuggestionsHintSettingId) == true;
        }

        private static void OpenSettings()
        {
            var mod = LoadedModManager.GetMod<BetterWorkTabMod>();
            if (mod != null)
            {
                Find.WindowStack.Add(new Dialog_ModSettings(mod));
            }
        }

        private static void SetStep(RuleBuilder2TutorialStep step)
        {
            if (BetterWorkTabMod.Settings == null)
            {
                return;
            }

            BetterWorkTabMod.Settings.ruleBuilder2TutorialStep = (int)step;
        }

        private static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }

        private enum TutorialButton
        {
            None,
            Dismiss,
            Settings
        }
    }
}
