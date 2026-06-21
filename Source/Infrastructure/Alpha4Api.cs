#if vAlpha4
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Verse
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StaticConstructorOnStartupAttribute : Attribute
    {
    }

    public interface IExposable
    {
        void ExposeData();
    }

    public interface ILoadReferenceable
    {
        string GetUniqueLoadID();
    }

    public enum GameFont
    {
        Tiny,
        Small,
        Medium
    }

    public static class Text
    {
        private static GameFont font = GameFont.Small;

        public static GameFont Font
        {
            get { return font; }
            set
            {
                font = value;
                switch (value)
                {
                    case GameFont.Tiny:
                        GenFont.SetFontTiny();
                        break;
                    case GameFont.Medium:
                        GenFont.SetFontMedium();
                        break;
                    default:
                        GenFont.SetFontSmall();
                        break;
                }
            }
        }

        public static TextAnchor Anchor
        {
            get { return GUI.skin.label.alignment; }
            set { GUI.skin.label.alignment = value; }
        }

        public static bool WordWrap
        {
            get { return GUI.skin.label.wordWrap; }
            set { GUI.skin.label.wordWrap = value; }
        }

        public static float LineHeight => GUI.skin.label.lineHeight;

        public static Vector2 CalcSize(string text)
        {
            return GUI.skin.label.CalcSize(new GUIContent(text ?? string.Empty));
        }

        public static float CalcHeight(string text, float width)
        {
            return GUI.skin.label.CalcHeight(new GUIContent(text ?? string.Empty), width);
        }
    }

    public static class Mouse
    {
        public static bool IsOver(Rect rect)
        {
            return rect.Contains(Event.current?.mousePosition ?? Vector2.zero);
        }
    }

    public static class Alpha4RectExtensions
    {
        public static Rect ContractedBy(this Rect rect, float margin)
        {
            return new Rect(rect.x + margin, rect.y + margin, rect.width - margin * 2f, rect.height - margin * 2f);
        }
    }

    public static class Alpha4CollectionExtensions
    {
        public static void RemoveDuplicates<T>(this List<T> list)
        {
            if (list == null)
                return;

            var seen = new HashSet<T>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!seen.Add(list[i]))
                    list.RemoveAt(i);
            }
        }
    }

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

    public abstract class Window : EditWindow
    {
        public bool absorbInputAroundWindow;
        public bool closeOnClickedOutside;
        public bool preventCameraMotion = true;
        public bool draggable = true;
        public bool resizeableWindow;
        public Rect windowRect;

        public virtual Vector2 InitialSize => new Vector2(500f, 500f);
        public virtual Vector2 InitialWindowSize => InitialSize;
        public virtual Vector2 InitialPosition => new Vector2((Screen.width - InitialWindowSize.x) / 2f, (Screen.height - InitialWindowSize.y) / 2f);
        protected virtual float Margin => 18f;

        protected Window()
        {
            Vector2 size = InitialWindowSize;
            SetInitialSizeAndPosition();
            category = LayerCategory.GameDialog;
            closeOnEscapeKey = true;
            resizeable = false;
        }

        protected virtual void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialWindowSize;
            SetCentered(size.x, size.y);
            windowRect = winRect;
        }

        public virtual void PreOpen()
        {
        }

        public virtual void PostOpen()
        {
        }

        public virtual void PreClose()
        {
        }

        public virtual void PostClose()
        {
        }

        public virtual void DoWindowContents(Rect inRect)
        {
        }

        protected override void FillWindow(Rect inRect)
        {
            windowRect = winRect;
            DoWindowContents(inRect);

            if (closeOnClickedOutside &&
                Event.current.type == EventType.MouseDown &&
                !winRect.Contains(Event.current.mousePosition))
            {
                Close();
                Event.current.Use();
            }
        }

        protected override void OnPreClose()
        {
            PreClose();
            base.OnPreClose();
            PostClose();
        }
    }

    public enum WindowLayer
    {
        GameUI,
        Dialog,
        Super
    }

    public sealed class WindowStack
    {
        private readonly List<Layer> openedLayers = new List<Layer>();

        public void Add(Layer window)
        {
            PruneClosedLayers();

            if (window != null && !openedLayers.Contains(window))
            {
                openedLayers.Add(window);
            }

            if (window is Window modernWindow)
            {
                modernWindow.PreOpen();
                Verse.Find.LayerStack.Add(window);
                modernWindow.PostOpen();
                return;
            }

            Verse.Find.LayerStack.Add(window);
        }

        public bool TryRemove(Layer window, bool doCloseSound = true)
        {
            if (window == null)
            {
                return false;
            }

            openedLayers.Remove(window);
            if (!IsLayerOpen(window))
            {
                return false;
            }

            window.Close();
            return true;
        }

        public bool TryRemove(Type type, bool doCloseSound = true)
        {
            if (type == null)
                return false;

            PruneClosedLayers();

            for (int i = openedLayers.Count - 1; i >= 0; i--)
            {
                Layer layer = openedLayers[i];
                if (layer != null && type.IsInstanceOfType(layer))
                {
                    return TryRemove(layer, doCloseSound);
                }
            }

            return false;
        }

        private void PruneClosedLayers()
        {
            for (int i = openedLayers.Count - 1; i >= 0; i--)
            {
                if (!IsLayerOpen(openedLayers[i]))
                {
                    openedLayers.RemoveAt(i);
                }
            }
        }

        private static bool IsLayerOpen(Layer layer)
        {
            if (layer == null)
            {
                return false;
            }

            foreach (Layer openLayer in Verse.Find.LayerStack)
            {
                if (ReferenceEquals(openLayer, layer))
                {
                    return true;
                }
            }

            return false;
        }

        public T WindowOfType<T>() where T : Layer
        {
            return Verse.Find.LayerStack.FirstLayerOfType<T>();
        }

        public bool IsOpen(Type type)
        {
            return Verse.Find.LayerStack.IsOpen(type);
        }

        public void ImmediateWindow(int id, Rect rect, WindowLayer layer, Action doWindowContents)
        {
            GUI.Window(id, rect, _ => doWindowContents?.Invoke(), string.Empty);
        }
    }

    public class FloatMenu : Layer_FloatMenu
    {
        public FloatMenu(List<FloatMenuOption> options) : base(options)
        {
        }

        public FloatMenu(List<FloatMenuOption> options, string title, bool needSelection = false)
            : base(options, title, needSelection)
        {
        }
    }

    public class DefMap<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public DefMap()
        {
        }
    }

    public class TraitDegreeData
    {
        public string label;
        public string description;
        public int degree;
    }

    public struct LocalTargetInfo
    {
        public Thing Thing;
        public IntVec3 Cell;

        public LocalTargetInfo(Thing thing)
        {
            Thing = thing;
            Cell = default(IntVec3);
        }

        public LocalTargetInfo(IntVec3 cell)
        {
            Thing = null;
            Cell = cell;
        }
    }

    public struct GlobalTargetInfo
    {
        public Thing Thing;
        public IntVec3 Cell;

        public GlobalTargetInfo(Thing thing)
        {
            Thing = thing;
            Cell = default(IntVec3);
        }

        public GlobalTargetInfo(IntVec3 cell)
        {
            Thing = null;
            Cell = cell;
        }
    }
}

