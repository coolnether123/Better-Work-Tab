using UnityEngine;
using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Solves the layout for ALL vanilla headers in a single pass.
    /// FIXED: Coordinate system now matches Vanilla staggering exactly.
    /// </summary>
    public class VanillaHeaderLayoutSolver
    {
        private int _solvedFrame = -1;
        private Dictionary<PawnColumnDef, float> _frameOffsets = new Dictionary<PawnColumnDef, float>();

        // Height of one "step" in the stagger - exact vanilla spacing
        private const float LevelStepHeight = 20f;
        
        // Base offset for level 0 - distance from header bottom to TEXT MIDDLE
        // Adjusted to 19px to achieve 2px gap between text bottom and stem line
        // Level 0 (low): 19px from middle of text to pawn box
        // Level 1 (high): 39px from middle of text to pawn box
        private const float Level0Offset = 19f; 

        private List<ColumnLayoutInfo> _columns = new List<ColumnLayoutInfo>();
        private List<Rect> _placedRects = new List<Rect>();

        public void SolveLayout(PawnTable table)
        {
            if (Time.frameCount == _solvedFrame) return;

            _solvedFrame = Time.frameCount;
            _frameOffsets.Clear();
            _columns.Clear();
            _placedRects.Clear();

            if (table == null) return;
            var tableDef = table.def;
            if (tableDef?.columns == null) return;

            float currentX = 0f;
            foreach (var col in tableDef.columns)
            {
                if (col?.Worker == null) continue;
                if (!(col.Worker is PawnColumnWorker_WorkPriority)) continue;

                var workType = col.workType;
                if (workType == null) continue;

                string text = !col.LabelCap.NullOrEmpty() ? col.LabelCap.ToString() : "Work";
                bool isMoved = workType != null && MainTabWindow_BetterWork.ShouldShowColumnMarker(workType);

                if (isMoved && !text.EndsWith("*"))
                    text += "*";

                Vector2 textSize = Text.CalcSize(text);

                // === FIX: VANILLA LEVEL LOGIC ===
                // moveWorkTypeLabelDown = TRUE  -> Low Position  (Level 0)
                // moveWorkTypeLabelDown = FALSE -> High Position (Level 1)
                // Previous code had this inverted.
                int vanillaLevel = col.moveWorkTypeLabelDown ? 0 : 1;

                _columns.Add(new ColumnLayoutInfo
                {
                    ColumnDef = col,
                    HeaderRect = new Rect(currentX, 0f, col.width, 30f),
                    TextSize = textSize,
                    VanillaLevel = vanillaLevel,
                    IsMoved = isMoved,
                    Text = text
                });

                currentX += col.width;
            }

            // PASS 1: Place non-moved columns at vanilla levels
            foreach (var colInfo in _columns)
            {
                if (colInfo.IsMoved) continue;

                // Calculate offset: Level 0 = 19px, Level 1 = 39px, Level 2 = 59px, etc.
                float yOffset = Level0Offset + (colInfo.VanillaLevel * LevelStepHeight);
                Rect screenCollisionRect = CalculateScreenCollisionRect(colInfo.HeaderRect, colInfo.TextSize, yOffset);

                _frameOffsets[colInfo.ColumnDef] = yOffset;
                _placedRects.Add(screenCollisionRect);
            }

            // PASS 2: Place moved columns
            foreach (var colInfo in _columns)
            {
                if (!colInfo.IsMoved) continue;

                int[] levelsToTry = GenerateLevelSearchOrder(colInfo.VanillaLevel);
                bool placed = false;

                foreach (int level in levelsToTry)
                {
                    // Calculate offset: Level 0 = 19px, Level 1 = 39px, Level 2 = 59px, etc.
                    float yOffset = Level0Offset + (level * LevelStepHeight);
                    Rect testScreenRect = CalculateScreenCollisionRect(colInfo.HeaderRect, colInfo.TextSize, yOffset);

                    if (!_placedRects.Any(placed2 => testScreenRect.Overlaps(placed2)))
                    {
                        _frameOffsets[colInfo.ColumnDef] = yOffset;
                        _placedRects.Add(testScreenRect);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    _frameOffsets[colInfo.ColumnDef] = Level0Offset; // Fallback to level 0 position
                }
            }
        }

        public float GetOffset(PawnColumnDef column)
        {
            if (column == null || _frameOffsets == null) return 0f;
            return _frameOffsets.TryGetValue(column, out float offset) ? offset : 0f;
        }

        /// <summary>
        /// Calculates the bounding box of the text in actual screen coordinates.
        /// This ensures collision detection matches what the user sees.
        /// </summary>
        private Rect CalculateScreenCollisionRect(Rect headerRect, Vector2 textSize, float yOffset)
        {
            const float padding = 1f;

            // Math must match VanillaHeaderRenderer exactly
            float headerBottom = headerRect.yMax;
            
            // yOffset = distance from headerBottom to TEXT MIDDLE
            // textMiddle = headerBottom - yOffset
            // textY (top) = textMiddle - (textSize.y / 2)
            float textY = headerBottom - yOffset - (textSize.y / 2f);

            return new Rect(
                headerRect.center.x - (textSize.x / 2f) - padding,
                textY - padding,
                textSize.x + (padding * 2f),
                textSize.y + (padding * 2f)
            );
        }

        private int[] GenerateLevelSearchOrder(int vanillaLevel)
        {
            var levels = new List<int> { vanillaLevel };
            for (int i = 1; i <= 5; i++)
            {
                if (vanillaLevel + i >= 0) levels.Add(vanillaLevel + i);
                if (vanillaLevel - i >= 0) levels.Add(vanillaLevel - i);
            }
            return levels.ToArray();
        }

        private class ColumnLayoutInfo
        {
            public PawnColumnDef ColumnDef;
            public Rect HeaderRect;
            public Vector2 TextSize;
            public int VanillaLevel;
            public bool IsMoved;
            public string Text;
        }
    }
}
