using System.Collections.Generic;
using Better_Work_Tab.UI.RuleBuilder.State;
using NUnit.Framework;

namespace Better_Work_Tab.Tests
{
    /// <summary>
    /// Tests for PrioritySnapshot pure logic. Uses strings as stand-ins for Pawn / WorkTypeDef
    /// so no RimWorld game engine is needed.
    /// </summary>
    [TestFixture]
    public class PrioritySnapshotTests
    {
        // ── helpers ──────────────────────────────────────────────────────────

        private static PrioritySnapshot<string, string> MakeSnapshot(
            IList<string> pawns,
            IList<string> workTypes,
            Dictionary<string, Dictionary<string, int>> before,
            Dictionary<string, Dictionary<string, int>> after,
            Dictionary<string, Dictionary<string, int>> baseline = null)
        {
            // When no explicit baseline is provided, use before (non-reset ruleset behaviour).
            return new PrioritySnapshot<string, string>(pawns, workTypes, before, baseline ?? before, after);
        }

        // ── GetAfterPriority ─────────────────────────────────────────────────

        [Test]
        public void GetAfterPriority_ReturnsValue_WhenPresent()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 2 }
                });

            Assert.That(snap.GetAfterPriority("Alice", "Cooking"), Is.EqualTo(2));
        }

        [Test]
        public void GetAfterPriority_ReturnsZero_WhenPawnMissing()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>>(),
                after: new Dictionary<string, Dictionary<string, int>>());

            Assert.That(snap.GetAfterPriority("Alice", "Cooking"), Is.EqualTo(0));
        }

        [Test]
        public void GetAfterPriority_ReturnsZero_WhenWorkTypeMissing()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int>()
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int>()
                });

            Assert.That(snap.GetAfterPriority("Alice", "Mining"), Is.EqualTo(0));
        }

        // ── GetBeforePriority ────────────────────────────────────────────────

        [Test]
        public void GetBeforePriority_ReturnsValue_WhenPresent()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 3 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 3 }
                });

            Assert.That(snap.GetBeforePriority("Alice", "Cooking"), Is.EqualTo(3));
        }

        // ── HasChanged ───────────────────────────────────────────────────────

        [Test]
        public void HasChanged_ReturnsFalse_WhenPriorityUnchanged()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 2 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 2 }
                });

            Assert.That(snap.HasChanged("Alice", "Cooking"), Is.False);
        }

        [Test]
        public void HasChanged_ReturnsTrue_WhenPriorityChanges()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 1 }
                });

            Assert.That(snap.HasChanged("Alice", "Cooking"), Is.True);
        }

        [Test]
        public void HasChanged_ReturnsTrue_WhenPriorityDropsToZero()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 3 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                });

            Assert.That(snap.HasChanged("Alice", "Cooking"), Is.True);
        }

        [Test]
        public void HasChanged_ReturnsFalse_WhenBothAbsent()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>>(),
                after: new Dictionary<string, Dictionary<string, int>>());

            Assert.That(snap.HasChanged("Alice", "Cooking"), Is.False);
        }

        // ── MatchedPawns ─────────────────────────────────────────────────────

        [Test]
        public void MatchedPawns_IncludesPawn_WithNonZeroAfterPriority()
        {
            var snap = MakeSnapshot(
                new[] { "Alice", "Bob" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 },
                    ["Bob"]   = new Dictionary<string, int> { ["Cooking"] = 0 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 2 },
                    ["Bob"]   = new Dictionary<string, int> { ["Cooking"] = 0 }
                });

            Assert.That(snap.MatchedPawns, Does.Contain("Alice"));
            Assert.That(snap.MatchedPawns, Does.Not.Contain("Bob"));
        }

        [Test]
        public void MatchedPawns_IsEmpty_WhenAllAfterPrioritiesZero()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 3 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                });

            Assert.That(snap.MatchedPawns, Is.Empty);
        }

        [Test]
        public void MatchedPawns_IncludesPawn_IfAnyWorkTypeIsNonZero()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking", "Mining", "Cleaning" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int>
                        { ["Cooking"] = 0, ["Mining"] = 0, ["Cleaning"] = 0 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int>
                        { ["Cooking"] = 0, ["Mining"] = 3, ["Cleaning"] = 0 }
                });

            Assert.That(snap.MatchedPawns, Does.Contain("Alice"));
        }

        // ── HasData ──────────────────────────────────────────────────────────

        [Test]
        public void HasData_IsFalse_WhenNoPawns()
        {
            var snap = new PrioritySnapshot<string, string>();
            Assert.That(snap.HasData, Is.False);
        }

        [Test]
        public void HasData_IsTrue_WhenPawnsExist()
        {
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                });

            Assert.That(snap.HasData, Is.True);
        }

        // ── Baseline (reset-before-apply) ─────────────────────────────────────

        [Test]
        public void HasChanged_ReturnsFalse_WhenResetWipedPriorityButNoRuleAssigned()
        {
            // Colony had Mining=2; ruleset resets (baseline=0) but has no Mining rule (after=0).
            // The reset wipe should NOT appear as a rule-driven change.
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Mining" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Mining"] = 2 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Mining"] = 0 }
                },
                baseline: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Mining"] = 0 }
                });

            Assert.That(snap.HasChanged("Alice", "Mining"), Is.False);
        }

        [Test]
        public void HasChanged_ReturnsTrue_WhenRuleAssignsPriorityFromResetBaseline()
        {
            // Colony had Cooking=0; reset leaves baseline=0; rule assigns Cooking=1.
            // The rule assignment SHOULD be highlighted.
            var snap = MakeSnapshot(
                new[] { "Alice" },
                new[] { "Cooking" },
                before: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                },
                after: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 1 }
                },
                baseline: new Dictionary<string, Dictionary<string, int>> {
                    ["Alice"] = new Dictionary<string, int> { ["Cooking"] = 0 }
                });

            Assert.That(snap.HasChanged("Alice", "Cooking"), Is.True);
        }
    }
}
