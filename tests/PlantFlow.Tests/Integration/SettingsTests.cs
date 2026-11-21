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
            pumpRestShifts = rest,
            reason = "Test setup"
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

    // Reads the current settings and sends them back with the given changes and reason.
    private static async Task<HttpResponseMessage> ChangeAsync(
        HttpClient admin, Func<TenantSettingsDto, TenantSettingsDto> change, string? reason)
    {
        var current = (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        var next = change(current);
        return await admin.PutAsJsonAsync("/api/settings", new
        {
            next.Name, next.VolumeUnit, next.PumpFreeAboveCft, next.RequireSurveyBeforePlanning, next.PumpRestShifts,
            next.RequireCreditApproval, next.VatOnConcrete, next.AverageSpeedKmh, next.PumpAssemblyMinutes,
            next.TruckLoadCft, next.LateThresholdMinutes, next.UtcOffsetMinutes, next.ReturnWindowMinutes,
            next.CustomerOwnershipDays, reason
        }, ApiFactory.Json);
    }

    [Fact]
    public async Task Changing_vat_credit_approval_or_the_free_pump_threshold_needs_a_reason()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var vat = await ChangeAsync(admin, s => s with { VatOnConcrete = false }, null);
        var both = await ChangeAsync(admin, s => s with { RequireCreditApproval = true, PumpFreeAboveCft = 500m }, " ");
        var other = await ChangeAsync(admin, s => s with { AverageSpeedKmh = 30 }, null);

        Assert.Equal(HttpStatusCode.BadRequest, vat.StatusCode);
        Assert.Contains("Give a reason for changing VAT on concrete.", await vat.Content.ReadAsStringAsync());
        Assert.Contains("Give a reason for changing credit approval and the free pump threshold.", await both.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);

        var withReason = await ChangeAsync(admin, s => s with { VatOnConcrete = false }, "Customers are VAT registered");
        Assert.Equal(HttpStatusCode.NoContent, withReason.StatusCode);
        var audit = await admin.GetFromJsonAsync<List<AuditLogDto>>("/api/audit", ApiFactory.Json);
        Assert.Contains(audit!, e => e.Action == "tenant.settings_changed" && e.ChangesJson!.Contains("Customers are VAT registered"));
    }

    [Fact]
    public async Task The_time_zone_is_fixed_once_the_company_has_orders()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var before = await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json);
        Assert.False(before!.TimeZoneLocked);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ChangeAsync(admin, s => s with { UtcOffsetMinutes = 330 }, null)).StatusCode);

        var w = await TestData.CreateOrderWorldAsync(admin);
        await TestData.CreateDraftAsync(admin, w, [TestData.LineBody(w.ProductId, 50m)]);

        var after = await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json);
        Assert.True(after!.TimeZoneLocked);
        var moved = await ChangeAsync(admin, s => s with { UtcOffsetMinutes = 360 }, null);
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Contains(SettingsController.TimeZoneLockedMessage, await moved.Content.ReadAsStringAsync());

        // Saving other settings with the same time zone still works.
        Assert.Equal(HttpStatusCode.NoContent,
            (await ChangeAsync(admin, s => s with { AverageSpeedKmh = 30 }, null)).StatusCode);
    }
}
