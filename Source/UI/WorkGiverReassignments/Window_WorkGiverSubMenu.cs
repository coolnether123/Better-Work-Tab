using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Workloads;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Main window for the WorkGiver sub-menu, allowing per-WorkGiver priority customization and reordering.
    /// </summary>
    internal class Window_WorkGiverSubMenu : Window
    {
        private readonly WorkTypeDef _workType;
        private readonly Pawn _pawn;
        private readonly Vector2 _triggerPos;

        public WorkTypeDef WorkType => _workType;
        public Pawn Pawn => _pawn;
        public float DynamicHeaderHeight => _dynamicHeaderHeight;

        private List<WorkGiver> _workGivers;
        private WorkGiverBaselineTracker _baselineTracker;
        private WorkGiverDragHandler _dragHandler;
        
        private bool _needsRefresh = false;
        private float _dynamicHeaderHeight = 120f;
        private bool _useAngledHeaders = true;
        private List<int> _vanillaHeaderLevels = null;
        private float _columnWidth = ColumnWidth;
        
        private const float ColumnWidth = 35f;
        private const float PriorityRowHeight = 45f;
        private const float FooterHeight = 50f;
        private const float WindowPadding = 12f;
        private const float HeaderTop = 30f;
        private const float VanillaMaxColumnWidth = 70f;

        private float DesiredContentWidth => _workGivers.Count * _columnWidth + WindowPadding * 2f;

        private float DesiredContentHeight => _dynamicHeaderHeight + PriorityRowHeight + FooterHeight + WindowPadding * 2f;

        private static IDisposable PushEffectiveStateScope()
        {
            // This submenu is a separate WindowStack entry. Its constructor and
            // later frames run after MainTabWindow_BetterWork has popped the
            // Work-tab pass scope, so re-enter the same session provider here.
            return WorkloadPreviewController.Current?.PushEffectiveStateScope();
        }

        public Window_WorkGiverSubMenu(WorkTypeDef workType, Vector2 triggerPos, Pawn pawn = null)
        {
            _workType = workType;
            _pawn = pawn;
            _triggerPos = triggerPos;
            
            RefreshWorkGivers();
            
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true; 
            preventCameraMotion = true;
            shadowAlpha = 0.6f;
        }

        public void NotifyPriorityChanged()
        {
            _needsRefresh = true;
        }

        public void NotifyDragCompleted()
        {
            _baselineTracker.UpdateMovedStatus(_workGivers);
        }

        private void RefreshWorkGivers()
        {
            using (PushEffectiveStateScope())
            {
                RefreshWorkGiversScoped();
            }
        }

        private void RefreshWorkGiversScoped()
        {
            bool specificJobOrderingBlocked =
                WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked;
            IReadOnlyList<WorkGiver> source =
                specificJobOrderingBlocked
                    ? GetStableWorkGiversForPreview()
                    : WorkTabEffectiveStateRuntime.IsPreviewDimensionOwned(
                    WorkTabEffectiveStateDimension.Schedule)
                    ? WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(_workType, _pawn)
                    : WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(_workType, _pawn);
            _workGivers = source?.ToList() ?? new List<WorkGiver>();
            ApplyPreviewSpecificJobOrder();
            _baselineTracker = new WorkGiverBaselineTracker(_workType, _workGivers, _pawn);
            _useAngledHeaders = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.HeadersAngled,
                BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders);
            if (!_useAngledHeaders)
            {
                RecalculateVanillaHeaderLevels();
            }
            else
            {
                _vanillaHeaderLevels = null;
            }
            if (_dragHandler == null)
            {
                _dragHandler = new WorkGiverDragHandler(this, _workType);
            }
            
            CalculateHeaderHeight();
            _needsRefresh = false;
        }

        private IReadOnlyList<WorkGiver> GetStableWorkGiversForPreview()
        {
            var result = new List<WorkGiver>();
            IReadOnlyList<WorkGiverDef> definitions =
                DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < definitions.Count; i++)
            {
                WorkGiverDef definition = definitions[i];
                if (definition == null ||
                    WorkGiverReassignmentManager.GetTargetWorkType(definition) != _workType)
                {
                    continue;
                }

                WorkGiver worker = definition.Worker;
                if (worker != null)
                {
                    result.Add(worker);
                }
            }

            result.Sort((left, right) =>
            {
                int priority = right.def.priorityInType.CompareTo(left.def.priorityInType);
                return priority != 0
                    ? priority
                    : StringComparer.Ordinal.Compare(left.def.defName, right.def.defName);
            });
            return result;
        }

        private void ApplyPreviewSpecificJobOrder()
        {
            if (!WorkTabEffectiveStateRuntime.IsPreviewDimensionOwned(
                    WorkTabEffectiveStateDimension.SpecificJobOrder) ||
                _pawn == null ||
                _workType == null ||
                _workGivers.Count < 2)
            {
                return;
            }

            var fallbackIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _workGivers.Count; i++)
            {
                string defName = _workGivers[i]?.def?.defName;
                if (!defName.NullOrEmpty() && !fallbackIndices.ContainsKey(defName))
                {
                    fallbackIndices.Add(defName, i);
                }
            }

            _workGivers.Sort((left, right) =>
            {
                string leftName = left?.def?.defName;
                string rightName = right?.def?.defName;
                int leftFallback = leftName.NullOrEmpty() || !fallbackIndices.TryGetValue(leftName, out int leftIndex)
                    ? int.MaxValue
                    : leftIndex;
                int rightFallback = rightName.NullOrEmpty() || !fallbackIndices.TryGetValue(rightName, out int rightIndex)
                    ? int.MaxValue
                    : rightIndex;
                int leftOrder = WorkTabEffectiveStateRuntime.GetSpecificJobOrder(
                    _pawn,
                    _workType,
                    left?.def,
                    leftFallback);
                int rightOrder = WorkTabEffectiveStateRuntime.GetSpecificJobOrder(
                    _pawn,
                    _workType,
                    right?.def,
                    rightFallback);
                int comparison = leftOrder.CompareTo(rightOrder);
                return comparison != 0
                    ? comparison
                    : leftFallback.CompareTo(rightFallback);
            });
        }

        private void CalculateHeaderHeight()
        {
            float minHeight = _useAngledHeaders ? 80f : 70f;
            float maxHeight = minHeight;
            float padding = _useAngledHeaders ? 20f : 16f;
            float maxLabelWidth = ColumnWidth;
            var settings = BetterWorkTabMod.Settings;

            _columnWidth = ColumnWidth;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;

            for (int i = 0; i < _workGivers.Count; i++)
            {
                var wg = _workGivers[i];
                string label = BuildHeaderLabel(wg, _baselineTracker.IsMovedFromBaseline(wg.def.defName));
                float neededHeight;
                if (_useAngledHeaders)
                {
                    neededHeight = CalculateAngledLabelHeight(label, settings);
                }
                else
                {
                    int level = (_vanillaHeaderLevels != null && i < _vanillaHeaderLevels.Count) ? _vanillaHeaderLevels[i] : 0;
                    neededHeight = CalculateVanillaLabelHeight(label, level);
                    maxLabelWidth = Mathf.Max(maxLabelWidth, Text.CalcSize(label).x);
                }

                if (neededHeight > maxHeight)
                {
                    maxHeight = neededHeight;
                }
            }

            Text.Font = oldFont;

            if (!_useAngledHeaders)
            {
                // Add compact padding and cap width so headers sit closer together.
                _columnWidth = Mathf.Clamp(maxLabelWidth + 8f, ColumnWidth, VanillaMaxColumnWidth);
            }

            float cap = _useAngledHeaders ? 250f : 160f;
            float clampedHeight = Mathf.Clamp(maxHeight + padding, minHeight, cap);
            _dynamicHeaderHeight = HeaderTop + clampedHeight;
        }

        public override Vector2 InitialSize
        {
            get
            {
                float desiredWidth = DesiredContentWidth + Margin * 2f;
                float maxAllowedWidth = Verse.UI.screenWidth - 40f; // Leave 20px margin on each side
                float width = Mathf.Max(250f, Mathf.Min(desiredWidth, maxAllowedWidth));
                
                float height = DesiredContentHeight + Margin * 2f;
                return new Vector2(width, height);
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            
            windowRect.x = _triggerPos.x - (windowRect.width / 2f);
            windowRect.y = _triggerPos.y - windowRect.height - 5f;
            
            if (windowRect.x < 10f) windowRect.x = 10f;
            if (windowRect.xMax > Verse.UI.screenWidth - 10f) windowRect.x = Verse.UI.screenWidth - windowRect.width - 10f;
            if (windowRect.y < 10f) windowRect.y = _triggerPos.y + 5f;
            if (windowRect.yMax > Verse.UI.screenHeight - 10f) windowRect.y = Verse.UI.screenHeight - windowRect.height - 10f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (PushEffectiveStateScope())
            {
                DoWindowContentsScoped(inRect);
            }
        }

        private void DoWindowContentsScoped(Rect inRect)
        {
            if (_workType == null)
            {
                Close();
                return;
            }

            if (_needsRefresh)
            {
                RefreshWorkGivers();
            }

            var settings = BetterWorkTabMod.Settings;
            bool desiredAngled = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.HeadersAngled,
                settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders);
            if (desiredAngled != _useAngledHeaders)
            {
                _useAngledHeaders = desiredAngled;
                if (!_useAngledHeaders)
                {
                    RecalculateVanillaHeaderLevels();
                }
                CalculateHeaderHeight();
            }

            DrawTitle(inRect);
            
            float headerY = HeaderTop;
            float boxY = _dynamicHeaderHeight;
            
            _dragHandler.UpdateDrag(Event.current.mousePosition, _workGivers, WindowPadding, _columnWidth);
            
            DrawWorkGiverColumns(headerY, boxY);
            DrawDragOverlay(headerY, boxY);
            DrawFooter(boxY, inRect.width);
            
            HandleEscapeKey();
        }

        private void DrawTitle(Rect inRect)
        {
            Text.Font = GameFont.Small;
            string workTypeLabel = WorkTypeDisplayNameService.HeaderLabel(_workType);
            string titleText = _pawn == null 
                ? $"Global: {workTypeLabel}"
                : $"{_pawn.LabelShortCap}: {workTypeLabel}";
            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked)
            {
                titleText += _pawn == null
                    ? " (preview order unavailable)"
                    : " (preview order staged here)";
            }
            Widgets.Label(new Rect(0, 0, inRect.width, 24f), titleText.Colorize(Color.gray));
        }

        private void DrawWorkGiverColumns(float headerY, float boxY)
        {
            float curX = WindowPadding;
            
            for (int i = 0; i < _workGivers.Count; i++)
            {
                var wg = _workGivers[i];
                Rect headerRect = new Rect(curX, headerY, _columnWidth, _dynamicHeaderHeight - headerY);
                Rect cellRect = new Rect(curX, boxY, _columnWidth, PriorityRowHeight);
                
                bool isMovedFromBaseline = _baselineTracker.IsMovedFromBaseline(wg.def.defName);
                string label = BuildHeaderLabel(wg, isMovedFromBaseline);

                int vanillaLevel = (!_useAngledHeaders && _vanillaHeaderLevels != null && i < _vanillaHeaderLevels.Count)
                    ? _vanillaHeaderLevels[i]
                    : 0;
                bool isHovered = _useAngledHeaders
                    ? DrawAngledHeader(wg, label, headerRect, isMovedFromBaseline)
                    : DrawVanillaHeader(wg, label, headerRect, isMovedFromBaseline, vanillaLevel);

                HandleHeaderDrag(i, isHovered);
                DrawPriorityBox(wg, cellRect);
                
                // Draw standard column divider line (1px grey) to match main work tab
                if (i < _workGivers.Count - 1)
                {
                    float dividerX = curX + _columnWidth;
                    // Draw divider from bottom of header area through priority row
                    Rect dividerRect = new Rect(dividerX, boxY, 1f, PriorityRowHeight);
                    Widgets.DrawBoxSolid(dividerRect, new Color(1f, 1f, 1f, 0.1f)); // Match vanilla/BWT subtle divider
                }

                curX += _columnWidth;
            }
        }

        private bool DrawAngledHeader(WorkGiver wg, string label, Rect headerRect, bool isMovedFromBaseline)
        {
            var layout = BuildAngledLayout(label, headerRect, isMovedFromBaseline, out var quad);
            bool isHovered = quad != null
                ? AngledHeaderCache.IsMouseOver(quad, Event.current.mousePosition)
                : Mouse.IsOver(headerRect);

            AngledLabelDrawer.Draw(layout, isHovered, headerRect: headerRect);

            // Tooltip bounded to the rotated quad box for accurate hover.
            if (Event.current.type == EventType.Repaint)
            {
                Rect tipRect = quad != null ? GetBoundingRectFromQuad(quad) : headerRect;
                TooltipHandler.TipRegion(tipRect, BuildWorkGiverTooltip(wg));
            }
            return isHovered;
        }

        private bool DrawVanillaHeader(WorkGiver wg, string label, Rect headerRect, bool isMovedFromBaseline, int level)
        {
            var evt = Event.current;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect textRect = GetVanillaTextRect(label, headerRect, level, out var textSize);

            Rect hoverRect = textRect.ExpandedBy(2f);
            bool isHovered = hoverRect.Contains(evt.mousePosition);
            if (isHovered)
            {
                GUI.color = HeaderUtility.Colors.HoverHighlight;
                Widgets.DrawHighlight(hoverRect);
            }

            GUI.color = (isMovedFromBaseline && BWTWorkTabEffectiveSettings.GetBool(
                "columns.showMovedColorTint",
                BetterWorkTabMod.Settings?.showMovedColumnColorTint ?? true))
                ? HeaderUtility.Colors.MovedMarkerColor
                : BetterWorkTabMod.Settings.angledHeaderColor;

            Widgets.Label(textRect, label);
            DrawVanillaStem(textRect, headerRect.yMax);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            if (Event.current.type == EventType.Repaint)
            {
                TooltipHandler.TipRegion(hoverRect, BuildWorkGiverTooltip(wg));
            }

            return isHovered;
        }

        private string BuildHeaderLabel(WorkGiver wg, bool isMovedFromBaseline)
        {
            string label = WorkGiverDisplayNameService.HeaderLabel(wg?.def);
            var settings = BetterWorkTabMod.Settings;
            bool showMovedMarker = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.ColumnsShowMovedIndicator,
                settings?.showColumnMovedMarker ?? DefaultSettings.showColumnMovedMarker);
            if (isMovedFromBaseline && showMovedMarker && !label.EndsWith(HeaderUtility.MovedMarker))
            {
                label += HeaderUtility.MovedMarker;
            }

            return label;
        }

        private string BuildWorkGiverTooltip(WorkGiver wg)
        {
            if (wg?.def == null) return HeaderUtility.DefaultHeaderText;

            string title = WorkGiverDisplayNameService.FullLabel(wg.def);
            string desc = wg.def.description;
            string workType = WorkTypeDisplayNameService.FullLabel(wg.def.workType ?? _workType);

            System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
            sb.Append(title.Colorize(ColoredText.TipSectionTitleColor));
            if (!workType.NullOrEmpty())
            {
                sb.Append("\n").Append("WorkType".Translate() + ": ").Append(workType);
            }
            if (!desc.NullOrEmpty())
            {
                sb.Append("\n\n").Append(desc);
            }

            sb.Append("\n\n").Append("Drag to reorder");
            sb.Append("\n").Append("Hold drag to move; drag out to another work type header to reassign");

            return sb.ToString();
        }

        private float CalculateAngledLabelHeight(string label, BetterWorkTabSettings settings)
        {
            float rotation = AngledLabelDrawer.CurrentRotation;
            float absSin = Mathf.Abs(Mathf.Sin(rotation * Mathf.Deg2Rad));
            float absCos = Mathf.Abs(Mathf.Cos(rotation * Mathf.Deg2Rad));
            bool isVerticalCjk = HeaderUtility.ShouldUseCJKVerticalLabel(label);

            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 size = Text.CalcSize(label);
            if (isVerticalCjk)
            {
                float charHeight = Text.LineHeight * (settings?.cjkVerticalKerning ?? 1f);
                size = new Vector2(size.y, label.Length * charHeight);
            }
            Text.Font = oldFont;

            if (isVerticalCjk)
            {
                return size.y + AngledLabelDrawer.STEM_BOTTOM_GAP;
            }

            return (size.x * absSin) + (size.y * absCos) + AngledLabelDrawer.STEM_BOTTOM_GAP;
        }

        private float CalculateVanillaLabelHeight(string label, int level)
        {
            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 size = Text.CalcSize(label);
            Text.Font = oldFont;

            float offset = GetVanillaOffset(level);
            // Height needed so text top stays within the header band with a small pad.
            return offset + (size.y / 2f) + 8f;
        }

        private Rect GetVanillaTextRect(string label, Rect headerRect, int level, out Vector2 textSize)
        {
            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            textSize = Text.CalcSize(label);
            Text.Font = oldFont;

            float vanillaOffset = GetVanillaOffset(level);
            float x = headerRect.center.x - (textSize.x / 2f);
            float y = headerRect.yMax - vanillaOffset - (textSize.y / 2f);
            return new Rect(x, y, textSize.x, textSize.y);
        }

        private float GetVanillaOffset(int level)
        {
            const float Level0Offset = 19f;
            const float LevelStep = 24f; // Raise every other header a bit more to allow tighter horizontal spacing
            level = Mathf.Clamp(level, 0, 1);
            return Level0Offset + (level * LevelStep);
        }

        private AngledLabelDrawer.AngledLabelLayout BuildAngledLayout(string label, Rect headerRect, bool showMarker, out Vector2[] quad)
        {
            var settings = BetterWorkTabMod.Settings;
            float rotation = AngledLabelDrawer.CurrentRotation;
            bool isVerticalCjk = HeaderUtility.ShouldUseCJKVerticalLabel(label);

            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 size = Text.CalcSize(label);
            if (isVerticalCjk)
            {
                float charHeight = Text.LineHeight * (settings?.cjkVerticalKerning ?? 1f);
                size = new Vector2(size.y, label.Length * charHeight);
            }
            Text.Font = oldFont;

            float drawWidth = isVerticalCjk ? size.x : headerRect.height;
            Rect drawRect;
            if (isVerticalCjk)
            {
                float yPos = headerRect.yMax - size.y - AngledLabelDrawer.STEM_BOTTOM_GAP;
                drawRect = new Rect(headerRect.center.x - drawWidth / 2f, yPos, drawWidth, size.y);
            }
            else
            {
                Vector2 anchor = new Vector2(headerRect.center.x, headerRect.yMax - AngledLabelDrawer.STEM_BOTTOM_GAP);
                Vector2 localUnderlineStart = new Vector2(-drawWidth / 2f, size.y / 2f);
                float anchorCos = Mathf.Cos(rotation * Mathf.Deg2Rad);
                float anchorSin = Mathf.Sin(rotation * Mathf.Deg2Rad);
                Vector2 rotatedUnderlineStart = RotatePoint(localUnderlineStart, anchorCos, anchorSin);
                drawRect = new Rect(0f, 0f, drawWidth, size.y)
                {
                    center = anchor - rotatedUnderlineStart
                };
            }

            Vector2 pivot = drawRect.center;
            float rot = isVerticalCjk ? 0f : rotation;
            float cos = Mathf.Cos(rot * Mathf.Deg2Rad);
            float sin = Mathf.Sin(rot * Mathf.Deg2Rad);

            quad = CalculateRotatedQuad(pivot, drawWidth, size.y, cos, sin);

            return new AngledLabelDrawer.AngledLabelLayout(label, size, pivot, showMarker, isVerticalCjk, drawRect);
        }

        private static Vector2[] CalculateRotatedQuad(Vector2 pivot, float labelWidth, float textHeight, float cos, float sin)
        {
            float halfW = (labelWidth / 2f) + 2f;
            float halfH = (textHeight / 2f) + 2f;

            Vector2 p1 = new Vector2(-halfW, -halfH);
            Vector2 p2 = new Vector2(halfW, -halfH);
            Vector2 p3 = new Vector2(halfW, halfH);
            Vector2 p4 = new Vector2(-halfW, halfH);

            return new[]
            {
                RotatePoint(p1, cos, sin) + pivot,
                RotatePoint(p2, cos, sin) + pivot,
                RotatePoint(p3, cos, sin) + pivot,
                RotatePoint(p4, cos, sin) + pivot
            };
        }

        private static Vector2 RotatePoint(Vector2 p, float cos, float sin)
        {
            return new Vector2(
                (p.x * cos) - (p.y * sin),
                (p.x * sin) + (p.y * cos)
            );
        }

        private Rect GetBoundingRectFromQuad(Vector2[] quad)
        {
            if (quad == null || quad.Length == 0) return Rect.zero;
            float minX = quad[0].x;
            float maxX = quad[0].x;
            float minY = quad[0].y;
            float maxY = quad[0].y;
            for (int i = 1; i < quad.Length; i++)
            {
                minX = Mathf.Min(minX, quad[i].x);
                maxX = Mathf.Max(maxX, quad[i].x);
                minY = Mathf.Min(minY, quad[i].y);
                maxY = Mathf.Max(maxY, quad[i].y);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private void DrawVanillaStem(Rect textRect, float headerBottom)
        {
            var settings = BetterWorkTabMod.Settings;
            if (BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.DragdropRemoveHeaderUnderline,
                settings?.removeHeaderUnderline ?? DefaultSettings.removeHeaderUnderline))
                return;

            const float StemBaseHeight = 11f;
            const float StemWidth = 2f;
            const float StemYAdjustment = -3f;

            float stemTop = textRect.center.y + (textRect.height / 2f) + StemYAdjustment;
            Rect stemRect = new Rect(textRect.center.x, stemTop, StemWidth, StemBaseHeight);

            GUI.color = HeaderUtility.Colors.VanillaStemColor;
            Widgets.DrawBoxSolid(stemRect, GUI.color);
        }

        private void HandleHeaderDrag(int index, bool isHovered)
        {
            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked &&
                _pawn == null)
            {
                if (isHovered && Event.current.type == EventType.Repaint)
                {
                    TooltipHandler.TipRegion(
                        new Rect(WindowPadding + index * _columnWidth, HeaderTop, _columnWidth, _dynamicHeaderHeight - HeaderTop),
                        "Specific-job ordering is not projected in this workload preview.");
                }
                return;
            }

            // Guard: Don't start drag if event was already consumed (e.g., by priority box click)
            if (Event.current.type == EventType.Used) return;

            if (isHovered && !_dragHandler.IsDragging)
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _dragHandler.BeginDrag(index, Event.current.mousePosition, _workGivers);
                    Event.current.Use();
                }
            }
        }

        private void RecalculateVanillaHeaderLevels()
        {
            _vanillaHeaderLevels = new List<int>(_workGivers.Count);
            for (int i = 0; i < _workGivers.Count; i++)
            {
                // Alternate levels 0/1 to emulate vanilla staggering and avoid overlapping text.
                _vanillaHeaderLevels.Add(i % 2);
            }
        }

        private void DrawPriorityBox(WorkGiver wg, Rect cellRect)
        {
            const float boxSize = 25f;
            float x = cellRect.x + (cellRect.width - boxSize) / 2f;
            float y = cellRect.y + (cellRect.height - boxSize) / 2f;
            Rect boxRect = new Rect(x, y, boxSize, boxSize);

            // Just draw the priority box - no need to refresh ordering when priority changes
            // The displayed priority will update via the renderer without reordering
            WorkGiverPriorityBoxRenderer.DrawPriorityBox(wg, _workType, _pawn, boxRect);
        }

        private void DrawDragOverlay(float headerY, float boxY)
        {
            float totalHeight = (boxY - headerY) + PriorityRowHeight;
            _dragHandler.DrawDragOverlay(WindowPadding, _columnWidth, headerY, totalHeight, _workGivers, _baselineTracker);
        }

        private void DrawFooter(float boxY, float contentWidth)
        {
            // Only show footer in global window, not pawn-specific windows
            if (_pawn != null) return;
            
            Rect footerRect = new Rect(0, boxY + PriorityRowHeight + 5f, contentWidth, FooterHeight);
            Widgets.DrawLineHorizontal(footerRect.x, footerRect.y, footerRect.width);
            
            List<Pawn> overrides = WorkGiverReassignmentManager.GetPawnsWithOverrides(_workType);
            int count = overrides.Count;
            
            Rect textRect = new Rect(footerRect.x + 5f, footerRect.y + 8f, footerRect.width - 40f, 24f);
            Widgets.Label(textRect, $"Pawns with overrides: {count}");
            
            Rect btnRect = new Rect(footerRect.xMax - 30f, footerRect.y + 8f, 24f, 24f);
            if (Widgets.ButtonText(btnRect, "▼"))
            {
                ShowPawnDropdown(overrides);
            }
        }

        private void ShowPawnDropdown(List<Pawn> pawns)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (pawns.Count == 0)
            {
                options.Add(new FloatMenuOption("None", null));
            }
            else
            {
                foreach (var p in pawns)
                {
                    var localP = p;
                    options.Add(new FloatMenuOption(localP.LabelShortCap, () => {
                        Find.WindowStack.Add(new Window_WorkGiverSubMenu(_workType, _triggerPos, localP));
                        this.Close();
                    }));
                }
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void HandleEscapeKey()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
            }
        }
    }
}