namespace RimWorld.Planet
{
    public sealed class World
    {
        public WorldInfo info;
    }

    public sealed class WorldInfo
    {
        public string name;
        public string seedString;
    }
}

namespace RimWorld
{
    public enum MainTabWindowAnchor
    {
        Left,
        Right
    }

    public class MainTabDef : Def
    {
        public Type windowClass;
        public MainTabWindow TabWindow;
    }

    public abstract class MainTabWindow : Window
    {
        public MainTabWindowAnchor Anchor = MainTabWindowAnchor.Left;
        public Rect currentWindowRect;
        public const float Margin = 18f;

        public virtual Vector2 RequestedTabSize => InitialWindowSize;

        protected void SetInitialSizeAndPosition()
        {
            Vector2 size = RequestedTabSize;
            size.x = Mathf.Min(size.x, Verse.UI.screenWidth);
            size.y = Mathf.Min(size.y, Verse.UI.screenHeight - 35f);
            float x = Anchor == MainTabWindowAnchor.Left ? 0f : Verse.UI.screenWidth - size.x;
            currentWindowRect = new Rect(Mathf.Max(0f, x), Mathf.Max(0f, Verse.UI.screenHeight - 35f - size.y), size.x, size.y);
            winRect = currentWindowRect;
        }
    }

    public class MainTabWindow_Work : MainTabWindow
    {
        protected readonly List<Pawn> pawns = new List<Pawn>();
        protected Vector2 scrollPosition = Vector2.zero;

        public override void PreOpen()
        {
            base.PreOpen();
            RecachePawns();
        }

        protected void RecachePawns()
        {
            pawns.Clear();
            if (Verse.Find.ListerPawns == null)
            {
                return;
            }

            foreach (Pawn pawn in Verse.Find.ListerPawns.FreeColonists)
            {
                pawns.Add(pawn);
            }
        }

