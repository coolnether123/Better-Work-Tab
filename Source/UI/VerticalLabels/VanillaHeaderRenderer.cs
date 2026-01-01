using UnityEngine;
using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.PawnOrganizer;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Renders vanilla-style headers with the yellow asterisk marker when columns are moved.
    /// Automatically repositions headers vertically if they would overlap with neighbors.
    /// This renderer is only used when angled headers are disabled AND a column needs the marker.
    /// </summary>
    public class VanillaHeaderRenderer : IHeaderRenderer
    {
        // Global layout cache for the current frame
        private static int _layoutFrame = -1;
        private static Dictionary<PawnColumnDef, float> _frameOffsets = new Dictionary<PawnColumnDef, float>();
        
        /// <summary>
        /// Solves the layout for the entire header row once per frame.
        /// Uses a Left-to-Right greedy packing algorithm to prevent overlaps.
        /// </summary>
        private void EnsureLayoutBuilt(PawnTable table)
        {
            // Safety: Ensure static cache is initialized
            if (_frameOffsets == null) _frameOffsets = new Dictionary<PawnColumnDef, float>();

            // Frame check
            if (Time.frameCount == _layoutFrame) return;
            _layoutFrame = Time.frameCount;
            _frameOffsets.Clear();

            // Safety: access singleton safely
            var organizer = PawnOrganizerSystem.Instance;
            if (organizer == null) return;
            
            var layout = organizer.Layout;
            if (layout == null || layout.Columns == null) return;

            // We need to simulate the placement of every active column
            List<Rect> placedRects = new List<Rect>();

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;

            float rowHeight = Text.LineHeight + 2f; 

            // Iterate through all columns in visual order
            foreach (var colData in layout.Columns)
            {
                // colData is a struct, so it cannot be null, but its properties might be
                PawnColumnDef col = null;
                try { col = colData.Column; } catch { continue; }
                
                if (col == null) continue;
                if (col.Worker == null) continue;
                if (!(col.Worker is PawnColumnWorker_WorkPriority)) continue;

                var workType = col.workType;
                
                bool showMarker = false;
                try 
                {
                    if (workType != null)
                        showMarker = MainTabWindow_BetterWork.ShouldShowColumnMarker(workType);
                }
                catch { } // Swallow
                
                string text = "Header";
                try 
                { 
                    if (!string.IsNullOrEmpty(col.LabelCap)) 
                        text = col.LabelCap; 
                } 
                catch { }

                if (showMarker && !text.EndsWith("*")) text += "*";

                Vector2 textSize = Vector2.zero;
                if (!string.IsNullOrEmpty(text))
                {
                    textSize = Text.CalcSize(text);
                }
                
                Rect headerRect;
                try { headerRect = colData.HeaderRect; } catch { headerRect = default(Rect); }

                // Determine base rect
                Rect placedRect = new Rect(
                    headerRect.center.x - textSize.x / 2f,
                    0f, 
                    textSize.x,
                    textSize.y
                );
                
                // Expand rect slightly for padding
                Rect collisionRect = placedRect;
                collisionRect.xMin -= 2f;
                collisionRect.xMax += 2f;

                // Find a valid Y-level
                int level = 0;
                while (true)
                {
                    float currentY = level * rowHeight;
                    collisionRect.y = currentY; 
                    
                    bool overlap = false;
                    for (int i = 0; i < placedRects.Count; i++)
                    {
                        if (collisionRect.Overlaps(placedRects[i]))
                        {
                            overlap = true;
                            break;
                        }
                    }

                    if (!overlap)
                    {
                        // Found a spot!
                        if (col != null)
                        {
                            _frameOffsets[col] = currentY;
                        }
                        
                        Rect finalOccupied = collisionRect;
                        finalOccupied.y = currentY;
                        placedRects.Add(finalOccupied);
                        break;
                    }

                    level++;
                    if (level > 10) 
                    {
                         if (col != null)
                            _frameOffsets[col] = currentY;
                         break;
                    }
                }
            }

            Text.Font = oldFont;
        }

        public void DrawHeader(AngledLabelDrawer.AngledLabelLayout layout, bool isMouseOver, 
                               bool isSorted, bool sortDescending, Rect headerRect, 
                               PawnColumnDef column, bool showMarker)
        {
            // Ensure global layout is solved for this frame
            // We pass 'null' for table since we access it via singleton/manager if needed, 
            // but ideally we should pass the table if available. 
            // layout.Columns in EnsureLayoutBuilt comes from PawnOrganizerSystem.
            EnsureLayoutBuilt(null);

            // Calculate text with marker if needed
            string text = layout.Text;
            if (showMarker && !text.EndsWith("*"))
            {
                text += "*";
            }

            // Use vanilla font and sizing
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 textSize = Text.CalcSize(text);

            // Get cached offset from the solver
            float yOffset = 0f;
            if (_frameOffsets.TryGetValue(column, out float offset))
            {
                yOffset = offset;
            }

            // Apply Y offset to header rect
            Rect adjustedHeaderRect = headerRect;
            adjustedHeaderRect.y += yOffset;

            // Position text at BOTTOM of header area like vanilla does
            // For baseline (yOffset=0), text sits near headerRect.yMax
            // The stem gap constant matches vanilla's spacing
            float stemGap = 4f;
            float textY = headerRect.yMax - textSize.y - stemGap - yOffset;
            
            // For displaced headers, they move UP from the baseline
            // yOffset pushes them up by that amount
            Rect textRect = new Rect(
                headerRect.center.x - textSize.x / 2f, 
                textY, 
                textSize.x, 
                textSize.y
            );

            // Drawing logic
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            
            Text.Anchor = TextAnchor.MiddleCenter;
            
            // Highlights (use the adjusted header rect - full column width but shifted down)
            if (isMouseOver)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.2f);
                Widgets.DrawHighlight(adjustedHeaderRect);
            }
            
            // Multi-select highlight (mod feature)
            if (column != null && ColumnSelectionManager.IsSelected(column))
            {
                GUI.color = new Color(1f, 0.92f, 0.4f, 0.4f);
                Widgets.DrawHighlight(adjustedHeaderRect);
            }

            // Text color: yellow if showing marker, otherwise use setting
            GUI.color = showMarker 
                ? new Color(1f, 0.85f, 0.2f, 1f) 
                : BetterWorkTabMod.Settings.angledHeaderColor;
            
            Widgets.Label(textRect, text);

            // Draw stem line from bottom of text down to the column (like vanilla)
            // This applies to ALL headers, not just displaced ones
            {
                float centerX = textRect.center.x;
                
                // Start at bottom of the text
                float stemTop = textRect.yMax;
                // End at bottom of entire header area (where the column data begins)
                float stemBottom = headerRect.yMax;
                float stemHeight = stemBottom - stemTop;
                
                // Draw if there's enough space for a visible line
                if (stemHeight >= 2f)
                {
                    Rect stemRect = new Rect(centerX - 1f, stemTop, 2f, stemHeight);
                    Widgets.DrawBoxSolid(stemRect, new Color(1f, 1f, 1f, 0.35f));
                }
            }
            
            // Sort indicator (vanilla)
            if (isSorted)
            {
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Rect sortRect = new Rect(textRect.xMax + 2f, textRect.y, 10f, 10f);
                Widgets.Label(sortRect, sortDescending ? "▼" : "▲");
            }
            
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        // Removed old CalculateYOffset and related caches as they are replaced by the global solver
        private Rect CheckAndRepositionIfNeeded(Rect labelRect, PawnColumnDef column, Rect headerRect) => labelRect;
        
        public static void ClearCache()
        {
            _layoutFrame = -1;
            _frameOffsets.Clear();
        }
    }
}
