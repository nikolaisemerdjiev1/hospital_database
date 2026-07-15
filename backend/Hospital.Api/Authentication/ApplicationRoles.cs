using Hospital.Core.Profiles;

namespace Hospital.Api.Authentication;

public static class ApplicationRoles
{
    public const string Patient = "patient";
    public const string Doctor = "doctor";
    public const string Pharmacist = "pharmacist";
    public const string Administrator = "administrator";

    internal static bool TryGetProfileType(string role, out ProfileType profileType)
    {
        profileType = role switch
        {
            Patient => ProfileType.Patient,
            Doctor => ProfileType.Doctor,
            Pharmacist => ProfileType.Pharmacist,
            Administrator => ProfileType.Administrator,
            _ => default,
        };

        return profileType != default;
    }

    internal static string GetRole(ProfileType profileType) => profileType switch
    {
        ProfileType.Patient => Patient,
        ProfileType.Doctor => Doctor,
        ProfileType.Pharmacist => Pharmacist,
        ProfileType.Administrator => Administrator,
        _ => throw new ArgumentOutOfRangeException(nameof(profileType), profileType, null),
    };
}
