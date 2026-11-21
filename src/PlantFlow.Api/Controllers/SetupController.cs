using Microsoft.AspNetCore.Mvc;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

// The company Admin's "getting started" list. Admin only, like the settings it starts with.
[ApiController]
[Route("api/setup")]
[HasPermission(Permissions.SettingsManage)]
public class SetupController(SetupChecklist checklist, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<SetupProgressDto> Get(CancellationToken cancellationToken) =>
        checklist.ForTenantAsync(currentUser.TenantId!.Value, cancellationToken);
}
