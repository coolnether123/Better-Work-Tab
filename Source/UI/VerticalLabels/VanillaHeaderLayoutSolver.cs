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
    /// PERFORMANCE: Self-validating signature check handles resizing/text changes automatically.
    /// </summary>
    public class VanillaHeaderLayoutSolver
    {
        private bool _solutionValid = false;
        private Dictionary<PawnColumnDef, float> _frameOffsets = new Dictionary<PawnColumnDef, float>();
        private Dictionary<PawnColumnDef, int> _levels = new Dictionary<PawnColumnDef, int>();
        private int _lastMaxLevel = 1;

        // Auto-invalidation Signature
        private int _currentSignature;
        private int _lastSolvedSignature;

        // Collection: gather actual header rects during Layout event
        private int _collectedFrame = -1;
        private readonly Dictionary<PawnColumnDef, ColumnLayoutInfo> _collected = new Dictionary<PawnColumnDef, ColumnLayoutInfo>();

        // Height of one "step" in the stagger - exact vanilla spacing
        private const float LevelStepHeight = 20f;
        
        // Base offset for level 0 - distance from header bottom to TEXT MIDDLE
        // Adjusted to 19px to achieve 2px gap between text bottom and stem line
        // Level 0 (low): 19px from middle of text to pawn box
        // Level 1 (high): 39px from middle of text to pawn box
        private const float Level0Offset = 19f; 

        // Max level for header placement
        private const int MaxLevel = 3;

        private List<ColumnLayoutInfo> _columns = new List<ColumnLayoutInfo>();

        private const float CollisionPadding = 1f;

        private struct Cost
        {
            public int ExcessLevelSum;     // Sum(max(0, level - VanillaLevel))
            public int VanillaMovedCount;
            public int LevelSum;
            public int VanillaDeviationSum;

            public static Cost MaxValue => new Cost
            {
                ExcessLevelSum = int.MaxValue / 4,
                VanillaMovedCount = int.MaxValue / 4,
                LevelSum = int.MaxValue / 4,
                VanillaDeviationSum = int.MaxValue / 4
            };

            public bool IsBetterThan(Cost other)
            {
                // Primary: minimize "Excess height" pushed above vanilla stagger
                if (ExcessLevelSum != other.ExcessLevelSum) return ExcessLevelSum < other.ExcessLevelSum;

                // Secondary: minimize how many "vanilla" (non-moved) headers we displace
                if (VanillaMovedCount != other.VanillaMovedCount) return VanillaMovedCount < other.VanillaMovedCount;

                // Tertiary: minimize total sum of levels for overall compaction
                if (LevelSum != other.LevelSum) return LevelSum < other.LevelSum;

                // Final tie-breaker: total distance moved
                return VanillaDeviationSum < other.VanillaDeviationSum;
            }

            public void AddFor(ColumnLayoutInfo info, int level)
            {
                ExcessLevelSum += Mathf.Max(0, level - info.VanillaLevel);
                LevelSum += level;
                
                if (!info.IsMoved)
                {
                    if (level != info.VanillaLevel)
                    {
                        VanillaMovedCount += 1;
                        VanillaDeviationSum += Mathf.Abs(level - info.VanillaLevel);
                    }
                }
            }
        }

        private struct Node
        {
            public ColumnLayoutInfo Info;
            public float XMin;
            public float XMax;
            public List<int> Neighbors;
        }

        private static void ComputeXInterval(Rect headerRect, Vector2 textSize, out float xMin, out float xMax)
        {
            float left = headerRect.center.x - (textSize.x / 2f) - CollisionPadding;
            float right = left + textSize.x + (CollisionPadding * 2f);
            xMin = left;
            xMax = right;
        }

        private static bool XOverlaps(Node a, Node b)
        {
            return a.XMin < b.XMax && a.XMax > b.XMin;
        }

        /// <summary>
        /// Call this to invalidate the current solution and force a recalculation on next SolveLayout call.
        /// Should be called when columns are moved or settings change.
        /// </summary>
        public void InvalidateSolution()
        {
            _solutionValid = false;
        }

        private static int Quant(float v, float step = 0.25f) => Mathf.RoundToInt(v / step);

        private void BeginCollectSignature()
        {
            unchecked { _currentSignature = 17; }
        }

        private void AddToCollectSignature(ColumnLayoutInfo info)
        {
            unchecked
            {
                // PawnColumnDef is stable; GetHashCode is fine for a signature.
                _currentSignature = _currentSignature * 397 ^ (info.ColumnDef?.GetHashCode() ?? 0);

                // Quantize floats so tiny float jitter doesn’t thrash the solver.
                _currentSignature = _currentSignature * 397 ^ Quant(info.HeaderRect.x);
                _currentSignature = _currentSignature * 397 ^ Quant(info.HeaderRect.width);
                _currentSignature = _currentSignature * 397 ^ Quant(info.TextSize.x);
                _currentSignature = _currentSignature * 397 ^ info.VanillaLevel;
                _currentSignature = _currentSignature * 397 ^ (info.IsMoved ? 1 : 0);
            }
        }

        /// <summary>
        /// Collects the actual header rect and work type info during Layout event.
        /// This gathers REAL runtime data instead of trying to reconstruct from PawnColumnDef (which has width=-1).
        /// </summary>
        public void CollectHeader(PawnColumnDef colDef, Rect headerRect, WorkTypeDef workType, bool isMoved)
        {
            if (colDef == null || workType == null) return;

            // Collect per-frame during Layout. Clear collection when frame changes.
            int frame = Time.frameCount;
            if (_collectedFrame != frame)
            {
                _collectedFrame = frame;
                _collected.Clear();
                BeginCollectSignature();
            }

            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;

                // Use actual work type label, not col.LabelCap (which is just "Work")
                string baseText = workType.labelShort;
                if (baseText.NullOrEmpty())
                    baseText = workType.label;
                if (baseText.NullOrEmpty())
                    baseText = workType.defName;

                string text = (baseText.NullOrEmpty() ? "Work" : baseText).CapitalizeFirst();
                if (isMoved && !text.EndsWith("*"))
                    text += "*";

                Vector2 textSize = Text.CalcSize(text);

                // Vanilla stagger flag lives on the PawnColumnDef.
                int vanillaLevel = colDef.moveWorkTypeLabelDown ? 0 : 1;

                var info = new ColumnLayoutInfo
                {
                    ColumnDef = colDef,
                    HeaderRect = headerRect,
                    TextSize = textSize,
                    VanillaLevel = vanillaLevel,
                    IsMoved = isMoved,
                    Text = text
                };

                _collected[colDef] = info;
                AddToCollectSignature(info);
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

        public void SolveLayout(PawnTable table)
        {
            if (_solutionValid && _currentSignature == _lastSolvedSignature) return;

            // Signature changed or invalidation requested -> re-solve
            _solutionValid = false; 

            if (table == null) return;

            _frameOffsets.Clear();
            _levels.Clear();
            _columns.Clear();

            if (_collected.Count == 0)
            {
                _lastSolvedSignature = _currentSignature;
                _solutionValid = true;
                return;
            }

            _columns.AddRange(_collected.Values);

            // Build nodes with X intervals
            var nodes = new List<Node>(_columns.Count);
            foreach (var info in _columns)
            {
                ComputeXInterval(info.HeaderRect, info.TextSize, out float xMin, out float xMax);
                nodes.Add(new Node
                {
                    Info = info,
                    XMin = xMin,
                    XMax = xMax,
                    Neighbors = null
                });
            }

            // Sort by XMin so we can split into connected components cheaply
            nodes.Sort((a, b) => a.XMin.CompareTo(b.XMin));

            // Split into components (interval-graph components)
            var components = new List<List<int>>();
            if (nodes.Count > 0)
            {
                int start = 0;
                float currentMaxX = nodes[0].XMax;

                for (int i = 1; i < nodes.Count; i++)
                {
                    if (nodes[i].XMin <= currentMaxX)
                    {
                        if (nodes[i].XMax > currentMaxX) currentMaxX = nodes[i].XMax;
                    }
                    else
                    {
                        var comp = new List<int>();
                        for (int k = start; k < i; k++) comp.Add(k);
                        components.Add(comp);

                        start = i;
                        currentMaxX = nodes[i].XMax;
                    }
                }
                var last = new List<int>();
                for (int k = start; k < nodes.Count; k++) last.Add(k);
                components.Add(last);
            }

            var debugLog = new System.Text.StringBuilder();
            debugLog.AppendLine($"[VanillaHeaderLayoutSolver] GLOBAL SOLVE (Frame {Time.frameCount})");
            debugLog.AppendLine("═══════════════════════════════════════════════════════════════");

            int globalMaxLevel = 1;
            int globalLevelSum = 0;

            foreach (var compIndices in components)
            {
                var localNodes = new Node[compIndices.Count];
                for (int i = 0; i < compIndices.Count; i++)
                {
                    var n = nodes[compIndices[i]];
                    n.Neighbors = new List<int>();
                    localNodes[i] = n;
                }

                for (int i = 0; i < localNodes.Length; i++)
                {
                    for (int j = i + 1; j < localNodes.Length; j++)
                    {
                        if (localNodes[j].XMin >= localNodes[i].XMax) break;
                        if (XOverlaps(localNodes[i], localNodes[j]))
                        {
                            localNodes[i].Neighbors.Add(j);
                            localNodes[j].Neighbors.Add(i);
                        }
                    }
                }

                var assignment = new int[localNodes.Length];
                for (int i = 0; i < assignment.Length; i++) assignment[i] = -1;

                var order = Enumerable.Range(0, localNodes.Length)
                                      .OrderByDescending(i => localNodes[i].Neighbors.Count)
                                      .ThenByDescending(i => (localNodes[i].XMax - localNodes[i].XMin))
                                      .ToArray();

                Cost bestCost = Cost.MaxValue;
                int[] bestAssign = null;

                void Dfs(int pos, Cost costSoFar)
                {
                    if (pos == order.Length)
                    {
                        if (costSoFar.IsBetterThan(bestCost))
                        {
                            bestCost = costSoFar;
                            bestAssign = (int[])assignment.Clone();
                        }
                        return;
                    }

                    int v = order[pos];
                    ref Node node = ref localNodes[v];

                    for (int level = 0; level <= MaxLevel; level++)
                    {
                        bool ok = true;
                        foreach (int nb in node.Neighbors)
                        {
                            if (assignment[nb] == level)
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (!ok) continue;

                        Cost next = costSoFar;
                        next.AddFor(node.Info, level);

                        // Pruning logic must match IsBetterThan priority
                        if (next.ExcessLevelSum > bestCost.ExcessLevelSum) continue;
                        if (next.ExcessLevelSum == bestCost.ExcessLevelSum && next.VanillaMovedCount > bestCost.VanillaMovedCount) continue;
                        if (next.ExcessLevelSum == bestCost.ExcessLevelSum && next.VanillaMovedCount == bestCost.VanillaMovedCount &&
                            next.LevelSum > bestCost.LevelSum) continue;
                        if (next.ExcessLevelSum == bestCost.ExcessLevelSum && next.VanillaMovedCount == bestCost.VanillaMovedCount &&
                            next.LevelSum == bestCost.LevelSum && next.VanillaDeviationSum >= bestCost.VanillaDeviationSum) continue;

                        assignment[v] = level;
                        Dfs(pos + 1, next);
                        assignment[v] = -1;
                    }
                }

                Dfs(0, new Cost());

                // Fallback greedy logic (using 'order' direction now, with -1 init)
                if (bestAssign == null)
                {
                    bestAssign = Enumerable.Repeat(-1, localNodes.Length).ToArray();
                    foreach (int v in order)
                    {
                        for (int level = 0; level <= MaxLevel; level++)
                        {
                            bool ok = true;
                            foreach (int nb in localNodes[v].Neighbors)
                            {
                                // Only check already-assigned neighbors
                                if (bestAssign[nb] == level) 
                                { 
                                    ok = false; 
                                    break; 
                                }
                            }
                            if (ok) 
                            { 
                                bestAssign[v] = level; 
                                break; 
                            }
                        }
                        // Absolute fallback if MaxLevel exceeded
                        if (bestAssign[v] == -1) 
                            bestAssign[v] = MaxLevel; 
                    }
                }

                int localMax = 0;
                int localSum = 0;

                for (int i = 0; i < localNodes.Length; i++)
                {
                    var info = localNodes[i].Info;
                    int level = bestAssign[i];

                    _levels[info.ColumnDef] = level;
                    float yOffset = Level0Offset + (level * LevelStepHeight);
                    _frameOffsets[info.ColumnDef] = yOffset;
                    
                    if (level > localMax) localMax = level;
                    localSum += level;
                    
                    debugLog.AppendLine($"  '{info.Text}' | Level {level} (Vanilla={info.VanillaLevel}) | X={localNodes[i].XMin:F1}..{localNodes[i].XMax:F1}");
                }

                if (localMax > globalMaxLevel) globalMaxLevel = localMax;
                globalLevelSum += localSum;

                // Component summary
                debugLog.AppendLine($"  -- Component Max: {localMax} | Sum: {localSum}");
            }

            debugLog.AppendLine("═══════════════════════════════════════════════════════════════");
            debugLog.AppendLine($"Global Max Level: {globalMaxLevel} | Global Level Sum: {globalLevelSum}");
            
            if (BetterWorkTabMod.Settings.debugPrintLayout)
            {
                Log.Message(debugLog.ToString());
            }

            _lastMaxLevel = globalMaxLevel;
            _lastSolvedSignature = _currentSignature;
            _solutionValid = true;
        }

        public float GetOffset(PawnColumnDef column)
        {
            if (column == null || _frameOffsets == null) return 0f;
            return _frameOffsets.TryGetValue(column, out float offset) ? offset : 0f;
        }

        public int GetMaxLevelUsed()
        {
            return _lastMaxLevel;
        }

        /// <summary>
        /// Returns the actual bounding box of the header label for collision/hover detection.
        /// </summary>
        public Rect GetBounds(PawnColumnDef column)
        {
            if (column == null || !_collected.TryGetValue(column, out var info))
                return Rect.zero;

            float yOffset = GetOffset(column);
            
            // Formula from CalculateScreenCollisionRect
            float headerBottom = info.HeaderRect.yMax;
            float textY = headerBottom - yOffset - (info.TextSize.y / 2f);

            return new Rect(
                info.HeaderRect.center.x - (info.TextSize.x / 2f) - CollisionPadding,
                textY - CollisionPadding,
                info.TextSize.x + (CollisionPadding * 2f),
                info.TextSize.y + (CollisionPadding * 2f)
            );
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
