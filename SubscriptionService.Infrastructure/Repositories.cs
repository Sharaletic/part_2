using Microsoft.EntityFrameworkCore;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Infrastructure;

public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync() => db.SaveChangesAsync();
}

public class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id) => db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public Task<User?> GetByEmailAsync(string email) => db.Users.FirstOrDefaultAsync(u => u.Email == email);

    public void Add(User user) => db.Users.Add(user);

    public void Remove(User user) => db.Users.Remove(user);
}

public class SubscriptionRepository(AppDbContext db) : ISubscriptionRepository
{
    public Task<Subscription?> GetAsync(Guid userId, Guid id) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId && s.Id == id);

    public Task<List<Subscription>> ListAsync(Guid userId, SubscriptionStatus? status)
    {
        var query = db.Subscriptions.Where(s => s.UserId == userId);
        if (status is not null)
            query = query.Where(s => s.Status == status);
        return query.OrderBy(s => s.NextPaymentDate).ToListAsync();
    }

    public void Add(Subscription subscription) => db.Subscriptions.Add(subscription);

    public void Remove(Subscription subscription) => db.Subscriptions.Remove(subscription);
}

public class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetAsync(Guid userId, Guid id) =>
        db.Payments.FirstOrDefaultAsync(p => p.UserId == userId && p.Id == id);

    public Task<List<Payment>> ListAsync(Guid userId, DateOnly? dateFrom, DateOnly? dateTo, Guid? subscriptionId)
    {
        var query = db.Payments.Include(p => p.Subscription).Where(p => p.UserId == userId);
        if (dateFrom is not null)
            query = query.Where(p => p.PaidAt >= dateFrom);
        if (dateTo is not null)
            query = query.Where(p => p.PaidAt <= dateTo);
        if (subscriptionId is not null)
            query = query.Where(p => p.SubscriptionId == subscriptionId);
        return query.OrderByDescending(p => p.PaidAt).ToListAsync();
    }

    public void Add(Payment payment) => db.Payments.Add(payment);

    public void Remove(Payment payment) => db.Payments.Remove(payment);
}
