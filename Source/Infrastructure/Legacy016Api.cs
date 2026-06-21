#if v0_16
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab
{
    internal static class Legacy016ModSettingsStore
    {
        private static readonly Dictionary<Type, ModSettings> SettingsByType = new Dictionary<Type, ModSettings>();

        public static T Get<T>() where T : ModSettings, new()
        {
            Type type = typeof(T);
            if (!SettingsByType.TryGetValue(type, out ModSettings settings))
            {
                settings = new T();
                SettingsByType[type] = settings;
            }

            return (T)settings;
        }
    }

    internal static class Legacy016GameComponentStore
    {
#if v0_13
        private static readonly Dictionary<Type, GameComponent> ComponentsByType =
            new Dictionary<Type, GameComponent>();

        public static T Get<T>() where T : GameComponent
        {
            Type type = typeof(T);
            if (!ComponentsByType.TryGetValue(type, out GameComponent component))
            {
                component = (GameComponent)Activator.CreateInstance(type);
                ComponentsByType[type] = component;
                component.FinalizeInit();
            }

            return (T)component;
        }
#else
        private static readonly Dictionary<Game, Dictionary<Type, GameComponent>> ComponentsByGame =
            new Dictionary<Game, Dictionary<Type, GameComponent>>();

        public static T Get<T>(Game game) where T : GameComponent
        {
            if (game == null)
                return null;

            if (!ComponentsByGame.TryGetValue(game, out Dictionary<Type, GameComponent> components))
            {
                components = new Dictionary<Type, GameComponent>();
                ComponentsByGame[game] = components;
            }

            Type type = typeof(T);
            if (!components.TryGetValue(type, out GameComponent component))
            {
                component = (GameComponent)Activator.CreateInstance(type, game);
                components[type] = component;
                component.FinalizeInit();
            }

            return (T)component;
        }
#endif
    }
}

namespace Verse
{
#if v0_13
    public class ModContentPack
    {
    }

    public enum ProgramState
    {
        Entry,
        Playing,
        MapPlaying
    }

    public sealed class LegacyGameInfo
    {
        public string permadeathModeUniqueName
        {
            get { return null; }
        }
    }

    public sealed class LegacyCurrentGame
    {
        public RimWorld.PlaySettings playSettings
        {
            get { return Find.PlaySettings; }
        }

        public RimWorld.Planet.World World
        {
            get { return RimWorld.Current.World; }
        }

        public LegacyGameInfo Info
        {
            get { return null; }
        }

        public T GetComponent<T>() where T : GameComponent
        {
            return Better_Work_Tab.Legacy016GameComponentStore.Get<T>();
        }
    }

    public static class Current
    {
        private static readonly LegacyCurrentGame currentGame = new LegacyCurrentGame();

        public static LegacyCurrentGame Game
        {
            get { return currentGame; }
        }

        public static ProgramState ProgramState
        {
            get { return Find.Map != null ? Verse.ProgramState.MapPlaying : Verse.ProgramState.Entry; }
        }
    }
#endif

    public abstract class ModSettings : IExposable
    {
        public virtual void ExposeData()
        {
        }

        public virtual void Write()
        {
        }
    }

    public class Mod
    {
        protected ModContentPack Content { get; private set; }

        public Mod(ModContentPack content)
        {
            Content = content;
        }

        protected T GetSettings<T>() where T : ModSettings, new()
        {
            return Better_Work_Tab.Legacy016ModSettingsStore.Get<T>();
        }

        public virtual string SettingsCategory()
        {
            return null;
        }

        public virtual void DoSettingsWindowContents(Rect inRect)
        {
        }
    }

    public abstract class GameComponent : IExposable
    {
#if v0_13
        protected object game;
#else
        protected Game game;
#endif

        protected GameComponent()
        {
        }

#if v0_13
        protected GameComponent(object game)
#else
        protected GameComponent(Game game)
#endif
        {
            this.game = game;
        }

        public virtual void FinalizeInit()
        {
        }

        public virtual void GameComponentUpdate()
        {
        }

        public virtual void GameComponentOnGUI()
        {
        }

        public virtual void LoadedGame()
        {
        }

        public virtual void StartedNewGame()
        {
        }

        public virtual void ExposeData()
        {
        }
    }

#if !v0_13
    public static class GameComponentCompatExtensions
    {
        public static T GetComponent<T>(this Game game) where T : GameComponent
        {
            return Better_Work_Tab.Legacy016GameComponentStore.Get<T>(game);
        }
    }
#endif

#if v0_15
    public static class UI
    {
        public static int screenWidth => Screen.width;
        public static int screenHeight => Screen.height;
        public static Vector2 MousePositionOnUI => Event.current?.mousePosition ?? Vector2.zero;

        public static void FocusControl(string controlName, Window window = null)
        {
            GUI.FocusControl(controlName);
        }

        public static void UnfocusCurrentControl()
        {
            GUI.FocusControl(null);
        }
    }

