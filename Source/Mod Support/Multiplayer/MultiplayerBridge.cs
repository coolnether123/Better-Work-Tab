using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using Multiplayer.API;

namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    internal static class MultiplayerBridge
    {
        private static IWorkloadTransactionAuthentication _workloadAuthentication;

        internal static bool ApiReady => MP.enabled;
        internal static bool Active => MP.enabled && MP.IsInMultiplayer;
        internal static bool Host => Active && MP.IsHosting;
        internal static string LocalPlayerName => MP.enabled && !string.IsNullOrEmpty(MP.PlayerName)
            ? MP.PlayerName
            : "SinglePlayer";

        internal static bool WorkloadSenderAuthenticationAvailable
        {
            get
            {
                try
                {
                    return _workloadAuthentication != null &&
                           _workloadAuthentication.IsAvailable;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// True only after the protocol has a real sender-identity binding and
        /// the loaded Multiplayer API can enforce the host-only command
        /// boundary.  A roster membership check is deliberately not enough:
        /// it authenticates a name supplied by the wire, not the connection
        /// that supplied the message.
        /// </summary>
        internal static bool WorkloadRuntimeAuthenticationAvailable =>
            Active && WorkloadSenderAuthenticationAvailable;

        internal static void SetWorkloadTransactionAuthentication(
            IWorkloadTransactionAuthentication authentication)
        {
            _workloadAuthentication = authentication;
        }

        internal static bool TryGetAuthenticatedRuntimeSender(
            string claimedSenderKey,
            string hostSessionEpoch,
            string rosterFingerprint,
            bool requireHost,
            out string actualSenderKey,
            out string diagnostic)
        {
            actualSenderKey = null;
            diagnostic = null;

            if (!Active)
            {
                actualSenderKey = LocalPlayerName;
                return true;
            }

            if (!WorkloadSenderAuthenticationAvailable ||
                _workloadAuthentication == null)
            {
                diagnostic = "The Multiplayer API does not expose an authenticated workload sender binding.";
                return false;
            }

            try
            {
                if (!_workloadAuthentication.TryGetSynchronizedSenderKey(out actualSenderKey) ||
                    string.IsNullOrEmpty(actualSenderKey))
                {
                    actualSenderKey = null;
                    diagnostic = "The synchronized workload sender identity is unavailable.";
                    return false;
                }

                if (!string.Equals(actualSenderKey, claimedSenderKey, StringComparison.Ordinal))
                {
                    diagnostic = "The workload message claimed a different sender than the synchronized runtime identity.";
                    actualSenderKey = null;
                    return false;
                }

                if (!MatchesCurrentWorkloadSession(hostSessionEpoch, rosterFingerprint))
                {
                    diagnostic = "The workload message session context is stale.";
                    actualSenderKey = null;
                    return false;
                }

                bool authenticated = requireHost
                    ? _workloadAuthentication.IsAuthenticatedHost(
                        actualSenderKey, hostSessionEpoch, rosterFingerprint)
                    : _workloadAuthentication.IsAuthenticatedPeer(
                        actualSenderKey, hostSessionEpoch, rosterFingerprint);
                if (!authenticated)
                {
                    diagnostic = requireHost
                        ? "The workload control sender is not the authenticated host."
                        : "The workload report sender is not an authenticated participant.";
                    actualSenderKey = null;
                    return false;
                }

                return true;
            }
            catch
            {
                actualSenderKey = null;
                diagnostic = "The synchronized workload sender could not be authenticated.";
                return false;
            }
        }

        internal static bool TryGetAuthenticatedHostParticipantKey(
            out string hostParticipantKey,
            string hostSessionEpoch,
            string rosterFingerprint)
        {
            hostParticipantKey = null;
            if (!Active)
            {
                hostParticipantKey = LocalPlayerName;
                return !string.IsNullOrEmpty(hostParticipantKey);
            }

            if (!WorkloadSenderAuthenticationAvailable ||
                _workloadAuthentication == null ||
                !MatchesCurrentWorkloadSession(hostSessionEpoch, rosterFingerprint))
                return false;

            try
            {
                return _workloadAuthentication.TryGetAuthenticatedHostKey(
                    hostSessionEpoch,
                    rosterFingerprint,
                    out hostParticipantKey) &&
                    !string.IsNullOrEmpty(hostParticipantKey);
            }
            catch
            {
                hostParticipantKey = null;
                return false;
            }
        }

        internal static bool TryGetWorkloadSessionContext(
            out string hostSessionEpoch,
            out string rosterFingerprint)
        {
            if (!Active)
            {
                hostSessionEpoch = "singleplayer";
                rosterFingerprint = "singleplayer";
                return true;
            }

            hostSessionEpoch = TryReadGlobalSessionId();
            rosterFingerprint = TryReadRosterFingerprint();
            return !string.IsNullOrEmpty(hostSessionEpoch) &&
                   !string.IsNullOrEmpty(rosterFingerprint);
        }

        internal static bool TryGetWorkloadParticipantKeys(out IReadOnlyList<string> participantKeys)
        {
            participantKeys = null;
            try
            {
                var method = FindParameterlessStaticMethod("GetPlayers");
                var players = method?.Invoke(null, null) as IEnumerable;
                if (players == null) return false;

                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var player in players)
                {
                    string key = ReadStringMember(player, "Username") ??
                                 ReadStringMember(player, "Name") ??
                                 ReadStringMember(player, "Id");
                    if (!string.IsNullOrEmpty(key)) keys.Add(key);
                }

                if (keys.Count == 0) return false;
                var sorted = keys.ToList();
                sorted.Sort(StringComparer.Ordinal);
                participantKeys = new ReadOnlyCollection<string>(sorted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryAuthorizeWorkloadInitiator(
            string senderKey,
            string hostSessionEpoch,
            string rosterFingerprint,
            out WorkloadTransactionAdmissionCode failureCode,
            out string diagnostic)
        {
            failureCode = WorkloadTransactionAdmissionCode.Accepted;
            diagnostic = null;

            if (!Active)
                return true;

            if (string.IsNullOrEmpty(hostSessionEpoch) ||
                string.IsNullOrEmpty(rosterFingerprint))
            {
                failureCode = WorkloadTransactionAdmissionCode.SessionContextUnavailable;
                diagnostic = "The multiplayer session or roster identity is unavailable.";
                return false;
            }

            if (!MatchesCurrentWorkloadSession(hostSessionEpoch, rosterFingerprint))
            {
                failureCode = WorkloadTransactionAdmissionCode.SessionMismatch;
                diagnostic = "The workload transaction session context is stale.";
                return false;
            }

            // The local host begins a request through the ordinary BWT UI, so
            // its runtime identity is already the authenticated local player.
            // Incoming synchronized requests take the separate sender-binding
            // path below; a roster/name fallback is never accepted.
            if (Host &&
                string.Equals(senderKey, LocalPlayerName, StringComparison.Ordinal) &&
                !MP.IsExecutingSyncCommand)
                return true;

            if (!WorkloadSenderAuthenticationAvailable)
            {
                failureCode = WorkloadTransactionAdmissionCode.SenderAuthenticationFailed;
                diagnostic = "Workload transactions require an authenticated synchronized sender binding.";
                return false;
            }

            try
            {
                if (_workloadAuthentication.IsAuthenticatedInitiator(
                    senderKey,
                    hostSessionEpoch,
                    rosterFingerprint))
                {
                    return true;
                }
            }
            catch
            {
                // Authentication failures are fail-closed. A broken optional
                // adapter must not turn into an implicit host handoff.
            }

            failureCode = WorkloadTransactionAdmissionCode.SenderAuthenticationFailed;
            diagnostic = "The workload transaction sender could not be authenticated.";
            return false;
        }

        internal static bool TryAuthorizeWorkloadHostMessage(
            string senderKey,
            string hostSessionEpoch,
            string rosterFingerprint,
            string sourceTemplateFingerprint = "legacy",
            long taxonomyRevision = 0,
            string taxonomyFingerprint = "legacy")
        {
            if (!Active)
                return true;

            return TryGetAuthenticatedRuntimeSender(
                senderKey,
                hostSessionEpoch,
                rosterFingerprint,
                true,
                out _,
                out _);
        }

        internal static bool TryAuthorizeWorkloadPeerMessage(
            string senderKey,
            string hostSessionEpoch,
            string rosterFingerprint)
        {
            if (!Active)
                return true;

            return TryGetAuthenticatedRuntimeSender(
                senderKey,
                hostSessionEpoch,
                rosterFingerprint,
                false,
                out _,
                out _);
        }

        private static bool MatchesCurrentWorkloadSession(
            string hostSessionEpoch,
            string rosterFingerprint)
        {
            if (string.IsNullOrEmpty(hostSessionEpoch) ||
                string.IsNullOrEmpty(rosterFingerprint) ||
                !TryGetWorkloadSessionContext(out var currentHostSessionEpoch, out var currentRosterFingerprint))
            {
                return false;
            }

            return string.Equals(
                       hostSessionEpoch,
                       currentHostSessionEpoch,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       rosterFingerprint,
                       currentRosterFingerprint,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string TryReadGlobalSessionId()
        {
            try
            {
                var method = FindParameterlessStaticMethod("GetGlobalSessionManager");
                if (method == null)
                    return null;

                var manager = method.Invoke(null, null);
                string direct = ReadStringMember(manager, "SessionId") ??
                                ReadStringMember(manager, "SessionID") ??
                                ReadStringMember(manager, "Id");
                if (!string.IsNullOrEmpty(direct))
                    return direct;

                // Multiplayer API 0.6 exposes session identity through the
                // global session collection rather than a manager SessionId.
                // Derive the host epoch only from those synchronized session
                // IDs; never invent a process-local value.
                var sessions = ReadMember(manager, "AllSessions") as IEnumerable;
                if (sessions == null)
                    return null;

                var ids = new List<string>();
                foreach (var session in sessions)
                {
                    string id = ReadStringMember(session, "SessionId") ??
                                ReadStringMember(session, "Id");
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }

                if (ids.Count == 0)
                    return null;
                ids.Sort(StringComparer.Ordinal);
                return WorkloadTransactionCanonicalization.ComputeFingerprint(
                    Encoding.UTF8.GetBytes(string.Join("\u001e", ids)));
            }
            catch
            {
                // Older MultiplayerAPI versions do not expose a session manager.
                // The protocol must fail closed instead of inventing a peer-shared ID.
                return null;
            }
        }

        private static string TryReadRosterFingerprint()
        {
            try
            {
                var method = FindParameterlessStaticMethod("GetPlayers");
                if (method == null)
                    return null;

                var players = method.Invoke(null, null) as IEnumerable;
                if (players == null)
                    return null;

                var identities = new List<string>();
                foreach (var player in players)
                {
                    var id = ReadStringMember(player, "Id");
                    var name = ReadStringMember(player, "Username") ??
                               ReadStringMember(player, "Name");
                    if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name))
                        continue;

                    identities.Add((id ?? string.Empty) + "\u001f" + (name ?? string.Empty));
                }

                if (identities.Count == 0)
                    return null;

                identities.Sort(StringComparer.Ordinal);
                return WorkloadTransactionCanonicalization.ComputeFingerprint(
                    Encoding.UTF8.GetBytes(string.Join("\u001e", identities)));
            }
            catch
            {
                // Reflection keeps the 1.3/0.3 compile configuration source-compatible.
                return null;
            }
        }

        private static object ReadMember(object instance, string memberName)
        {
            if (instance == null) return null;
            var type = instance.GetType();
            var property = type.GetProperty(memberName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null) return property.GetValue(instance, null);
            return type.GetField(memberName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
        }

        private static string ReadStringMember(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            var type = instance.GetType();
            var property = type.GetProperty(
                memberName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
            {
                var value = property.GetValue(instance, null);
                if (value != null)
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            var field = type.GetField(
                memberName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var value = field.GetValue(instance);
                if (value != null)
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static MethodInfo FindParameterlessStaticMethod(string methodName)
        {
            return typeof(MP)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                    method.GetParameters().Length == 0);
        }
    }
}

namespace Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads
{
    internal enum WorkloadTransactionOperation : byte
    {
        Apply = 1,
        Update = 2,
        Fork = 3
    }

    internal enum WorkloadTransactionPhase : byte
    {
        Request = 1,
        Prepare = 2,
        Execute = 3,
        Confirm = 4,
        Abort = 5
    }

    internal enum WorkloadTransactionTerminalState : byte
    {
        Pending = 0,
        Succeeded = 1,
        Rejected = 2,
        Aborted = 3,
        TimedOut = 4,
        Failed = 5,
        RollbackRequired = 6,
        RolledBack = 7,
        RollbackFailed = 8
    }

    internal enum WorkloadTransactionAdmissionCode : byte
    {
        Accepted = 0,
        SinglePlayerNotRequired = 1,
        InvalidRequest = 2,
        Duplicate = 3,
        MismatchedDuplicate = 4,
        Busy = 5,
        CapacityExceeded = 6,
        HostOnlyRequired = 7,
        SenderAuthenticationFailed = 8,
        SessionContextUnavailable = 9,
        SessionMismatch = 10,
        ProtocolMismatch = 11,
        UnauthenticatedMessage = 12,
        UnknownRequest = 13
    }

    internal enum WorkloadTransactionEventKind : byte
    {
        RequestSent = 1,
        PrepareAcknowledged = 2,
        ExecuteRequested = 3,
        ExecuteReported = 4,
        ConfirmationControlReceived = 5,
        ConfirmationAcknowledged = 6,
        FinalConfirmationReceived = 7,
        Confirmed = 7,
        AbortRequested = 8,
        Timeout = 9,
        Failure = 10,
        RollbackReported = 11
    }

    internal enum WorkloadTransactionTransitionAction : byte
    {
        None = 0,
        SendPrepare = 1,
        SendExecute = 2,
        ExecuteLocally = 3,
        SendConfirm = 4,
        ConfirmLocally = 5,
        SendFinalConfirmation = 6,
        SendConfirmationAcknowledgement = 7,
        SendAbort = 8
    }

    internal enum WorkloadTransactionControlKind : byte
    {
        Execute = 1,
        ConfirmationRequest = 2,
        FinalConfirmation = 3,
        Abort = 4
    }

    internal enum WorkloadTransactionTransitionDisposition : byte
    {
        Applied = 0,
        Ignored = 1,
        Rejected = 2
    }

    internal interface IWorkloadTransactionAuthentication
    {
        bool IsAvailable { get; }

        /// <summary>
        /// Returns the identity supplied by the synchronized runtime for the
        /// command currently being invoked.  The value must come from the
        /// transport/API context, never from a field in the wire payload.
        /// </summary>
        bool TryGetSynchronizedSenderKey(out string senderKey);

        bool TryGetAuthenticatedHostKey(
            string hostSessionEpoch,
            string rosterFingerprint,
            out string hostKey);

        bool IsAuthenticatedInitiator(
            string senderKey,
            string hostSessionEpoch,
            string rosterFingerprint);

        bool IsAuthenticatedHost(
            string senderKey,
            string hostSessionEpoch,
            string rosterFingerprint);

        bool IsAuthenticatedPeer(
            string senderKey,
            string hostSessionEpoch,
            string rosterFingerprint);
    }

    internal interface IWorkloadTransactionCallbacks
    {
        void OnRequestAccepted(WorkloadTransactionRequest request);

        void OnAdmissionRejected(WorkloadTransactionAdmission admission);

        void OnPrepareRequested(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state);

        void OnExecuteRequested(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state);

        void OnConfirmationControlReceived(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state);

        void OnFinalConfirmationRequested(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state);

        void OnConfirmRequested(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state);

        void OnAbortRequested(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state);

        void OnRollbackRequired(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state);

        void OnTerminal(WorkloadTransactionResult result);
    }

    internal interface IWorkloadTransactionTransport
    {
        void SendRequest(WorkloadTransactionWireRequest request);

        void SendPrepareAcknowledgement(WorkloadTransactionWireAcknowledgement acknowledgement);

        void SendExecuteResult(WorkloadTransactionWireResult result);

        void SendControl(WorkloadTransactionWireControl control);
    }

    internal sealed class WorkloadTransactionPayload
    {
        internal const int MaxPayloadBytes = 1024 * 1024;

        private readonly byte[] _canonicalBytes;
        private readonly string _fingerprint;

        private WorkloadTransactionPayload(byte[] canonicalBytes, string fingerprint)
        {
            _canonicalBytes = canonicalBytes;
            _fingerprint = fingerprint;
        }

        internal int Length => _canonicalBytes.Length;
        internal string Fingerprint => _fingerprint;

        internal byte[] CopyBytes()
        {
            return (byte[])_canonicalBytes.Clone();
        }

        internal IReadOnlyList<byte> Bytes =>
            new ReadOnlyCollection<byte>(CopyBytes());

        internal static bool TryCreate(
            byte[] canonicalBytes,
            string expectedFingerprint,
            out WorkloadTransactionPayload payload,
            out string diagnostic)
        {
            payload = null;
            diagnostic = null;

            if (canonicalBytes == null)
            {
                diagnostic = "The canonical workload payload is missing.";
                return false;
            }

            if (canonicalBytes.Length > MaxPayloadBytes)
            {
                diagnostic = "The canonical workload payload exceeds the protocol limit.";
                return false;
            }

            var copy = (byte[])canonicalBytes.Clone();
            var fingerprint = WorkloadTransactionCanonicalization.ComputeFingerprint(copy);
            if (!string.IsNullOrEmpty(expectedFingerprint) &&
                !string.Equals(fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "The workload payload fingerprint does not match its bytes.";
                return false;
            }

            payload = new WorkloadTransactionPayload(copy, fingerprint);
            return true;
        }
    }

    internal sealed class WorkloadTransactionRevisionVector
    {
        internal WorkloadTransactionRevisionVector(
            long storeRevision,
            long sessionRevision,
            long authorityRevision,
            long priorityServiceRevision,
            long scheduleServiceRevision,
            long specificJobServiceRevision,
            long settingsServiceRevision,
            long membershipRevision,
            string authorityOwner,
            string hostSessionEpoch,
            string rosterFingerprint,
            string sourceTemplateFingerprint = "legacy",
            long taxonomyRevision = 0,
            string taxonomyFingerprint = "legacy")
        {
            StoreRevision = storeRevision;
            SessionRevision = sessionRevision;
            AuthorityRevision = authorityRevision;
            PriorityServiceRevision = priorityServiceRevision;
            ScheduleServiceRevision = scheduleServiceRevision;
            SpecificJobServiceRevision = specificJobServiceRevision;
            SettingsServiceRevision = settingsServiceRevision;
            MembershipRevision = membershipRevision;
            AuthorityOwner = authorityOwner ?? string.Empty;
            HostSessionEpoch = hostSessionEpoch ?? string.Empty;
            RosterFingerprint = (rosterFingerprint ?? string.Empty).ToLowerInvariant();
            SourceTemplateFingerprint = sourceTemplateFingerprint ?? string.Empty;
            TaxonomyRevision = taxonomyRevision;
            TaxonomyFingerprint = taxonomyFingerprint ?? string.Empty;
        }

        internal long StoreRevision { get; }
        internal long SessionRevision { get; }
        internal long AuthorityRevision { get; }
        internal long PriorityServiceRevision { get; }
        internal long ScheduleServiceRevision { get; }
        internal long SpecificJobServiceRevision { get; }
        internal long SettingsServiceRevision { get; }
        internal long MembershipRevision { get; }
        internal string AuthorityOwner { get; }
        internal string HostSessionEpoch { get; }
        internal string RosterFingerprint { get; }
        internal string SourceTemplateFingerprint { get; }
        internal long TaxonomyRevision { get; }
        internal string TaxonomyFingerprint { get; }

        internal string ContextFingerprint
        {
            get
            {
                var builder = new StringBuilder(512);
                AppendCanonical(builder);
                return WorkloadTransactionCanonicalization.ComputeFingerprint(
                    Encoding.UTF8.GetBytes(builder.ToString()));
            }
        }

        internal bool IsValidForProtocol =>
            StoreRevision >= 0 &&
            SessionRevision >= 0 &&
            AuthorityRevision >= 0 &&
            PriorityServiceRevision >= 0 &&
            ScheduleServiceRevision >= 0 &&
            SpecificJobServiceRevision >= 0 &&
            SettingsServiceRevision >= 0 &&
            MembershipRevision >= 0 &&
            TaxonomyRevision >= 0 &&
            IsValidContextToken(AuthorityOwner) &&
            IsValidContextToken(HostSessionEpoch) &&
            IsValidContextToken(RosterFingerprint) &&
            IsValidContextToken(SourceTemplateFingerprint) &&
            IsValidContextToken(TaxonomyFingerprint);

        internal void AppendCanonical(StringBuilder builder)
        {
            WorkloadTransactionCanonicalization.AppendToken(builder, StoreRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, SessionRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, AuthorityRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, PriorityServiceRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, ScheduleServiceRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, SpecificJobServiceRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, SettingsServiceRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, MembershipRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, AuthorityOwner);
            WorkloadTransactionCanonicalization.AppendToken(builder, HostSessionEpoch);
            WorkloadTransactionCanonicalization.AppendToken(builder, RosterFingerprint);
            WorkloadTransactionCanonicalization.AppendToken(builder, SourceTemplateFingerprint);
            WorkloadTransactionCanonicalization.AppendToken(builder, TaxonomyRevision.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(builder, TaxonomyFingerprint);
        }

        private static bool IsValidContextToken(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.Length > WorkloadTransactionRequest.MaxIdentifierLength)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                    return false;
            }

            return true;
        }
    }

    internal sealed class WorkloadTransactionRequest
    {
        internal const int CurrentProtocolVersion = 1;
        internal const int MaxIdentifierLength = 256;
        internal const int MaxParticipants = 256;

        private WorkloadTransactionRequest(
            int protocolVersion,
            WorkloadTransactionOperation operation,
            string requestId,
            string idempotencyKey,
            string sessionId,
            string sourceWorkloadId,
            string targetWorkloadId,
            string requesterPlayerKey,
            int timeoutBudget,
            WorkloadTransactionPayload payload,
            WorkloadTransactionRevisionVector expectedRevisions,
            IReadOnlyList<string> participantKeys,
            string requestFingerprint,
            string replayFingerprint)
        {
            ProtocolVersion = protocolVersion;
            Operation = operation;
            RequestId = requestId;
            IdempotencyKey = idempotencyKey;
            SessionId = sessionId;
            SourceWorkloadId = sourceWorkloadId;
            TargetWorkloadId = targetWorkloadId;
            RequesterPlayerKey = requesterPlayerKey;
            TimeoutBudget = timeoutBudget;
            Payload = payload;
            ExpectedRevisions = expectedRevisions;
            ParticipantKeys = participantKeys;
            RequestFingerprint = requestFingerprint;
            ReplayFingerprint = replayFingerprint;
        }

        internal int ProtocolVersion { get; }
        internal WorkloadTransactionOperation Operation { get; }
        internal string RequestId { get; }
        internal string IdempotencyKey { get; }
        internal string SessionId { get; }
        internal string SourceWorkloadId { get; }
        internal string TargetWorkloadId { get; }
        internal string RequesterPlayerKey { get; }
        internal int TimeoutBudget { get; }
        internal WorkloadTransactionPayload Payload { get; }
        internal WorkloadTransactionRevisionVector ExpectedRevisions { get; }
        internal IReadOnlyList<string> ParticipantKeys { get; }
        internal string RequestFingerprint { get; }
        internal string ReplayFingerprint { get; }

        internal string TableKey =>
            ExpectedRevisions.HostSessionEpoch + "\u001f" +
            ExpectedRevisions.RosterFingerprint + "\u001f" +
            RequesterPlayerKey + "\u001f" +
            IdempotencyKey;

        internal static bool TryCreate(
            WorkloadTransactionOperation operation,
            string requestId,
            string sessionId,
            string sourceWorkloadId,
            string targetWorkloadId,
            string requesterPlayerKey,
            int timeoutBudget,
            WorkloadTransactionPayload payload,
            WorkloadTransactionRevisionVector expectedRevisions,
            IEnumerable<string> participantKeys,
            out WorkloadTransactionRequest request,
            out string diagnostic)
        {
            return TryCreateCanonical(
                operation, requestId, requestId, sessionId, sourceWorkloadId,
                targetWorkloadId, requesterPlayerKey, timeoutBudget, payload,
                expectedRevisions, participantKeys, out request, out diagnostic);
        }

        internal static bool TryCreateCanonical(
            WorkloadTransactionOperation operation,
            string requestId,
            string idempotencyKey,
            string sessionId,
            string sourceWorkloadId,
            string targetWorkloadId,
            string requesterPlayerKey,
            int timeoutBudget,
            WorkloadTransactionPayload payload,
            WorkloadTransactionRevisionVector expectedRevisions,
            IEnumerable<string> participantKeys,
            out WorkloadTransactionRequest request,
            out string diagnostic)
        {
            request = null;
            diagnostic = null;

            if (!Enum.IsDefined(typeof(WorkloadTransactionOperation), operation))
            {
                diagnostic = "The workload transaction operation is invalid.";
                return false;
            }

            if (!IsValidIdentifier(requestId) ||
                !IsValidIdentifier(idempotencyKey) ||
                !IsValidIdentifier(sessionId) ||
                !IsValidIdentifier(sourceWorkloadId) ||
                !IsValidIdentifier(requesterPlayerKey))
            {
                diagnostic = "The workload transaction contains a missing or invalid identity.";
                return false;
            }

            if (operation == WorkloadTransactionOperation.Apply)
            {
                if (string.IsNullOrEmpty(targetWorkloadId))
                    targetWorkloadId = sourceWorkloadId;
                else if (!IsValidIdentifier(targetWorkloadId))
                {
                    diagnostic = "Apply contains an invalid target workload identity.";
                    return false;
                }
            }
            else if (!IsValidIdentifier(targetWorkloadId))
            {
                diagnostic = "Update and Fork require a stable target workload identity.";
                return false;
            }

            if (operation == WorkloadTransactionOperation.Update &&
                !string.Equals(sourceWorkloadId, targetWorkloadId, StringComparison.Ordinal))
            {
                diagnostic = "Update must target the same stable workload ID.";
                return false;
            }

            if (operation == WorkloadTransactionOperation.Fork &&
                string.Equals(sourceWorkloadId, targetWorkloadId, StringComparison.Ordinal))
            {
                diagnostic = "Fork must target a new stable workload ID.";
                return false;
            }

            if (timeoutBudget <= 0 || timeoutBudget > 1000000)
            {
                diagnostic = "The workload transaction timeout budget is invalid.";
                return false;
            }

            if (payload == null || expectedRevisions == null || !expectedRevisions.IsValidForProtocol)
            {
                diagnostic = "The workload transaction payload or revision vector is invalid.";
                return false;
            }

            if (!TryNormalizeParticipants(participantKeys, out var normalizedParticipants, out diagnostic))
                return false;

            var fingerprintBuilder = new StringBuilder(512);
            WorkloadTransactionCanonicalization.AppendToken(
                fingerprintBuilder,
                CurrentProtocolVersion.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(
                fingerprintBuilder,
                ((byte)operation).ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, requestId);
            WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, idempotencyKey);
            WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, sessionId);
            WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, sourceWorkloadId);
            WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, targetWorkloadId);
            WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, requesterPlayerKey);
            WorkloadTransactionCanonicalization.AppendToken(
                fingerprintBuilder,
                timeoutBudget.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, payload.Fingerprint);
            expectedRevisions.AppendCanonical(fingerprintBuilder);
            foreach (var participant in normalizedParticipants)
                WorkloadTransactionCanonicalization.AppendToken(fingerprintBuilder, participant);

            var replayBuilder = new StringBuilder(512);
            WorkloadTransactionCanonicalization.AppendToken(
                replayBuilder,
                CurrentProtocolVersion.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(
                replayBuilder,
                ((byte)operation).ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(replayBuilder, sessionId);
            WorkloadTransactionCanonicalization.AppendToken(replayBuilder, sourceWorkloadId);
            WorkloadTransactionCanonicalization.AppendToken(replayBuilder, targetWorkloadId);
            WorkloadTransactionCanonicalization.AppendToken(replayBuilder, requesterPlayerKey);
            WorkloadTransactionCanonicalization.AppendToken(
                replayBuilder,
                timeoutBudget.ToString(CultureInfo.InvariantCulture));
            WorkloadTransactionCanonicalization.AppendToken(replayBuilder, payload.Fingerprint);
            expectedRevisions.AppendCanonical(replayBuilder);
            foreach (var participant in normalizedParticipants)
                WorkloadTransactionCanonicalization.AppendToken(replayBuilder, participant);

            request = new WorkloadTransactionRequest(
                CurrentProtocolVersion,
                operation,
                requestId,
                idempotencyKey,
                sessionId,
                sourceWorkloadId,
                targetWorkloadId,
                requesterPlayerKey,
                timeoutBudget,
                payload,
                expectedRevisions,
                normalizedParticipants,
                WorkloadTransactionCanonicalization.ComputeFingerprint(
                    Encoding.UTF8.GetBytes(fingerprintBuilder.ToString())),
                WorkloadTransactionCanonicalization.ComputeFingerprint(
                    Encoding.UTF8.GetBytes(replayBuilder.ToString())));
            return true;
        }

        internal WorkloadTransactionWireRequest ToWire()
        {
            return new WorkloadTransactionWireRequest
            {
                ProtocolVersion = ProtocolVersion,
                Operation = (byte)Operation,
                RequestId = RequestId,
                IdempotencyKey = IdempotencyKey,
                SessionId = SessionId,
                SourceWorkloadId = SourceWorkloadId,
                TargetWorkloadId = TargetWorkloadId,
                RequesterPlayerKey = RequesterPlayerKey,
                TimeoutBudget = TimeoutBudget,
                PayloadBytes = new List<byte>(Payload.CopyBytes()),
                PayloadFingerprint = Payload.Fingerprint,
                RequestFingerprint = RequestFingerprint,
                ParticipantKeys = new List<string>(ParticipantKeys),
                StoreRevision = ExpectedRevisions.StoreRevision,
                SessionRevision = ExpectedRevisions.SessionRevision,
                AuthorityRevision = ExpectedRevisions.AuthorityRevision,
                PriorityServiceRevision = ExpectedRevisions.PriorityServiceRevision,
                ScheduleServiceRevision = ExpectedRevisions.ScheduleServiceRevision,
                SpecificJobServiceRevision = ExpectedRevisions.SpecificJobServiceRevision,
                SettingsServiceRevision = ExpectedRevisions.SettingsServiceRevision,
                MembershipRevision = ExpectedRevisions.MembershipRevision,
                SourceTemplateFingerprint = ExpectedRevisions.SourceTemplateFingerprint,
                TaxonomyRevision = ExpectedRevisions.TaxonomyRevision,
                TaxonomyFingerprint = ExpectedRevisions.TaxonomyFingerprint,
                AuthorityOwner = ExpectedRevisions.AuthorityOwner,
                HostSessionEpoch = ExpectedRevisions.HostSessionEpoch,
                RosterFingerprint = ExpectedRevisions.RosterFingerprint
            };
        }

        internal static bool TryFromWire(
            WorkloadTransactionWireRequest wire,
            out WorkloadTransactionRequest request,
            out string diagnostic)
        {
            request = null;
            diagnostic = null;
            if (wire == null)
            {
                diagnostic = "The workload transaction request is missing.";
                return false;
            }

            if (wire.ProtocolVersion != CurrentProtocolVersion)
            {
                diagnostic = "The workload transaction protocol version is unsupported.";
                return false;
            }

            if (wire.PayloadBytes != null &&
                wire.PayloadBytes.Count > WorkloadTransactionPayload.MaxPayloadBytes)
            {
                diagnostic = "The canonical workload payload exceeds the protocol limit.";
                return false;
            }

            if (wire.ParticipantKeys != null &&
                wire.ParticipantKeys.Count > MaxParticipants)
            {
                diagnostic = "The workload transaction participant roster exceeds the protocol limit.";
                return false;
            }

            if (string.IsNullOrEmpty(wire.PayloadFingerprint) ||
                string.IsNullOrEmpty(wire.RequestFingerprint))
            {
                diagnostic = "The workload transaction wire envelope is missing a fingerprint.";
                return false;
            }

            if (!WorkloadTransactionPayload.TryCreate(
                wire.PayloadBytes == null ? null : wire.PayloadBytes.ToArray(),
                wire.PayloadFingerprint,
                out var payload,
                out diagnostic))
            {
                return false;
            }

            if (!TryCreateCanonical(
                (WorkloadTransactionOperation)wire.Operation,
                wire.RequestId,
                wire.IdempotencyKey,
                wire.SessionId,
                wire.SourceWorkloadId,
                wire.TargetWorkloadId,
                wire.RequesterPlayerKey,
                wire.TimeoutBudget,
                payload,
                new WorkloadTransactionRevisionVector(
                    wire.StoreRevision,
                    wire.SessionRevision,
                    wire.AuthorityRevision,
                    wire.PriorityServiceRevision,
                    wire.ScheduleServiceRevision,
                    wire.SpecificJobServiceRevision,
                    wire.SettingsServiceRevision,
                    wire.MembershipRevision,
                    wire.AuthorityOwner,
                    wire.HostSessionEpoch,
                    wire.RosterFingerprint,
                    wire.SourceTemplateFingerprint,
                    wire.TaxonomyRevision,
                    wire.TaxonomyFingerprint),
                wire.ParticipantKeys,
                out request,
                out diagnostic))
            {
                return false;
            }

            if (!string.Equals(request.RequestFingerprint, wire.RequestFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                request = null;
                diagnostic = "The workload transaction request fingerprint does not match its canonical envelope.";
                return false;
            }

            return true;
        }

        private static bool TryNormalizeParticipants(
            IEnumerable<string> participantKeys,
            out IReadOnlyList<string> normalized,
            out string diagnostic)
        {
            normalized = null;
            diagnostic = null;
            if (participantKeys == null)
            {
                diagnostic = "The workload transaction participant roster is missing.";
                return false;
            }

            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var participant in participantKeys)
            {
                if (!IsValidIdentifier(participant))
                {
                    diagnostic = "The workload transaction participant roster contains an invalid identity.";
                    return false;
                }

                values.Add(participant);
                if (values.Count > MaxParticipants)
                {
                    diagnostic = "The workload transaction participant roster exceeds the protocol limit.";
                    return false;
                }
            }

            if (values.Count == 0)
            {
                diagnostic = "The workload transaction participant roster is empty.";
                return false;
            }

            var sorted = values.ToList();
            sorted.Sort(StringComparer.Ordinal);
            normalized = new ReadOnlyCollection<string>(sorted.ToArray());
            return true;
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxIdentifierLength)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                    return false;
            }

            return true;
        }
    }

    internal static class WorkloadTransactionCanonicalization
    {
        internal static void AppendToken(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append(';');
        }

        internal static string ComputeFingerprint(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes ?? new byte[0]);
                var chars = new char[hash.Length * 2];
                const string digits = "0123456789abcdef";
                for (var i = 0; i < hash.Length; i++)
                {
                    chars[i * 2] = digits[hash[i] >> 4];
                    chars[(i * 2) + 1] = digits[hash[i] & 0x0f];
                }

                return new string(chars);
            }
        }
    }

    internal sealed class WorkloadTransactionWireRequest
    {
        public int ProtocolVersion;
        public byte Operation;
        public string RequestId;
        public string IdempotencyKey;
        public string SessionId;
        public string SourceWorkloadId;
        public string TargetWorkloadId;
        public string RequesterPlayerKey;
        public int TimeoutBudget;
        public List<byte> PayloadBytes = new List<byte>();
        public string PayloadFingerprint;
        public string RequestFingerprint;
        public List<string> ParticipantKeys = new List<string>();
        public long StoreRevision;
        public long SessionRevision;
        public long AuthorityRevision;
        public long PriorityServiceRevision;
        public long ScheduleServiceRevision;
        public long SpecificJobServiceRevision;
        public long SettingsServiceRevision;
        public long MembershipRevision;
        public string SourceTemplateFingerprint;
        public long TaxonomyRevision;
        public string TaxonomyFingerprint;
        public string AuthorityOwner;
        public string HostSessionEpoch;
        public string RosterFingerprint;
    }

    internal sealed class WorkloadTransactionWireAcknowledgement
    {
        public byte Phase;
        public bool Accepted;
        public bool RequiresRollback;
        public string RequestId;
        public string RequestFingerprint;
        public string HostSessionEpoch;
        public string RosterFingerprint;
        public string PeerKey;
        public string Code;
        public string Detail;
        public string ReportFingerprint;
        public long Sequence;
        public string ContextFingerprint;
        public string AuthorityOwner;
        public string SourceTemplateFingerprint;
        public string TaxonomyFingerprint;
        public long StoreRevision;
        public long SessionRevision;
        public long AuthorityRevision;
        public long PriorityServiceRevision;
        public long ScheduleServiceRevision;
        public long SpecificJobServiceRevision;
        public long SettingsServiceRevision;
        public long MembershipRevision;
        public long TaxonomyRevision;
    }

    internal sealed class WorkloadTransactionWireResult
    {
        public byte Phase;
        public byte TerminalState;
        public bool Accepted;
        public bool RequiresRollback;
        public string RequestId;
        public string RequestFingerprint;
        public string HostSessionEpoch;
        public string RosterFingerprint;
        public string PeerKey;
        public string Code;
        public string Detail;
        public string ReportFingerprint;
        public long Sequence;
        public long StoreRevision;
        public long SessionRevision;
        public long AuthorityRevision;
        public long PriorityServiceRevision;
        public long ScheduleServiceRevision;
        public long SpecificJobServiceRevision;
        public long SettingsServiceRevision;
        public long MembershipRevision;
        public string ContextFingerprint;
        public string AuthorityOwner;
        public string SourceTemplateFingerprint;
        public string TaxonomyFingerprint;
        public long TaxonomyRevision;
    }

    internal sealed class WorkloadTransactionWireControl
    {
        public byte Phase;
        public byte TerminalState;
        public bool Accepted;
        public bool RequiresRollback;
        public string RequestId;
        public string RequestFingerprint;
        public string HostSessionEpoch;
        public string RosterFingerprint;
        public string PeerKey;
        public string Code;
        public string Detail;
        public string ReportFingerprint;
        public long Sequence;
        public string ContextFingerprint;
        public string AuthorityOwner;
        public string SourceTemplateFingerprint;
        public string TaxonomyFingerprint;
        public long StoreRevision;
        public long SessionRevision;
        public long AuthorityRevision;
        public long PriorityServiceRevision;
        public long ScheduleServiceRevision;
        public long SpecificJobServiceRevision;
        public long SettingsServiceRevision;
        public long MembershipRevision;
        public long TaxonomyRevision;
        public byte ControlKind;
        public bool IsRollbackReport;
    }

    internal sealed class WorkloadTransactionState
    {
        internal WorkloadTransactionState(
            string requestId,
            string requestFingerprint,
            WorkloadTransactionPhase phase,
            WorkloadTransactionTerminalState terminalState,
            IEnumerable<string> participantKeys,
            IEnumerable<string> preparedParticipants,
            IEnumerable<string> executedParticipants,
            IEnumerable<string> rollbackParticipants,
            bool mutationStarted,
            long deadlineSequence,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            IEnumerable<string> rollbackReportedParticipants = null,
            IEnumerable<string> confirmationAcknowledgedParticipants = null,
            bool confirmationControlReceived = false,
            bool abortDispatched = false,
            string hostParticipantKey = null,
            bool confirmationControlAccepted = false)
        {
            RequestId = requestId;
            RequestFingerprint = requestFingerprint;
            Phase = phase;
            TerminalState = terminalState;
            ParticipantKeys = Freeze(participantKeys);
            PreparedParticipants = Freeze(preparedParticipants);
            ExecutedParticipants = Freeze(executedParticipants);
            RollbackParticipants = Freeze(rollbackParticipants);
            RollbackReportedParticipants = Freeze(rollbackReportedParticipants);
            ConfirmationAcknowledgedParticipants = Freeze(confirmationAcknowledgedParticipants);
            ConfirmationControlReceived = confirmationControlReceived;
            AbortDispatched = abortDispatched;
            HostParticipantKey = hostParticipantKey ?? string.Empty;
            ConfirmationControlAccepted = confirmationControlAccepted;
            MutationStarted = mutationStarted;
            DeadlineSequence = deadlineSequence;
            Code = code ?? string.Empty;
            Detail = detail ?? string.Empty;
            ReportFingerprint = reportFingerprint ?? string.Empty;
            Sequence = sequence;
        }

        internal string RequestId { get; }
        internal string RequestFingerprint { get; }
        internal WorkloadTransactionPhase Phase { get; }
        internal WorkloadTransactionTerminalState TerminalState { get; }
        internal IReadOnlyList<string> ParticipantKeys { get; }
        internal IReadOnlyList<string> PreparedParticipants { get; }
        internal IReadOnlyList<string> ExecutedParticipants { get; }
        internal IReadOnlyList<string> RollbackParticipants { get; }
        internal IReadOnlyList<string> RollbackReportedParticipants { get; }
        internal IReadOnlyList<string> ConfirmationAcknowledgedParticipants { get; }
        internal bool ConfirmationControlReceived { get; }
        internal bool AbortDispatched { get; }
        internal string HostParticipantKey { get; }
        internal bool ConfirmationControlAccepted { get; }
        internal bool MutationStarted { get; }
        internal long DeadlineSequence { get; }
        internal string Code { get; }
        internal string Detail { get; }
        internal string ReportFingerprint { get; }
        internal long Sequence { get; }

        internal bool IsFinal =>
            TerminalState == WorkloadTransactionTerminalState.Succeeded ||
            TerminalState == WorkloadTransactionTerminalState.Rejected ||
            TerminalState == WorkloadTransactionTerminalState.Aborted ||
            TerminalState == WorkloadTransactionTerminalState.TimedOut ||
            TerminalState == WorkloadTransactionTerminalState.Failed ||
            TerminalState == WorkloadTransactionTerminalState.RolledBack ||
            TerminalState == WorkloadTransactionTerminalState.RollbackFailed;

        internal bool IsTerminal =>
            IsFinal;

        internal bool RequiresRollback =>
            TerminalState == WorkloadTransactionTerminalState.RollbackRequired;

        private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
        {
            var copy = values == null
                ? new string[0]
                : values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            return new ReadOnlyCollection<string>(copy);
        }
    }

    internal sealed class WorkloadTransactionEvent
    {
        private WorkloadTransactionEvent(
            WorkloadTransactionEventKind kind,
            string requestId,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool requiresRollback = false)
        {
            Kind = kind;
            RequestId = requestId;
            PeerKey = peerKey;
            Accepted = accepted;
            Code = code ?? string.Empty;
            Detail = detail ?? string.Empty;
            ReportFingerprint = reportFingerprint ?? string.Empty;
            Sequence = sequence;
            RequiresRollback = requiresRollback;
        }

        internal WorkloadTransactionEventKind Kind { get; }
        internal string RequestId { get; }
        internal string PeerKey { get; }
        internal bool Accepted { get; }
        internal string Code { get; }
        internal string Detail { get; }
        internal string ReportFingerprint { get; }
        internal long Sequence { get; }
        internal bool RequiresRollback { get; }

        internal static WorkloadTransactionEvent RequestSent(string requestId, long sequence)
        {
            return New(WorkloadTransactionEventKind.RequestSent, requestId, null, true, null, null, null, sequence);
        }

        internal static WorkloadTransactionEvent PrepareAcknowledged(
            string requestId,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.PrepareAcknowledged, requestId, peerKey, accepted, code, detail, reportFingerprint, sequence);
        }

        internal static WorkloadTransactionEvent ExecuteReported(
            string requestId,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool requiresRollback = false)
        {
            return New(WorkloadTransactionEventKind.ExecuteReported, requestId, peerKey, accepted, code, detail, reportFingerprint, sequence, requiresRollback);
        }

        internal static WorkloadTransactionEvent ExecuteRequested(
            string requestId,
            bool accepted,
            string code,
            string detail,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.ExecuteRequested, requestId, null, accepted, code, detail, null, sequence, false);
        }

        internal static WorkloadTransactionEvent ConfirmationControlReceived(
            string requestId,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.ConfirmationControlReceived, requestId, null, accepted, code, detail, reportFingerprint, sequence, false);
        }

        internal static WorkloadTransactionEvent ConfirmationAcknowledged(
            string requestId,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool requiresRollback = false)
        {
            return New(WorkloadTransactionEventKind.ConfirmationAcknowledged, requestId, peerKey, accepted, code, detail, reportFingerprint, sequence, requiresRollback);
        }

        internal static WorkloadTransactionEvent FinalConfirmationReceived(
            string requestId,
            bool accepted,
            string code,
            string detail,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.FinalConfirmationReceived, requestId, null, accepted, code, detail, null, sequence, false);
        }

        internal static WorkloadTransactionEvent Confirmed(
            string requestId,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.FinalConfirmationReceived, requestId, null, accepted, code, detail, reportFingerprint, sequence, false);
        }

        internal static WorkloadTransactionEvent AbortRequested(
            string requestId,
            string code,
            string detail,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.AbortRequested, requestId, null, false, code, detail, null, sequence, false);
        }

        internal static WorkloadTransactionEvent Timeout(
            string requestId,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.Timeout, requestId, null, false, "timeout", "The workload transaction timed out.", null, sequence, false);
        }

        internal static WorkloadTransactionEvent Failure(
            string requestId,
            string code,
            string detail,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.Failure, requestId, null, false, code, detail, null, sequence, false);
        }

        internal static WorkloadTransactionEvent RollbackReported(
            string requestId,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            long sequence)
        {
            return New(WorkloadTransactionEventKind.RollbackReported, requestId, peerKey, accepted, code, detail, null, sequence, !accepted);
        }

        private static WorkloadTransactionEvent New(
            WorkloadTransactionEventKind kind,
            string requestId,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool requiresRollback = false)
        {
            return new WorkloadTransactionEvent(kind, requestId, peerKey, accepted, code, detail, reportFingerprint, sequence, requiresRollback);
        }
    }

    internal sealed class WorkloadTransactionTransitionResult
    {
        internal WorkloadTransactionTransitionResult(
            WorkloadTransactionState state,
            WorkloadTransactionTransitionDisposition disposition,
            WorkloadTransactionTransitionAction action,
            string code,
            string detail)
        {
            State = state;
            Disposition = disposition;
            Action = action;
            Code = code ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        internal WorkloadTransactionState State { get; }
        internal WorkloadTransactionTransitionDisposition Disposition { get; }
        internal WorkloadTransactionTransitionAction Action { get; }
        internal string Code { get; }
        internal string Detail { get; }
    }

    internal static class WorkloadTransactionStateMachine
    {
        internal static WorkloadTransactionTransitionResult Begin(
            WorkloadTransactionRequest request,
            long sequence,
            string hostParticipantKey = null)
        {
            if (request == null)
                return Invalid(null, "invalid-request", "The workload transaction request is missing.");

            hostParticipantKey = string.IsNullOrEmpty(hostParticipantKey)
                ? request.RequesterPlayerKey
                : hostParticipantKey;
            if (!Contains(request.ParticipantKeys, request.RequesterPlayerKey))
                return Invalid(null, "requester-not-in-roster", "The workload requester is not present in the frozen participant roster.");
            if (!Contains(request.ParticipantKeys, hostParticipantKey))
                return Invalid(null, "host-not-in-roster", "The authenticated host is not present in the frozen participant roster.");

            var deadline = sequence > long.MaxValue - request.TimeoutBudget
                ? long.MaxValue
                : sequence + request.TimeoutBudget;
            var state = new WorkloadTransactionState(
                request.RequestId,
                request.RequestFingerprint,
                WorkloadTransactionPhase.Request,
                WorkloadTransactionTerminalState.Pending,
                request.ParticipantKeys,
                null,
                null,
                null,
                false,
                deadline,
                null,
                null,
                null,
                sequence,
                hostParticipantKey: hostParticipantKey);
            return Applied(state, WorkloadTransactionTransitionAction.None, null, null);
        }

        internal static WorkloadTransactionTransitionResult Apply(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            if (state == null || transactionEvent == null)
                return Invalid(state, "invalid-transition", "The workload transaction transition is missing.");

            if (!string.Equals(state.RequestId, transactionEvent.RequestId, StringComparison.Ordinal))
                return Invalid(state, "request-mismatch", "The transition belongs to a different workload request.");

            if (state.IsFinal)
                return Ignored(state, "already-terminal", "The workload transaction is already terminal.");

            if (transactionEvent.Sequence < state.Sequence)
            {
                return Ignored(state, "stale-transition", "The workload transaction transition is older than the current state.");
            }

            if (state.Phase == WorkloadTransactionPhase.Abort &&
                state.RequiresRollback &&
                (transactionEvent.Kind == WorkloadTransactionEventKind.AbortRequested ||
                 transactionEvent.Kind == WorkloadTransactionEventKind.Timeout ||
                 transactionEvent.Kind == WorkloadTransactionEventKind.Failure))
            {
                return Ignored(state, "rollback-already-required", "The workload transaction is already awaiting rollback reports.");
            }

            switch (transactionEvent.Kind)
            {
                case WorkloadTransactionEventKind.RequestSent:
                    if (state.Phase != WorkloadTransactionPhase.Request)
                        return Ignored(state, "request-already-sent", "The workload request was already dispatched.");

                    return Applied(With(
                        state,
                        WorkloadTransactionPhase.Prepare,
                        WorkloadTransactionTerminalState.Pending,
                        state.PreparedParticipants,
                        state.ExecutedParticipants,
                        state.RollbackParticipants,
                        state.MutationStarted,
                        transactionEvent,
                        null,
                        null), WorkloadTransactionTransitionAction.SendPrepare, null, null);

                case WorkloadTransactionEventKind.ExecuteRequested:
                    if (state.Phase != WorkloadTransactionPhase.Prepare)
                        return Ignored(state, "execute-not-expected", "The workload peer is not awaiting execute control.");

                    if (!transactionEvent.Accepted)
                        return Abort(state, transactionEvent, transactionEvent.Code, transactionEvent.Detail);

                    return Applied(With(
                        state,
                        WorkloadTransactionPhase.Execute,
                        WorkloadTransactionTerminalState.Pending,
                        state.PreparedParticipants,
                        state.ExecutedParticipants,
                        state.RollbackParticipants,
                        state.MutationStarted,
                        transactionEvent,
                        transactionEvent.Code,
                        transactionEvent.Detail,
                        reportFingerprintOverride: string.Empty),
                        WorkloadTransactionTransitionAction.ExecuteLocally,
                        transactionEvent.Code,
                        transactionEvent.Detail);

                case WorkloadTransactionEventKind.PrepareAcknowledged:
                    return ApplyPrepareAcknowledgement(state, transactionEvent);

                case WorkloadTransactionEventKind.ExecuteReported:
                    return ApplyExecuteResult(state, transactionEvent);

                case WorkloadTransactionEventKind.ConfirmationControlReceived:
                    return ApplyConfirmationControl(state, transactionEvent);

                case WorkloadTransactionEventKind.ConfirmationAcknowledged:
                    return ApplyConfirmationAcknowledgement(state, transactionEvent);

                case WorkloadTransactionEventKind.FinalConfirmationReceived:
                    return ApplyFinalConfirmation(state, transactionEvent);

                case WorkloadTransactionEventKind.AbortRequested:
                    return Abort(state, transactionEvent, transactionEvent.Code, transactionEvent.Detail);

                case WorkloadTransactionEventKind.Timeout:
                    return Timeout(state, transactionEvent);

                case WorkloadTransactionEventKind.Failure:
                    return Failure(state, transactionEvent);

                case WorkloadTransactionEventKind.RollbackReported:
                    return ApplyRollbackReport(state, transactionEvent);

                default:
                    return Invalid(state, "unknown-transition", "The workload transaction transition is unsupported.");
            }
        }

        private static WorkloadTransactionTransitionResult ApplyPrepareAcknowledgement(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            if (state.Phase != WorkloadTransactionPhase.Prepare)
                return Ignored(state, "prepare-not-expected", "The workload transaction is not preparing.");

            if (!Contains(state.ParticipantKeys, transactionEvent.PeerKey))
                return Abort(state, transactionEvent, "unknown-peer", "The prepare acknowledgement came from an unknown peer.");

            if (Contains(state.PreparedParticipants, transactionEvent.PeerKey))
                return Ignored(state, "duplicate-prepare", "The prepare acknowledgement was already recorded.");

            if (state.PreparedParticipants.Count > 0 &&
                !MatchingReportFingerprint(state.ReportFingerprint, transactionEvent.ReportFingerprint))
            {
                return Abort(
                    state,
                    transactionEvent,
                    "prepare-report-mismatch",
                    "Peers produced different deterministic workload prepare plans.");
            }

            var prepared = Add(state.PreparedParticipants, transactionEvent.PeerKey);
            if (!transactionEvent.Accepted)
            {
                return Applied(With(
                    state,
                    WorkloadTransactionPhase.Abort,
                    WorkloadTransactionTerminalState.Rejected,
                    prepared,
                    state.ExecutedParticipants,
                    state.RollbackParticipants,
                    state.MutationStarted,
                    transactionEvent,
                    transactionEvent.Code,
                    transactionEvent.Detail,
                    abortDispatched: true), WorkloadTransactionTransitionAction.SendAbort, transactionEvent.Code, transactionEvent.Detail);
            }

            if (prepared.Count != state.ParticipantKeys.Count)
            {
                return Applied(With(
                    state,
                    WorkloadTransactionPhase.Prepare,
                    WorkloadTransactionTerminalState.Pending,
                    prepared,
                    state.ExecutedParticipants,
                    state.RollbackParticipants,
                    state.MutationStarted,
                    transactionEvent,
                    transactionEvent.Code,
                    transactionEvent.Detail), WorkloadTransactionTransitionAction.None, transactionEvent.Code, transactionEvent.Detail);
            }

            return Applied(With(
                state,
                WorkloadTransactionPhase.Execute,
                WorkloadTransactionTerminalState.Pending,
                prepared,
                state.ExecutedParticipants,
                state.RollbackParticipants,
                state.MutationStarted,
                transactionEvent,
                transactionEvent.Code,
                transactionEvent.Detail,
                reportFingerprintOverride: string.Empty), WorkloadTransactionTransitionAction.SendExecute, transactionEvent.Code, transactionEvent.Detail);
        }

        private static WorkloadTransactionTransitionResult ApplyExecuteResult(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            if (state.Phase != WorkloadTransactionPhase.Execute)
                return Ignored(state, "execute-not-expected", "The workload transaction is not executing.");

            if (!Contains(state.ParticipantKeys, transactionEvent.PeerKey))
                return Rollback(state, transactionEvent, "unknown-peer");

            if (Contains(state.ExecutedParticipants, transactionEvent.PeerKey))
                return Ignored(state, "duplicate-execute", "The execute result was already recorded.");

            if (!string.IsNullOrEmpty(state.ReportFingerprint) &&
                !MatchingReportFingerprint(state.ReportFingerprint, transactionEvent.ReportFingerprint))
            {
                return Rollback(state, transactionEvent, "execute-report-mismatch");
            }

            var executed = transactionEvent.Accepted || transactionEvent.RequiresRollback
                ? Add(state.ExecutedParticipants, transactionEvent.PeerKey)
                : state.ExecutedParticipants;
            if (!transactionEvent.Accepted)
            {
                if (transactionEvent.RequiresRollback || executed.Count > 0)
                    return Rollback(state, transactionEvent, "execute-failed", executed);

                return Applied(With(
                    state,
                    WorkloadTransactionPhase.Abort,
                    WorkloadTransactionTerminalState.Rejected,
                    state.PreparedParticipants,
                    executed,
                    state.RollbackParticipants,
                    false,
                    transactionEvent,
                    transactionEvent.Code,
                    transactionEvent.Detail,
                    abortDispatched: true), WorkloadTransactionTransitionAction.SendAbort, transactionEvent.Code, transactionEvent.Detail);
            }

            if (executed.Count != state.ParticipantKeys.Count)
            {
                return Applied(With(
                    state,
                    WorkloadTransactionPhase.Execute,
                    WorkloadTransactionTerminalState.Pending,
                    state.PreparedParticipants,
                    executed,
                    state.RollbackParticipants,
                    true,
                    transactionEvent,
                    transactionEvent.Code,
                    transactionEvent.Detail), WorkloadTransactionTransitionAction.None, transactionEvent.Code, transactionEvent.Detail);
            }

            return Applied(With(
                state,
                WorkloadTransactionPhase.Confirm,
                WorkloadTransactionTerminalState.Pending,
                state.PreparedParticipants,
                executed,
                state.RollbackParticipants,
                true,
                transactionEvent,
                transactionEvent.Code,
                transactionEvent.Detail,
                confirmationAcknowledgedParticipants: Add(null, state.HostParticipantKey),
                reportFingerprintOverride: string.Empty), WorkloadTransactionTransitionAction.SendConfirm, transactionEvent.Code, transactionEvent.Detail);
        }

        private static WorkloadTransactionTransitionResult ApplyConfirmationControl(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            if (state.Phase != WorkloadTransactionPhase.Execute &&
                state.Phase != WorkloadTransactionPhase.Confirm)
                return Ignored(state, "confirmation-not-expected", "The workload peer is not awaiting host confirmation control.");

            if (state.ConfirmationControlReceived)
                return Ignored(state, "duplicate-confirmation-control", "The host confirmation control was already received.");

            return Applied(With(
                state,
                WorkloadTransactionPhase.Confirm,
                WorkloadTransactionTerminalState.Pending,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                state.RollbackParticipants,
                state.MutationStarted,
                transactionEvent,
                transactionEvent.Code,
                transactionEvent.Detail,
                confirmationControlReceived: true,
                confirmationControlAccepted: transactionEvent.Accepted,
                reportFingerprintOverride: string.Empty),
                WorkloadTransactionTransitionAction.ConfirmLocally,
                transactionEvent.Code,
                transactionEvent.Detail);
        }

        private static WorkloadTransactionTransitionResult ApplyConfirmationAcknowledgement(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            if (state.Phase != WorkloadTransactionPhase.Confirm)
                return Ignored(state, "confirmation-not-expected", "The workload host is not awaiting peer confirmation acknowledgements.");

            if (!Contains(state.ParticipantKeys, transactionEvent.PeerKey) ||
                string.Equals(state.HostParticipantKey, transactionEvent.PeerKey, StringComparison.Ordinal))
                return Rollback(state, transactionEvent, "unknown-confirmation-peer", state.ExecutedParticipants);

            if (Contains(state.ConfirmationAcknowledgedParticipants, transactionEvent.PeerKey))
                return Ignored(state, "duplicate-confirmation", "The peer confirmation acknowledgement was already recorded.");

            if (!transactionEvent.Accepted)
                return Rollback(state, transactionEvent, "confirmation-rejected", state.ExecutedParticipants);

            var acknowledged = Add(
                state.ConfirmationAcknowledgedParticipants,
                transactionEvent.PeerKey);
            if (acknowledged.Count != state.ParticipantKeys.Count)
            {
                return Applied(With(
                    state,
                    WorkloadTransactionPhase.Confirm,
                    WorkloadTransactionTerminalState.Pending,
                    state.PreparedParticipants,
                    state.ExecutedParticipants,
                    state.RollbackParticipants,
                    state.MutationStarted,
                    transactionEvent,
                    transactionEvent.Code,
                    transactionEvent.Detail,
                    confirmationAcknowledgedParticipants: acknowledged),
                    WorkloadTransactionTransitionAction.None,
                    transactionEvent.Code,
                    transactionEvent.Detail);
            }

            return Applied(With(
                state,
                WorkloadTransactionPhase.Confirm,
                WorkloadTransactionTerminalState.Succeeded,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                state.RollbackParticipants,
                state.MutationStarted,
                transactionEvent,
                transactionEvent.Code,
                transactionEvent.Detail,
                confirmationAcknowledgedParticipants: acknowledged),
                WorkloadTransactionTransitionAction.SendFinalConfirmation,
                transactionEvent.Code,
                transactionEvent.Detail);
        }

        private static WorkloadTransactionTransitionResult ApplyFinalConfirmation(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            if (state.Phase != WorkloadTransactionPhase.Confirm ||
                !state.ConfirmationControlReceived)
                return Ignored(state, "final-confirmation-not-expected", "The workload peer has not accepted host confirmation control.");

            if (!transactionEvent.Accepted)
                return Rollback(state, transactionEvent, "final-confirmation-rejected", state.ExecutedParticipants);

            return Applied(With(
                state,
                WorkloadTransactionPhase.Confirm,
                WorkloadTransactionTerminalState.Succeeded,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                state.RollbackParticipants,
                state.MutationStarted,
                transactionEvent,
                transactionEvent.Code,
                transactionEvent.Detail),
                WorkloadTransactionTransitionAction.None,
                transactionEvent.Code,
                transactionEvent.Detail);
        }

        private static WorkloadTransactionTransitionResult ApplyRollbackReport(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            if (state.TerminalState != WorkloadTransactionTerminalState.RollbackRequired)
                return Ignored(state, "rollback-not-required", "The workload transaction is not awaiting rollback.");

            if (!Contains(state.RollbackParticipants, transactionEvent.PeerKey))
                return Invalid(state, "unknown-rollback-peer", "The rollback report came from an unknown peer.");

            if (Contains(state.RollbackReportedParticipants, transactionEvent.PeerKey))
                return Ignored(state, "duplicate-rollback", "The rollback report was already recorded.");

            if (!transactionEvent.Accepted)
            {
                return Applied(With(
                    state,
                    WorkloadTransactionPhase.Abort,
                    WorkloadTransactionTerminalState.RollbackFailed,
                    state.PreparedParticipants,
                    state.ExecutedParticipants,
                    state.RollbackParticipants,
                    true,
                    transactionEvent,
                    transactionEvent.Code,
                    transactionEvent.Detail),
                    WorkloadTransactionTransitionAction.None,
                    transactionEvent.Code,
                    transactionEvent.Detail);
            }

            var rolledBack = Add(state.RollbackReportedParticipants, transactionEvent.PeerKey);
            if (rolledBack.Count != state.RollbackParticipants.Count)
            {
                return Applied(With(
                    state,
                    WorkloadTransactionPhase.Abort,
                    WorkloadTransactionTerminalState.RollbackRequired,
                    state.PreparedParticipants,
                    state.ExecutedParticipants,
                    state.RollbackParticipants,
                    true,
                    transactionEvent,
                    transactionEvent.Code,
                    transactionEvent.Detail,
                    rolledBack), WorkloadTransactionTransitionAction.None, transactionEvent.Code, transactionEvent.Detail);
            }

            return Applied(With(
                state,
                WorkloadTransactionPhase.Abort,
                WorkloadTransactionTerminalState.RolledBack,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                state.RollbackParticipants,
                true,
                transactionEvent,
                transactionEvent.Code,
                transactionEvent.Detail,
                rolledBack), WorkloadTransactionTransitionAction.None, transactionEvent.Code, transactionEvent.Detail);
        }

        private static bool MatchingReportFingerprint(string left, string right)
        {
            return string.Equals(
                left ?? string.Empty,
                right ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private static WorkloadTransactionTransitionResult Abort(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent,
            string code,
            string detail)
        {
            var rollbackRequired = MutationMayHaveStarted(state);
            var terminal = rollbackRequired
                ? WorkloadTransactionTerminalState.RollbackRequired
                : WorkloadTransactionTerminalState.Aborted;
            var action = state.AbortDispatched
                ? WorkloadTransactionTransitionAction.None
                : WorkloadTransactionTransitionAction.SendAbort;
            return Applied(With(
                state,
                WorkloadTransactionPhase.Abort,
                terminal,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                rollbackRequired ? RollbackTargets(state, null) : state.RollbackParticipants,
                rollbackRequired,
                transactionEvent,
                code,
                detail,
                abortDispatched: state.AbortDispatched || action == WorkloadTransactionTransitionAction.SendAbort), action, code, detail);
        }

        private static WorkloadTransactionTransitionResult Rollback(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent,
            string code,
            IEnumerable<string> executedParticipants = null)
        {
            return Applied(With(
                state,
                WorkloadTransactionPhase.Abort,
                WorkloadTransactionTerminalState.RollbackRequired,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                RollbackTargets(state, executedParticipants),
                true,
                transactionEvent,
                code,
                transactionEvent.Detail,
                abortDispatched: true), WorkloadTransactionTransitionAction.SendAbort, code, transactionEvent.Detail);
        }

        private static WorkloadTransactionTransitionResult Timeout(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            var rollbackRequired = MutationMayHaveStarted(state);
            var terminal = rollbackRequired
                ? WorkloadTransactionTerminalState.RollbackRequired
                : WorkloadTransactionTerminalState.TimedOut;
            var action = state.AbortDispatched
                ? WorkloadTransactionTransitionAction.None
                : WorkloadTransactionTransitionAction.SendAbort;
            return Applied(With(
                state,
                WorkloadTransactionPhase.Abort,
                terminal,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                rollbackRequired ? RollbackTargets(state, null) : state.RollbackParticipants,
                rollbackRequired,
                transactionEvent,
                "timeout",
                transactionEvent.Detail,
                abortDispatched: state.AbortDispatched || action == WorkloadTransactionTransitionAction.SendAbort), action, "timeout", transactionEvent.Detail);
        }

        private static WorkloadTransactionTransitionResult Failure(
            WorkloadTransactionState state,
            WorkloadTransactionEvent transactionEvent)
        {
            var rollbackRequired = MutationMayHaveStarted(state);
            var terminal = rollbackRequired
                ? WorkloadTransactionTerminalState.RollbackRequired
                : WorkloadTransactionTerminalState.Failed;
            var action = state.AbortDispatched
                ? WorkloadTransactionTransitionAction.None
                : WorkloadTransactionTransitionAction.SendAbort;
            return Applied(With(
                state,
                WorkloadTransactionPhase.Abort,
                terminal,
                state.PreparedParticipants,
                state.ExecutedParticipants,
                rollbackRequired ? RollbackTargets(state, null) : state.RollbackParticipants,
                rollbackRequired,
                transactionEvent,
                transactionEvent.Code,
                transactionEvent.Detail,
                abortDispatched: state.AbortDispatched || action == WorkloadTransactionTransitionAction.SendAbort), action, transactionEvent.Code, transactionEvent.Detail);
        }

        private static bool MutationMayHaveStarted(WorkloadTransactionState state)
        {
            return state.MutationStarted ||
                   state.Phase == WorkloadTransactionPhase.Execute ||
                   state.Phase == WorkloadTransactionPhase.Confirm ||
                   state.ExecutedParticipants.Count > 0;
        }

        private static IEnumerable<string> RollbackTargets(
            WorkloadTransactionState state,
            IEnumerable<string> executedParticipants)
        {
            if (executedParticipants != null && executedParticipants.Any())
                return executedParticipants;
            if (state.ExecutedParticipants != null && state.ExecutedParticipants.Count > 0)
                return state.ExecutedParticipants;
            return state.ParticipantKeys;
        }

        private static WorkloadTransactionState With(
            WorkloadTransactionState state,
            WorkloadTransactionPhase phase,
            WorkloadTransactionTerminalState terminalState,
            IEnumerable<string> preparedParticipants,
            IEnumerable<string> executedParticipants,
            IEnumerable<string> rollbackParticipants,
            bool mutationStarted,
            WorkloadTransactionEvent transactionEvent,
            string code,
            string detail,
            IEnumerable<string> rollbackReportedParticipants = null,
            IEnumerable<string> confirmationAcknowledgedParticipants = null,
            bool confirmationControlReceived = false,
            bool abortDispatched = false,
            string reportFingerprintOverride = null,
            bool confirmationControlAccepted = false)
        {
            return new WorkloadTransactionState(
                state.RequestId,
                state.RequestFingerprint,
                phase,
                terminalState,
                state.ParticipantKeys,
                preparedParticipants,
                executedParticipants,
                rollbackParticipants,
                mutationStarted,
                state.DeadlineSequence,
                code,
                detail,
                reportFingerprintOverride ??
                    (transactionEvent == null ? state.ReportFingerprint : transactionEvent.ReportFingerprint),
                transactionEvent == null ? state.Sequence : transactionEvent.Sequence,
                rollbackReportedParticipants ?? state.RollbackReportedParticipants,
                confirmationAcknowledgedParticipants ?? state.ConfirmationAcknowledgedParticipants,
                confirmationControlReceived || state.ConfirmationControlReceived,
                abortDispatched || state.AbortDispatched,
                state.HostParticipantKey,
                confirmationControlAccepted || state.ConfirmationControlAccepted);
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            return !string.IsNullOrEmpty(value) && values != null && values.Contains(value);
        }

        private static IReadOnlyList<string> Add(IReadOnlyList<string> values, string value)
        {
            var result = values == null ? new List<string>() : values.ToList();
            if (!result.Contains(value))
                result.Add(value);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static WorkloadTransactionTransitionResult Applied(
            WorkloadTransactionState state,
            WorkloadTransactionTransitionAction action,
            string code,
            string detail)
        {
            return new WorkloadTransactionTransitionResult(
                state,
                WorkloadTransactionTransitionDisposition.Applied,
                action,
                code,
                detail);
        }

        private static WorkloadTransactionTransitionResult Ignored(
            WorkloadTransactionState state,
            string code,
            string detail)
        {
            return new WorkloadTransactionTransitionResult(
                state,
                WorkloadTransactionTransitionDisposition.Ignored,
                WorkloadTransactionTransitionAction.None,
                code,
                detail);
        }

        private static WorkloadTransactionTransitionResult Invalid(
            WorkloadTransactionState state,
            string code,
            string detail)
        {
            return new WorkloadTransactionTransitionResult(
                state,
                WorkloadTransactionTransitionDisposition.Rejected,
                WorkloadTransactionTransitionAction.None,
                code,
                detail);
        }
    }

    internal sealed class WorkloadTransactionResult
    {
        internal WorkloadTransactionResult(
            string requestId,
            string idempotencyKey,
            string sessionId,
            string requestFingerprint,
            WorkloadTransactionPhase phase,
            WorkloadTransactionTerminalState terminalState,
            bool accepted,
            bool requiresRollback,
            string code,
            string detail,
            string reportFingerprint,
            long sequence)
        {
            RequestId = requestId;
            IdempotencyKey = idempotencyKey ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            RequestFingerprint = requestFingerprint;
            Phase = phase;
            TerminalState = terminalState;
            Accepted = accepted;
            RequiresRollback = requiresRollback;
            Code = code ?? string.Empty;
            Detail = detail ?? string.Empty;
            ReportFingerprint = reportFingerprint ?? string.Empty;
            Sequence = sequence;
        }

        internal string RequestId { get; }
        internal string IdempotencyKey { get; }
        internal string SessionId { get; }
        internal string RequestFingerprint { get; }
        internal WorkloadTransactionPhase Phase { get; }
        internal WorkloadTransactionTerminalState TerminalState { get; }
        internal bool Accepted { get; }
        internal bool RequiresRollback { get; }
        internal string Code { get; }
        internal string Detail { get; }
        internal string ReportFingerprint { get; }
        internal long Sequence { get; }
    }

    internal sealed class WorkloadTransactionTableDecision
    {
        internal WorkloadTransactionTableDecision(
            WorkloadTransactionAdmissionCode code,
            WorkloadTransactionRequest request,
            WorkloadTransactionResult terminalResult,
            string diagnostic,
            WorkloadTransactionState state = null)
        {
            Code = code;
            Request = request;
            TerminalResult = terminalResult;
            State = state;
            Diagnostic = diagnostic ?? string.Empty;
        }

        internal WorkloadTransactionAdmissionCode Code { get; }
        internal WorkloadTransactionRequest Request { get; }
        internal WorkloadTransactionResult TerminalResult { get; }
        internal WorkloadTransactionState State { get; }
        internal string Diagnostic { get; }
        internal bool Accepted => Code == WorkloadTransactionAdmissionCode.Accepted;
    }

    internal sealed class WorkloadTransactionRequestTable
    {
        private sealed class Entry
        {
            internal WorkloadTransactionRequest Request;
            internal long LastTouchedSequence;
            internal WorkloadTransactionResult TerminalResult;
            internal WorkloadTransactionState State;
            internal bool AppliedTransaction;
        }

        private readonly int _capacity;
        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        internal WorkloadTransactionRequestTable(int capacity)
        {
            _capacity = Math.Max(1, Math.Min(256, capacity));
        }

        internal int Count => _entries.Count;
        internal int Capacity => _capacity;

        internal WorkloadTransactionTableDecision TryRegister(
            WorkloadTransactionRequest request,
            long sequence)
        {
            if (request == null)
            {
                return new WorkloadTransactionTableDecision(
                    WorkloadTransactionAdmissionCode.InvalidRequest,
                    null,
                    null,
                    "The workload transaction request is missing.");
            }

            if (_entries.TryGetValue(request.TableKey, out var existing))
            {
                existing.LastTouchedSequence = sequence;
                if (string.Equals(
                    existing.Request.ReplayFingerprint,
                    request.ReplayFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return new WorkloadTransactionTableDecision(
                        WorkloadTransactionAdmissionCode.Duplicate,
                        existing.Request,
                        existing.TerminalResult,
                        "The workload transaction request was already registered.",
                        existing.State);
                }

                return new WorkloadTransactionTableDecision(
                        WorkloadTransactionAdmissionCode.MismatchedDuplicate,
                        existing.Request,
                        existing.TerminalResult,
                        "The idempotency key was reused with a different canonical payload.",
                        existing.State);
            }

            if (_entries.Count >= _capacity)
            {
                var evictable = _entries
                    .Where(pair => pair.Value.TerminalResult != null &&
                                   !pair.Value.AppliedTransaction)
                    .OrderBy(pair => pair.Value.LastTouchedSequence)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (evictable.Value == null)
                {
                    return new WorkloadTransactionTableDecision(
                        WorkloadTransactionAdmissionCode.CapacityExceeded,
                        null,
                        null,
                        "The bounded workload transaction table has no terminal entry available for eviction.");
                }

                _entries.Remove(evictable.Key);
            }

            _entries.Add(request.TableKey, new Entry
            {
                Request = request,
                LastTouchedSequence = sequence
            });
            return new WorkloadTransactionTableDecision(
                WorkloadTransactionAdmissionCode.Accepted,
                request,
                null,
                null);
        }

        internal bool TryMarkTerminal(
            WorkloadTransactionRequest request,
            WorkloadTransactionResult result,
            long sequence)
        {
            if (request == null || result == null ||
                !_entries.TryGetValue(request.TableKey, out var entry) ||
                !string.Equals(
                    entry.Request.RequestFingerprint,
                    request.RequestFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            entry.LastTouchedSequence = sequence;
            entry.TerminalResult = result;
            entry.AppliedTransaction = result.TerminalState == WorkloadTransactionTerminalState.Succeeded ||
                                       result.TerminalState == WorkloadTransactionTerminalState.RolledBack ||
                                       result.TerminalState == WorkloadTransactionTerminalState.RollbackFailed;
            return true;
        }

        internal bool TryUpdateState(
            WorkloadTransactionRequest request,
            WorkloadTransactionState state,
            long sequence)
        {
            if (request == null || state == null ||
                !_entries.TryGetValue(request.TableKey, out var entry) ||
                !string.Equals(
                    entry.Request.ReplayFingerprint,
                    request.ReplayFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            entry.LastTouchedSequence = sequence;
            entry.State = state;
            return true;
        }

        internal bool TryGet(
            string tableKey,
            out WorkloadTransactionRequest request,
            out WorkloadTransactionResult terminalResult)
        {
            request = null;
            terminalResult = null;
            if (!_entries.TryGetValue(tableKey, out var entry))
                return false;

            request = entry.Request;
            terminalResult = entry.TerminalResult;
            return true;
        }
    }

    internal sealed class WorkloadTransactionAdmission
    {
        internal WorkloadTransactionAdmission(
            WorkloadTransactionAdmissionCode code,
            WorkloadTransactionRequest request,
            WorkloadTransactionState state,
            string diagnostic,
            WorkloadTransactionResult terminalResult = null,
            WorkloadTransactionRequest registeredRequest = null)
        {
            Code = code;
            Request = request;
            RegisteredRequest = registeredRequest ?? request;
            State = state;
            Diagnostic = diagnostic ?? string.Empty;
            TerminalResult = terminalResult;
        }

        internal WorkloadTransactionAdmissionCode Code { get; }
        // Request is the correlation envelope supplied by the caller. The
        // registered request remains separate so an idempotent replay with a
        // new RequestId can report against the new caller without changing
        // the original protocol state or terminal result.
        internal WorkloadTransactionRequest Request { get; }
        internal WorkloadTransactionRequest RegisteredRequest { get; }
        internal WorkloadTransactionState State { get; }
        internal string Diagnostic { get; }
        internal WorkloadTransactionResult TerminalResult { get; }
        internal bool Accepted => Code == WorkloadTransactionAdmissionCode.Accepted;
    }

    internal sealed class NullWorkloadTransactionCallbacks : IWorkloadTransactionCallbacks
    {
        internal static readonly NullWorkloadTransactionCallbacks Instance =
            new NullWorkloadTransactionCallbacks();

        public void OnRequestAccepted(WorkloadTransactionRequest request) { }
        public void OnAdmissionRejected(WorkloadTransactionAdmission admission) { }
        public void OnPrepareRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
        public void OnExecuteRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
        public void OnConfirmationControlReceived(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
        public void OnFinalConfirmationRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
        public void OnConfirmRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
        public void OnAbortRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
        public void OnRollbackRequired(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
        public void OnTerminal(WorkloadTransactionResult result) { }
    }

    internal sealed class WorkloadTransactionProtocol
    {
        internal const int DefaultRequestTableCapacity = 32;

        private readonly WorkloadTransactionRequestTable _requestTable;
        private IWorkloadTransactionCallbacks _callbacks = NullWorkloadTransactionCallbacks.Instance;
        private WorkloadTransactionRequest _currentRequest;
        private WorkloadTransactionState _currentState;
        private WorkloadTransactionResult _lastResult;

        internal WorkloadTransactionProtocol(int requestTableCapacity = DefaultRequestTableCapacity)
        {
            _requestTable = new WorkloadTransactionRequestTable(requestTableCapacity);
        }

        internal WorkloadTransactionRequestTable RequestTable => _requestTable;
        internal WorkloadTransactionRequest CurrentRequest => _currentRequest;
        internal WorkloadTransactionState CurrentState => _currentState;
        internal WorkloadTransactionResult LastResult => _lastResult;

        internal void SetCallbacks(IWorkloadTransactionCallbacks callbacks)
        {
            _callbacks = callbacks ?? NullWorkloadTransactionCallbacks.Instance;
        }

        internal WorkloadTransactionAdmission TryBegin(
            WorkloadTransactionRequest request,
            long sequence,
            string hostParticipantKey = null)
        {
            if (request == null)
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.InvalidRequest,
                    null,
                    _currentState,
                    "The workload transaction request is missing.");
            }

            if (_currentState != null && !_currentState.IsFinal &&
                !string.Equals(_currentRequest?.TableKey, request.TableKey, StringComparison.Ordinal))
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.Busy,
                    request,
                    _currentState,
                    "Another workload transaction is already in progress.");
            }

            var decision = _requestTable.TryRegister(request, sequence);
            if (decision.Code != WorkloadTransactionAdmissionCode.Accepted)
            {
                if (decision.Code == WorkloadTransactionAdmissionCode.Duplicate &&
                    decision.TerminalResult != null)
                {
                    _lastResult = decision.TerminalResult;
                }
                if (decision.Code == WorkloadTransactionAdmissionCode.Duplicate &&
                    decision.State != null)
                {
                    _currentRequest = decision.Request ?? request;
                    _currentState = decision.State;
                }
                return new WorkloadTransactionAdmission(
                    decision.Code,
                    request,
                    decision.State ?? _currentState,
                    decision.Diagnostic,
                    decision.TerminalResult,
                    decision.Request ?? request);
            }

            var transition = WorkloadTransactionStateMachine.Begin(request, sequence, hostParticipantKey);
            if (transition.Disposition != WorkloadTransactionTransitionDisposition.Applied)
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.InvalidRequest,
                    request,
                    _currentState,
                    transition.Detail);
            }

            _currentRequest = request;
            _currentState = transition.State;
            _lastResult = null;
            _requestTable.TryUpdateState(request, _currentState, sequence);
            SafeCallback(() => _callbacks.OnRequestAccepted(request));
            return new WorkloadTransactionAdmission(
                WorkloadTransactionAdmissionCode.Accepted,
                request,
                _currentState,
                null);
        }

        internal WorkloadTransactionAdmission AcceptIncoming(
            WorkloadTransactionRequest request,
            long sequence,
            bool senderAuthenticated,
            WorkloadTransactionAdmissionCode authenticationFailureCode = WorkloadTransactionAdmissionCode.UnauthenticatedMessage,
            string authenticationDiagnostic = null,
            string hostParticipantKey = null)
        {
            if (!senderAuthenticated)
            {
                var rejected = new WorkloadTransactionAdmission(
                    authenticationFailureCode == WorkloadTransactionAdmissionCode.Accepted
                        ? WorkloadTransactionAdmissionCode.UnauthenticatedMessage
                        : authenticationFailureCode,
                    request,
                    _currentState,
                    authenticationDiagnostic ?? "The workload transaction message sender is not authenticated.");
                SafeCallback(() => _callbacks.OnAdmissionRejected(rejected));
                return rejected;
            }

            var admission = TryBegin(request, sequence, hostParticipantKey);
            if (!admission.Accepted)
            {
                SafeCallback(() => _callbacks.OnAdmissionRejected(admission));
                return admission;
            }

            // A request delivered to a peer is not merely admitted.  It must
            // enter the same deterministic local Prepare phase as the host;
            // otherwise the host can wait forever for a report that the peer
            // never produces.
            var dispatched = MarkRequestSent(
                request.RequestId,
                sequence == long.MaxValue ? long.MaxValue : sequence + 1);
            if (dispatched.Disposition != WorkloadTransactionTransitionDisposition.Applied)
            {
                var rejected = new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.InvalidRequest,
                    request,
                    dispatched.State,
                    dispatched.Detail);
                SafeCallback(() => _callbacks.OnAdmissionRejected(rejected));
                return rejected;
            }

            return admission;
        }

        internal WorkloadTransactionTransitionResult MarkRequestSent(
            string requestId,
            long sequence)
        {
            return Apply(WorkloadTransactionEvent.RequestSent(requestId, sequence));
        }

        internal WorkloadTransactionTransitionResult RecordPrepareAcknowledgement(
            string requestId,
            string requestFingerprint,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool senderAuthenticated)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-peer", "The prepare acknowledgement sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The prepare acknowledgement does not match the active request.");

            return Apply(WorkloadTransactionEvent.PrepareAcknowledged(
                requestId,
                peerKey,
                accepted,
                code,
                detail,
                reportFingerprint,
                sequence));
        }

        internal WorkloadTransactionTransitionResult RecordExecuteResult(
            string requestId,
            string requestFingerprint,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool senderAuthenticated,
            bool requiresRollback = false)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-peer", "The execute result sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The execute result does not match the active request.");

            return Apply(WorkloadTransactionEvent.ExecuteReported(
                requestId,
                peerKey,
                accepted,
                code,
                detail,
                reportFingerprint,
                sequence,
                requiresRollback));
        }

        internal WorkloadTransactionTransitionResult RecordExecuteControl(
            string requestId,
            string requestFingerprint,
            bool accepted,
            string code,
            string detail,
            long sequence,
            bool senderAuthenticated)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-host", "The execute control sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The execute control does not match the active request.");

            return Apply(WorkloadTransactionEvent.ExecuteRequested(
                requestId,
                accepted,
                code,
                detail,
                sequence));
        }

        internal WorkloadTransactionTransitionResult RecordConfirmationControl(
            string requestId,
            string requestFingerprint,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool senderAuthenticated)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-host", "The confirmation control sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The confirmation control does not match the active request.");

            return Apply(WorkloadTransactionEvent.ConfirmationControlReceived(
                requestId,
                accepted,
                code,
                detail,
                reportFingerprint,
                sequence));
        }

        internal WorkloadTransactionTransitionResult RecordConfirmationAcknowledgement(
            string requestId,
            string requestFingerprint,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool senderAuthenticated,
            bool requiresRollback = false)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-peer", "The confirmation acknowledgement sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The confirmation acknowledgement does not match the active request.");

            return Apply(WorkloadTransactionEvent.ConfirmationAcknowledged(
                requestId,
                peerKey,
                accepted,
                code,
                detail,
                reportFingerprint,
                sequence,
                requiresRollback));
        }

        internal WorkloadTransactionTransitionResult RecordFinalConfirmation(
            string requestId,
            string requestFingerprint,
            bool accepted,
            string code,
            string detail,
            long sequence,
            bool senderAuthenticated)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-host", "The final confirmation sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The final confirmation does not match the active request.");

            return Apply(WorkloadTransactionEvent.FinalConfirmationReceived(
                requestId,
                accepted,
                code,
                detail,
                sequence));
        }

        internal WorkloadTransactionTransitionResult RecordLocalPrepareAcknowledgement(
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence)
        {
            if (_currentRequest == null)
                return RejectExternalMessage("unknown-request", "There is no local workload request to acknowledge.");
            return Apply(WorkloadTransactionEvent.PrepareAcknowledged(
                _currentRequest.RequestId,
                peerKey,
                accepted,
                code,
                detail,
                reportFingerprint,
                sequence));
        }

        internal WorkloadTransactionTransitionResult RecordLocalExecuteResult(
            string peerKey,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool requiresRollback)
        {
            if (_currentRequest == null)
                return RejectExternalMessage("unknown-request", "There is no local workload request to report.");
            return Apply(WorkloadTransactionEvent.ExecuteReported(
                _currentRequest.RequestId,
                peerKey,
                accepted,
                code,
                detail,
                reportFingerprint,
                sequence,
                requiresRollback));
        }

        internal WorkloadTransactionTransitionResult RecordLocalRollback(
            string peerKey,
            bool accepted,
            string code,
            string detail,
            long sequence)
        {
            if (_currentRequest == null)
                return RejectExternalMessage("unknown-request", "There is no local workload request to roll back.");
            return Apply(WorkloadTransactionEvent.RollbackReported(
                _currentRequest.RequestId,
                peerKey,
                accepted,
                code,
                detail,
                sequence));
        }

        internal WorkloadTransactionTransitionResult RecordConfirmation(
            string requestId,
            string requestFingerprint,
            bool accepted,
            string code,
            string detail,
            string reportFingerprint,
            long sequence,
            bool senderAuthenticated)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-host", "The confirmation sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The confirmation does not match the active request.");

            return Apply(WorkloadTransactionEvent.Confirmed(
                requestId,
                accepted,
                code,
                detail,
                reportFingerprint,
                sequence));
        }

        internal WorkloadTransactionTransitionResult RequestAbort(
            string requestId,
            string requestFingerprint,
            string code,
            string detail,
            long sequence)
        {
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The abort request does not match the active request.");

            return Apply(WorkloadTransactionEvent.AbortRequested(requestId, code, detail, sequence));
        }

        internal WorkloadTransactionTransitionResult CheckTimeout(
            long sequence)
        {
            if (_currentState == null || _currentState.IsFinal || sequence < _currentState.DeadlineSequence)
                return new WorkloadTransactionTransitionResult(
                    _currentState,
                    WorkloadTransactionTransitionDisposition.Ignored,
                    WorkloadTransactionTransitionAction.None,
                    "not-timed-out",
                    "The workload transaction has not reached its deadline.");

            return Apply(WorkloadTransactionEvent.Timeout(_currentState.RequestId, sequence));
        }

        internal WorkloadTransactionTransitionResult Fail(
            string requestId,
            string code,
            string detail,
            long sequence)
        {
            return Apply(WorkloadTransactionEvent.Failure(requestId, code, detail, sequence));
        }

        internal WorkloadTransactionTransitionResult RecordRollback(
            string requestId,
            string requestFingerprint,
            string peerKey,
            bool accepted,
            string code,
            string detail,
            long sequence,
            bool senderAuthenticated)
        {
            if (!senderAuthenticated)
                return RejectExternalMessage("unauthenticated-peer", "The rollback report sender is not authenticated.");
            if (!MatchesCurrentRequest(requestId, requestFingerprint))
                return RejectExternalMessage("request-mismatch", "The rollback report does not match the active request.");

            return Apply(WorkloadTransactionEvent.RollbackReported(
                requestId,
                peerKey,
                accepted,
                code,
                detail,
                sequence));
        }

        private WorkloadTransactionTransitionResult Apply(WorkloadTransactionEvent transactionEvent)
        {
            var previous = _currentState;
            var transition = WorkloadTransactionStateMachine.Apply(previous, transactionEvent);
            if (transition.Disposition != WorkloadTransactionTransitionDisposition.Applied)
                return transition;

            _currentState = transition.State;
            _requestTable.TryUpdateState(_currentRequest, _currentState, _currentState?.Sequence ?? 0L);
            NotifyTransition(previous, transition);
            if (_currentState != null && _currentState.IsFinal && _currentRequest != null)
            {
                _lastResult = new WorkloadTransactionResult(
                    _currentState.RequestId,
                    _currentRequest.IdempotencyKey,
                    _currentRequest.SessionId,
                    _currentState.RequestFingerprint,
                    _currentState.Phase,
                    _currentState.TerminalState,
                    _currentState.TerminalState == WorkloadTransactionTerminalState.Succeeded,
                    _currentState.RequiresRollback ||
                        _currentState.TerminalState == WorkloadTransactionTerminalState.RollbackFailed,
                    _currentState.Code,
                    _currentState.Detail,
                    _currentState.ReportFingerprint,
                    _currentState.Sequence);
                _requestTable.TryMarkTerminal(_currentRequest, _lastResult, _currentState.Sequence);
                SafeCallback(() => _callbacks.OnTerminal(_lastResult));
            }

            return transition;
        }

        private void NotifyTransition(
            WorkloadTransactionState previous,
            WorkloadTransactionTransitionResult transition)
        {
            if (_currentRequest == null || transition == null)
                return;

            switch (transition.Action)
            {
                case WorkloadTransactionTransitionAction.SendPrepare:
                    SafeCallback(() => _callbacks.OnPrepareRequested(_currentRequest, _currentState));
                    break;
                case WorkloadTransactionTransitionAction.SendExecute:
                    SafeCallback(() => _callbacks.OnExecuteRequested(_currentRequest, _currentState));
                    break;
                case WorkloadTransactionTransitionAction.ExecuteLocally:
                    SafeCallback(() => _callbacks.OnExecuteRequested(_currentRequest, _currentState));
                    break;
                case WorkloadTransactionTransitionAction.SendConfirm:
                    SafeCallback(() => _callbacks.OnConfirmRequested(_currentRequest, _currentState));
                    break;
                case WorkloadTransactionTransitionAction.ConfirmLocally:
                    SafeCallback(() => _callbacks.OnConfirmationControlReceived(_currentRequest, _currentState));
                    break;
                case WorkloadTransactionTransitionAction.SendFinalConfirmation:
                    SafeCallback(() => _callbacks.OnFinalConfirmationRequested(_currentRequest, _currentState));
                    break;
                case WorkloadTransactionTransitionAction.SendAbort:
                    SafeCallback(() => _callbacks.OnAbortRequested(_currentRequest, _currentState));
                    break;
            }

            if (_currentState.TerminalState == WorkloadTransactionTerminalState.RollbackRequired &&
                (previous == null || previous.TerminalState != WorkloadTransactionTerminalState.RollbackRequired))
            {
                SafeCallback(() => _callbacks.OnRollbackRequired(_currentRequest, _currentState));
            }
        }

        private bool MatchesCurrentRequest(string requestId, string requestFingerprint)
        {
            return _currentRequest != null &&
                   string.Equals(_currentRequest.RequestId, requestId, StringComparison.Ordinal) &&
                   string.Equals(_currentRequest.RequestFingerprint, requestFingerprint, StringComparison.OrdinalIgnoreCase);
        }

        private WorkloadTransactionTransitionResult RejectExternalMessage(
            string code,
            string detail)
        {
            return new WorkloadTransactionTransitionResult(
                _currentState,
                WorkloadTransactionTransitionDisposition.Rejected,
                WorkloadTransactionTransitionAction.None,
                code,
                detail);
        }

        private static void SafeCallback(Action callback)
        {
            try
            {
                callback?.Invoke();
            }
            catch
            {
                // A future backend/UI callback must not corrupt the protocol state.
                // It can report its own failure through Fail/RequestAbort.
            }
        }
    }

    internal static class WorkloadTransactionMultiplayer
    {
        private static bool _workersRegistered;
        private static bool _methodsRegistered;
        private static bool _registrationAttempted;
        private static bool _protocolAvailable;
        private static IWorkloadTransactionTransport _transport = new MultiplayerApiWorkloadTransactionTransport();

        internal static readonly WorkloadTransactionProtocol Protocol =
            new WorkloadTransactionProtocol();

        internal static bool ProtocolAvailable => _protocolAvailable;

        internal static bool TryRegisterProtocol()
        {
            if (_protocolAvailable || _registrationAttempted)
                return _protocolAvailable;

            // 1.3 and other old API surfaces may expose RegisterSyncMethod but
            // not a host-only enforcement seam.  Never register a subset of
            // the workload protocol in that case.
            if (!MP.enabled)
                return false;

            if (!CanRegisterHostOnlySyncMethods())
            {
                _registrationAttempted = true;
                return false;
            }

            // Multiplayer may not be active when StaticConstructorOnStartup
            // runs.  Leave this retryable until the authenticated runtime
            // adapter is installed and a synchronized session exists.
            if (!MultiplayerBridge.WorkloadRuntimeAuthenticationAvailable)
                return false;

            _registrationAttempted = true;

            try
            {
                RegisterSyncWorkers();
                RegisterSyncMethods();
                _protocolAvailable = _workersRegistered && _methodsRegistered;
            }
            catch
            {
                _protocolAvailable = false;
            }

            return _protocolAvailable;
        }

        internal static void RegisterSyncWorkers()
        {
            if (_workersRegistered || !MP.enabled ||
                !MultiplayerBridge.WorkloadRuntimeAuthenticationAvailable)
                return;

            MP.RegisterSyncWorker<WorkloadTransactionWireRequest>(SyncRequest);
            MP.RegisterSyncWorker<WorkloadTransactionWireAcknowledgement>(SyncAcknowledgement);
            MP.RegisterSyncWorker<WorkloadTransactionWireResult>(SyncResult);
            MP.RegisterSyncWorker<WorkloadTransactionWireControl>(SyncControl);
            _workersRegistered = true;
        }

        internal static void RegisterSyncMethods()
        {
            if (_methodsRegistered || !MP.enabled ||
                !MultiplayerBridge.WorkloadRuntimeAuthenticationAvailable ||
                !CanRegisterHostOnlySyncMethods()) return;
            MethodInfo register = typeof(MP).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "RegisterSyncMethod", StringComparison.Ordinal) &&
                    method.GetParameters().Length >= 2 &&
                    method.GetParameters()[0].ParameterType == typeof(Type) &&
                    method.GetParameters()[1].ParameterType == typeof(string));
            if (register == null) return;

            // Every protocol entry point is a host-enforced sync method.  The
            // authentication adapter still binds the actual sender; host-only
            // prevents an API without a sender boundary from being advertised.
            string[] methods =
            {
                nameof(ReceiveRequest),
                nameof(ReceivePrepareAcknowledgement),
                nameof(ReceiveExecuteResult),
                nameof(ReceiveConfirmationAcknowledgement),
                nameof(ReceiveControl)
            };
            for (int i = 0; i < methods.Length; i++)
            {
                if (!TryRegisterHostOnly(register, methods[i]))
                    return;
            }

            _methodsRegistered = true;
        }

        private static bool CanRegisterHostOnlySyncMethods()
        {
            MethodInfo register = typeof(MP).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "RegisterSyncMethod", StringComparison.Ordinal) &&
                    method.GetParameters().Length >= 2 &&
                    method.GetParameters()[0].ParameterType == typeof(Type) &&
                    method.GetParameters()[1].ParameterType == typeof(string));
            if (register == null)
                return false;

            Type syncMethodType = typeof(MP).Assembly.GetType("Multiplayer.API.ISyncMethod");
            return syncMethodType != null &&
                   syncMethodType.GetMethod(
                       "SetHostOnly",
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
        }

        private static bool TryRegisterHostOnly(MethodInfo register, string methodName)
        {
            ParameterInfo[] parameters = register.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = typeof(WorkloadTransactionMultiplayer);
            arguments[1] = methodName;
            for (int i = 2; i < arguments.Length; i++)
                arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;

            object registered = register.Invoke(null, arguments);
            MethodInfo hostOnly = registered?.GetType().GetMethod(
                "SetHostOnly", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (hostOnly == null && registered != null)
            {
                hostOnly = registered.GetType().GetInterfaces()
                    .Select(type => type.GetMethod("SetHostOnly"))
                    .FirstOrDefault(method => method != null);
            }
            if (hostOnly == null)
                return false;

            hostOnly.Invoke(registered, null);
            return true;
        }

        internal static void SetCallbacks(IWorkloadTransactionCallbacks callbacks)
        {
            Protocol.SetCallbacks(callbacks);
        }

        internal static void SetTransport(IWorkloadTransactionTransport transport)
        {
            _transport = transport ?? new MultiplayerApiWorkloadTransactionTransport();
        }

        internal static WorkloadTransactionAdmission TryBegin(
            WorkloadTransactionRequest request,
            long sequence)
        {
            if (request == null)
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.InvalidRequest,
                    null,
                    Protocol.CurrentState,
                    "The workload transaction request is missing.");
            }

            if (!MultiplayerBridge.Active)
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.SinglePlayerNotRequired,
                    request,
                    Protocol.CurrentState,
                    "Multiplayer workload synchronization is not required outside multiplayer.");
            }

            if (!ProtocolAvailable)
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.HostOnlyRequired,
                    request,
                    Protocol.CurrentState,
                    "Workload multiplayer support is unavailable because the loaded API lacks authenticated host enforcement.");
            }

            if (!MultiplayerBridge.TryGetWorkloadSessionContext(
                out var hostSessionEpoch,
                out var rosterFingerprint))
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.SessionContextUnavailable,
                    request,
                    Protocol.CurrentState,
                    "The multiplayer session or roster identity is unavailable.");
            }

            if (!string.Equals(request.ExpectedRevisions.HostSessionEpoch, hostSessionEpoch, StringComparison.Ordinal) ||
                !string.Equals(request.ExpectedRevisions.RosterFingerprint, rosterFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.SessionMismatch,
                    request,
                    Protocol.CurrentState,
                    "The workload transaction was built against a stale multiplayer session context.");
            }

            if (!MultiplayerBridge.TryAuthorizeWorkloadInitiator(
                request.RequesterPlayerKey,
                hostSessionEpoch,
                rosterFingerprint,
                out var failureCode,
                out var diagnostic))
            {
                return new WorkloadTransactionAdmission(
                    failureCode,
                    request,
                    Protocol.CurrentState,
                    diagnostic);
            }

            if (!MultiplayerBridge.TryGetAuthenticatedHostParticipantKey(
                out var hostParticipantKey,
                hostSessionEpoch,
                rosterFingerprint))
            {
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.SenderAuthenticationFailed,
                    request,
                    Protocol.CurrentState,
                    "The authenticated host identity is unavailable for this workload transaction.");
            }

            var admission = Protocol.TryBegin(request, sequence, hostParticipantKey);
            if (!admission.Accepted)
                return admission;

            try
            {
                _transport.SendRequest(request.ToWire());
                Protocol.MarkRequestSent(
                    request.RequestId,
                    sequence == long.MaxValue ? long.MaxValue : sequence + 1);
            }
            catch (Exception ex)
            {
                Protocol.Fail(
                    request.RequestId,
                    "transport-failure",
                    ex.Message,
                    sequence == long.MaxValue ? long.MaxValue : sequence + 1);
                return new WorkloadTransactionAdmission(
                    WorkloadTransactionAdmissionCode.UnknownRequest,
                    request,
                    Protocol.CurrentState,
                    "The workload transaction request could not be dispatched.");
            }

            return new WorkloadTransactionAdmission(
                WorkloadTransactionAdmissionCode.Accepted,
                request,
                Protocol.CurrentState,
                null);
        }

        public static void ReceiveRequest(WorkloadTransactionWireRequest wire)
        {
            if (!MultiplayerBridge.Active || !ProtocolAvailable)
                return;

            if (!WorkloadTransactionRequest.TryFromWire(wire, out var request, out var diagnostic))
                return;

            string hostParticipantKey = TryGetCurrentHostParticipantKey(request);
            bool authorized = MP.IsExecutingSyncCommand &&
                MatchesCurrentContext(request) &&
                !string.IsNullOrEmpty(hostParticipantKey) &&
                MultiplayerBridge.TryGetAuthenticatedRuntimeSender(
                    request.RequesterPlayerKey,
                    request.ExpectedRevisions.HostSessionEpoch,
                    request.ExpectedRevisions.RosterFingerprint,
                    true,
                    out _,
                    out _);
            var failureCode = authorized
                ? WorkloadTransactionAdmissionCode.Accepted
                : string.IsNullOrEmpty(hostParticipantKey)
                    ? WorkloadTransactionAdmissionCode.SenderAuthenticationFailed
                    : WorkloadTransactionAdmissionCode.HostOnlyRequired;
            string authenticationDiagnostic = authorized
                ? null
                : string.IsNullOrEmpty(hostParticipantKey)
                    ? "The authenticated host identity is unavailable for this workload transaction."
                    : "The workload request was not delivered through the host-only synchronized command.";
            Protocol.AcceptIncoming(
                request,
                NextSequence(),
                authorized,
                failureCode,
                authenticationDiagnostic,
                hostParticipantKey);
        }

        private static string TryGetCurrentHostParticipantKey(WorkloadTransactionRequest request)
        {
            return request != null &&
                   request.ExpectedRevisions != null &&
                   MultiplayerBridge.TryGetAuthenticatedHostParticipantKey(
                       out var hostParticipantKey,
                       request.ExpectedRevisions.HostSessionEpoch,
                       request.ExpectedRevisions.RosterFingerprint)
                ? hostParticipantKey
                : null;
        }

        private static bool MatchesCurrentContext(WorkloadTransactionRequest request)
        {
            if (request == null || request.ExpectedRevisions == null ||
                !MultiplayerBridge.TryGetWorkloadSessionContext(
                    out var epoch,
                    out var roster))
                return false;

            return string.Equals(
                       epoch,
                       request.ExpectedRevisions.HostSessionEpoch,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       roster,
                       request.ExpectedRevisions.RosterFingerprint,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesContext(
            WorkloadTransactionRequest request,
            string contextFingerprint,
            string hostSessionEpoch,
            string rosterFingerprint,
            string authorityOwner,
            string sourceTemplateFingerprint,
            string taxonomyFingerprint,
            long storeRevision,
            long sessionRevision,
            long authorityRevision,
            long priorityServiceRevision,
            long scheduleServiceRevision,
            long specificJobServiceRevision,
            long settingsServiceRevision,
            long membershipRevision,
            long taxonomyRevision)
        {
            var expected = request?.ExpectedRevisions;
            return expected != null &&
                   string.Equals(contextFingerprint, expected.ContextFingerprint, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(hostSessionEpoch, expected.HostSessionEpoch, StringComparison.Ordinal) &&
                   string.Equals(rosterFingerprint, expected.RosterFingerprint, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(authorityOwner, expected.AuthorityOwner, StringComparison.Ordinal) &&
                   string.Equals(sourceTemplateFingerprint, expected.SourceTemplateFingerprint, StringComparison.Ordinal) &&
                   string.Equals(taxonomyFingerprint, expected.TaxonomyFingerprint, StringComparison.Ordinal) &&
                   storeRevision == expected.StoreRevision &&
                   sessionRevision == expected.SessionRevision &&
                   authorityRevision == expected.AuthorityRevision &&
                   priorityServiceRevision == expected.PriorityServiceRevision &&
                   scheduleServiceRevision == expected.ScheduleServiceRevision &&
                   specificJobServiceRevision == expected.SpecificJobServiceRevision &&
                   settingsServiceRevision == expected.SettingsServiceRevision &&
                   membershipRevision == expected.MembershipRevision &&
                   taxonomyRevision == expected.TaxonomyRevision;
        }

        private static bool MatchesContext(
            WorkloadTransactionRequest request,
            WorkloadTransactionWireAcknowledgement message)
        {
            return message != null && MatchesContext(
                request,
                message.ContextFingerprint,
                message.HostSessionEpoch,
                message.RosterFingerprint,
                message.AuthorityOwner,
                message.SourceTemplateFingerprint,
                message.TaxonomyFingerprint,
                message.StoreRevision,
                message.SessionRevision,
                message.AuthorityRevision,
                message.PriorityServiceRevision,
                message.ScheduleServiceRevision,
                message.SpecificJobServiceRevision,
                message.SettingsServiceRevision,
                message.MembershipRevision,
                message.TaxonomyRevision);
        }

        private static bool MatchesContext(
            WorkloadTransactionRequest request,
            WorkloadTransactionWireResult message)
        {
            return message != null && MatchesContext(
                request,
                message.ContextFingerprint,
                message.HostSessionEpoch,
                message.RosterFingerprint,
                message.AuthorityOwner,
                message.SourceTemplateFingerprint,
                message.TaxonomyFingerprint,
                message.StoreRevision,
                message.SessionRevision,
                message.AuthorityRevision,
                message.PriorityServiceRevision,
                message.ScheduleServiceRevision,
                message.SpecificJobServiceRevision,
                message.SettingsServiceRevision,
                message.MembershipRevision,
                message.TaxonomyRevision);
        }

        private static bool MatchesContext(
            WorkloadTransactionRequest request,
            WorkloadTransactionWireControl message)
        {
            return message != null && MatchesContext(
                request,
                message.ContextFingerprint,
                message.HostSessionEpoch,
                message.RosterFingerprint,
                message.AuthorityOwner,
                message.SourceTemplateFingerprint,
                message.TaxonomyFingerprint,
                message.StoreRevision,
                message.SessionRevision,
                message.AuthorityRevision,
                message.PriorityServiceRevision,
                message.ScheduleServiceRevision,
                message.SpecificJobServiceRevision,
                message.SettingsServiceRevision,
                message.MembershipRevision,
                message.TaxonomyRevision);
        }

        public static void ReceivePrepareAcknowledgement(WorkloadTransactionWireAcknowledgement acknowledgement)
        {
            if (!MultiplayerBridge.Active || !ProtocolAvailable ||
                acknowledgement == null ||
                acknowledgement.Phase != (byte)WorkloadTransactionPhase.Prepare)
                return;

            var request = Protocol.CurrentRequest;
            string actualSender = null;
            var authorized = MatchesContext(request, acknowledgement) &&
                MultiplayerBridge.TryGetAuthenticatedRuntimeSender(
                acknowledgement.PeerKey,
                acknowledgement.HostSessionEpoch,
                acknowledgement.RosterFingerprint,
                false,
                out actualSender,
                out _);
            Protocol.RecordPrepareAcknowledgement(
                acknowledgement.RequestId,
                acknowledgement.RequestFingerprint,
                actualSender,
                acknowledgement.Accepted,
                acknowledgement.Code,
                acknowledgement.Detail,
                acknowledgement.ReportFingerprint,
                acknowledgement.Sequence,
                authorized);
        }

        public static void ReceiveExecuteResult(WorkloadTransactionWireResult result)
        {
            if (!MultiplayerBridge.Active || !ProtocolAvailable ||
                result == null ||
                result.Phase != (byte)WorkloadTransactionPhase.Execute)
                return;

            var request = Protocol.CurrentRequest;
            string actualSender = null;
            var authorized = MatchesContext(request, result) &&
                MultiplayerBridge.TryGetAuthenticatedRuntimeSender(
                result.PeerKey,
                result.HostSessionEpoch,
                result.RosterFingerprint,
                false,
                out actualSender,
                out _);
            Protocol.RecordExecuteResult(
                result.RequestId,
                result.RequestFingerprint,
                actualSender,
                result.Accepted,
                result.Code,
                result.Detail,
                result.ReportFingerprint,
                result.Sequence,
                authorized,
                result.RequiresRollback);
        }

        public static void ReceiveConfirmationAcknowledgement(WorkloadTransactionWireAcknowledgement acknowledgement)
        {
            if (!MultiplayerBridge.Active || !ProtocolAvailable ||
                acknowledgement == null ||
                acknowledgement.Phase != (byte)WorkloadTransactionPhase.Confirm)
                return;

            var request = Protocol.CurrentRequest;
            string actualSender = null;
            var authorized = MatchesContext(request, acknowledgement) &&
                MultiplayerBridge.TryGetAuthenticatedRuntimeSender(
                    acknowledgement.PeerKey,
                    acknowledgement.HostSessionEpoch,
                    acknowledgement.RosterFingerprint,
                    false,
                    out actualSender,
                    out _);
            Protocol.RecordConfirmationAcknowledgement(
                acknowledgement.RequestId,
                acknowledgement.RequestFingerprint,
                actualSender,
                acknowledgement.Accepted,
                acknowledgement.Code,
                acknowledgement.Detail,
                acknowledgement.ReportFingerprint,
                acknowledgement.Sequence,
                authorized,
                acknowledgement.RequiresRollback);
        }

        public static void ReceiveControl(WorkloadTransactionWireControl control)
        {
            if (!MultiplayerBridge.Active || !ProtocolAvailable ||
                control == null ||
                (control.Phase != (byte)WorkloadTransactionPhase.Execute &&
                 control.Phase != (byte)WorkloadTransactionPhase.Confirm &&
                 control.Phase != (byte)WorkloadTransactionPhase.Abort))
                return;

            var request = Protocol.CurrentRequest;
            if (!MatchesContext(request, control))
                return;

            var controlKind = (WorkloadTransactionControlKind)control.ControlKind;
            if (controlKind == WorkloadTransactionControlKind.Execute)
            {
                var authorized = MultiplayerBridge.TryAuthorizeWorkloadHostMessage(
                    control.PeerKey,
                    control.HostSessionEpoch,
                    control.RosterFingerprint);
                Protocol.RecordExecuteControl(
                    control.RequestId,
                    control.RequestFingerprint,
                    control.Accepted,
                    control.Code,
                    control.Detail,
                    control.Sequence,
                    authorized);
            }
            else if (controlKind == WorkloadTransactionControlKind.ConfirmationRequest)
            {
                var authorized = MultiplayerBridge.TryAuthorizeWorkloadHostMessage(
                    control.PeerKey,
                    control.HostSessionEpoch,
                    control.RosterFingerprint);
                Protocol.RecordConfirmationControl(
                    control.RequestId,
                    control.RequestFingerprint,
                    control.Accepted,
                    control.Code,
                    control.Detail,
                    control.ReportFingerprint,
                    control.Sequence,
                    authorized);
            }
            else if (controlKind == WorkloadTransactionControlKind.FinalConfirmation)
            {
                var authorized = MultiplayerBridge.TryAuthorizeWorkloadHostMessage(
                    control.PeerKey,
                    control.HostSessionEpoch,
                    control.RosterFingerprint);
                Protocol.RecordFinalConfirmation(
                    control.RequestId,
                    control.RequestFingerprint,
                    control.Accepted,
                    control.Code,
                    control.Detail,
                    control.Sequence,
                    authorized);
            }
            else if (controlKind == WorkloadTransactionControlKind.Abort)
            {
                if (control.IsRollbackReport)
                {
                    string actualSender;
                    var authorized = MultiplayerBridge.TryGetAuthenticatedRuntimeSender(
                        control.PeerKey,
                        control.HostSessionEpoch,
                        control.RosterFingerprint,
                        false,
                        out actualSender,
                        out _);
                    Protocol.RecordRollback(
                        control.RequestId,
                        control.RequestFingerprint,
                        actualSender,
                        control.Accepted,
                        control.Code,
                        control.Detail,
                        control.Sequence,
                        authorized);
                }
                else
                {
                    var authorized = MultiplayerBridge.TryAuthorizeWorkloadHostMessage(
                        control.PeerKey,
                        control.HostSessionEpoch,
                        control.RosterFingerprint);
                    if (authorized)
                    {
                        Protocol.RequestAbort(
                            control.RequestId,
                            control.RequestFingerprint,
                            control.Code,
                            control.Detail,
                            control.Sequence);
                    }
                }
            }
        }

        internal static void SendPrepareAcknowledgement(WorkloadTransactionWireAcknowledgement acknowledgement)
        {
            _transport.SendPrepareAcknowledgement(acknowledgement);
        }

        internal static void SendExecuteResult(WorkloadTransactionWireResult result)
        {
            _transport.SendExecuteResult(result);
        }

        internal static void SendConfirmationAcknowledgement(
            WorkloadTransactionWireAcknowledgement acknowledgement)
        {
            _transport.SendPrepareAcknowledgement(acknowledgement);
        }

        internal static void SendControl(WorkloadTransactionWireControl control)
        {
            _transport.SendControl(control);
        }

        internal static void CheckSessionContext(long sequence)
        {
            if (!ProtocolAvailable || Protocol.CurrentRequest == null ||
                Protocol.CurrentState == null || Protocol.CurrentState.IsFinal)
                return;

            if (!MatchesCurrentContext(Protocol.CurrentRequest))
            {
                Protocol.Fail(
                    Protocol.CurrentRequest.RequestId,
                    "session-context-changed",
                    "The synchronized workload session, roster, or host epoch changed during the transaction.",
                    sequence);
            }
        }

        internal static void PopulateContext(
            WorkloadTransactionRequest request,
            WorkloadTransactionWireAcknowledgement message)
        {
            if (request == null || message == null) return;
            PopulateContext(request.ExpectedRevisions, message);
        }

        internal static void PopulateContext(
            WorkloadTransactionRequest request,
            WorkloadTransactionWireResult message)
        {
            if (request == null || message == null) return;
            PopulateContext(request.ExpectedRevisions, message);
        }

        internal static void PopulateContext(
            WorkloadTransactionRequest request,
            WorkloadTransactionWireControl message)
        {
            if (request == null || message == null) return;
            PopulateContext(request.ExpectedRevisions, message);
        }

        private static void PopulateContext(
            WorkloadTransactionRevisionVector context,
            WorkloadTransactionWireAcknowledgement message)
        {
            message.ContextFingerprint = context.ContextFingerprint;
            message.AuthorityOwner = context.AuthorityOwner;
            message.SourceTemplateFingerprint = context.SourceTemplateFingerprint;
            message.TaxonomyFingerprint = context.TaxonomyFingerprint;
            message.StoreRevision = context.StoreRevision;
            message.SessionRevision = context.SessionRevision;
            message.AuthorityRevision = context.AuthorityRevision;
            message.PriorityServiceRevision = context.PriorityServiceRevision;
            message.ScheduleServiceRevision = context.ScheduleServiceRevision;
            message.SpecificJobServiceRevision = context.SpecificJobServiceRevision;
            message.SettingsServiceRevision = context.SettingsServiceRevision;
            message.MembershipRevision = context.MembershipRevision;
            message.TaxonomyRevision = context.TaxonomyRevision;
        }

        private static void PopulateContext(
            WorkloadTransactionRevisionVector context,
            WorkloadTransactionWireResult message)
        {
            message.ContextFingerprint = context.ContextFingerprint;
            message.AuthorityOwner = context.AuthorityOwner;
            message.SourceTemplateFingerprint = context.SourceTemplateFingerprint;
            message.TaxonomyFingerprint = context.TaxonomyFingerprint;
            message.StoreRevision = context.StoreRevision;
            message.SessionRevision = context.SessionRevision;
            message.AuthorityRevision = context.AuthorityRevision;
            message.PriorityServiceRevision = context.PriorityServiceRevision;
            message.ScheduleServiceRevision = context.ScheduleServiceRevision;
            message.SpecificJobServiceRevision = context.SpecificJobServiceRevision;
            message.SettingsServiceRevision = context.SettingsServiceRevision;
            message.MembershipRevision = context.MembershipRevision;
            message.TaxonomyRevision = context.TaxonomyRevision;
        }

        private static void PopulateContext(
            WorkloadTransactionRevisionVector context,
            WorkloadTransactionWireControl message)
        {
            message.ContextFingerprint = context.ContextFingerprint;
            message.AuthorityOwner = context.AuthorityOwner;
            message.SourceTemplateFingerprint = context.SourceTemplateFingerprint;
            message.TaxonomyFingerprint = context.TaxonomyFingerprint;
            message.StoreRevision = context.StoreRevision;
            message.SessionRevision = context.SessionRevision;
            message.AuthorityRevision = context.AuthorityRevision;
            message.PriorityServiceRevision = context.PriorityServiceRevision;
            message.ScheduleServiceRevision = context.ScheduleServiceRevision;
            message.SpecificJobServiceRevision = context.SpecificJobServiceRevision;
            message.SettingsServiceRevision = context.SettingsServiceRevision;
            message.MembershipRevision = context.MembershipRevision;
            message.TaxonomyRevision = context.TaxonomyRevision;
        }

        private static long NextSequence()
        {
            var state = Protocol.CurrentState;
            if (state == null)
                return 0;

            return state.Sequence == long.MaxValue
                ? long.MaxValue
                : state.Sequence + 1;
        }

        private static void SyncRequest(SyncWorker sync, ref WorkloadTransactionWireRequest value)
        {
            if (!SyncPresence(sync, ref value))
                return;

            sync.Bind(ref value.ProtocolVersion);
            sync.Bind(ref value.Operation);
            sync.Bind(ref value.RequestId);
            sync.Bind(ref value.IdempotencyKey);
            sync.Bind(ref value.SessionId);
            sync.Bind(ref value.SourceWorkloadId);
            sync.Bind(ref value.TargetWorkloadId);
            sync.Bind(ref value.RequesterPlayerKey);
            sync.Bind(ref value.TimeoutBudget);
            sync.Bind(ref value.PayloadBytes);
            sync.Bind(ref value.PayloadFingerprint);
            sync.Bind(ref value.RequestFingerprint);
            sync.Bind(ref value.ParticipantKeys);
            sync.Bind(ref value.StoreRevision);
            sync.Bind(ref value.SessionRevision);
            sync.Bind(ref value.AuthorityRevision);
            sync.Bind(ref value.PriorityServiceRevision);
            sync.Bind(ref value.ScheduleServiceRevision);
            sync.Bind(ref value.SpecificJobServiceRevision);
            sync.Bind(ref value.SettingsServiceRevision);
            sync.Bind(ref value.MembershipRevision);
            sync.Bind(ref value.SourceTemplateFingerprint);
            sync.Bind(ref value.TaxonomyRevision);
            sync.Bind(ref value.TaxonomyFingerprint);
            sync.Bind(ref value.AuthorityOwner);
            sync.Bind(ref value.HostSessionEpoch);
            sync.Bind(ref value.RosterFingerprint);
        }

        private static void SyncAcknowledgement(SyncWorker sync, ref WorkloadTransactionWireAcknowledgement value)
        {
            if (!SyncPresence(sync, ref value))
                return;

            sync.Bind(ref value.Phase);
            sync.Bind(ref value.Accepted);
            sync.Bind(ref value.RequiresRollback);
            sync.Bind(ref value.RequestId);
            sync.Bind(ref value.RequestFingerprint);
            sync.Bind(ref value.HostSessionEpoch);
            sync.Bind(ref value.RosterFingerprint);
            sync.Bind(ref value.PeerKey);
            sync.Bind(ref value.Code);
            sync.Bind(ref value.Detail);
            sync.Bind(ref value.ReportFingerprint);
            sync.Bind(ref value.Sequence);
            sync.Bind(ref value.ContextFingerprint);
            sync.Bind(ref value.AuthorityOwner);
            sync.Bind(ref value.SourceTemplateFingerprint);
            sync.Bind(ref value.TaxonomyFingerprint);
            sync.Bind(ref value.StoreRevision);
            sync.Bind(ref value.SessionRevision);
            sync.Bind(ref value.AuthorityRevision);
            sync.Bind(ref value.PriorityServiceRevision);
            sync.Bind(ref value.ScheduleServiceRevision);
            sync.Bind(ref value.SpecificJobServiceRevision);
            sync.Bind(ref value.SettingsServiceRevision);
            sync.Bind(ref value.MembershipRevision);
            sync.Bind(ref value.TaxonomyRevision);
        }

        private static void SyncResult(SyncWorker sync, ref WorkloadTransactionWireResult value)
        {
            if (!SyncPresence(sync, ref value))
                return;

            sync.Bind(ref value.Phase);
            sync.Bind(ref value.TerminalState);
            sync.Bind(ref value.Accepted);
            sync.Bind(ref value.RequiresRollback);
            sync.Bind(ref value.RequestId);
            sync.Bind(ref value.RequestFingerprint);
            sync.Bind(ref value.HostSessionEpoch);
            sync.Bind(ref value.RosterFingerprint);
            sync.Bind(ref value.PeerKey);
            sync.Bind(ref value.Code);
            sync.Bind(ref value.Detail);
            sync.Bind(ref value.ReportFingerprint);
            sync.Bind(ref value.Sequence);
            sync.Bind(ref value.StoreRevision);
            sync.Bind(ref value.SessionRevision);
            sync.Bind(ref value.AuthorityRevision);
            sync.Bind(ref value.PriorityServiceRevision);
            sync.Bind(ref value.ScheduleServiceRevision);
            sync.Bind(ref value.SpecificJobServiceRevision);
            sync.Bind(ref value.SettingsServiceRevision);
            sync.Bind(ref value.MembershipRevision);
            sync.Bind(ref value.ContextFingerprint);
            sync.Bind(ref value.AuthorityOwner);
            sync.Bind(ref value.SourceTemplateFingerprint);
            sync.Bind(ref value.TaxonomyFingerprint);
            sync.Bind(ref value.TaxonomyRevision);
        }

        private static void SyncControl(SyncWorker sync, ref WorkloadTransactionWireControl value)
        {
            if (!SyncPresence(sync, ref value))
                return;

            sync.Bind(ref value.Phase);
            sync.Bind(ref value.TerminalState);
            sync.Bind(ref value.Accepted);
            sync.Bind(ref value.RequiresRollback);
            sync.Bind(ref value.RequestId);
            sync.Bind(ref value.RequestFingerprint);
            sync.Bind(ref value.HostSessionEpoch);
            sync.Bind(ref value.RosterFingerprint);
            sync.Bind(ref value.PeerKey);
            sync.Bind(ref value.Code);
            sync.Bind(ref value.Detail);
            sync.Bind(ref value.ReportFingerprint);
            sync.Bind(ref value.Sequence);
            sync.Bind(ref value.ContextFingerprint);
            sync.Bind(ref value.AuthorityOwner);
            sync.Bind(ref value.SourceTemplateFingerprint);
            sync.Bind(ref value.TaxonomyFingerprint);
            sync.Bind(ref value.StoreRevision);
            sync.Bind(ref value.SessionRevision);
            sync.Bind(ref value.AuthorityRevision);
            sync.Bind(ref value.PriorityServiceRevision);
            sync.Bind(ref value.ScheduleServiceRevision);
            sync.Bind(ref value.SpecificJobServiceRevision);
            sync.Bind(ref value.SettingsServiceRevision);
            sync.Bind(ref value.MembershipRevision);
            sync.Bind(ref value.TaxonomyRevision);
            sync.Bind(ref value.ControlKind);
            sync.Bind(ref value.IsRollbackReport);
        }

        private static bool SyncPresence<T>(SyncWorker sync, ref T value)
            where T : class, new()
        {
            if (sync.isWriting)
            {
                sync.Write(value != null);
                return value != null;
            }

            if (!sync.Read<bool>())
            {
                value = null;
                return false;
            }

            if (value == null)
                value = new T();
            return true;
        }

        private sealed class MultiplayerApiWorkloadTransactionTransport : IWorkloadTransactionTransport
        {
            public void SendRequest(WorkloadTransactionWireRequest request)
            {
                ReceiveRequest(request);
            }

            public void SendPrepareAcknowledgement(WorkloadTransactionWireAcknowledgement acknowledgement)
            {
                if (acknowledgement != null &&
                    acknowledgement.Phase == (byte)WorkloadTransactionPhase.Confirm)
                    ReceiveConfirmationAcknowledgement(acknowledgement);
                else
                    ReceivePrepareAcknowledgement(acknowledgement);
            }

            public void SendExecuteResult(WorkloadTransactionWireResult result)
            {
                ReceiveExecuteResult(result);
            }

            public void SendControl(WorkloadTransactionWireControl control)
            {
                ReceiveControl(control);
            }
        }
    }
}
