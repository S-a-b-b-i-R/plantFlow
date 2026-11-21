namespace PlantFlow.Api.Infrastructure;

public static class PlantFlowClaims
{
    public const string Status = "plantflow:status";
    public const string PlatformAdmin = "plantflow:platform_admin";
    // Present when the person's company is suspended: they keep their identity but get no permissions.
    public const string CompanySuspended = "plantflow:company_suspended";
    public const string Permission = "plantflow:permission";
}

public static class Policies
{
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string PlatformAdmin = "PlatformAdmin";
}