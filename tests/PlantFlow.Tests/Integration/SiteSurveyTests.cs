using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Planning;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class SiteSurveyTests(ApiFactory factory)
{
    private static object Body(
        string result = "Ok", string date = "2026-05-30", decimal latitude = 23.8103m, decimal longitude = 90.4125m) =>
        new
        {
            surveyedOn = date,
            result,
            latitude,
            longitude,
            accuracyMeters = 8m,
            pipingMetres = 40.5m,
            pumpPosition = "Beside the east gate",
            truckConnectionPoint = "Ramp at the back lane",
            locationNote = "Turn left after the tall blue building",
            notes = "Gate is 3 m wide"
        };

    private static async Task<SiteSurveyDto> CreateAsync(HttpClient c, Guid siteId, object body)
    {
        var response = await c.PostAsJsonAsync($"/api/sites/{siteId}/surveys", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SiteSurveyDto>(ApiFactory.Json))!;
    }

    // A byte array that starts like a PNG file.
    private static byte[] Png(int length)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return bytes;
    }

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient c, Guid surveyId, byte[] bytes, string fileName = "site.png", string? caption = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        if (caption is not null) form.Add(new StringContent(caption), "caption");
        return c.PostAsync($"/api/surveys/{surveyId}/images", form);
    }

    private static Task<List<SiteSurveyDto>?> ForSiteAsync(HttpClient c, Guid siteId) =>
        c.GetFromJsonAsync<List<SiteSurveyDto>>($"/api/sites/{siteId}/surveys", ApiFactory.Json);

    [Fact]
    public async Task A_survey_needs_a_position_and_keeps_the_instructions()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        var technical = await factory.AddMemberAsync(company, "Technical");
        var url = $"/api/sites/{w.SiteId}/surveys";

        var noPosition = await admin.PostAsJsonAsync(url, new { surveyedOn = "2026-05-30", result = "Ok" });
        var badLatitude = await admin.PostAsJsonAsync(url, Body(latitude: 95m));
        var tooPrecise = await admin.PostAsJsonAsync(url, Body(longitude: 90.12345678m));
        Assert.Equal(HttpStatusCode.BadRequest, noPosition.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLatitude.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);

        // A rep can read surveys but not make them. The technical team can.
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.PostAsJsonAsync(url, Body())).StatusCode);
        var survey = await CreateAsync(technical.Client, w.SiteId, Body());

        Assert.Equal("Tower A", survey.SiteName);
        Assert.Equal(23.8103m, survey.Latitude);
        Assert.Equal(90.4125m, survey.Longitude);
        Assert.Equal(8m, survey.AccuracyMeters);
        Assert.Equal(40.5m, survey.PipingMetres);
        Assert.Equal("Beside the east gate", survey.PumpPosition);
        Assert.Equal("Ramp at the back lane", survey.TruckConnectionPoint);
        Assert.Equal("Turn left after the tall blue building", survey.LocationNote);
        Assert.Empty(survey.Images);

        var listed = Assert.Single((await ForSiteAsync(rep.Client, w.SiteId))!);
        Assert.Equal(survey.Id, listed.Id);
        Assert.Equal("Turn left after the tall blue building", listed.LocationNote);
    }

    [Fact]
    public async Task A_survey_can_have_three_photos_checked_by_their_content()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        var planner = await factory.AddMemberAsync(company, "Planner");
        var survey = await CreateAsync(admin, w.SiteId, Body());

        // Not an image, even though it says it is. Too big. Not allowed for a rep.
        var text = await UploadAsync(admin, survey.Id, System.Text.Encoding.UTF8.GetBytes("not a picture"));
        var huge = await UploadAsync(admin, survey.Id, Png(5 * 1024 * 1024 + 1));
        var denied = await UploadAsync(rep.Client, survey.Id, Png(64));
        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var first = await UploadAsync(admin, survey.Id, Png(64), "gate.png", "East gate");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstImage = (await first.Content.ReadFromJsonAsync<SurveyImageDto>(ApiFactory.Json))!;
        Assert.Equal("image/png", firstImage.ContentType);
        Assert.Equal(64, firstImage.SizeBytes);
        Assert.Equal("East gate", firstImage.Caption);

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(planner.Client, survey.Id, Png(80), "lane.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(admin, survey.Id, Png(96), "yard.png")).StatusCode);

        // A fourth photo is refused. Removing one makes room.
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(admin, survey.Id, Png(32))).StatusCode);

        var listed = Assert.Single((await ForSiteAsync(rep.Client, w.SiteId))!);
        Assert.Equal(["gate.png", "lane.png", "yard.png"], listed.Images.Select(i => i.FileName));

        // The photo comes back exactly as it was stored.
        var download = await rep.Client.GetAsync($"/api/surveys/{survey.Id}/images/{firstImage.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png(64), await download.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.DeleteAsync($"/api/surveys/{survey.Id}/images/{firstImage.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/surveys/{survey.Id}/images/{firstImage.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/surveys/{survey.Id}/images/{firstImage.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(admin, survey.Id, Png(32), "again.png")).StatusCode);

        // Another company sees none of it.
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(other.Admin.Client, survey.Id, Png(32))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/sites/{w.SiteId}/surveys")).StatusCode);
    }

    [Fact]
    public async Task A_customers_last_three_surveys_come_from_all_their_sites()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var siteB = await CreateSiteAsync(admin, w.CustomerId, "Tower B");

        var otherCustomer = await CreateCustomerAsync(admin, "C2");
        var otherSite = await CreateSiteAsync(admin, otherCustomer, "Elsewhere");

        await CreateAsync(admin, w.SiteId, Body("NotOk", "2026-05-01"));
        await CreateAsync(admin, siteB, Body("Ok", "2026-05-10"));
        await CreateAsync(admin, w.SiteId, Body("Ok", "2026-05-20"));
        await CreateAsync(admin, siteB, Body("NotOk", "2026-05-25"));
        await CreateAsync(admin, otherSite, Body("Ok", "2026-05-30"));

        var recent = await admin.GetFromJsonAsync<List<SiteSurveyDto>>($"/api/customers/{w.CustomerId}/surveys", ApiFactory.Json);

        Assert.Equal(3, recent!.Count);
        Assert.Equal([new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 20), new DateOnly(2026, 5, 10)], recent.Select(s => s.SurveyedOn));
        Assert.Equal(["Tower B", "Tower A", "Tower B"], recent.Select(s => s.SiteName));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/customers/{Guid.NewGuid()}/surveys")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/customers/{w.CustomerId}/surveys")).StatusCode);
    }
}