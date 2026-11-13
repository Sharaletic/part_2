using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Core.Services;

public class SubscriptionsService(ISubscriptionRepository subscriptions, IUnitOfWork uow, IClock clock)
{
    public const int MaxUpcomingDays = 365;

    public async Task<SubscriptionResponse> CreateAsync(Guid userId, SubscriptionRequest request)
    {
        var (name, amount, period, start) = Validate(request);

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Amount = amount,
            Period = period,
            StartDate = start,
            NextPaymentDate = PeriodCalculator.NextPaymentDate(start, period, clock.Today),
            Status = SubscriptionStatus.Active,
            Category = Clean(request.Category),
            Note = Clean(request.Note)
        };
        subscriptions.Add(subscription);
        await uow.SaveChangesAsync();

        return SubscriptionResponse.From(subscription);
    }

    public async Task<List<SubscriptionResponse>> ListAsync(Guid userId, SubscriptionStatus? status)
    {
        var list = await subscriptions.ListAsync(userId, status);
        return list.Select(SubscriptionResponse.From).ToList();
    }

    public async Task<SubscriptionResponse> GetAsync(Guid userId, Guid id) =>
        SubscriptionResponse.From(await GetOwnedAsync(userId, id));

    public async Task<SubscriptionResponse> UpdateAsync(Guid userId, Guid id, SubscriptionRequest request)
    {
        var subscription = await GetOwnedAsync(userId, id);
        var (name, amount, period, start) = Validate(request);

        var scheduleChanged = subscription.Period != period || subscription.StartDate != start;

        subscription.Name = name;
        subscription.Amount = amount;
        subscription.Period = period;
        subscription.StartDate = start;
        subscription.Category = Clean(request.Category);
        subscription.Note = Clean(request.Note);

        if (scheduleChanged)
            subscription.NextPaymentDate = PeriodCalculator.NextPaymentDate(start, period, clock.Today);

        await uow.SaveChangesAsync();
        return SubscriptionResponse.From(subscription);
    }

    public async Task DeleteAsync(Guid userId, Guid id)
    {
        var subscription = await GetOwnedAsync(userId, id);
        subscriptions.Remove(subscription);
        await uow.SaveChangesAsync();
    }

    public async Task<SubscriptionResponse> PauseAsync(Guid userId, Guid id)
    {
        var subscription = await GetOwnedAsync(userId, id);
        if (subscription.Status != SubscriptionStatus.Active)
            throw new ConflictException("invalid_status", "Only an active subscription can be paused.");

        subscription.Status = SubscriptionStatus.Paused;
        await uow.SaveChangesAsync();
        return SubscriptionResponse.From(subscription);
    }

    /// <summary>Возобновление: следующее списание = сегодня + один период.</summary>
    public async Task<SubscriptionResponse> ResumeAsync(Guid userId, Guid id)
    {
        var subscription = await GetOwnedAsync(userId, id);
        if (subscription.Status != SubscriptionStatus.Paused)
            throw new ConflictException("invalid_status", "Only a paused subscription can be resumed.");

        subscription.Status = SubscriptionStatus.Active;
        subscription.NextPaymentDate = PeriodCalculator.Add(clock.Today, subscription.Period);
        await uow.SaveChangesAsync();
        return SubscriptionResponse.From(subscription);
    }

    public async Task<SubscriptionResponse> CancelAsync(Guid userId, Guid id)
    {
        var subscription = await GetOwnedAsync(userId, id);
        if (subscription.Status == SubscriptionStatus.Cancelled)
            throw new ConflictException("invalid_status", "Subscription is already cancelled.");

        subscription.Status = SubscriptionStatus.Cancelled;
        await uow.SaveChangesAsync();
        return SubscriptionResponse.From(subscription);
    }

    /// <summary>Активные подписки со списанием в ближайшие days дней (включая сегодня).</summary>
    public async Task<List<SubscriptionResponse>> UpcomingAsync(Guid userId, int days)
    {
        if (days < 1 || days > MaxUpcomingDays)
            throw new BadRequestException($"days must be between 1 and {MaxUpcomingDays}.");

        var today = clock.Today;
        var until = today.AddDays(days);

        var active = await subscriptions.ListAsync(userId, SubscriptionStatus.Active);
        return active
            .Where(s => s.NextPaymentDate >= today && s.NextPaymentDate <= until)
            .OrderBy(s => s.NextPaymentDate)
            .Select(SubscriptionResponse.From)
            .ToList();
    }

    private async Task<Subscription> GetOwnedAsync(Guid userId, Guid id) =>
        await subscriptions.GetAsync(userId, id)
        ?? throw new NotFoundException("Subscription not found.");

    private static (string Name, decimal Amount, BillingPeriod Period, DateOnly Start) Validate(SubscriptionRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new BadRequestException("Name is required.");
        if (request.Amount is not > 0)
            throw new BadRequestException("Amount must be greater than zero.");
        if (request.Period is null)
            throw new BadRequestException("Period is required (week, month or year).");
        if (request.StartDate is null)
            throw new BadRequestException("Start date is required.");

        return (name, PeriodCalculator.Round(request.Amount.Value), request.Period.Value, request.StartDate.Value);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
