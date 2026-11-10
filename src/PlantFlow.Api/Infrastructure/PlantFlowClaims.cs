namespace PlantFlow.Api.Infrastructure;

public static class PlantFlowClaims
{
    public const string Status = "plantflow:status";
    public const string PlatformAdmin = "plantflow:platform_admin";
    public const string Permission = "plantflow:permission";
}

public static class Policies
{
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string PlatformAdmin = "PlatformAdmin";
}