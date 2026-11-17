using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PlantFlow.Api.Domain.Identity;

namespace PlantFlow.Api.Infrastructure;

// Usage: [HasPermission(Permissions.OrdersView)] on a controller or an action.
// More than one key means any of them is enough: [HasPermission(Permissions.BatchesManage, Permissions.ReturnsQc)].
public sealed class HasPermissionAttribute(params string[] permissions)
    : AuthorizeAttribute(PermissionPolicyProvider.PolicyPrefix + string.Join(PermissionPolicyProvider.AnyOf, permissions))
{
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

// Builds a policy on demand for any "permission:xyz" name, so we don't register 37 policies by hand.
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "permission:";
    public const char AnyOf = '|';

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(PlantFlowClaims.Status, nameof(UserStatus.Active))
            .AddRequirements(new PermissionRequirement(policyName[PolicyPrefix.Length..]))
            .Build();
    }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var anyOf = requirement.Permission.Split(PermissionPolicyProvider.AnyOf);
        if (anyOf.Any(permission => context.User.HasClaim(PlantFlowClaims.Permission, permission)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}