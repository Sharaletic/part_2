using System.Globalization;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Tests.Support;

/// <summary>Фиксированное время: 2026-11-13 10:00 UTC.</summary>
public class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}

public class FakeUserRepository : IUserRepository
{
    public List<User> Items { get; } = new();

    public Task<User?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByEmailAsync(string email) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Email == email));

    public void Add(User user) => Items.Add(user);

    public void Remove(User user) => Items.Remove(user);
}

public class FakeSubscriptionRepository : ISubscriptionRepository
{
    public List<Subscription> Items { get; } = new();

    public Task<Subscription?> GetAsync(Guid userId, Guid id) =>
        Task.FromResult(Items.FirstOrDefault(s => s.UserId == userId && s.Id == id));

    public Task<List<Subscription>> ListAsync(Guid userId, SubscriptionStatus? status) =>
        Task.FromResult(Items.Where(s => s.UserId == userId && (status == null || s.Status == status)).ToList());

    public void Add(Subscription subscription) => Items.Add(subscription);

    public void Remove(Subscription subscription) => Items.Remove(subscription);
}

public class FakePaymentRepository : IPaymentRepository
{
    public List<Payment> Items { get; } = new();

    public Task<Payment?> GetAsync(Guid userId, Guid id) =>
        Task.FromResult(Items.FirstOrDefault(p => p.UserId == userId && p.Id == id));

    public Task<List<Payment>> ListAsync(Guid userId, DateOnly? dateFrom, DateOnly? dateTo, Guid? subscriptionId) =>
        Task.FromResult(Items
            .Where(p => p.UserId == userId
                        && (dateFrom == null || p.PaidAt >= dateFrom)
                        && (dateTo == null || p.PaidAt <= dateTo)
                        && (subscriptionId == null || p.SubscriptionId == subscriptionId))
            .OrderByDescending(p => p.PaidAt)
            .ToList());

    public void Add(Payment payment) => Items.Add(payment);

    public void Remove(Payment payment) => Items.Remove(payment);
}

public static class TestData
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static DateOnly D(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Subscription Sub(
        Guid userId,
        string name = "Netflix",
        decimal amount = 100m,
        BillingPeriod period = BillingPeriod.Month,
        string next = "2026-11-20",
        SubscriptionStatus status = SubscriptionStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = name,
        Amount = amount,
        Period = period,
        StartDate = D("2026-01-01"),
        NextPaymentDate = D(next),
        Status = status
    };

    public static Payment Pay(
        Guid userId,
        decimal amount,
        string paidAt,
        Subscription? subscription = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Amount = amount,
        PaidAt = D(paidAt),
        SubscriptionId = subscription?.Id,
        Subscription = subscription
    };
}
