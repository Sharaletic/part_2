using Moq;
using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;
using SubscriptionService.Core.Services;
using SubscriptionService.Tests.Support;
using Xunit;

namespace SubscriptionService.Tests;

public class AuthServiceTests
{
    private const string Email = "test@example.com";
    private const string Password = "password123";

    private readonly FakeUserRepository _users = new();
    private readonly FakeClock _clock = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenGenerator> _tokens = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash:" + p);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((password, hash) => hash == "hash:" + password);
        _tokens.Setup(t => t.Generate(It.IsAny<User>())).Returns<User>(u => "token-for-" + u.Email);

        _sut = new AuthService(_users, _uow.Object, _hasher.Object, _tokens.Object, _clock);
    }

    private User AddUser(int failed = 0, DateTime? lockedUntil = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = Email,
            PasswordHash = "hash:" + Password,
            FailedLoginCount = failed,
            LockedUntil = lockedUntil
        };
        _users.Items.Add(user);
        return user;
    }

    // ---------- Register ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Name <a@b.com>")]
    public async Task Register_InvalidEmail_ThrowsBadRequest(string? email)
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.RegisterAsync(new RegisterRequest(email, Password)));
        Assert.Empty(_users.Items);
    }

    [Fact]
    public async Task Register_TooLongEmail_ThrowsBadRequest()
    {
        var email = new string('a', 250) + "@x.com";
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.RegisterAsync(new RegisterRequest(email, Password)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1234567")]
    public async Task Register_ShortPassword_ThrowsBadRequest(string? password)
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.RegisterAsync(new RegisterRequest(Email, password)));
        Assert.Empty(_users.Items);
    }

    [Fact]
    public async Task Register_EmailAlreadyTaken_ThrowsConflict()
    {
        AddUser();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _sut.RegisterAsync(new RegisterRequest(Email, Password)));

        Assert.Equal("email_taken", ex.Code);
        Assert.Single(_users.Items);
    }

    [Fact]
    public async Task Register_Valid_NormalizesEmailHashesPasswordAndSaves()
    {
        var result = await _sut.RegisterAsync(new RegisterRequest("  Test@Example.COM ", Password));

        Assert.Equal(Email, result.Email);
        var saved = Assert.Single(_users.Items);
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal("hash:" + Password, saved.PasswordHash);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    // ---------- Login ----------

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenWithoutExtraSave()
    {
        AddUser();

        var result = await _sut.LoginAsync(new LoginRequest(Email, Password));

        Assert.Equal("token-for-" + Email, result.AccessToken);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Theory]
    [InlineData("unknown@example.com")]
    [InlineData(null)]
    public async Task Login_UnknownEmail_ThrowsUnauthorized(string? email)
    {
        AddUser();
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _sut.LoginAsync(new LoginRequest(email, Password)));
    }

    [Fact]
    public async Task Login_NullPassword_ThrowsUnauthorized()
    {
        AddUser();
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _sut.LoginAsync(new LoginRequest(Email, null)));
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsFailedCounter()
    {
        var user = AddUser();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _sut.LoginAsync(new LoginRequest(Email, "wrong-password")));

        Assert.Equal(1, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Login_FifthWrongPassword_LocksAccountAndResetsCounter()
    {
        var user = AddUser(failed: AuthService.MaxFailedAttempts - 1);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _sut.LoginAsync(new LoginRequest(Email, "wrong-password")));

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Equal(_clock.UtcNow + AuthService.LockDuration, user.LockedUntil);
    }

    [Fact]
    public async Task Login_LockedAccount_ThrowsLockedEvenWithCorrectPassword()
    {
        var user = AddUser(failed: 2, lockedUntil: _clock.UtcNow.AddMinutes(5));

        await Assert.ThrowsAsync<LockedException>(
            () => _sut.LoginAsync(new LoginRequest(Email, Password)));

        Assert.Equal(2, user.FailedLoginCount);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Login_LockExpired_AllowsLoginAndClearsLock()
    {
        var user = AddUser(lockedUntil: _clock.UtcNow.AddMinutes(-1));

        var result = await _sut.LoginAsync(new LoginRequest(Email, Password));

        Assert.NotEmpty(result.AccessToken);
        Assert.Null(user.LockedUntil);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Login_SuccessAfterFailures_ResetsCounter()
    {
        var user = AddUser(failed: 3);

        await _sut.LoginAsync(new LoginRequest(Email, Password));

        Assert.Equal(0, user.FailedLoginCount);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }
}
