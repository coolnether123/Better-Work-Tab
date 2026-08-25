using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.Workloads
{
    /// <summary>
    /// Owns the workload candidate roster and its pawn lookup index. Known
    /// pawn-table changes advance a cheap revision; full pawn-list scans happen
    /// only when a consumer rebuilds after that revision or during the bounded
    /// compatibility audit.
    /// </summary>
    internal static class WorkloadPawnRosterCache
    {
        internal const int AuditIntervalTicks = 250;

        private static readonly List<PawnScopeCandidate> Candidates =
            new List<PawnScopeCandidate>();
        private static readonly Dictionary<int, Pawn> PawnsByThingId =
            new Dictionary<int, Pawn>();

        private static Game _boundGame;
        private static long _revision = 1L;
        private static long _candidateCacheRevision = long.MinValue;
        private static long _pawnIndexRevision = long.MinValue;
        private static int _nextAuditTick;
        private static long _candidateAuditSignature;
        private static bool _hasCandidateAuditSignature;

        internal static long CurrentRevision
        {
            get
            {
                EnsureGameIdentity();
                return _revision;
            }
        }

        internal static IReadOnlyList<PawnScopeCandidate> GetCandidates()
        {
            EnsureGameIdentity();
            if (_candidateCacheRevision != _revision)
            {
                RebuildCandidates();
            }

            return Candidates;
        }

        internal static Pawn ResolvePawn(int thingId)
        {
            if (thingId <= 0)
            {
                return null;
            }

            EnsureGameIdentity();
            if (_pawnIndexRevision != _revision)
            {
                RebuildPawnIndex();
            }

            if (PawnsByThingId.TryGetValue(thingId, out Pawn pawn))
            {
                return pawn;
            }

            return null;
        }

        internal static void NotifyRosterChanged()
        {
            EnsureGameIdentity();
            InvalidateCaches();
        }

        internal static void AuditIfDue(int ticksGame)
        {
            EnsureGameIdentity();
            if (_boundGame == null || ticksGame < _nextAuditTick)
            {
                return;
            }

            _nextAuditTick = ticksGame + AuditIntervalTicks;
            long signature = ComputeCandidateSignature();
            if (_hasCandidateAuditSignature && signature != _candidateAuditSignature)
            {
                InvalidateCaches();
            }

            _candidateAuditSignature = signature;
            _hasCandidateAuditSignature = true;
        }

        private static void EnsureGameIdentity()
        {
            Game game = Current.Game;
            if (ReferenceEquals(game, _boundGame))
            {
                return;
            }

            _boundGame = game;
            Candidates.Clear();
            PawnsByThingId.Clear();
            _candidateCacheRevision = long.MinValue;
            _pawnIndexRevision = long.MinValue;
            _candidateAuditSignature = 0L;
            _hasCandidateAuditSignature = false;
            _nextAuditTick = 0;
            AdvanceRevision();
        }

        private static void InvalidateCaches()
        {
            AdvanceRevision();
            _candidateCacheRevision = long.MinValue;
            _pawnIndexRevision = long.MinValue;
        }

        private static void AdvanceRevision()
        {
            unchecked
            {
                _revision++;
                if (_revision == long.MinValue)
                {
                    _revision = 1L;
                }
            }
        }

        private static void RebuildCandidates()
        {
            Candidates.Clear();
            IReadOnlyList<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            Map currentMap = Find.CurrentMap;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null || pawn.thingIDNumber <= 0)
                    {
                        continue;
                    }

                    bool isCurrentMap = pawn.Map == currentMap;
                    Candidates.Add(new PawnScopeCandidate(
                        WorkTabEffectiveStateIds.ForPawn(pawn),
                        isCurrentMap,
                        pawn.IsColonist,
                        isCurrentMap && pawn.IsFreeColonist));
                }
            }

            _candidateCacheRevision = _revision;
            _candidateAuditSignature = ComputeCandidateSignature(pawns, currentMap);
            _hasCandidateAuditSignature = true;
        }

        private static void RebuildPawnIndex()
        {
            PawnsByThingId.Clear();
            IReadOnlyList<Pawn> pawns = PawnsFinder.All_AliveOrDead;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn != null && pawn.thingIDNumber > 0 &&
                        !PawnsByThingId.ContainsKey(pawn.thingIDNumber))
                    {
                        // Preserve the previous resolver's first-match behavior
                        // if a malformed pawn list contains duplicate IDs.
                        PawnsByThingId.Add(pawn.thingIDNumber, pawn);
                    }
                }
            }

            _pawnIndexRevision = _revision;
        }

        private static long ComputeCandidateSignature()
        {
            return ComputeCandidateSignature(
                PawnsFinder.AllMapsWorldAndTemporary_Alive,
                Find.CurrentMap);
        }

        private static long ComputeCandidateSignature(
            IReadOnlyList<Pawn> pawns,
            Map currentMap)
        {
            unchecked
            {
                long signature = 17L;
                signature = (signature * 31L) + (pawns?.Count ?? 0);
                if (pawns == null)
                {
                    return signature;
                }

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null)
                    {
                        signature = (signature * 31L) + 1L;
                        continue;
                    }

                    bool isCurrentMap = pawn.Map == currentMap;
                    signature = (signature * 31L) + pawn.thingIDNumber;
                    signature = (signature * 31L) + (isCurrentMap ? 1L : 0L);
                    signature = (signature * 31L) + (pawn.IsColonist ? 1L : 0L);
                    signature = (signature * 31L) +
                        (isCurrentMap && pawn.IsFreeColonist ? 1L : 0L);
                }

                return signature;
            }
        }
    }

}
