using System;
using System.Globalization;

namespace Spine.Api
{
    public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        public SemanticVersion(int major, int minor, int patch, string prerelease = null)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));

            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string Prerelease { get; }
        public bool IsPrerelease => Prerelease != null;

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
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string withoutBuild = value.Split('+')[0];
            int prereleaseSeparator = withoutBuild.IndexOf('-');
            string core = prereleaseSeparator < 0 ? withoutBuild : withoutBuild.Substring(0, prereleaseSeparator);
            string prerelease = prereleaseSeparator < 0 ? null : withoutBuild.Substring(prereleaseSeparator + 1);
            string[] parts = core.Split('.');
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(prerelease) && prereleaseSeparator >= 0)
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
        Revisions = 1UL << 0,
        DirtyRegions = 1UL << 1,
        BoundedCaches = 1UL << 2,
        RenderPipelines = 1UL << 3,
        ViewportResolution = 1UL << 4,
        RenderAtlases = 1UL << 5,
        CompatibilityProviders = 1UL << 6,
        Diagnostics = 1UL << 7
    }

    public readonly struct SpineApiDescriptor
    {
        public SpineApiDescriptor(string apiId, SemanticVersion version, SpineCapability capabilities)
        {
            if (string.IsNullOrWhiteSpace(apiId))
            {
                throw new ArgumentException("An API identifier is required.", nameof(apiId));
            }

            ApiId = apiId;
            Version = version;
            Capabilities = capabilities;
        }

        public string ApiId { get; }
        public SemanticVersion Version { get; }
        public SpineCapability Capabilities { get; }

        public bool Supports(SemanticVersion minimumVersion, SpineCapability requiredCapabilities)
        {
            return Version >= minimumVersion && (Capabilities & requiredCapabilities) == requiredCapabilities;
        }
    }
}
