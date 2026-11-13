using SubscriptionService.Core.Entities;

namespace SubscriptionService.Core.Interfaces;

public interface IUnitOfWork
{
    /// <summary>Сохраняет все изменения одной транзакцией.</summary>
    Task SaveChangesAsync();
}

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenGenerator
{
    string Generate(User user);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    void Add(User user);
    void Remove(User user);
}

public interface ISubscriptionRepository
{
    Task<Subscription?> GetAsync(Guid userId, Guid id);
    Task<List<Subscription>> ListAsync(Guid userId, SubscriptionStatus? status);
    void Add(Subscription subscription);
    void Remove(Subscription subscription);
}

public interface IPaymentRepository
{
    Task<Payment?> GetAsync(Guid userId, Guid id);
    Task<List<Payment>> ListAsync(Guid userId, DateOnly? dateFrom, DateOnly? dateTo, Guid? subscriptionId);
    void Add(Payment payment);
    void Remove(Payment payment);
}
