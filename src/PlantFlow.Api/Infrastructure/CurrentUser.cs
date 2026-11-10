namespace PlantFlow.Api.Infrastructure;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
}

// Reads the user and tenant from claims that our own code adds after the Firebase token is validated.
public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string UserIdClaim = "plantflow:user_id";
    public const string TenantIdClaim = "plantflow:tenant_id";

    public Guid? UserId => ReadGuid(UserIdClaim);
    public Guid? TenantId => ReadGuid(TenantIdClaim);

    private Guid? ReadGuid(string claimType)
    {
        var value = accessor.HttpContext?.User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}