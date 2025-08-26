using Better_Work_Tab.Features.Rules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Better_Work_Tab.Features
{
    interface IAssignmentRule
    {
        void Apply(Pawn p, Action<WorkTypeDef, int> a, AutoAssignmentContext context);
    }
}
