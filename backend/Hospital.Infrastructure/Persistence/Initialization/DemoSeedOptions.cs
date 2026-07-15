using System.Globalization;

namespace Hospital.Infrastructure.Persistence.Initialization;

public sealed class DemoSeedOptions
{
    public const string SectionName = "DemoSeed";

    public string AnchorDate { get; init; } = string.Empty;

    public DemoIdentitySubjects Subjects { get; init; } = new();

    public DateOnly ValidateAndGetAnchorDate()
    {
        if (!DateOnly.TryParseExact(
                AnchorDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly anchorDate))
        {
            throw new InvalidOperationException(
                "DemoSeed:AnchorDate must use the yyyy-MM-dd format.");
        }

        string[] subjects =
        [
            Subjects.Patient,
            Subjects.Doctor,
            Subjects.Pharmacist,
            Subjects.Administrator,
        ];

        if (subjects.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "All four DemoSeed:Subjects values are required.");
        }

        if (subjects.Any(static subject => subject.Length > 255))
        {
            throw new InvalidOperationException(
                "DemoSeed:Subjects values cannot exceed 255 characters.");
        }

        if (subjects.Any(static subject =>
                !string.Equals(subject, subject.Trim(), StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "DemoSeed:Subjects values cannot have leading or trailing whitespace.");
        }

        if (subjects.Distinct(StringComparer.Ordinal).Count() != subjects.Length)
        {
            throw new InvalidOperationException(
                "DemoSeed:Subjects values must be unique.");
        }

        return anchorDate;
    }
}

public sealed class DemoIdentitySubjects
{
    public string Patient { get; init; } = string.Empty;

    public string Doctor { get; init; } = string.Empty;

    public string Pharmacist { get; init; } = string.Empty;

    public string Administrator { get; init; } = string.Empty;
}
