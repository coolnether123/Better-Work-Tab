using Better_Work_Tab.Features.RaisedPriorityMaximum;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RWWidgets = Verse.Widgets;

namespace Better_Work_Tab.UI.RuleBuilder
{
    public class Window_PriorityNumberPicker : Window
    {
        private const float RowHeight = 28f;
        private const float Gap = 6f;

        private readonly int _maxPriority;
        private readonly Action<int> _onSelected;
        private readonly List<int> _recentPriorities;
        private readonly List<PriorityBucket> _buckets;

        private int _selectedPriority;
        private PriorityBucket _selectedBucket;
        private string _typedPriority;

        public Window_PriorityNumberPicker(
            int maxPriority,
            int initialPriority,
            IEnumerable<int> recentPriorities,
            Action<int> onSelected)
        {
            _maxPriority = WorkPrioritySystem.NormalizeMaxPriority(maxPriority);
            _onSelected = onSelected;
            _selectedPriority = WorkPrioritySystem.ClampPriority(initialPriority, _maxPriority);
            _typedPriority = _selectedPriority.ToString();
            _recentPriorities = BuildRecentPriorities(recentPriorities);
            _buckets = BuildBuckets(_maxPriority);
            _selectedBucket = FindBucketForPriority(_selectedPriority);

            forcePause = false;
            doCloseX = true;
#if !(v0_18 || v0_17 || v0_16)
            closeOnAccept = false;
            closeOnCancel = true;
#endif
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(460f, 380f);

        protected override float Margin => 10f;

#if !(v0_18 || v0_17 || v0_16)
        public override void OnAcceptKeyPressed()
        {
            ConfirmSelection();
            Event.current?.Use();
        }
#endif

        public override void DoWindowContents(Rect inRect)
        {
            HandleKeyboardNavigation();

            DrawHeader(new Rect(0f, 0f, inRect.width, RowHeight));
            DrawRecentRow(new Rect(0f, 36f, inRect.width, RowHeight));
            DrawBucketTabs(new Rect(0f, 72f, inRect.width, RowHeight));
            DrawBucketPanel(new Rect(0f, 108f, inRect.width, inRect.height - 158f));
            DrawFooter(new Rect(0f, inRect.height - 36f, inRect.width, 32f));
        }

        private void DrawHeader(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(new Rect(rect.x, rect.y, 190f, rect.height), "BWT_PriorityPicker_SelectPriority".Translate());

            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            Rect fieldRect = new Rect(rect.xMax - 110f, rect.y, 110f, rect.height);
            string edited = RWWidgets.TextField(fieldRect, _typedPriority ?? string.Empty);
            HandleTypedPriorityChanged(edited);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawRecentRow(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            RWWidgets.Label(new Rect(rect.x, rect.y, 42f, rect.height), "BWT_PriorityPicker_Recent".Translate());
            GUI.color = Color.white;

            float x = rect.x + 46f;
            foreach (int priority in _recentPriorities)
            {
                Rect buttonRect = new Rect(x, rect.y + 3f, 36f, rect.height - 6f);
                DrawPriorityButton(buttonRect, priority, priority == _selectedPriority);
                x += buttonRect.width + Gap;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawBucketTabs(Rect rect)
        {
            float x = rect.x;
            foreach (PriorityBucket bucket in _buckets)
            {
                float width = bucket.IsSingle ? 36f : 68f;
                Rect buttonRect = new Rect(x, rect.y + 2f, width, rect.height - 4f);
                bool selected = bucket.Contains(_selectedPriority) &&
                                bucket.Start == _selectedBucket.Start &&
                                bucket.End == _selectedBucket.End;

                DrawTextButton(buttonRect, bucket.Label, selected);
                if (RWWidgets.ButtonInvisible(buttonRect))
                {
                    _selectedBucket = bucket;
                    if (!bucket.Contains(_selectedPriority))
                    {
                        SelectPriority(bucket.Start);
                    }
                }

                x += width + Gap;
            }
        }

        private void DrawBucketPanel(Rect rect)
        {
            RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.PanelBackground);
            RWWidgets.DrawBox(rect, 1);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(new Rect(rect.x + 12f, rect.y + 6f, rect.width - 24f, 22f), "BWT_PriorityPicker_Bucket".Translate(_selectedBucket.Label));

            Text.Font = GameFont.Tiny;
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            RWWidgets.Label(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, 18f), "BWT_PriorityPicker_BucketHint".Translate());
            GUI.color = Color.white;

            Rect pickedRect = new Rect(rect.xMax - 88f, rect.y + 58f, 74f, rect.height - 76f);
            DrawPickedValuePanel(pickedRect);

            Rect gridRect = new Rect(rect.x + 14f, rect.y + 58f, pickedRect.x - rect.x - 28f, rect.height - 74f);
            DrawPriorityGrid(gridRect);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawPriorityGrid(Rect rect)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width + Gap) / (48f + Gap)));
            float buttonWidth = Mathf.Floor((rect.width - Gap * (columns - 1)) / columns);
            float buttonHeight = 24f;
            float rowGap = 3f;

            int index = 0;
            for (int priority = _selectedBucket.Start; priority <= _selectedBucket.End; priority++)
            {
                int column = index % columns;
                int row = index / columns;
                Rect buttonRect = new Rect(
                    rect.x + column * (buttonWidth + Gap),
                    rect.y + row * (buttonHeight + rowGap),
                    buttonWidth,
                    buttonHeight);

                DrawPriorityButton(buttonRect, priority, priority == _selectedPriority);
                index++;
            }
        }