    public class Dialog_MessageBox : Window
    {
        private readonly string text;
        private readonly Action confirmedAct;
        private readonly string title;

#if v0_13
        public override Vector2 InitialWindowSize => new Vector2(520f, 220f);
#else
        public override Vector2 InitialSize => new Vector2(520f, 220f);
#endif

        private Dialog_MessageBox(string text, Action confirmedAct, string title)
        {
            this.text = text;
            this.confirmedAct = confirmedAct;
            this.title = title;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            doCloseX = true;
        }

        public static Dialog_MessageBox CreateConfirmation(
            string text,
            Action confirmedAct,
            bool destructive = false,
            string title = null)
        {
            return new Dialog_MessageBox(text, confirmedAct, title);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            float top = 0f;
            if (!string.IsNullOrEmpty(title))
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), title);
                Text.Font = GameFont.Small;
                top = 38f;
            }

            Widgets.Label(new Rect(0f, top, inRect.width, inRect.height - top - 48f), text ?? string.Empty);

            Rect confirmRect = new Rect(inRect.width - 190f, inRect.height - 35f, 85f, 32f);
            Rect cancelRect = new Rect(inRect.width - 95f, inRect.height - 35f, 85f, 32f);
            if (Better_Work_Tab.WidgetsCompat.ButtonText(confirmRect, "OK".Translate()))
            {
                confirmedAct?.Invoke();
                Close();
            }

            if (Better_Work_Tab.WidgetsCompat.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }
    }
#endif
}

namespace RimWorld
{
    public class MainButtonDef : MainTabDef
    {
    }

    public class PawnTableDef : Def
    {
        public List<PawnColumnDef> columns = new List<PawnColumnDef>();
    }

    public static class PawnTableDefOf
    {
        private static PawnTableDef work;

        public static PawnTableDef Work
        {
            get
            {
                if (work == null)
                    work = Legacy016PawnTableFactory.CreateWorkTableDef();

                return work;
            }
        }
    }

    public class PawnColumnDef : Def
    {
        private PawnColumnWorker workerInt;

        public WorkTypeDef workType;
        public Type workerClass;
        public float width = 36f;
        public bool showIcon = true;
        public bool groupable;
        public bool moveWorkTypeLabelDown;
        public bool sortable = true;

        public PawnColumnWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    Type type = workerClass ?? typeof(PawnColumnWorker);
                    workerInt = (PawnColumnWorker)Activator.CreateInstance(type);
                    workerInt.def = this;
                }

                return workerInt;
            }
        }
    }

    public class PawnTable
    {
        public PawnTableDef def;
        public List<PawnColumnDef> Columns = new List<PawnColumnDef>();
        public List<Pawn> cachedPawns = new List<Pawn>();
        public List<float> cachedColumnWidths = new List<float>();
        public List<float> cachedRowHeights = new List<float>();
        public float cachedHeaderHeight = 50f;
        public Vector2 cachedSize = new Vector2(1010f, 300f);
        public Vector2 scrollPosition = Vector2.zero;
        public PawnColumnDef SortingBy;
        public bool SortingDescending;

        public IList<PawnColumnDef> ColumnsListForReading => Columns;
        public List<Pawn> PawnsListForReading => cachedPawns;
        public float HeaderHeight => cachedHeaderHeight;
        public Vector2 Size => cachedSize;

        public PawnTable()
        {
            def = PawnTableDefOf.Work;
            Columns.AddRange(def.columns);
        }

        public void SetDirty()
        {
        }

        public void SortBy(PawnColumnDef column, bool descending)
        {
            SortingBy = column;
            SortingDescending = descending;
        }

        public void RecacheIfDirty()
        {
        }

        public void PawnTableOnGUI(Vector2 position)
        {
        }

        public int PrimarySortFunction(Pawn left, Pawn right)
        {
            if (SortingBy == null)
                return 0;

            int result = SortingBy.Worker.Compare(left, right);
            return SortingDescending ? -result : result;
        }
    }

    public class PawnColumnWorker
    {
        public PawnColumnDef def;

        public virtual void DoHeader(Rect rect, PawnTable table)
        {
            if (def?.workType != null)
                Widgets.Label(rect, def.workType.labelShort);
        }

        public virtual void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
        }

        public virtual int GetMinHeaderHeight(PawnTable table)
        {
            return 50;
        }

        public virtual int Compare(Pawn left, Pawn right)
        {
            return 0;
        }
    }

    public class PawnColumnWorker_Label : PawnColumnWorker
    {
        public virtual int GetMinCellHeight(Pawn pawn)
        {
            return 30;
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (pawn == null)
                return;

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect, pawn.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }

    public class PawnColumnWorker_WorkPriority : PawnColumnWorker
    {
        public override void DoHeader(Rect rect, PawnTable table)
        {
            if (def?.workType == null)
                return;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, def.workType.labelShort);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (pawn == null || def?.workType == null)
                return;

            Better_Work_Tab.WidgetsWorkCompat.DrawWorkBoxFor(rect.x, rect.y, pawn, def.workType, false);
        }

        public override int GetMinHeaderHeight(PawnTable table)
        {
            return base.GetMinHeaderHeight(table);
        }

        public virtual void HeaderClicked(Rect headerRect, PawnTable table)
        {
        }

        public override int Compare(Pawn left, Pawn right)
        {
            if (def?.workType == null)
                return 0;

            int leftPriority = left?.workSettings?.GetPriority(def.workType) ?? 0;
            int rightPriority = right?.workSettings?.GetPriority(def.workType) ?? 0;
            return leftPriority.CompareTo(rightPriority);
        }
    }

    public abstract class MainTabWindow_PawnTable : MainTabWindow
    {
        private readonly PawnTable table = new PawnTable();

        public virtual void Notify_PawnsChanged()
        {
            table.SetDirty();
        }
    }

    public class Dialog_ModSettings : Window
    {
        private const float SettingsWindowPreferredWidth = 1100f;
        private const float SettingsWindowPreferredHeight = 760f;
        private const float SettingsWindowMinimumWidth = 700f;
        private const float SettingsWindowMinimumHeight = 600f;
        private const float SettingsWindowEdgeMargin = 80f;

        private readonly Mod mod;

        public Dialog_ModSettings()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
        }

        public Dialog_ModSettings(Mod mod) : this()
        {
            this.mod = mod;
        }

