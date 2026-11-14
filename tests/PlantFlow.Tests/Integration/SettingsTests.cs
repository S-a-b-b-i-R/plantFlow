using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class SettingsTests(ApiFactory factory)
{
    private static object Body(
        string name = "Valid Co.", string? volumeUnit = "Cft", decimal? pumpFree = null,
        string? survey = "PumpJobs", int rest = 1) =>
        new
        {
            name,
            volumeUnit,
            pumpFreeAboveCft = pumpFree,
            requireSurveyBeforePlanning = survey,
            pumpRestShifts = rest
        };

    [Fact]
    public async Task A_new_company_starts_with_sensible_defaults()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var settings = await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json);

        Assert.Equal(VolumeUnit.Cft, settings!.VolumeUnit);
        Assert.Null(settings.PumpFreeAboveCft);
        Assert.Equal(SurveyRequirement.PumpJobs, settings.RequireSurveyBeforePlanning);
        Assert.Equal(1, settings.PumpRestShifts);
    }

    [Fact]
    public async Task Settings_can_be_changed_and_the_change_is_audited()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var update = await admin.PutAsJsonAsync("/api/settings",
            Body("Renamed Co.", "M3", 1200.50m, "All", 2));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var settings = await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json);
        Assert.Equal("Renamed Co.", settings!.Name);
        Assert.Equal(VolumeUnit.M3, settings.VolumeUnit);
        Assert.Equal(1200.50m, settings.PumpFreeAboveCft);
        Assert.Equal(SurveyRequirement.All, settings.RequireSurveyBeforePlanning);
        Assert.Equal(2, settings.PumpRestShifts);

        var audit = await admin.GetFromJsonAsync<List<AuditLogDto>>("/api/audit", ApiFactory.Json);
        Assert.Contains(audit!, e => e.Action == "tenant.settings_changed");
    }

    [Fact]
    public async Task The_pump_threshold_can_be_cleared_and_the_rest_rule_can_be_turned_off()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        (await admin.PutAsJsonAsync("/api/settings", Body(pumpFree: 800m, rest: 2))).EnsureSuccessStatusCode();

        var update = await admin.PutAsJsonAsync("/api/settings", Body(pumpFree: null, rest: 0));
        var settings = await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Null(settings!.PumpFreeAboveCft);
        Assert.Equal(0, settings.PumpRestShifts); // a real 0 must be saved, not replaced by the default
    }

    [Fact]
    public async Task Settings_input_is_validated()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var restTooHigh = await admin.PutAsJsonAsync("/api/settings", Body(rest: 5));
        var negativeRest = await admin.PutAsJsonAsync("/api/settings", Body(rest: -1));
        var thresholdNegative = await admin.PutAsJsonAsync("/api/settings", Body(pumpFree: -10m));
        var thresholdDecimals = await admin.PutAsJsonAsync("/api/settings", Body(pumpFree: 100.123m));
        var badUnit = await admin.PutAsJsonAsync("/api/settings", Body(volumeUnit: "Gallons"));
        var missingUnit = await admin.PutAsJsonAsync("/api/settings", Body(volumeUnit: null));
        var badSurvey = await admin.PutAsJsonAsync("/api/settings", Body(survey: "Sometimes"));
        var shortName = await admin.PutAsJsonAsync("/api/settings", Body(name: "A"));

        Assert.All(
            new[] { restTooHigh, negativeRest, thresholdNegative, thresholdDecimals,
                    badUnit, missingUnit, badSurvey, shortName },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
    }

    [Fact]
    public async Task Without_the_permission_settings_are_forbidden_but_the_public_part_is_readable()
    {
        var company = await factory.CreateCompanyAsync();
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var get = await rep.Client.GetAsync("/api/settings");
        var put = await rep.Client.PutAsJsonAsync("/api/settings", Body());
        var publicPart = await rep.Client.GetFromJsonAsync<PublicSettingsDto>("/api/settings/public", ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(VolumeUnit.Cft, publicPart!.VolumeUnit);
    }

    [Fact]
    public async Task A_pending_user_cannot_read_even_the_public_settings()
    {
        var company = await factory.CreateCompanyAsync();
        var pending = await factory.AddMemberAsync(company, approve: false);

        var response = await pending.Client.GetAsync("/api/settings/public");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Each_company_has_its_own_settings()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();

        (await a.Admin.Client.PutAsJsonAsync("/api/settings", Body("Company A Renamed", "M3", 500m, "Off", 0)))
            .EnsureSuccessStatusCode();

        var seenByB = await b.Admin.Client.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json);

        Assert.NotEqual("Company A Renamed", seenByB!.Name);
        Assert.Equal(VolumeUnit.Cft, seenByB.VolumeUnit);
        Assert.Null(seenByB.PumpFreeAboveCft);
        Assert.Equal(1, seenByB.PumpRestShifts);
    }
}