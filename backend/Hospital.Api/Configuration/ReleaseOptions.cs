using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Hospital.Api.Configuration;

public sealed partial class ReleaseOptions
{
    public const string SectionName = "Release";

    [Required]
    [MaxLength(64)]
    public string Revision { get; init; } = string.Empty;

    public static bool IsLocalOrGitRevision(ReleaseOptions options) =>
        string.Equals(options.Revision, "local", StringComparison.Ordinal) ||
        GitRevisionExpression().IsMatch(options.Revision);

    public static bool IsDeployableGitRevision(ReleaseOptions options) =>
        GitRevisionExpression().IsMatch(options.Revision);

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitRevisionExpression();
}