        public virtual void Notify_PawnsChanged()
        {
            RecachePawns();
        }

        protected virtual void DrawPawnRow(Rect rect, Pawn pawn)
        {
        }
    }

    public class Dialog_Rename : Window
    {
        protected string curName;

        public Dialog_Rename()
        {
        }

        public Dialog_Rename(string name)
        {
            curName = name;
        }

        protected virtual AcceptanceReport NameIsValid(string name)
        {
            return !string.IsNullOrEmpty(name);
        }

        protected virtual void SetName(string name)
        {
            curName = name;
        }
    }

    public static class Current
    {
        public static RimWorld.Planet.World World { get; } = new RimWorld.Planet.World();
    }
}

namespace Verse.AI
{
    public class WorkGiver_Scanner : WorkGiver
    {
        public WorkGiver_Scanner() : base(null)
        {
        }

        public WorkGiver_Scanner(WorkGiverDef def) : base(def)
        {
        }
    }
}

namespace Better_Work_Tab
{
    internal static class LongEventHandler
    {
        private static readonly List<Action> pendingActions = new List<Action>();

        public static void ExecuteWhenFinished(Action action)
        {
            if (action != null)
            {
                pendingActions.Add(action);
            }
        }

        public static void RunPending()
        {
            if (pendingActions.Count == 0)
            {
                return;
            }

            var actions = pendingActions.ToArray();
            pendingActions.Clear();

            for (int i = 0; i < actions.Length; i++)
            {
                actions[i]?.Invoke();
            }
        }
    }

    internal static class Find
    {
        private static readonly WindowStack alpha4WindowStack = new WindowStack();

        public static WindowStack WindowStack => alpha4WindowStack;
        public static LayerStack LayerStack => Verse.Find.LayerStack;
        public static MainTabsRoot MainTabsRoot => Verse.Find.MainTabsRoot;
        public static Selector Selector => Verse.Find.Selector;
        public static CameraMap CameraMap => Verse.Find.CameraMap;
        public static Map Map => Verse.Find.Map;
        public static PlaySettings PlaySettings => Verse.Find.PlaySettings;
        public static ListerPawns ListerPawns => Verse.Find.ListerPawns;
        public static ThingGrid ThingGrid => Verse.Find.ThingGrid;
    }

    internal static class Alpha4PawnWorkSettingsExtensions
    {
        public static int GetPriority(this Pawn_WorkSettings workSettings, WorkTypeDef workType)
        {
            return workSettings?.GetPriorityOf(workType) ?? 0;
        }

        public static void SetPriority(this Pawn_WorkSettings workSettings, WorkTypeDef workType, int priority)
        {
            workSettings?.SetWorkToPriority(workType, priority);
        }

        public static bool WorkIsActive(this Pawn_WorkSettings workSettings, WorkTypeDef workType)
        {
            return workSettings?.GetPriorityOf(workType) > 0;
        }

        public static void Notify_UseWorkPrioritiesChanged(this Pawn_WorkSettings workSettings)
        {
        }
    }

    internal static class Widgets
    {
        private static Texture2D solidTex;

        private static Texture2D SolidTex
        {
            get
            {
                if (solidTex == null)
                {
                    solidTex = new Texture2D(1, 1);
                    solidTex.SetPixel(0, 0, Color.white);
                    solidTex.Apply();
                }

                return solidTex;
            }
        }

        public static void Label(Rect rect, string label)
        {
            GUI.Label(rect, label ?? string.Empty);
        }

        public static void DrawBox(Rect rect, int thickness = 1)
        {
            GenUI.DrawBox(rect, thickness);
        }

        public static void DrawBox(Rect rect, int thickness, Texture2D lineTexture)
        {
            GenUI.DrawBox(rect, thickness);
        }

        public static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            if (Mathf.Abs(start.x - end.x) < Mathf.Abs(start.y - end.y))
            {
                GUI.DrawTexture(new Rect(start.x, Mathf.Min(start.y, end.y), Mathf.Max(1f, width), Mathf.Abs(end.y - start.y)), GenUI.WhiteTex);
            }
            else
            {
                GUI.DrawTexture(new Rect(Mathf.Min(start.x, end.x), start.y, Mathf.Abs(end.x - start.x), Mathf.Max(1f, width)), GenUI.WhiteTex);
            }
            GUI.color = oldColor;
        }

        public static void DrawLineVertical(float x, float y, float length)
        {
            GenUI.DrawLineVertical(new Vector2(x, y), length);
        }

