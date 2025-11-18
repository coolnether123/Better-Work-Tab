namespace Better_Work_Tab.Features.Rules
{
    public class AssignmentValidationResult
    {
        public bool IsValid { get; set; }
        public bool ShouldSkipRemainingPawns { get; set; }

        public AssignmentValidationResult(bool isValid, bool shouldSkipRemainingPawns)
        {
            IsValid = isValid;
            ShouldSkipRemainingPawns = shouldSkipRemainingPawns;
        }
    }
}
