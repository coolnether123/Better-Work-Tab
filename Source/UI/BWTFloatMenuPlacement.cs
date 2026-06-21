using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.UI.Headers.Angled;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    internal static class BWTFloatMenuPlacement
    {
        private const float FallbackMenuWidth = 304f;
        private const float FallbackMenuOptionHeight = 32f;
        private const float ScreenMargin = 4f;

        public static void AddBottomRightAnchored(List<FloatMenuOption> options, Rect anchorRect)
        {
            FloatMenu menu = new FloatMenu(options);
            Vector2 anchorPoint = GUIClipUtility.Unclip(new Vector2(anchorRect.xMax, anchorRect.yMin - ScreenMargin));
            TryPlaceBottomRight(menu, CountOptions(options), anchorPoint);
            Find.WindowStack.Add(menu);
        }

        public static void AddBottomRightAtMouse(List<FloatMenuOption> options)
        {
            FloatMenu menu = new FloatMenu(options);
            Vector2 mousePosition = CurrentMousePosition();
            TryPlaceBottomRight(menu, CountOptions(options), mousePosition);
            Find.WindowStack.Add(menu);
        }

        private static int CountOptions(List<FloatMenuOption> options)
        {
            return options == null ? 1 : Math.Max(1, options.Count);
        }

        private static Vector2 CurrentMousePosition()
        {
            Event currentEvent = Event.current;
            if (currentEvent != null)
            {
                return GUIClipUtility.Unclip(currentEvent.mousePosition);
            }

            return new Vector2(Verse.UI.screenWidth - ScreenMargin, Verse.UI.screenHeight - ScreenMargin);
        }

        private static void TryPlaceBottomRight(FloatMenu menu, int optionCount, Vector2 anchorPoint)
        {
            if (menu == null)
            {
                return;
            }

            Vector2 size = ResolveMenuSize(menu, optionCount);
            float maxX = Math.Max(ScreenMargin, Verse.UI.screenWidth - size.x - ScreenMargin);
            float maxY = Math.Max(ScreenMargin, Verse.UI.screenHeight - size.y - ScreenMargin);
            float x = Mathf.Clamp(anchorPoint.x - size.x, ScreenMargin, maxX);
            float y = Mathf.Clamp(anchorPoint.y - size.y, ScreenMargin, maxY);
            Vector2 topLeft = new Vector2(x, y);

            if (TrySetWindowRect(menu, topLeft, size))
            {
                return;
            }

            TrySetTopLeft(menu, topLeft);
        }

        private static Vector2 ResolveMenuSize(FloatMenu menu, int optionCount)
        {
#if vAlpha4
            return new Vector2(
                Layer_FloatMenu.ChoiceSize.x + ScreenMargin,
                Math.Max(Layer_FloatMenu.ChoiceSize.y, optionCount * (Layer_FloatMenu.ChoiceSize.y + ScreenMargin)));
#else
            object initialSize = GetMemberValue(menu, "InitialSize");
            if (initialSize is Vector2)
            {
                Vector2 size = (Vector2)initialSize;
                if (size.x > 1f && size.y > 1f)
                {
                    return size;
                }
            }

            return new Vector2(FallbackMenuWidth, Math.Max(FallbackMenuOptionHeight, optionCount * FallbackMenuOptionHeight));
#endif
        }

        private static bool TrySetWindowRect(FloatMenu menu, Vector2 topLeft, Vector2 size)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, size.x, size.y);
            if (TrySetMemberValue(menu, "windowRect", rect))
            {
                return true;
            }

            return TrySetMemberValue(menu, "WindowRect", rect);
        }

        private static bool TrySetTopLeft(FloatMenu menu, Vector2 topLeft)
        {
            return TrySetMemberValue(menu, "topLeft", topLeft);
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanRead)
                {
                    return property.GetValue(instance, null);
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(instance);
                }

                type = type.BaseType;
            }

            return null;
        }

        private static bool TrySetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null)
            {
                return false;
            }

            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, value, null);
                    return true;
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }
    }
}
