using Moq;
using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;
using SubscriptionService.Core.Services;
using SubscriptionService.Tests.Support;
using Xunit;
using static SubscriptionService.Tests.Support.TestData;

namespace SubscriptionService.Tests;

public class SubscriptionsServiceTests
{
    private readonly FakeSubscriptionRepository _repo = new();
    private readonly FakeClock _clock = new(); // сегодня 2026-11-13
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly SubscriptionsService _sut;

    public SubscriptionsServiceTests() => _sut = new SubscriptionsService(_repo, _uow.Object, _clock);

    private static SubscriptionRequest Valid =>
        new("Netflix", 999m, BillingPeriod.Month, D("2026-11-20"), "Кино", "заметка");

    public static TheoryData<SubscriptionRequest> InvalidRequests => new()
    {
        Valid with { Name = null },
        Valid with { Name = "   " },
        Valid with { Amount = null },
        Valid with { Amount = 0m },
        Valid with { Amount = -5m },
        Valid with { Period = null },
        Valid with { StartDate = null }
    };

    private Subscription Add(
        string name = "Netflix",
        decimal amount = 100m,
        BillingPeriod period = BillingPeriod.Month,
        string next = "2026-11-20",
        SubscriptionStatus status = SubscriptionStatus.Active,
        Guid? userId = null)
    {
        var sub = Sub(userId ?? UserId, name, amount, period, next, status);
        _repo.Items.Add(sub);
        return sub;
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_StartInFuture_NextPaymentEqualsStart()
    {
        var result = await _sut.CreateAsync(UserId, Valid);

        Assert.Equal(D("2026-11-20"), result.NextPaymentDate);
        Assert.Equal(SubscriptionStatus.Active, result.Status);
        var saved = Assert.Single(_repo.Items);
        Assert.Equal(UserId, saved.UserId);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Create_StartInPast_CalculatesNextFutureDate()
    {
        var result = await _sut.CreateAsync(UserId, Valid with { StartDate = D("2026-11-01") });

        Assert.Equal(D("2026-12-01"), result.NextPaymentDate);
    }

    [Fact]
    public async Task Create_TrimsAndRoundsFields()
    {
        var result = await _sut.CreateAsync(UserId,
            Valid with { Name = "  Netflix ", Amount = 99.999m, Category = " Кино ", Note = "   " });

        Assert.Equal("Netflix", result.Name);
        Assert.Equal(100.00m, result.Amount);
        Assert.Equal("Кино", result.Category);
        Assert.Null(result.Note);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_InvalidRequest_ThrowsBadRequest(SubscriptionRequest request)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(UserId, request));
        Assert.Empty(_repo.Items);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    // ---------- List / Get ----------

    [Fact]
    public async Task List_ReturnsOnlyOwnSubscriptionsFilteredByStatus()
    {
        Add("A");
        Add("B", status: SubscriptionStatus.Paused);
        Add("C", userId: OtherUserId);

        var all = await _sut.ListAsync(UserId, null);
        var paused = await _sut.ListAsync(UserId, SubscriptionStatus.Paused);

        Assert.Equal(2, all.Count);
        Assert.Equal("B", Assert.Single(paused).Name);
    }

    [Fact]
    public async Task Get_Own_ReturnsSubscription()
    {
        var sub = Add();

        var result = await _sut.GetAsync(UserId, sub.Id);

        Assert.Equal(sub.Id, result.Id);
    }

    [Fact]
    public async Task Get_AnotherUsersSubscription_ThrowsNotFound()
    {
        var sub = Add(userId: OtherUserId);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(UserId, sub.Id));
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_NotFound_Throws() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdateAsync(UserId, Guid.NewGuid(), Valid));

    [Fact]
    public async Task Update_InvalidRequest_ThrowsBadRequest()
    {
        var sub = Add();

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.UpdateAsync(UserId, sub.Id, Valid with { Amount = 0m }));
    }

