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

    // A pumped draft order, which asks for a new survey.
    private static async Task<OrderDetailDto> RequestingOrderAsync(HttpClient c, OrderWorld w, string date = "2026-06-01")
    {
        var response = await c.PostAsJsonAsync("/api/orders",
            OrderBody(w, [LineBody(w.ProductId, 10m, null, "Slab", "Pump")], date: date));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderDetailDto>(ApiFactory.Json))!;
        Assert.True(order.Order.SurveyRequested);
        return order;
    }

    private static async Task<SiteSurveyDto> CreateAsync(HttpClient c, Guid orderId, object body)
    {
        var response = await c.PostAsJsonAsync($"/api/orders/{orderId}/surveys", body);
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

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_survey_needs_a_position_keeps_the_instructions_and_answers_the_orders_request()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        var technical = await factory.AddMemberAsync(company, "Technical");
        var order = await RequestingOrderAsync(admin, w);
        var url = $"/api/orders/{order.Order.Id}/surveys";

        var noPosition = await admin.PostAsJsonAsync(url, new { surveyedOn = "2026-05-30", result = "Ok" });
        var badLatitude = await admin.PostAsJsonAsync(url, Body(latitude: 95m));
        var tooPrecise = await admin.PostAsJsonAsync(url, Body(longitude: 90.12345678m));
        Assert.Equal(HttpStatusCode.BadRequest, noPosition.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLatitude.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);

        // A rep can read surveys but not make them. The technical team can.
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.PostAsJsonAsync(url, Body())).StatusCode);
        var survey = await CreateAsync(technical.Client, order.Order.Id, Body());

        Assert.Equal("Tower A", survey.SiteName);
        Assert.Equal(order.Order.Id, survey.OrderId);
        Assert.Equal(order.Order.Number, survey.OrderNumber);
        Assert.Equal(23.8103m, survey.Latitude);
        Assert.Equal(90.4125m, survey.Longitude);
        Assert.Equal(8m, survey.AccuracyMeters);
        Assert.Equal(40.5m, survey.PipingMetres);
        Assert.Equal("Beside the east gate", survey.PumpPosition);
        Assert.Equal("Ramp at the back lane", survey.TruckConnectionPoint);
        Assert.Equal("Turn left after the tall blue building", survey.LocationNote);
        Assert.Empty(survey.Images);

        // The order now relies on it and is no longer waiting.
        var after = (await admin.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{order.Order.Id}", ApiFactory.Json))!;
        Assert.Equal(survey.Id, after.Order.SurveyId);
        Assert.False(after.Order.SurveyRequested);

        var listed = Assert.Single((await ForSiteAsync(rep.Client, w.SiteId))!);
        Assert.Equal(survey.Id, listed.Id);
        Assert.Equal("Turn left after the tall blue building", listed.LocationNote);
    }

    [Fact]
    public async Task A_survey_is_recorded_only_for_an_open_request()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);

        // No request: a chute order needs no survey.
        var chute = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/api/orders/{chute.Order.Id}/surveys", Body()),
            HttpStatusCode.Conflict, "This order has not asked for a new survey. Request one on the order first.");

        // Answered once: a second survey for the same request is refused.
        var order = await RequestingOrderAsync(admin, w);
        await CreateAsync(admin, order.Order.Id, Body());
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/orders/{order.Order.Id}/surveys", Body())).StatusCode);

        // A cancelled order no longer needs one.
        var cancelled = await RequestingOrderAsync(admin, w);
        (await admin.PostAsJsonAsync($"/api/orders/{cancelled.Order.Id}/cancel", new { reason = "Customer called off" }))
            .EnsureSuccessStatusCode();
        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/api/orders/{cancelled.Order.Id}/surveys", Body()),
            HttpStatusCode.Conflict, "This order is Cancelled, so it no longer needs a survey.");

        // Another company's order is not found.
        var other = (await factory.CreateCompanyAsync()).Admin.Client;
        var open = await RequestingOrderAsync(admin, w);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/orders/{open.Order.Id}/surveys", Body())).StatusCode);
    }

    [Fact]
    public async Task The_work_queue_shows_requests_for_the_surveyors_plants_soonest_first()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var northPlant = await CreatePlantAsync(admin, "N1");
        var atNorth = w with { PlantId = northPlant };

        var later = await RequestingOrderAsync(admin, w, "2026-06-05");
        var sooner = await RequestingOrderAsync(admin, w, "2026-06-02");
        var north = await RequestingOrderAsync(admin, atNorth, "2026-06-01");
        await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 10m)]); // no request

        // A surveyor with no plants sees every request; one assigned to P1 sees only P1's.
        var anywhere = await factory.AddMemberAsync(company, "Technical");
        var p1Only = await factory.AddMemberAsync(company, "Technical");
        (await admin.PutAsJsonAsync($"/api/users/{p1Only.Id}/plants", new { plantIds = new[] { w.PlantId } }))
            .EnsureSuccessStatusCode();

        var all = (await anywhere.Client.GetFromJsonAsync<List<SurveyRequestDto>>("/api/survey-requests", ApiFactory.Json))!;
        Assert.Equal([north.Order.Id, sooner.Order.Id, later.Order.Id], all.Select(r => r.OrderId));

        var mine = (await p1Only.Client.GetFromJsonAsync<List<SurveyRequestDto>>("/api/survey-requests", ApiFactory.Json))!;
        Assert.Equal([sooner.Order.Id, later.Order.Id], mine.Select(r => r.OrderId));
        Assert.Equal("Tower A", mine[0].SiteName);
        Assert.Equal("P1", mine[0].PlantCode);
        Assert.True(mine[0].HasPumpLines);

        // The P1 surveyor cannot open or record the N1 request.
        Assert.Equal(HttpStatusCode.NotFound, (await p1Only.Client.GetAsync($"/api/survey-requests/{north.Order.Id}")).StatusCode);
        await AssertProblemAsync(
            await p1Only.Client.PostAsJsonAsync($"/api/orders/{north.Order.Id}/surveys", Body()),
            HttpStatusCode.Forbidden, "This order is for a plant you do not work for.");

        // Recording a request takes it off the queue.
        await CreateAsync(p1Only.Client, sooner.Order.Id, Body());
        mine = (await p1Only.Client.GetFromJsonAsync<List<SurveyRequestDto>>("/api/survey-requests", ApiFactory.Json))!;
        Assert.Equal([later.Order.Id], mine.Select(r => r.OrderId));

        // Only surveyors see the queue, and another company sees none of it.
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.GetAsync("/api/survey-requests")).StatusCode);
        var stranger = (await factory.CreateCompanyAsync()).Admin.Client;
        Assert.Empty((await stranger.GetFromJsonAsync<List<SurveyRequestDto>>("/api/survey-requests", ApiFactory.Json))!);
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
        var survey = await CreateAsync(admin, (await RequestingOrderAsync(admin, w)).Order.Id, Body());

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
}
