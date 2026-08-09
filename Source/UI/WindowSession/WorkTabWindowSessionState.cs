using System;
using System.Reflection;
using Better_Work_Tab.Features.Patches;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WindowSession
{
    /// <summary>
    /// Owns the reflected PawnTable reference and the identity snapshot used to
    /// decide whether vanilla's open-time recache can be safely skipped.
    /// </summary>
    internal sealed class WorkTabWindowSessionState
    {
        // MainTabWindow_PawnTable keeps the table private. Keeping this single
        // reflection boundary here prevents the main window from owning table
        // identity and cache lifetime as well.
        private static readonly FieldInfo PawnTableField =
            typeof(MainTabWindow_PawnTable).GetField(
                "table",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly MainTabWindow_PawnTable _window;

        private PawnTable _cachedPawnTable;
        private Game _cachedPawnTableGame;
        private PawnTable _warmOpenTable;
        private Game _warmOpenGame;
        private Map _warmOpenMap;
        private int _warmOpenScreenWidth;
        private int _warmOpenScreenHeight;
        private int _warmOpenLayoutRevision = -1;
        private int _warmOpenPawnCount = -1;
        private int _warmOpenColumnCount = -1;
        private bool _warmOpenStateValid;
        private bool _warmOpenCacheActive;

        internal WorkTabWindowSessionState(MainTabWindow_PawnTable window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        internal PawnTable GetPawnTable(Game currentGame)
        {
            if (_cachedPawnTable == null || !ReferenceEquals(_cachedPawnTableGame, currentGame))
            {
                _cachedPawnTable = ReadPawnTable(_window);
                _cachedPawnTableGame = currentGame;
            }

            return _cachedPawnTable;
        }

        internal static PawnTable ReadPawnTable(MainTabWindow_PawnTable window)
        {
            return window == null
                ? null
                : (PawnTable)PawnTableField?.GetValue(window);
        }

        internal void InvalidatePawnTableCache()
        {
            _cachedPawnTable = null;
            _cachedPawnTableGame = null;
        }

        internal void BeginWarmOpenIfEligible(
            PawnTable table,
            Game currentGame,
            Map currentMap,
            int screenWidth,
            int screenHeight,
            int layoutRevision)
        {
            _warmOpenCacheActive = CanReuseWarmOpenTable(
                table,
                currentGame,
                currentMap,
                screenWidth,
                screenHeight,
                layoutRevision);
            if (_warmOpenCacheActive)
            {
                WarmOpenPawnTableCache.Begin(table);
            }
        }

        internal void EndWarmOpen()
        {
            if (!_warmOpenCacheActive)
            {
                return;
            }

            _warmOpenCacheActive = false;
            WarmOpenPawnTableCache.End();
        }

        internal void CaptureWarmOpenTableState(
            PawnTable table,
            Game currentGame,
            Map currentMap,
            int screenWidth,
            int screenHeight,
            int layoutRevision)
        {
            _warmOpenTable = table;
            _warmOpenGame = currentGame;
            _warmOpenMap = currentMap;
            _warmOpenScreenWidth = screenWidth;
            _warmOpenScreenHeight = screenHeight;
            _warmOpenLayoutRevision = layoutRevision;
            _warmOpenPawnCount = table?.cachedPawns?.Count ?? 0;
            _warmOpenColumnCount = table?.def?.columns?.Count ?? 0;
            _warmOpenStateValid = table != null && !table.dirty;
        }

        private bool CanReuseWarmOpenTable(
            PawnTable table,
            Game currentGame,
            Map currentMap,
            int screenWidth,
            int screenHeight,
            int layoutRevision)
        {
            return _warmOpenStateValid &&
                table != null &&
                !table.dirty &&
                ReferenceEquals(table, _warmOpenTable) &&
                ReferenceEquals(currentGame, _warmOpenGame) &&
                ReferenceEquals(currentMap, _warmOpenMap) &&
                screenWidth == _warmOpenScreenWidth &&
                screenHeight == _warmOpenScreenHeight &&
                layoutRevision == _warmOpenLayoutRevision &&
                (table.cachedPawns?.Count ?? 0) == _warmOpenPawnCount &&
                (table.def?.columns?.Count ?? 0) == _warmOpenColumnCount;
        }
    }
}