        private void DrawPickedValuePanel(Rect rect)
        {
            RWWidgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.95f));
            RWWidgets.DrawBox(rect, 1);

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            RWWidgets.Label(new Rect(rect.x, rect.y + 18f, rect.width, 18f), "BWT_PriorityPicker_Picked".Translate());

            Text.Font = GameFont.Medium;
            GUI.color = RuleBuilderConstants.SuccessColor;
            RWWidgets.Label(new Rect(rect.x, rect.y + 58f, rect.width, 30f), _selectedPriority.ToString());

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawFooter(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            RWWidgets.Label(new Rect(rect.x, rect.y, rect.width - 160f, rect.height), "BWT_PriorityPicker_FooterHint".Translate());

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect okRect = new Rect(rect.xMax - 50f, rect.y, 50f, rect.height);
            Rect cancelRect = new Rect(okRect.x - 58f, rect.y, 52f, rect.height);

            if (RWWidgets.ButtonText(cancelRect, "BWT_Cancel".Translate()))
            {
                Close();
            }

            if (RWWidgets.ButtonText(okRect, "OK"))
            {
                ConfirmSelection();
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawPriorityButton(Rect rect, int priority, bool selected)
        {
            DrawTextButton(rect, priority.ToString(), selected);
            if (RWWidgets.ButtonInvisible(rect))
            {
                SelectPriority(priority);
            }
        }

        private void DrawTextButton(Rect rect, string label, bool selected)
        {
            Color background = selected
                ? RuleBuilderConstants.CardBackgroundSelected
                : (Mouse.IsOver(rect) ? RuleBuilderConstants.CardBackgroundHover : RuleBuilderConstants.CardBackground);
            Color border = selected ? RuleBuilderConstants.SuccessColor : new Color(0.32f, 0.32f, 0.32f);

            RWWidgets.DrawBoxSolid(rect, background);
            GUI.color = border;
            RWWidgets.DrawBox(rect, selected ? 2 : 1);

            GUI.color = RuleBuilderConstants.LabelColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            RWWidgets.Label(rect, label);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void HandleTypedPriorityChanged(string edited)
        {
            if (edited == _typedPriority)
            {
                return;
            }

            if (string.IsNullOrEmpty(edited) || edited.Trim().Length == 0)
            {
                _typedPriority = string.Empty;
                return;
            }

            int parsedPriority;
            if (!int.TryParse(edited, out parsedPriority))
            {
                return;
            }

            SelectPriority(parsedPriority);
        }

        private void HandleKeyboardNavigation()
        {
            Event evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown)
            {
                return;
            }

            if (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.DownArrow)
            {
                SelectPriority(_selectedPriority - 1);
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.RightArrow || evt.keyCode == KeyCode.UpArrow)
            {
                SelectPriority(_selectedPriority + 1);
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                ConfirmSelection();
                evt.Use();
            }
        }

        private void SelectPriority(int priority)
        {
            _selectedPriority = WorkPrioritySystem.ClampPriority(priority, _maxPriority);
            _typedPriority = _selectedPriority.ToString();
            _selectedBucket = FindBucketForPriority(_selectedPriority);
        }

        private void ConfirmSelection()
        {
            _onSelected?.Invoke(_selectedPriority);
            Close();
        }

        private List<int> BuildRecentPriorities(IEnumerable<int> recentPriorities)
        {
            var result = new List<int>();
            var added = new HashSet<int>();

            AddRecent(_selectedPriority, result, added);

            if (recentPriorities != null)
            {
                foreach (int priority in recentPriorities)
                {
                    AddRecent(priority, result, added);
                    if (result.Count >= 5)
                    {
                        break;
                    }
                }
            }

            for (int priority = 1; priority <= _maxPriority && result.Count < 5; priority++)
            {
                AddRecent(priority, result, added);
            }

            if (result.Count < 5)
            {
                AddRecent(0, result, added);
            }

            return result;
        }

        private void AddRecent(int priority, List<int> result, HashSet<int> added)
        {
            int effectivePriority = WorkPrioritySystem.ClampPriority(priority, _maxPriority);
            if (added.Add(effectivePriority))
            {
                result.Add(effectivePriority);
            }
        }

        private PriorityBucket FindBucketForPriority(int priority)
        {
            foreach (PriorityBucket bucket in _buckets)
            {
                if (bucket.Contains(priority))
                {
                    return bucket;
                }
            }

            return _buckets.Count > 0 ? _buckets[0] : new PriorityBucket(0, 0);
        }

        private static List<PriorityBucket> BuildBuckets(int maxPriority)
        {
            var buckets = new List<PriorityBucket>();
            int start = 0;
            while (start <= maxPriority)
            {
                int end = Mathf.Min(start == 0 ? 25 : start + 24, maxPriority);
                buckets.Add(new PriorityBucket(start, end));
                start = end + 1;
            }

            return buckets;
        }

        private struct PriorityBucket
        {
            public readonly int Start;
            public readonly int End;

            public PriorityBucket(int start, int end)
            {
                Start = start;
                End = end;
            }

            public bool IsSingle => Start == End;

            public string Label => IsSingle ? Start.ToString() : $"{Start}-{End}";

            public bool Contains(int priority)
            {
                return priority >= Start && priority <= End;
            }
        }
    }
}
