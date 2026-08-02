using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// Adds a visual tutorial demonstration to a real FloatMenu option while
    /// leaving selection, input, ordering, and closing behavior to RimWorld.
    /// </summary>
    internal sealed class BWTTutorialFloatMenuOption : FloatMenuOption
    {
        private readonly string lessonId;
        private readonly int phase;
        private readonly string gestureIdentity;

        internal BWTTutorialFloatMenuOption(
            string label,
            Action action,
            string lessonId,
            int phase,
            string gestureIdentity)
            : base(label, action)
        {
            this.lessonId = lessonId;
            this.phase = phase;
            this.gestureIdentity = gestureIdentity;
        }

        public override bool DoGUI(Rect rect, bool colonistOrdering, FloatMenu floatMenu)
        {
            bool chosen = base.DoGUI(rect, colonistOrdering, floatMenu);
            if (!chosen &&
                Event.current?.type == EventType.Repaint &&
                BWTGeneralTutorial.IsActiveLesson(lessonId, out int activePhase) &&
                activePhase == phase)
            {
                BWTTutorialGestureDemo.DrawExternal(
                    gestureIdentity,
                    rect,
                    BWTTutorialGestureDemo.GestureKind.LeftClick,
                    "BWT_Tutorial_Gesture_SelectOption".Translate());
            }

            return chosen;
        }
    }
}
