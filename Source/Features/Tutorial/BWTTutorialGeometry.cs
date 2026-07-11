using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal static class BWTTutorialGeometry
    {
        private const float InfoIconSize = 24f;
        private const float RightEdgeMargin = 10f;

        internal static List<Rect> WholeTab(Rect inRect)
        {
            return new List<Rect> { inRect.ContractedBy(12f) };
        }

        internal static List<Rect> WorkHeaders(Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            AddWorkHeaderArea(rects, layout);
            AddWholeTabFallback(rects, inRect);
            return rects;
        }

        internal static List<Rect> ManualPriorities(Rect inRect)
        {
            return new List<Rect> { new Rect(5f, 5f, 220f, 62f).ExpandedBy(4f) };
        }

        internal static List<Rect> PriorityLegend(Rect inRect)
        {
            return new List<Rect>
            {
                new Rect(370f, inRect.y + 5f, 420f, 30f).ExpandedBy(4f)
            };
        }

        internal static List<Rect> ContextSettingsHint(Rect inRect)
        {
            const float width = 230f;
            return new List<Rect>
            {
                new Rect(inRect.xMax - width - 42f, inRect.y + 5f, width, 24f).ExpandedBy(4f)
            };
        }

        internal static List<Rect> PawnRows(Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            if (layout?.Rows != null && layout.Columns != null)
            {
                Rect union = Rect.zero;
                bool hasAny = false;
                for (int i = 0; i < layout.Rows.Count; i++)
                {
                    WorkTabLayoutRow row = layout.Rows[i];
                    if (row.Pawn == null)
                    {
                        continue;
                    }

                    Rect rowRect = layout.GetScreenRect(row);
                    union = hasAny ? Union(union, rowRect) : rowRect;
                    hasAny = true;
                    if (i >= 2)
                    {
                        break;
                    }
                }

                if (hasAny)
                {
                    rects.Add(union.ExpandedBy(4f));
                }
            }

            AddWholeTabFallback(rects, inRect);
            return rects;
        }

        internal static List<Rect> PawnNameColumn(Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            if (layout?.Rows != null && layout.Columns != null)
            {
                WorkTabLayoutColumn nameColumn = default(WorkTabLayoutColumn);
                bool hasNameColumn = false;
                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    if (!(layout.Columns[i].Column?.Worker is PawnColumnWorker_WorkPriority))
                    {
                        nameColumn = layout.Columns[i];
                        hasNameColumn = true;
                        break;
                    }
                }

                if (hasNameColumn)
                {
                    Rect union = Rect.zero;
                    bool hasAny = false;
                    for (int i = 0; i < layout.Rows.Count; i++)
                    {
                        WorkTabLayoutRow row = layout.Rows[i];
                        if (row.Pawn == null)
                        {
                            continue;
                        }

                        Rect rowRect = layout.GetScreenRect(row);
                        Rect cellRect = new Rect(nameColumn.HeaderRect.x, rowRect.y, nameColumn.Width, rowRect.height);
                        union = hasAny ? Union(union, cellRect) : cellRect;
                        hasAny = true;
                        if (i >= 2)
                        {
                            break;
                        }
                    }

                    if (hasAny)
                    {
                        rects.Add(union.ExpandedBy(4f));
                    }
                }
            }

            AddWholeTabFallback(rects, inRect);
            return rects;
        }

        internal static List<Rect> FirstPriorityCell(
            Rect inRect,
            IWorkTabLayoutController layout,
            bool requireSubWorkColumn,
            bool allowDisabledFallback = true)
        {
            var rects = new List<Rect>();
            AddFirstPriorityCell(rects, layout, requireSubWorkColumn, allowDisabledFallback);
            AddWholeTabFallback(rects, inRect);
            return rects;
        }

        internal static List<Rect> GlobalSubWorkPriorityRow(Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            AddGlobalPriorityRow(rects, layout);
            AddWholeTabFallback(rects, inRect);
            return rects;
        }

        internal static List<Rect> TimePriorityEditor(Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            if (TimePriorityScheduleEditor.TryGetLastPanelRect(out Rect rect))
            {
                rects.Add(rect.ExpandedBy(5f));
            }
            else
            {
                AddFirstPriorityCell(rects, layout, requireSubWorkColumn: false, allowDisabledFallback: true);
            }

            AddWholeTabFallback(rects, inRect);
            return rects;
        }

        internal static List<Rect> SubWorkExit(Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = WorkHeaders(inRect, layout);
            rects.Add(GetSubWorkExitRect(inRect));
            return rects;
        }

        internal static List<Rect> Workloads(Rect inRect)
        {
            Rect infoRect = GetInfoIconRect(inRect);
            HeaderButtons.BottomButtonRects rects = HeaderButtons.GetBottomButtonRects(inRect, infoRect);
            return rects.HasWorkload
                ? new List<Rect> { Union(rects.WorkloadMain, rects.WorkloadMenu).ExpandedBy(4f) }
                : WholeTab(inRect);
        }

        internal static List<Rect> Rulesets(Rect inRect)
        {
            Rect infoRect = GetInfoIconRect(inRect);
            HeaderButtons.BottomButtonRects rects = HeaderButtons.GetBottomButtonRects(inRect, infoRect);
            return rects.HasRuleset
                ? new List<Rect> { Union(rects.RulesetMain, rects.RulesetMenu).ExpandedBy(4f) }
                : WholeTab(inRect);
        }

        internal static List<Rect> BottomInstructions(Rect inRect)
        {
            return new List<Rect>
            {
                new Rect(inRect.x, inRect.yMax - 44f, Mathf.Min(520f, inRect.width), 36f).ExpandedBy(4f)
            };
        }

        internal static List<Rect> InfoButton(Rect inRect)
        {
            return new List<Rect> { GetInfoIconRect(inRect).ExpandedBy(5f) };
        }

        internal static List<Rect> FirstDivider(Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            if (layout?.Rows != null)
            {
                for (int i = 0; i < layout.Rows.Count; i++)
                {
                    WorkTabLayoutRow row = layout.Rows[i];
                    if (row.Divider == null)
                    {
                        continue;
                    }

                    rects.Add(layout.GetScreenRect(row).ExpandedBy(4f));
                    break;
                }
            }

            AddWholeTabFallback(rects, inRect);
            return rects;
        }

        private static void AddWorkHeaderArea(List<Rect> rects, IWorkTabLayoutController layout)
        {
            if (layout?.Columns == null)
            {
                return;
            }

            Rect union = Rect.zero;
            bool hasAny = false;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                union = hasAny ? Union(union, column.HeaderRect) : column.HeaderRect;
                hasAny = true;
            }

            if (hasAny)
            {
                rects.Add(union.ExpandedBy(6f));
            }
        }

        private static void AddGlobalPriorityRow(List<Rect> rects, IWorkTabLayoutController layout)
        {
            if (layout?.Table == null || !SubWorkDrilldownState.IsActive)
            {
                return;
            }

            Rect rect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight + TimePriorityScheduleEditor.HeaderPinnedRowsHeight,
                Mathf.Max(layout.Table.Size.x - 16f, 1f),
                Mathf.Max(SubWorkDrilldownState.GlobalRowVisibleHeight, SubWorkDrilldownState.GlobalRowHeight));
            rects.Add(rect.ExpandedBy(4f));
        }

        private static void AddFirstPriorityCell(
            List<Rect> rects,
            IWorkTabLayoutController layout,
            bool requireSubWorkColumn,
            bool allowDisabledFallback)
        {
            if (layout?.Rows == null || layout.Columns == null)
            {
                return;
            }

            Rect fallbackRect = Rect.zero;
            bool hasFallback = false;
            for (int r = 0; r < layout.Rows.Count; r++)
            {
                WorkTabLayoutRow row = layout.Rows[r];
                if (row.Pawn == null)
                {
                    continue;
                }

                Rect rowRect = layout.GetScreenRect(row);
                for (int c = 0; c < layout.Columns.Count; c++)
                {
                    WorkTabLayoutColumn column = layout.Columns[c];
                    if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                    {
                        continue;
                    }

                    if (requireSubWorkColumn &&
                        !SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out _, out _))
                    {
                        continue;
                    }

                    Rect cellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, rowRect.height);
                    Rect priorityBoxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect).ExpandedBy(5f);
                    if (!hasFallback)
                    {
                        fallbackRect = priorityBoxRect;
                        hasFallback = true;
                    }

                    if (CanUsePriorityExample(row.Pawn, column.Column.workType))
                    {
                        rects.Add(priorityBoxRect);
                        return;
                    }
                }
            }

            if (allowDisabledFallback && hasFallback)
            {
                rects.Add(fallbackRect);
            }
        }

        private static bool CanUsePriorityExample(Pawn pawn, WorkTypeDef workType)
        {
            return pawn != null &&
                !pawn.Dead &&
                pawn.workSettings != null &&
                pawn.workSettings.EverWork &&
                workType != null &&
                !pawn.WorkTypeIsDisabled(workType);
        }

        private static Rect GetSubWorkExitRect(Rect inRect)
        {
            const float buttonSize = 24f;
            float topRightReservedWidth = HeaderButtons.GetTopRightReservedWidth();
            return new Rect(
                    inRect.xMax - buttonSize - RightEdgeMargin - topRightReservedWidth,
                    inRect.y + 8f,
                    buttonSize,
                    buttonSize)
                .ExpandedBy(5f);
        }

        private static Rect GetInfoIconRect(Rect inRect)
        {
            return new Rect(
                inRect.xMax - InfoIconSize - RightEdgeMargin,
                inRect.yMax - InfoIconSize - 10f,
                InfoIconSize,
                InfoIconSize);
        }

        private static void AddWholeTabFallback(List<Rect> rects, Rect inRect)
        {
            if (rects.Count == 0)
            {
                rects.Add(inRect.ContractedBy(12f));
            }
        }

        private static Rect Union(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
