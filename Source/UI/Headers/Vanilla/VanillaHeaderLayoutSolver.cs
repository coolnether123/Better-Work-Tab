using UnityEngine;
using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGiverReassignments;

namespace Better_Work_Tab.UI.Headers.Vanilla
{
    /// <summary>
    /// Solves the layout for ALL vanilla headers in a single Pass.
    /// Coordinate system is designed to match Vanilla RimWorld staggering logic.
    /// Implements a backtracking solver to find optimal non-overlapping placements.
    /// </summary>
    public class VanillaHeaderLayoutSolver : FrameCachedSystem
    {
        private bool _solutionValid = false;
        private Dictionary<PawnColumnDef, float> _frameOffsets = new Dictionary<PawnColumnDef, float>();
        private Dictionary<PawnColumnDef, int> _levels = new Dictionary<PawnColumnDef, int>();
        private int _lastMaxLevel = 1;
        private int _layoutVersion;

        // Auto-invalidation Signature
        private int _currentSignature;
        private int _lastSolvedSignature;

        // Collection: gather actual header rects during Layout event
        private readonly Dictionary<PawnColumnDef, ColumnLayoutInfo> _collected = new Dictionary<PawnColumnDef, ColumnLayoutInfo>();

        // Vanilla work types generally fit in four rows. Dense sub-work groups need more headroom.
        private const int StandardMaxLevel = 3;
        private const int SubWorkMaxLevel = 7;

        private List<ColumnLayoutInfo> _columns = new List<ColumnLayoutInfo>();

        private const float CollisionPadding = HeaderUtility.CollisionPadding;
        private const float QuantizationStep = 0.25f;
        private const int SignatureSeed = 17;
        private const int SignatureMultiplier = 397;
        private const int MaxExactSolveNodes = 10;
        private const int MaxExactSolveSteps = 50000;

        private struct Cost
        {
            public int ExcessLevelSum;     // Sum(max(0, level - VanillaLevel))
            public int VanillaMovedCount;  // How many vanilla headers were displaced
            public int LevelSum;           // Total sum of levels (compaction)
            public int VanillaDeviationSum;// Total distance vanilla headers moved

            /// <summary>
            /// Definition of cost priorities for tie-breaking and pruning.
            /// 1. Minimize height above vanilla (ExcessLevelSum)
            /// 2. Minimize displaced vanilla headers (VanillaMovedCount)
            /// 3. Minimize total levels (LevelSum)
            /// 4. Minimize distance moved (VanillaDeviationSum)
            /// </summary>
            private static readonly (System.Func<Cost, int> selector, string description)[] Priorities =
            {
                (c => c.ExcessLevelSum, "Minimize height above vanilla"),
                (c => c.VanillaMovedCount, "Minimize displaced vanilla headers"),
                (c => c.LevelSum, "Minimize total levels"),
                (c => c.VanillaDeviationSum, "Minimize distance moved"),
            };

            public static Cost MaxValue => new Cost
            {
                ExcessLevelSum = int.MaxValue / 4,
                VanillaMovedCount = int.MaxValue / 4,
                LevelSum = int.MaxValue / 4,
                VanillaDeviationSum = int.MaxValue / 4
            };

            public bool IsBetterThan(Cost other)
            {
                foreach (var (selector, _) in Priorities)
                {
                    int thisVal = selector(this);
                    int otherVal = selector(other);
                    if (thisVal != otherVal) return thisVal < otherVal;
                }
                return false;
            }