        public static void DrawLineHorizontal(float x, float y, float length)
        {
            GenUI.DrawLineHorizontal(new Vector2(x, y), length);
        }

        public static void DrawHighlight(Rect rect)
        {
            Verse.Widgets.DrawHighlight(rect);
        }

        public static void DrawHighlightSelected(Rect rect)
        {
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            GUI.DrawTexture(rect, GenUI.WhiteTex);
            GUI.color = oldColor;
        }

        public static void DrawHighlightIfMouseover(Rect rect)
        {
            if (Mouse.IsOver(rect))
            {
                DrawHighlight(rect);
            }
        }

        public static void DrawMenuSection(Rect rect)
        {
            Verse.Widgets.DrawMenuSection(rect);
        }

        public static void BeginScrollView(Rect outRect, ref Vector2 scrollPosition, Rect viewRect)
        {
            scrollPosition = GUI.BeginScrollView(outRect, scrollPosition, viewRect);
        }

        public static void EndScrollView()
        {
            GUI.EndScrollView();
        }

        public static string TextField(Rect rect, string text)
        {
            return GUI.TextField(rect, text ?? string.Empty);
        }

        public static bool ButtonText(Rect rect, string label, bool active = true)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && active;
            bool clicked = Verse.Widgets.TextButton(rect, label ?? string.Empty, true, true);
            GUI.enabled = oldEnabled;
            return clicked && active;
        }

        public static bool ButtonText(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active = true)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && active;
            bool clicked = Verse.Widgets.TextButton(rect, label ?? string.Empty, drawBackground, doMouseoverSound, Color.white);
            GUI.enabled = oldEnabled;
            return clicked && active;
        }

        public static bool ButtonInvisible(Rect rect)
        {
            return Verse.Widgets.InvisibleButton(rect);
        }

        public static bool ButtonImage(Rect rect, Texture2D tex)
        {
            return Verse.Widgets.ImageButton(rect, tex);
        }

        public static bool ButtonImage(Rect rect, Texture2D tex, Color baseColor)
        {
            return Verse.Widgets.ImageButton(rect, tex, baseColor);
        }

        public static bool ButtonImage(Rect rect, Texture2D tex, Color baseColor, Color mouseoverColor)
        {
            Color oldColor = GUI.color;
            if (rect.Contains(Event.current.mousePosition))
            {
                GUI.color = mouseoverColor;
            }

            bool clicked = Verse.Widgets.ImageButton(rect, tex, baseColor);
            GUI.color = oldColor;
            return clicked;
        }

        public static void CheckboxLabeled(Rect rect, string label, ref bool checkOn)
        {
            Verse.Widgets.LabelCheckbox(rect, label ?? string.Empty, ref checkOn);
        }

        public static void CheckboxLabeled(Rect rect, string label, ref bool checkOn, bool disabled)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && !disabled;
            Verse.Widgets.LabelCheckbox(rect, label ?? string.Empty, ref checkOn);
            GUI.enabled = oldEnabled;
        }

        public static void Checkbox(Vector2 topLeft, ref bool checkOn)
        {
            Verse.Widgets.Checkbox(topLeft, ref checkOn);
        }

        public static void Checkbox(float x, float y, ref bool checkOn, bool disabled = false, bool paintable = false)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && !disabled;
            Verse.Widgets.Checkbox(new Vector2(x, y), ref checkOn);
            GUI.enabled = oldEnabled;
        }

        public static float HorizontalSlider(Rect rect, float value, float leftValue, float rightValue, bool middleAlignment = false, string leftAlignedLabel = null, string rightAlignedLabel = null)
        {
            return GUI.HorizontalSlider(rect, value, leftValue, rightValue);
        }

        public static void FillableBar(Rect rect, float fillPercent, Texture2D fillTex, bool doBlackBorder, Texture2D innerBGTex)
        {
            Verse.Widgets.FillableBar(rect, fillPercent, fillTex, doBlackBorder, innerBGTex);
        }

        public static void DrawBoxSolid(Rect rect, Color color)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, SolidTex);
            GUI.color = oldColor;
        }

        public static void DrawBoxSolidWithOutline(Rect rect, Color fillColor, Color outlineColor, int outlineThickness = 1)
        {
            DrawBoxSolid(rect, fillColor);
            Color oldColor = GUI.color;
            GUI.color = outlineColor;
            GenUI.DrawBox(rect, outlineThickness);
            GUI.color = oldColor;
        }
    }
}
#endif
