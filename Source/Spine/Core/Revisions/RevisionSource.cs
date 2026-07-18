using System;
using System.Threading;

namespace Spine.Api
{
    public interface IRevisionSource
    {
        long Revision { get; }
    }
}

namespace Spine.Revisions
{
    using Spine.Api;

    /// <summary>
    /// Thread-safe source of strictly increasing, process-local revisions.
    /// </summary>
    public sealed class RevisionSource : IRevisionSource
    {
        private long _revision;

        public RevisionSource(long initialRevision = 0)
        {
            if (initialRevision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialRevision));
            }

            _revision = initialRevision;
        }

        public long Revision => Interlocked.Read(ref _revision);

        public long Advance()
        {
            while (true)
            {
                long current = Interlocked.Read(ref _revision);
                if (current == long.MaxValue)
                {
                    throw new InvalidOperationException("The revision source is exhausted and cannot advance past Int64.MaxValue.");
                }

                long next = current + 1;
                if (Interlocked.CompareExchange(ref _revision, next, current) == current)
                {
                    return next;
                }
            }
        }
    }
}
