using SubscriptionService.Core.Entities;

namespace SubscriptionService.Core.Dtos;

// ---------- Auth / Users ----------

public record RegisterRequest(string? Email, string? Password);

public record LoginRequest(string? Email, string? Password);

public record TokenResponse(string AccessToken);

public record UserResponse(Guid Id, string Email);

// ---------- Subscriptions ----------

public record SubscriptionRequest(
    string? Name,
    decimal? Amount,
    BillingPeriod? Period,
    DateOnly? StartDate,
    string? Category,
    string? Note);

public record SubscriptionResponse(
    Guid Id,
    string Name,
    decimal Amount,
    BillingPeriod Period,
    DateOnly StartDate,
    DateOnly NextPaymentDate,
    SubscriptionStatus Status,
    string? Category,
    string? Note)
{
    public static SubscriptionResponse From(Subscription s) =>
        new(s.Id, s.Name, s.Amount, s.Period, s.StartDate, s.NextPaymentDate, s.Status, s.Category, s.Note);
}

// ---------- Payments ----------

public record PaymentRequest(Guid? SubscriptionId, decimal? Amount, DateOnly? PaidAt);

public record PaymentResponse(Guid Id, Guid? SubscriptionId, decimal Amount, DateOnly PaidAt)
{
    public static PaymentResponse From(Payment p) => new(p.Id, p.SubscriptionId, p.Amount, p.PaidAt);
}

// ---------- Analytics ----------

public record SummaryGroup(string Name, decimal Amount, decimal Share);

public record SummaryResponse(decimal Total, List<SummaryGroup> Groups);

public record ForecastItem(string Month, decimal Amount);

public record TopItem(Guid Id, string Name, decimal MonthlyCost);