            public void AddFor(ColumnLayoutInfo info, int level)
            {
                // Both Level 0 and Level 1 are "Free" in terms of vanilla header height (~50px).
                // Only levels 2+ increase the required height and shrink the content area.
                ExcessLevelSum += Mathf.Max(0, level - 1);
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
        /// Call to invalidate the current solution and force a recalculation on next SolveLayout call.
        /// Should be called when columns are moved or settings change.
        /// </summary>
        public void InvalidateSolution()
        {
            _solutionValid = false;
        }

        /// <summary>
        /// Returns true if the solver has a valid solution for the current frame.
        /// </summary>
        public bool HasValidSolution()
        {
            return _solutionValid;
        }

        public int LayoutVersion => _layoutVersion;

        private static int Quant(float v) => Mathf.RoundToInt(v / QuantizationStep);

        private void BeginCollectSignature()
        {
            unchecked { _currentSignature = SignatureSeed; }
        }

        private void AddToCollectSignature(ColumnLayoutInfo info)
        {
            unchecked
            {
                // PawnColumnDef is stable; GetHashCode is fine for a signature.
                _currentSignature = _currentSignature * SignatureMultiplier ^ (info.ColumnDef?.GetHashCode() ?? 0);

                // Quantize floats so tiny float jitter does not interfere with the solver.
                _currentSignature = _currentSignature * SignatureMultiplier ^ Quant(info.HeaderRect.x);
                _currentSignature = _currentSignature * SignatureMultiplier ^ Quant(info.HeaderRect.width);
                _currentSignature = _currentSignature * SignatureMultiplier ^ Quant(info.TextSize.x);
                _currentSignature = _currentSignature * SignatureMultiplier ^ info.VanillaLevel;
                _currentSignature = _currentSignature * SignatureMultiplier ^ (info.IsMoved ? 1 : 0);
            }
        }

        /// <summary>
        /// Collects the actual header rect and work type info during Layout event.
        /// This gathers REAL runtime data instead of trying to reconstruct from PawnColumnDef.
        /// </summary>
        public void CollectHeader(PawnColumnDef colDef, Rect headerRect, WorkTypeDef workType, bool isMoved)
        {
            if (colDef == null || workType == null) return;

            // Collect per-frame during Layout. 
            // CRITICAL: We only clear at the start of a Layout event.
            // If we clear during Repaint, we'll have no data for the rest of the frame 
            // (since headers are drawn one by one and solver needs the full set).
            if (Event.current.type == EventType.Layout && IsFrameNew())
            {
                _collected.Clear();
                BeginCollectSignature();
            }
            else if (IsFrameNew())
            {
                // If it's a new frame but NOT a layout event, we just prepare for a potential re-solve
                // but we DO NOT clear the collected data from the previous layout pass.
                BeginCollectSignature();
            }

            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;

                string text = HeaderUtility.GetHeaderText(
                    workType,
                    isMoved,
                    WorkGiverHeaderLabelStyle.VanillaStaggered);
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

        /// <summary>
        /// Solves the layout for all collected headers if the signature has changed.
        /// </summary>
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

                // Exact coloring is useful for normal vanilla-sized groups, but large work-type
                // mods can create dense components where exhaustive backtracking is exponential.
                int[] bestAssign = null;
                if (localNodes.Length <= MaxExactSolveNodes)
                {
                    var problem = new ColoringProblem(localNodes, order, GetMaxLevel(), MaxExactSolveSteps);
                    bestAssign = problem.Solve();
                }

                // Greedy first-fit is the bounded path for dense modded work tabs and the fallback
                // if the exact solver exhausts its search budget.
                if (bestAssign == null)
                {
                    bestAssign = GreedyColoring(localNodes, order, GetMaxLevel());
                }

                int localMax = 0;
                int localSum = 0;

                for (int i = 0; i < localNodes.Length; i++)
                {
                    var info = localNodes[i].Info;
                    int level = bestAssign[i];

                    _levels[info.ColumnDef] = level;
                    float yOffset = VanillaHeaderMetrics.GetYOffset(level);
                    _frameOffsets[info.ColumnDef] = yOffset;
                    
                    if (level > localMax) localMax = level;
                    localSum += level;
                    
                    debugLog.AppendLine($"  '{info.Text}' | Level {level} (Vanilla={info.VanillaLevel}) | X={localNodes[i].XMin:F1}..{localNodes[i].XMax:F1}");
                }

                if (localMax > globalMaxLevel) globalMaxLevel = localMax;
                globalLevelSum += localSum;

                debugLog.AppendLine($"  -- Component Max: {localMax} | Sum: {localSum}");
            }

            debugLog.AppendLine("═══════════════════════════════════════════════════════════════");
            debugLog.AppendLine($"Global Max Level: {globalMaxLevel} | Global Level Sum: {globalLevelSum}");
            
            if (BetterWorkTabMod.Settings.debugPrintLayout)
            {
                Log.Message(debugLog.ToString());
            }

            if (_lastMaxLevel != globalMaxLevel)
            {
                _layoutVersion++;
            }

            _lastMaxLevel = globalMaxLevel;
            _lastSolvedSignature = _currentSignature;
            _solutionValid = true;
        }

