namespace Hospital.Api.Authentication;

public static class AuthorizationPolicies
{
    public const string ProfileResolved = "profile-resolved";
    public const string Patient = "patient";
    public const string Doctor = "doctor";
    public const string Pharmacist = "pharmacist";
    public const string Administrator = "administrator";
}
