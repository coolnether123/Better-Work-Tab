using System;
using UnityEngine;
using Verse;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class Dialog_RuleBuilder2SuggestRulesConfirmation : Window
    {
        private readonly Action onConfirm;
        internal static bool HasConfirmedThisSession { get; private set; }

        internal Dialog_RuleBuilder2SuggestRulesConfirmation(Action onConfirm)
        {
            this.onConfirm = onConfirm;
            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(460f, 178f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            DrawFittedLabel(new Rect(inRect.x, inRect.y, inRect.width, 30f), T("BWT_RuleBuilder2_SuggestConfirmTitle"));
            Text.Font = GameFont.Small;

            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, inRect.y + 38f, inRect.width, 56f), T("BWT_RuleBuilder2_SuggestConfirmBody"));
            GUI.color = Color.white;

            Rect cancel = new Rect(inRect.xMax - 188f, inRect.yMax - 34f, 86f, 30f);
            Rect suggest = new Rect(cancel.xMax + 8f, cancel.y, 94f, 30f);
            if (Widgets.ButtonText(cancel, T("BWT_Cancel")))
            {
                Close();
            }

            if (Widgets.ButtonText(suggest, T("BWT_RuleBuilder2_SuggestConfirmButton")))
            {
                HasConfirmedThisSession = true;
                Close();
                onConfirm?.Invoke();
            }
        }
    }
}
