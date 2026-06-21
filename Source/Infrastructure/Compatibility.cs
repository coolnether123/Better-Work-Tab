
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

#if v1_2 || v1_1 || (v1_0 || v0_19)
namespace RimWorld
{
    public class QuickSearchFilter
    {
        private string inputText = "";
        private string searchText = "";
        private readonly Dictionary<string, bool> cachedMatches = new Dictionary<string, bool>();

        public string Text
        {
            get => this.inputText;
            set
            {
                this.inputText = value ?? "";
                this.searchText = this.inputText.Trim();
                this.cachedMatches.Clear();
            }
        }

        public bool Active => !this.inputText.NullOrEmpty();

        public bool Matches(string value)
        {
            if (!this.Active)
                return true;
            if (value.NullOrEmpty())
                return false;
            if (!this.cachedMatches.TryGetValue(value, out bool result))
            {
                result = value.IndexOf(this.searchText, StringComparison.InvariantCultureIgnoreCase) != -1;
                this.cachedMatches[value] = result;
            }
            return result;
        }

        public bool Matches(ThingDef td)
        {
            return this.Matches(td.label);
        }
    }

    [StaticConstructorOnStartup]
    public class QuickSearchWidget
    {
        public QuickSearchFilter filter = new QuickSearchFilter();
        public bool noResultsMatched;
        public Color inactiveTextColor = Color.white;
        public int maxSearchTextLength = 30;
        private readonly string controlName;
        public const float WidgetHeight = 24f;
        private static int instanceCounter;

        public QuickSearchWidget()
        {
            this.controlName = string.Format("QuickSearchWidget_{0}", (object)QuickSearchWidget.instanceCounter++);
        }

        public void OnGUI(Rect rect, Action onFilterChange = null, Action onClear = null)
        {
            if (this.CurrentlyFocused() && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                this.Unfocus();
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseDown && !rect.Contains(Event.current.mousePosition))
                this.Unfocus();
            
            Color color = GUI.color;
            GUI.color = Color.white;
            float num1 = Mathf.Min(18f, rect.height);
            float num2 = num1 + 8f;
            float y = (float)((double)rect.y + ((double)rect.height - (double)num2) / 2.0 + 4.0);
            Rect position = new Rect(rect.x + 4f, y, num1, num1);
            
            if (TexButton.Search != null)
                GUI.DrawTexture(position, (Texture)TexButton.Search);

            GUI.SetNextControlName(this.controlName);
            Rect rect1 = new Rect(position.xMax + 4f, rect.y, rect.width - num2, rect.height);
            if (this.filter.Active)
                rect1.xMax -= num2;

            if (this.noResultsMatched && this.filter.Active)
                GUI.color = Color.red; 
            else if (!this.filter.Active && !this.CurrentlyFocused())
                GUI.color = this.inactiveTextColor;

#if vAlpha4
            string str = GUI.TextField(rect1, this.filter.Text ?? string.Empty);
#else
            // Use Verse.Widgets to avoid ambiguity
            string str = Verse.Widgets.TextField(rect1, this.filter.Text);
#endif
            if (str.Length > this.maxSearchTextLength) str = str.Substring(0, this.maxSearchTextLength);

            GUI.color = Color.white;
            if (str != this.filter.Text)
            {
                this.filter.Text = str;
                if (onFilterChange != null)
                    onFilterChange();
            }

            if (this.filter.Active && Better_Work_Tab.WidgetsCompat.ButtonImage(new Rect(rect1.xMax + 4f, y, num1, num1), RimWorld.TexButton.CloseXSmall))
            {
                this.filter.Text = "";
                if (onFilterChange != null)
                    onFilterChange();
                if (onClear != null)
                    onClear();
            }
            GUI.color = color;
        }

        public void Unfocus()
        {
            if (this.CurrentlyFocused())
                GUI.FocusControl(null);
        }

        public void Focus() => GUI.FocusControl(this.controlName);

        public bool CurrentlyFocused() => GUI.GetNameOfFocusedControl() == this.controlName;

        public void Reset()
        {
            this.filter.Text = "";
            this.noResultsMatched = false;
        }
    }

    // Note: In RimWorld 1.2, Verse.TexButton is internal. We access the public RimWorld.TexButton instead.
    [StaticConstructorOnStartup]
    public static class TexButton
    {
        public static readonly Texture2D Search;
        public static readonly Texture2D Info;
        public static readonly Texture2D DeleteX;
        public static readonly Texture2D CloseXSmall;

        static TexButton()
        {
            // Try to get textures from ContentFinder as fallback
            Search = ContentFinder<Texture2D>.Get("UI/Widgets/Search", false) ?? ContentFinder<Texture2D>.Get("UI/Buttons/InfoButton", false);
            Info = ContentFinder<Texture2D>.Get("UI/Buttons/InfoButton", false);
            DeleteX = ContentFinder<Texture2D>.Get("UI/Buttons/Delete", false);
            CloseXSmall = ContentFinder<Texture2D>.Get("UI/Widgets/CloseXSmall", false) ?? ContentFinder<Texture2D>.Get("UI/Widgets/CloseX", false);
        }
    }
}

