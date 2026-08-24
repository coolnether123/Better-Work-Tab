namespace Better_Work_Tab.Features.Application
{
    /// <summary>
    /// Records the schedule-service revision owned by one atomic mutation so
    /// a later rollback can reject an externally changed schedule safely.
    /// </summary>
    internal sealed class WorkTabScheduleRevisionReceipt
    {
        internal WorkTabScheduleRevisionReceipt(int initialRevision)
        {
            InitialRevision = initialRevision;
            OwnedRevision = initialRevision;
        }

        internal int InitialRevision { get; }
        internal int OwnedRevision { get; private set; }
        internal bool Owns(int revision) => revision == OwnedRevision;

        internal bool AcceptCommit(bool changed, int observedRevision)
        {
            int expected = changed ? unchecked(OwnedRevision + 1) : OwnedRevision;
            if (observedRevision != expected)
            {
                return false;
            }

            OwnedRevision = observedRevision;
            return true;
        }
    }
}