    [Fact]
    public async Task Update_ScheduleUnchanged_KeepsNextPaymentDate()
    {
        var sub = Add(next: "2027-01-01");
        sub.StartDate = D("2026-11-20");

        var result = await _sut.UpdateAsync(UserId, sub.Id, Valid with { Name = "Netflix Premium", Amount = 1299m });

        Assert.Equal("Netflix Premium", result.Name);
        Assert.Equal(1299m, result.Amount);
        Assert.Equal(D("2027-01-01"), result.NextPaymentDate);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Update_PeriodChanged_RecalculatesNextPaymentDate()
    {
        var sub = Add(next: "2027-01-01");
        sub.StartDate = D("2026-11-01");

        var result = await _sut.UpdateAsync(UserId, sub.Id,
            Valid with { Period = BillingPeriod.Year, StartDate = D("2026-11-01") });

        Assert.Equal(D("2027-11-01"), result.NextPaymentDate);
    }

    [Fact]
    public async Task Update_StartDateChanged_RecalculatesNextPaymentDate()
    {
        var sub = Add(next: "2027-01-01");
        sub.StartDate = D("2026-11-01");

        var result = await _sut.UpdateAsync(UserId, sub.Id, Valid with { StartDate = D("2026-11-25") });

        Assert.Equal(D("2026-11-25"), result.NextPaymentDate);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_Own_RemovesAndSaves()
    {
        var sub = Add();

        await _sut.DeleteAsync(UserId, sub.Id);

        Assert.Empty(_repo.Items);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Delete_NotFound_Throws() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(UserId, Guid.NewGuid()));

    // ---------- Pause / Resume / Cancel ----------

    [Fact]
    public async Task Pause_Active_BecomesPaused()
    {
        var sub = Add();

        var result = await _sut.PauseAsync(UserId, sub.Id);

        Assert.Equal(SubscriptionStatus.Paused, result.Status);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Paused)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task Pause_NotActive_ThrowsConflict(SubscriptionStatus status)
    {
        var sub = Add(status: status);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _sut.PauseAsync(UserId, sub.Id));

        Assert.Equal("invalid_status", ex.Code);
    }

    [Theory]
    [InlineData(BillingPeriod.Week, "2026-11-20")]
    [InlineData(BillingPeriod.Month, "2026-12-13")]
    [InlineData(BillingPeriod.Year, "2027-11-13")]
    public async Task Resume_Paused_BecomesActiveAndNextDateIsTodayPlusPeriod(BillingPeriod period, string expected)
    {
        var sub = Add(period: period, status: SubscriptionStatus.Paused);

        var result = await _sut.ResumeAsync(UserId, sub.Id);

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.Equal(D(expected), result.NextPaymentDate);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task Resume_NotPaused_ThrowsConflict(SubscriptionStatus status)
    {
        var sub = Add(status: status);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ResumeAsync(UserId, sub.Id));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Paused)]
    public async Task Cancel_ActiveOrPaused_BecomesCancelled(SubscriptionStatus status)
    {
        var sub = Add(status: status);

        var result = await _sut.CancelAsync(UserId, sub.Id);

        Assert.Equal(SubscriptionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ThrowsConflict()
    {
        var sub = Add(status: SubscriptionStatus.Cancelled);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CancelAsync(UserId, sub.Id));
    }

    [Fact]
    public async Task StatusChange_NotFound_Throws() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.PauseAsync(UserId, Guid.NewGuid()));

    // ---------- Upcoming ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SubscriptionsService.MaxUpcomingDays + 1)]
    public async Task Upcoming_DaysOutOfRange_ThrowsBadRequest(int days) =>
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpcomingAsync(UserId, days));

    [Fact]
    public async Task Upcoming_ReturnsActivePaymentsInWindowSortedByDate()
    {
        Add("Today", next: "2026-11-13");        // сегодня: входит
        Add("Boundary", next: "2026-11-20");     // последний день окна: входит
        Add("Middle", next: "2026-11-16");
        Add("TooLate", next: "2026-11-21");      // за окном
        Add("Overdue", next: "2026-11-12");      // просрочена: не входит
        Add("Paused", next: "2026-11-15", status: SubscriptionStatus.Paused);
        Add("Foreign", next: "2026-11-15", userId: OtherUserId);

        var result = await _sut.UpcomingAsync(UserId, 7);

        Assert.Equal(new[] { "Today", "Middle", "Boundary" }, result.Select(s => s.Name));
    }

    [Fact]
    public async Task Upcoming_MaxDaysAccepted()
    {
        var result = await _sut.UpcomingAsync(UserId, SubscriptionsService.MaxUpcomingDays);

        Assert.Empty(result);
    }
}
