using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    public struct AssignWorkParams
    {
        public AssignWorkParams(bool hasHighestSkill = false, bool skipIfAnotherPawnAssigned = false, bool isPregnant = false, bool isCapableOfViolence = false, int passionLevel = null,
            
            )
        {
            HasHighestSkill = hasHighestSkill;

            PassionLevel = passionLevel;
            SkillLevelGreaterThan = skillLevelGreaterThan;
            SkillLevelLessThan = skillLevelLessThan;
            SkipIfAnotherPawnAssigned = skipIfAnotherPawnAssigned;
            IsPregnant = isPreg;
            CapableOfViolence = false;
        }

        public bool HasHighestSkill;
        public bool SkipIfAnotherPawnAssigned;
        public bool IsPregnant;
        public bool IsCapableOfViolence;

        public int PassionLevel;
        public int SkillLevelGreaterThan;
        public int SkillLevelLessThan;

        public Gender Gender;
        
        public XenotypeDef Xenotype;
        
        public float MoveSpeedGreaterThan;
        public float MoveSpeedLessThan;
        
    }

    internal class AssignWorkRule
    {

        public AssignWorkRule(Pawn pawn, WorkTypeDef worktype, int priority, bool isBest = false, )
        {
        }

        public void Apply()
        {
        }
    }
}
