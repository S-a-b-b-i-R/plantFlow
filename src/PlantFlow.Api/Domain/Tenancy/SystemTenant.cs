namespace PlantFlow.Api.Domain.Tenancy;

// A built-in tenant that holds users who have signed in but have not been assigned a company yet.
public static class SystemTenant
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string Slug = "platform";
}