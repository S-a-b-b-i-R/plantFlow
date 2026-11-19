using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Planning;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class SurveyApprovalTests(ApiFactory factory)
{
    // A pumped draft order, which asks for a new survey.
    private static async Task<OrderDetailDto> RequestingOrderAsync(HttpClient c, OrderWorld w)
    {
        var response = await c.PostAsJsonAsync("/api/orders", OrderBody(w, [LineBody(w.ProductId, 10m, null, "Slab", "Pump")]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(ApiFactory.Json))!;
    }

    private static Task<SurveyApprovalsDto?> ApprovalsAsync(HttpClient c) =>
        c.GetFromJsonAsync<SurveyApprovalsDto>("/api/survey-approvals", ApiFactory.Json);

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient c, Guid surveyId) =>
        c.PostAsync($"/api/surveys/{surveyId}/approve", null);

    private static Task<HttpResponseMessage> SendBackAsync(HttpClient c, Guid surveyId, string reason) =>
        c.PostAsJsonAsync($"/api/surveys/{surveyId}/send-back", new { reason });

    private static Task<OrderDetailDto?> OrderAsync(HttpClient c, Guid orderId) =>
        c.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{orderId}", ApiFactory.Json);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_plant_manager_approves_surveys_for_their_own_plants_and_admin_for_all()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var atNorth = w with { PlantId = await CreatePlantAsync(admin, "N1") };

        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        (await admin.PutAsJsonAsync($"/api/users/{manager.Id}/plants", new { plantIds = new[] { w.PlantId } }))
            .EnsureSuccessStatusCode();
        var unassigned = await factory.AddMemberAsync(company, "Plant Manager");

        var south = await RecordSurveyAsync(admin, (await RequestingOrderAsync(admin, w)).Order.Id, approve: false);
        var north = await RecordSurveyAsync(admin, (await RequestingOrderAsync(admin, atNorth)).Order.Id, approve: false);
        Assert.Equal(SurveyStatus.Pending, south.Status);

        // Each approver sees only their plants. One with no plants sees nothing and is told why.
        var mine = (await ApprovalsAsync(manager.Client))!;
        Assert.True(mine.ApprovesForAnyPlant);
        var item = Assert.Single(mine.Surveys);
        Assert.Equal(south.Id, item.Survey.Id);
        Assert.Equal("P1", item.PlantCode);

        var none = (await ApprovalsAsync(unassigned.Client))!;
        Assert.False(none.ApprovesForAnyPlant);
        Assert.Empty(none.Surveys);
        await AssertProblemAsync(await ApproveAsync(unassigned.Client, south.Id),
            HttpStatusCode.Forbidden, "You do not approve surveys for this order's plant.");

        Assert.Equal([south.Id, north.Id], (await ApprovalsAsync(admin))!.Surveys.Select(s => s.Survey.Id));

        // The P1 manager cannot approve the N1 survey.
        await AssertProblemAsync(await ApproveAsync(manager.Client, north.Id),
            HttpStatusCode.Forbidden, "You do not approve surveys for this order's plant.");

        var approved = await ApproveAsync(manager.Client, south.Id);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var dto = (await approved.Content.ReadFromJsonAsync<SiteSurveyDto>(ApiFactory.Json))!;
        Assert.Equal(SurveyStatus.Approved, dto.Status);
        Assert.Equal(manager.Id, dto.ReviewedBy);
        Assert.NotNull(dto.ReviewedAtUtc);
        Assert.Empty((await ApprovalsAsync(manager.Client))!.Surveys);

        // A reviewed survey is never reviewed again.
        await AssertProblemAsync(await ApproveAsync(admin, south.Id), HttpStatusCode.Conflict, "This survey was already reviewed.");
        await AssertProblemAsync(await SendBackAsync(admin, south.Id, "Wrong gate"),
            HttpStatusCode.Conflict, "This survey was already reviewed.");

        // Admin may approve a survey they recorded themselves.
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(admin, north.Id)).StatusCode);

        // Surveyors and reps do not approve. Another company sees none of it.
        var surveyor = await factory.AddMemberAsync(company, "Technical");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        Assert.Equal(HttpStatusCode.Forbidden, (await surveyor.Client.GetAsync("/api/survey-approvals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(rep.Client, north.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendBackAsync(rep.Client, north.Id, "No")).StatusCode);

        var stranger = (await factory.CreateCompanyAsync()).Admin.Client;
        var pending = await RecordSurveyAsync(admin, (await RequestingOrderAsync(admin, w)).Order.Id, approve: false);
        Assert.Empty((await ApprovalsAsync(stranger))!.Surveys);
        Assert.Equal(HttpStatusCode.NotFound, (await ApproveAsync(stranger, pending.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendBackAsync(stranger, pending.Id, "Not ours")).StatusCode);
    }

    [Fact]
    public async Task Sending_a_survey_back_returns_the_order_to_the_work_queue_with_the_reason()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await RequestingOrderAsync(admin, w);
        var first = await RecordSurveyAsync(admin, order.Order.Id, approve: false);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendBackAsync(admin, first.Id, "no")).StatusCode);

        var sent = await SendBackAsync(admin, first.Id, "  The pump position is missing  ");
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var rejected = (await sent.Content.ReadFromJsonAsync<SiteSurveyDto>(ApiFactory.Json))!;
        Assert.Equal(SurveyStatus.Rejected, rejected.Status);
        Assert.Equal("The pump position is missing", rejected.ReviewComment);

        // The order asks again, and the surveyor sees why.
        var reopened = (await OrderAsync(admin, order.Order.Id))!;
        Assert.True(reopened.Order.SurveyRequested);
        Assert.Null(reopened.Order.SurveyId);
        var queued = Assert.Single((await admin.GetFromJsonAsync<List<SurveyRequestDto>>("/api/survey-requests", ApiFactory.Json))!);
        Assert.Equal(order.Order.Id, queued.OrderId);
        Assert.Equal("The pump position is missing", queued.SentBackReason);
        Assert.Empty((await ApprovalsAsync(admin))!.Surveys);

        // A new survey replaces it. The sent-back one stays as history and cannot be tagged.
        var second = await RecordSurveyAsync(admin, order.Order.Id);
        Assert.Equal(second.Id, (await OrderAsync(admin, order.Order.Id))!.Order.SurveyId);
        var history = (await admin.GetFromJsonAsync<List<SiteSurveyDto>>($"/api/sites/{w.SiteId}/surveys", ApiFactory.Json))!;
        Assert.Equal(SurveyStatus.Rejected, history.Single(s => s.Id == first.Id).Status);
        Assert.Equal(SurveyStatus.Approved, history.Single(s => s.Id == second.Id).Status);

        var other = await RequestingOrderAsync(admin, w);
        await AssertProblemAsync(await TagSurveyAsync(admin, other.Order.Id, first.Id),
            HttpStatusCode.BadRequest, "Tag one of this site's last three approved surveys.");
    }

    [Fact]
    public async Task Other_orders_tag_only_approved_surveys_while_the_requesting_order_keeps_its_own()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var asking = await RequestingOrderAsync(admin, w);
        var survey = await RecordSurveyAsync(admin, asking.Order.Id, approve: false);
        var other = await RequestingOrderAsync(admin, w);

        await AssertProblemAsync(await TagSurveyAsync(admin, other.Order.Id, survey.Id),
            HttpStatusCode.BadRequest, "Tag one of this site's last three approved surveys.");

        // The order that asked for it keeps it while it waits.
        Assert.True((await TagSurveyAsync(admin, asking.Order.Id, survey.Id)).IsSuccessStatusCode);
        Assert.Equal(survey.Id, (await OrderAsync(admin, asking.Order.Id))!.Order.SurveyId);

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(admin, survey.Id)).StatusCode);
        Assert.True((await TagSurveyAsync(admin, other.Order.Id, survey.Id)).IsSuccessStatusCode);
        Assert.Equal(survey.Id, (await OrderAsync(admin, other.Order.Id))!.Order.SurveyId);
    }
}
