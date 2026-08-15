// RecentColours.cs
// Copyright Karel Kroeze, 2018-2018

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace Spine.UI.ColourPicker {
    public class RecentColours {
        private const int max = 18;
        private const int maxPinned = 9;
        private static List<Color> _colors = new List<Color>();
        private static List<Color> _pinnedColors = new List<Color>();

        static RecentColours() {
            Read();
        }

        public Color this[int index] => _colors[index];

        public int Count => _colors.Count;
        public IReadOnlyList<Color> PinnedColors =>
            LegacyReadOnlyCollections.WrapList(_pinnedColors);
        public int PinnedCount => _pinnedColors.Count;

        public static List<Color> CopyRecentColors()
        {
            return new List<Color>(_colors ?? new List<Color>());
        }

        public static List<Color> CopyPinnedColors()
        {
            return new List<Color>(_pinnedColors ?? new List<Color>());
        }

        public static void ReplaceAll(IEnumerable<Color> recentColors, IEnumerable<Color> pinnedColors)
        {
            _colors = recentColors == null
                ? new List<Color>()
                : new List<Color>(recentColors);
            _pinnedColors = pinnedColors == null
                ? new List<Color>()
                : new List<Color>(pinnedColors);

            if (_colors.Count > max)
            {
                _colors.RemoveRange(max, _colors.Count - max);
            }

            if (_pinnedColors.Count > maxPinned)
            {
                _pinnedColors.RemoveRange(maxPinned, _pinnedColors.Count - maxPinned);
            }

            Write();
        }

        public void Add(Color color) {
            _colors.RemoveAll(c => ColorsEqual(c, color));
            _colors.Insert(0, color);

            while (_colors.Count > max) {
                _colors.RemoveAt(_colors.Count - 1);
            }

            Write();
        }

        public bool IsPinned(Color color)
        {
            return _pinnedColors.Exists(c => ColorsEqual(c, color));
        }

        public void Pin(Color color)
        {
            if (IsPinned(color))
            {
                return;
            }

            // Don't allow pinning more than maxPinned colors
            if (_pinnedColors.Count >= maxPinned)
            {
                return;
            }

            _pinnedColors.Insert(0, color);
            Write();
        }

        public bool CanPin()
        {
            return _pinnedColors.Count < maxPinned;
        }

        public void Unpin(Color color)
        {
            int removed = _pinnedColors.RemoveAll(c => ColorsEqual(c, color));
            if (removed > 0)
            {
                Write();
            }
        }

        private static void Read() {
            string path = Path.Combine(GenFilePaths.ConfigFolderPath, "ColourPicker.xml");
            if (!File.Exists(path)) {
                _colors.Clear();
                _pinnedColors.Clear();
                return;
            }

            try {
                Scribe.loader.InitLoading(path);
                ExposeData();
            } catch (Exception ex) {
                Log.Error("ColourPicker :: Error loading recent colours from file:" + ex);
            } finally {
                Scribe.loader.FinalizeLoading();
            }

            if (_pinnedColors == null)
            {
                _pinnedColors = new List<Color>();
            }
        }

        private static void Write() {
            try {
                string path = Path.Combine( GenFilePaths.ConfigFolderPath, "ColourPicker.xml" );
                Scribe.saver.InitSaving(path, "ColourPicker");
                ExposeData();
            } catch (Exception ex) {
                Log.Error("ColourPicker :: Error saving recent colours to file:" + ex);
            } finally {
                Scribe.saver.FinalizeSaving();
            }
        }

        private static void ExposeData() {
            Scribe_Collections.Look(ref _colors, "RecentColors", LookMode.Undefined, new object[0]);
            Scribe_Collections.Look(ref _pinnedColors, "PinnedColors", LookMode.Undefined, new object[0]);

            if (_colors == null)
            {
                _colors = new List<Color>();
            }
            if (_pinnedColors == null)
            {
                _pinnedColors = new List<Color>();
            }
        }

        private static bool ColorsEqual(Color a, Color b)
        {
            const float tolerance = 0.001f;
            return Mathf.Abs(a.r - b.r) < tolerance &&
                   Mathf.Abs(a.g - b.g) < tolerance &&
                   Mathf.Abs(a.b - b.b) < tolerance &&
                   Mathf.Abs(a.a - b.a) < tolerance;
        }
    }
}
