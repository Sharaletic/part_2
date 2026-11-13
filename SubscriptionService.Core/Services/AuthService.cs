using System.Net.Mail;
using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Core.Services;

public class AuthService(
    IUserRepository users,
    IUnitOfWork uow,
    IPasswordHasher hasher,
    ITokenGenerator tokens,
    IClock clock)
{
    public const int MaxFailedAttempts = 5;
    public const int MinPasswordLength = 8;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public async Task<UserResponse> RegisterAsync(RegisterRequest request)
    {
        var email = Normalize(request.Email);
        if (!IsValidEmail(email))
            throw new BadRequestException("Email has invalid format.");

        var password = request.Password ?? "";
        if (password.Length < MinPasswordLength)
            throw new BadRequestException($"Password must be at least {MinPasswordLength} characters.");

        if (await users.GetByEmailAsync(email) is not null)
            throw new ConflictException("email_taken", "Email is already registered.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hasher.Hash(password)
        };
        users.Add(user);
        await uow.SaveChangesAsync();

        return new UserResponse(user.Id, user.Email);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request)
    {
        var user = await users.GetByEmailAsync(Normalize(request.Email));
        if (user is null)
            throw new UnauthorizedException();

        var now = clock.UtcNow;
        if (user.LockedUntil is { } lockedUntil && lockedUntil > now)
            throw new LockedException();

        if (!hasher.Verify(request.Password ?? "", user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntil = now + LockDuration;
                user.FailedLoginCount = 0;
            }
            await uow.SaveChangesAsync();
            throw new UnauthorizedException();
        }

        if (user.FailedLoginCount != 0 || user.LockedUntil is not null)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            await uow.SaveChangesAsync();
        }

        return new TokenResponse(tokens.Generate(user));
    }

    private static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= 254
        && MailAddress.TryCreate(email, out var address)
        && address.Address == email;
}