#if v0_13
        public override Vector2 InitialWindowSize => SettingsWindowSize;
#else
        public override Vector2 InitialSize => SettingsWindowSize;
#endif

        private static Vector2 SettingsWindowSize
        {
            get
            {
                float availableWidth = Mathf.Max(SettingsWindowMinimumWidth, Screen.width - SettingsWindowEdgeMargin);
                float availableHeight = Mathf.Max(SettingsWindowMinimumHeight, Screen.height - SettingsWindowEdgeMargin);
                return new Vector2(
                    Mathf.Min(SettingsWindowPreferredWidth, availableWidth),
                    Mathf.Min(SettingsWindowPreferredHeight, availableHeight));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (mod != null)
            {
                mod.DoSettingsWindowContents(inRect);
                return;
            }

            DrawBetterWorkTabSettings(inRect);
        }

        private static void DrawBetterWorkTabSettings(Rect inRect)
        {
            Better_Work_Tab.BetterWorkTabSettings settings = Better_Work_Tab.BetterWorkTabMod.Settings;
            if (settings == null)
            {
                settings = Better_Work_Tab.Legacy016ModSettingsStore.Get<Better_Work_Tab.BetterWorkTabSettings>();
                Better_Work_Tab.BetterWorkTabMod.Settings = settings;
            }

            Better_Work_Tab.UI.BetterWorkTabSettingsUI.DoSettingsWindowContents(inRect, settings);
        }
    }

    internal static class Legacy016PawnTableFactory
    {
        public static PawnTableDef CreateWorkTableDef()
        {
            var def = new PawnTableDef
            {
                defName = "Work",
                columns = new List<PawnColumnDef>()
            };

            def.columns.Add(new PawnColumnDef
            {
                defName = "Label",
                label = "Pawn",
                width = 165f,
                workerClass = typeof(PawnColumnWorker_Label)
            });

            foreach (WorkTypeDef workType in WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder)
            {
                if (!workType.visible)
                    continue;

                def.columns.Add(new PawnColumnDef
                {
                    defName = "WorkPriority_" + workType.defName,
                    label = workType.labelShort,
                    workType = workType,
                    width = 36f,
                    workerClass = typeof(PawnColumnWorker_WorkPriority)
                });
            }

            return def;
        }
    }
}

#if v0_14
namespace Better_Work_Tab.UI
{
    internal static class UIHighlighter
    {
        public static void HighlightOpportunity(Rect rect, string key)
        {
        }
    }

    internal static class CopyPasteUI
    {
        public static void DoCopyPasteButtons(Rect rect, Action copyAction, Action pasteAction)
        {
            float buttonWidth = Mathf.Max(16f, rect.width / 2f - 1f);
            Rect copyRect = new Rect(rect.x, rect.y + 2f, buttonWidth, rect.height - 4f);
            Rect pasteRect = new Rect(copyRect.xMax + 2f, rect.y + 2f, buttonWidth, rect.height - 4f);

            if (Better_Work_Tab.WidgetsCompat.ButtonText(copyRect, "C"))
            {
                copyAction?.Invoke();
            }

            bool previousEnabled = GUI.enabled;
            GUI.enabled = pasteAction != null;
            if (Better_Work_Tab.WidgetsCompat.ButtonText(pasteRect, "P"))
            {
                pasteAction?.Invoke();
            }
            GUI.enabled = previousEnabled;
        }
    }
}
#endif
#endif
