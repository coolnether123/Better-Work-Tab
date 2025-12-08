using Better_Work_Tab.Features;
using RimWorld;
using Spine.UI.WidgetExtensions;
using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public class Dialog_SettingsCategory : Window
    {
        private readonly string _categoryId;
        private readonly string _categoryLabel;
        private readonly Action<Listing_Standard, BetterWorkTabSettings> _drawContents;
        private Vector2 _scrollPosition;
        
        // Track content height for proper scrolling
        private float _lastContentHeight = 600f;

        public override Vector2 InitialSize => new Vector2(500f, 600f);

        public Dialog_SettingsCategory(
            string categoryId,
            string categoryLabel,
            Action<Listing_Standard, BetterWorkTabSettings> drawContents)
        {
            _categoryId = categoryId;
            _categoryLabel = categoryLabel;
            _drawContents = drawContents;

            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            resizeable = true; 
        }

        public override void DoWindowContents(Rect inRect)
        {
            var settings = BetterWorkTabMod.Settings;

            // Header
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, 40f);
            DrawHeader(headerRect);

            // Content area
            Rect contentRect = new Rect(
                inRect.x,
                headerRect.yMax + 10f,
                inRect.width,
                inRect.height - headerRect.height - 60f);

            DrawContent(contentRect, settings);

            // Footer
            Rect footerRect = new Rect(
                inRect.x,
                inRect.yMax - 40f,
                inRect.width,
                35f);

            DrawFooter(footerRect);
        }

        private void DrawHeader(Rect rect)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect, _categoryLabel);

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        }

        private void DrawContent(Rect rect, BetterWorkTabSettings settings)
        {
            Widgets.DrawMenuSection(rect);
            Rect innerRect = rect.ContractedBy(10f);

            // Use the calculated height from the previous frame (or default)
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, _lastContentHeight);
            
            Widgets.BeginScrollView(innerRect, ref _scrollPosition, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            _drawContents?.Invoke(listing, settings);

            // Capture the actual height used for the next frame's scroll view
            _lastContentHeight = listing.CurHeight + 20f; // Buffer

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawFooter(Rect rect)
        {
            Rect closeButton = new Rect(rect.xMax - 120f, rect.y, 120f, rect.height);
            if (Widgets.ButtonText(closeButton, "Close"))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            BetterWorkTabMod.Settings.Write();
        }
    }
}
