using System;
using System.Globalization;

namespace Spine.Api
{
#if RWT_LEGACY_BCL
    public struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
#else
    public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
#endif
    {
        public SemanticVersion(int major, int minor, int patch, string prerelease = null)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));

            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = LegacyBcl.IsNullOrWhiteSpace(prerelease) ? null : prerelease;
#if RWT_LEGACY_BCL
            IsPrerelease = Prerelease != null;
#endif
        }

#if RWT_LEGACY_BCL
        public int Major;
        public int Minor;
        public int Patch;
        public string Prerelease;
#else
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string Prerelease { get; }
#endif
#if RWT_LEGACY_BCL
        public bool IsPrerelease;
#else
        public bool IsPrerelease => Prerelease != null;
#endif

        public static SemanticVersion Parse(string value)
        {
            if (!TryParse(value, out SemanticVersion version))
            {
                throw new FormatException("Semantic version must use major.minor.patch with an optional prerelease suffix.");
            }

            return version;
        }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = default(SemanticVersion);
            if (LegacyBcl.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string withoutBuild = value.Split('+')[0];
            int prereleaseSeparator = withoutBuild.IndexOf('-');
            string core = prereleaseSeparator < 0 ? withoutBuild : withoutBuild.Substring(0, prereleaseSeparator);
            string prerelease = prereleaseSeparator < 0 ? null : withoutBuild.Substring(prereleaseSeparator + 1);
            string[] parts = core.Split('.');
            if (parts.Length != 3 || LegacyBcl.IsNullOrWhiteSpace(prerelease) && prereleaseSeparator >= 0)
            {
                return false;
            }

            if (!TryParseComponent(parts[0], out int major) ||
                !TryParseComponent(parts[1], out int minor) ||
                !TryParseComponent(parts[2], out int patch))
            {
                return false;
            }

            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            int result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;

            if (Prerelease == null) return other.Prerelease == null ? 0 : 1;
            if (other.Prerelease == null) return -1;
            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        public bool Equals(SemanticVersion other)
        {
            return CompareTo(other) == 0;
        }

        public override bool Equals(object obj)
        {
            return obj is SemanticVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Major;
                hash = (hash * 397) ^ Minor;
                hash = (hash * 397) ^ Patch;
                hash = (hash * 397) ^ (Prerelease == null ? 0 : StringComparer.Ordinal.GetHashCode(Prerelease));
                return hash;
            }
        }

        public override string ToString()
        {
            string core = Major.ToString(CultureInfo.InvariantCulture) + "." +
                Minor.ToString(CultureInfo.InvariantCulture) + "." +
                Patch.ToString(CultureInfo.InvariantCulture);
            return Prerelease == null ? core : core + "-" + Prerelease;
        }

        public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
        public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
        public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
        public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
        public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);

        private static bool TryParseComponent(string value, out int component)
        {
            component = 0;
            return !string.IsNullOrEmpty(value) &&
                (value.Length == 1 || value[0] != '0') &&
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out component) &&
                component >= 0;
        }

        private static int ComparePrerelease(string left, string right)
        {
            string[] leftParts = left.Split('.');
            string[] rightParts = right.Split('.');
            int count = Math.Min(leftParts.Length, rightParts.Length);
            for (int i = 0; i < count; i++)
            {
                bool leftNumeric = int.TryParse(leftParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber);
                bool rightNumeric = int.TryParse(rightParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber);
                int result;
                if (leftNumeric && rightNumeric)
                {
                    result = leftNumber.CompareTo(rightNumber);
                }
                else if (leftNumeric != rightNumeric)
                {
                    result = leftNumeric ? -1 : 1;
                }
                else
                {
                    result = string.CompareOrdinal(leftParts[i], rightParts[i]);
                }

                if (result != 0) return result;
            }

            return leftParts.Length.CompareTo(rightParts.Length);
        }
    }

    [Flags]
    public enum SpineCapability : ulong
    {
        None = 0,
        // Capability values are wire identifiers. Keep the established bit
        // assigned to a retained capability even when unrelated APIs leave
        // Spine; compacting this enum would break already-built consumers.
        BoundedCaches = 1UL << 2,
        Settings = 1UL << 8,
        HarmonyPatching = 1UL << 9,
        // Retained as a reserved wire identifier after the legacy companion
        // assembly was removed; it is intentionally never advertised.
        FluentTranspilers = 1UL << 10,
        TooltipSizing = 1UL << 11,
        ContextualSettings = 1UL << 12,
        ModSettingsPages = 1UL << 13,
        SettingsSchema = 1UL << 14,
        SettingsPreviewTransactions = 1UL << 15
    }

#if RWT_LEGACY_BCL
    public struct SpineApiDescriptor
#else
    public readonly struct SpineApiDescriptor
#endif
    {
        public SpineApiDescriptor(string apiId, SemanticVersion version, SpineCapability capabilities)
        {
            if (LegacyBcl.IsNullOrWhiteSpace(apiId))
            {
                throw new ArgumentException("An API identifier is required.", nameof(apiId));
            }

            ApiId = apiId;
            Version = version;
            Capabilities = capabilities;
        }

#if RWT_LEGACY_BCL
        public string ApiId;
        public SemanticVersion Version;
        public SpineCapability Capabilities;
#else
        public string ApiId { get; }
        public SemanticVersion Version { get; }
        public SpineCapability Capabilities { get; }
#endif

        public bool Supports(SemanticVersion minimumVersion, SpineCapability requiredCapabilities)
        {
            return Version >= minimumVersion && (Capabilities & requiredCapabilities) == requiredCapabilities;
        }
    }

#if RWT_LEGACY_BCL
    public struct SpineRequirement
#else
    public readonly struct SpineRequirement
#endif
    {
        public SpineRequirement(
            string consumerId,
            SemanticVersion minimumVersion,
            SpineCapability requiredCapabilities)
        {
            if (LegacyBcl.IsNullOrWhiteSpace(consumerId))
            {
                throw new ArgumentException(
                    "A consumer identifier is required.",
                    nameof(consumerId));
            }

            ConsumerId = consumerId;
            MinimumVersion = minimumVersion;
            RequiredCapabilities = requiredCapabilities;
        }

#if RWT_LEGACY_BCL
        public string ConsumerId;
        public SemanticVersion MinimumVersion;
        public SpineCapability RequiredCapabilities;
#else
        public string ConsumerId { get; }
        public SemanticVersion MinimumVersion { get; }
        public SpineCapability RequiredCapabilities { get; }
#endif
    }

#if RWT_LEGACY_BCL
    public struct SpineCompatibilityResult
#else
    public readonly struct SpineCompatibilityResult
#endif
    {
        public SpineCompatibilityResult(
            bool isCompatible,
            SpineCapability missingCapabilities,
            string detail)
        {
            IsCompatible = isCompatible;
            MissingCapabilities = missingCapabilities;
            Detail = detail ?? string.Empty;
        }

#if RWT_LEGACY_BCL
        public bool IsCompatible;
        public SpineCapability MissingCapabilities;
        public string Detail;
#else
        public bool IsCompatible { get; }
        public SpineCapability MissingCapabilities { get; }
        public string Detail { get; }
#endif
    }

    public interface ISpineRuntimeFacade
    {
        SpineApiDescriptor Descriptor { get; }

        SpineCompatibilityResult Check(SpineRequirement requirement);

        void Require(SpineRequirement requirement);
    }

    public interface ITooltipSizingFacade
    {
        IDisposable Acquire(string consumerId);
    }
}
