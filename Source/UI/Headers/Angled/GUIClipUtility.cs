using System;
using System.Reflection;
using UnityEngine;
using HarmonyLib;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Proxy class to access the internal UnityEngine.GUIClip.Unclip method via reflection.
    /// This is used to convert local UI coordinates to global screen coordinates, 
    /// allowing headers to render outside of their parent clipping groups.
    /// </summary>
    public static class GUIClipUtility
    {
        private delegate Vector2 UnclipDelegate(Vector2 pos);
        private static readonly UnclipDelegate UnclipHandle;

        static GUIClipUtility()
        {
            try
            {
                // GUI resides in the same assembly as GUIClip in most Unity versions
                var assembly = typeof(GUI).Assembly;
                var type = assembly.GetType("UnityEngine.GUIClip");
                if (type != null)
                {
                    var method = AccessTools.Method(type, "Unclip", new Type[] { typeof(Vector2) });
                    if (method != null)
                    {
                        UnclipHandle = (UnclipDelegate)Delegate.CreateDelegate(typeof(UnclipDelegate), method);
                    }
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[Better Work Tab] Exception while binding GUIClip.Unclip: {ex}");
            }
            
            if (UnclipHandle == null)
            {
                Verse.Log.Error("[Better Work Tab] Failed to bind GUIClip.Unclip. Angled headers may clip incorrectly.");
            }
        }

        public static Vector2 Unclip(Vector2 pos)
        {
            if (UnclipHandle == null) return pos;
            return UnclipHandle(pos);
        }
    }
}
