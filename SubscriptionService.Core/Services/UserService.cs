using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Core.Services;

public class UserService(IUserRepository users, IUnitOfWork uow)
{
    public async Task<UserResponse> GetAsync(Guid userId)
    {
        var user = await users.GetByIdAsync(userId)
                   ?? throw new NotFoundException("User not found.");
        return new UserResponse(user.Id, user.Email);
    }

    /// <summary>Удаляет пользователя; подписки и платежи удаляются каскадом на уровне БД.</summary>
    public async Task DeleteAsync(Guid userId)
    {
        var user = await users.GetByIdAsync(userId)
                   ?? throw new NotFoundException("User not found.");
        users.Remove(user);
        await uow.SaveChangesAsync();
    }
}
