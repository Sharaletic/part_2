using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Services;

namespace SubscriptionService.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
}

[Route("api/v1/auth")]
public class AuthController(AuthService auth) : ApiControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = await auth.RegisterAsync(request);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request) =>
        await auth.LoginAsync(request);
}

[Authorize]
[Route("api/v1/users")]
public class UsersController(UserService users) : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me() => await users.GetAsync(UserId);

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe()
    {
        await users.DeleteAsync(UserId);
        return NoContent();
    }
}

[Authorize]
[Route("api/v1/subscriptions")]
public class SubscriptionsController(SubscriptionsService service) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(SubscriptionRequest request)
    {
        var created = await service.CreateAsync(UserId, request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<List<SubscriptionResponse>>> List([FromQuery] SubscriptionStatus? status) =>
        await service.ListAsync(UserId, status);

    // "upcoming" объявлен до "{id:guid}"; ограничение guid исключает конфликт маршрутов
    [HttpGet("upcoming")]
    public async Task<ActionResult<List<SubscriptionResponse>>> Upcoming([FromQuery] int days = 7) =>
        await service.UpcomingAsync(UserId, days);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SubscriptionResponse>> Get(Guid id) =>
        await service.GetAsync(UserId, id);

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SubscriptionResponse>> Update(Guid id, SubscriptionRequest request) =>
        await service.UpdateAsync(UserId, id, request);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await service.DeleteAsync(UserId, id);
        return NoContent();
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<SubscriptionResponse>> Pause(Guid id) =>
        await service.PauseAsync(UserId, id);

    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<SubscriptionResponse>> Resume(Guid id) =>
        await service.ResumeAsync(UserId, id);

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<SubscriptionResponse>> Cancel(Guid id) =>
        await service.CancelAsync(UserId, id);
}

[Authorize]
[Route("api/v1/payments")]
public class PaymentsController(PaymentsService service) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(PaymentRequest request)
    {
        var created = await service.CreateAsync(UserId, request);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet]
    public async Task<ActionResult<List<PaymentResponse>>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery(Name = "subscription_id")] Guid? subscriptionId) =>
        await service.ListAsync(UserId, from, to, subscriptionId);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await service.DeleteAsync(UserId, id);
        return NoContent();
    }
}

[Authorize]
[Route("api/v1/analytics")]
public class AnalyticsController(AnalyticsService service) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<SummaryResponse>> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        await service.SummaryAsync(UserId, from, to);

    [HttpGet("forecast")]
    public async Task<ActionResult<List<ForecastItem>>> Forecast([FromQuery] int months = 3) =>
        await service.ForecastAsync(UserId, months);

    [HttpGet("top")]
    public async Task<ActionResult<List<TopItem>>> Top([FromQuery] int limit = 5) =>
        await service.TopAsync(UserId, limit);
}