        /// <summary>
        /// Gets the vertical offset for a specific column based on the solved layout.
        /// </summary>
        public float GetOffset(PawnColumnDef column)
        {
            if (column == null || _frameOffsets == null) return 0f;
            return _frameOffsets.TryGetValue(column, out float offset) ? offset : 0f;
        }

        /// <summary>
        /// Gets the maximum stagger level used in the current OR last valid solution.
        /// Preserved across InvalidateSolution() calls to prevent header height jumps.
        /// </summary>
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
                return RectCompat.Zero;

            float yOffset = GetOffset(column);
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

        /// <summary>
        /// Dedicated solver for the header coloring (staggering) problem using backtracking DFS.
        /// Extracts the algorithm from the main controller to manage state cleanly.
        /// </summary>
        private static int GetMaxLevel()
        {
            return SubWorkDrilldownState.IsActive ? SubWorkMaxLevel : StandardMaxLevel;
        }

        private class ColoringProblem
        {
            private readonly Node[] _nodes;
            private readonly int[] _order;
            private readonly int[] _assignment;
            private readonly int _maxLevel;
            private readonly int _maxSteps;
            private int[] _bestAssign;
            private Cost _bestCost;
            private int _steps;

            public ColoringProblem(Node[] nodes, int[] order, int maxLevel, int maxSteps)
            {
                _nodes = nodes;
                _order = order;
                _maxLevel = maxLevel;
                _maxSteps = maxSteps;
                _assignment = new int[nodes.Length];
                ArrayCompat.Fill(_assignment, -1);
                _bestCost = Cost.MaxValue;
            }

            public int[] Solve()
            {
                Dfs(0, new Cost());
                return _bestAssign;
            }

            private void Dfs(int pos, Cost costSoFar)
            {
                if (_steps++ > _maxSteps)
                {
                    return;
                }

                // Base case: All nodes in this component assigned a level
                if (pos == _order.Length)
                {
                    if (costSoFar.IsBetterThan(_bestCost))
                    {
                        _bestCost = costSoFar;
                        _bestAssign = (int[])_assignment.Clone();
                    }
                    return;
                }

                int v = _order[pos];
                Node node = _nodes[v]; // Copy into local for fast neighbor access

                // Optimization: Try levels in a stable order
                for (int level = 0; level <= _maxLevel; level++)
                {
                    // Check for vertical collisions with already-assigned neighbors
                    bool ok = true;
                    foreach (int nb in node.Neighbors)
                    {
                        if (_assignment[nb] == level)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (!ok) continue;

                    Cost next = costSoFar;
                    next.AddFor(node.Info, level);

                    // Centralized Pruning: Skip this branch if it's already worse than our best solution
                    if (!next.IsBetterThan(_bestCost)) continue;

                    // Recursively solve next node in order
                    _assignment[v] = level;
                    Dfs(pos + 1, next);
                    _assignment[v] = -1; // Backtrack
                }
            }
        }

        /// <summary>
        /// Emergency backup coloring using a greedy first-fit approach.
        /// Called only if the DFS fails to find any solution (though a failure is not mathematically anticipated for this constraint set).
        /// </summary>
        private static int[] GreedyColoring(Node[] nodes, int[] order, int maxLevel)
        {
            int[] assignment = Enumerable.Repeat(-1, nodes.Length).ToArray();
            foreach (int v in order)
            {
                for (int level = 0; level <= maxLevel; level++)
                {
                    bool ok = true;
                    foreach (int nb in nodes[v].Neighbors)
                    {
                        if (assignment[nb] == level)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok)
                    {
                        assignment[v] = level;
                        break;
                    }
                }
                
                // Absolute fallback: just cap at maxLevel
                if (assignment[v] == -1)
                {
                    assignment[v] = maxLevel;
                }
            }
            return assignment;
        }
    }
}
