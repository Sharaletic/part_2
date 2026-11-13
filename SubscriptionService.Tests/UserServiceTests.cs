using Moq;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;
using SubscriptionService.Core.Services;
using SubscriptionService.Tests.Support;
using Xunit;

namespace SubscriptionService.Tests;

public class UserServiceTests
{
    private readonly FakeUserRepository _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly UserService _sut;

    public UserServiceTests() => _sut = new UserService(_users, _uow.Object);

    private User AddUser()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@example.com", PasswordHash = "hash" };
        _users.Items.Add(user);
        return user;
    }

    [Fact]
    public async Task Get_ExistingUser_ReturnsProfile()
    {
        var user = AddUser();

        var result = await _sut.GetAsync(user.Id);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task Get_UnknownUser_ThrowsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Guid.NewGuid()));

    [Fact]
    public async Task Delete_ExistingUser_RemovesAndSaves()
    {
        var user = AddUser();

        await _sut.DeleteAsync(user.Id);

        Assert.Empty(_users.Items);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Delete_UnknownUser_ThrowsNotFoundAndDoesNotSave()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(Guid.NewGuid()));
        _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
    }
}