namespace Better_Work_Tab
{
    public static class Widgets12
    {
        public static string TextField(Rect rect, string text, int maxLength)
        {
#if vAlpha4
            string input = GUI.TextField(rect, text ?? string.Empty);
#else
            string input = Verse.Widgets.TextField(rect, text);
#endif
            if (input.Length <= maxLength)
                return input;
            return text;
        }
        
        public static bool ButtonText(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor overrideTextAnchor)
        {
            TextAnchor old = Text.Anchor;
            Text.Anchor = overrideTextAnchor;
            bool result = Better_Work_Tab.WidgetsCompat.ButtonText(rect, label, drawBackground, doMouseoverSound, active);
            Text.Anchor = old;
            return result;
        }
        
        public static void DrawBox(Rect rect, int thickness, Texture2D lineTexture)
        {
#if vAlpha4
            GenUI.DrawBox(rect, thickness);
#else
            // In 1.2, DrawBox doesn't accept lineTexture parameter, so we ignore it
            Verse.Widgets.DrawBox(rect, thickness);
#endif
        }
    }

    public static class RectExtensions
    {
        public static void SplitHorizontally(this Rect rect, float topHeight, out Rect top, out Rect bottom, float gap = 0f)
        {
            top = new Rect(rect.x, rect.y, rect.width, topHeight);
            bottom = new Rect(rect.x, rect.y + topHeight + gap, rect.width, rect.height - topHeight - gap);
        }

        public static Rect LeftPart(this Rect rect, float pct)
        {
            return new Rect(rect.x, rect.y, rect.width * pct, rect.height);
        }

        public static Rect RightPart(this Rect rect, float pct)
        {
            float width = rect.width * pct;
            return new Rect(rect.xMax - width, rect.y, width, rect.height);
        }

        public static Rect MiddlePart(this Rect rect, float pct, float xOffsetPct = 0.5f)
        {
            float width = rect.width * pct;
            return new Rect(rect.x + (rect.width - width) * xOffsetPct, rect.y, width, rect.height);
        }

        public static Rect LeftHalf(this Rect rect)
        {
            return rect.LeftPart(0.5f);
        }

        public static Rect RightHalf(this Rect rect)
        {
            return rect.RightPart(0.5f);
        }

    }

    public static class ColoredTextCompat
    {
        // These colors don't exist in 1.2's ColoredText, so we define them here
        public static readonly Color TipSectionTitleColor = new Color(0.9f, 0.9f, 0.5f);
        public static readonly Color SubtleGrayColor = new Color(0.7f, 0.7f, 0.7f);
    }

    public static class LogCompat
    {
        private static readonly HashSet<string> warningsShown = new HashSet<string>();

        public static void WarningOnce(string text, int key)
        {
            string uniqueKey = key.ToString() + ":" + text;
            if (!warningsShown.Contains(uniqueKey))
            {
                Log.Warning(text);
                warningsShown.Add(uniqueKey);
            }
        }
    }

    public static class ListingStandardExtensions
    {
        private static readonly System.Reflection.FieldInfo ListingRectField =
            typeof(Listing).GetField("listingRect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static readonly System.Reflection.FieldInfo CurYField =
            typeof(Listing).GetField("curY", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public static Rect GetRect(this Listing_Standard listing, float height)
        {
            if (listing == null)
            {
                return RectCompat.Zero;
            }

            Rect listingRect = ListingRectField != null
                ? (Rect)ListingRectField.GetValue(listing)
                : new Rect(0f, 0f, 200f, 9999f);
            float curY = CurYField != null ? (float)CurYField.GetValue(listing) : 0f;
            Rect rect = new Rect(0f, curY, listingRect.width, height);
            CurYField?.SetValue(listing, curY + height);
            return rect;
        }

        public static bool ButtonText(this Listing_Standard listing, string label, float height = 30f)
        {
            return WidgetsCompat.ButtonText(listing.GetRect(height), label);
        }

        public static bool ButtonTextLabeled(this Listing_Standard listing, string label, string buttonLabel, float height = 30f)
        {
            Rect row = listing.GetRect(height);
            Rect labelRect = row.LeftPart(0.55f);
            Rect buttonRect = row.RightPart(0.4f);
            Widgets.Label(labelRect, label);
            return WidgetsCompat.ButtonText(buttonRect, buttonLabel);
        }

        public static void Gap(this Listing_Standard listing, float height = 12f)
        {
            listing.GetRect(height);
        }

        public static void Label(this Listing_Standard listing, string label, float height = 24f)
        {
            Widgets.Label(listing.GetRect(height), label);
        }

        public static void GapLine(this Listing_Standard listing, float gap = 4f)
        {
            listing.Gap(gap);
            Rect lineRect = listing.GetRect(1f);
            WidgetsCompat.DrawLineHorizontal(lineRect.x, lineRect.y, lineRect.width);
            listing.Gap(gap);
        }
    }
}
#endif
