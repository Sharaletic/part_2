using Moq;
using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;
using SubscriptionService.Core.Services;
using SubscriptionService.Tests.Support;
using SubscriptionService.Core.Entities;
using Xunit;
using static SubscriptionService.Tests.Support.TestData;

namespace SubscriptionService.Tests;

public class PaymentsServiceTests
{
    private readonly FakePaymentRepository _payments = new();
    private readonly FakeSubscriptionRepository _subscriptions = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly PaymentsService _sut;

    public PaymentsServiceTests() => _sut = new PaymentsService(_payments, _subscriptions, _uow.Object);

    // ---------- Create ----------

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Create_InvalidAmount_ThrowsBadRequest(int? amount)
    {
        decimal? value = amount;

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.CreateAsync(UserId, new PaymentRequest(null, value, D("2026-11-13"))));

        Assert.Empty(_payments.Items);
    }

    [Fact]
    public async Task Create_MissingPaidAt_ThrowsBadRequest() =>
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.CreateAsync(UserId, new PaymentRequest(null, 100m, null)));

    [Fact]
    public async Task Create_WithoutSubscription_SavesPaymentWithoutLink()
    {
        var result = await _sut.CreateAsync(UserId, new PaymentRequest(null, 99.999m, D("2026-11-13")));

        Assert.Null(result.SubscriptionId);
        Assert.Equal(100.00m, result.Amount);
        Assert.Equal(D("2026-11-13"), result.PaidAt);
        var saved = Assert.Single(_payments.Items);
        Assert.Equal(UserId, saved.UserId);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Create_WithSubscription_ShiftsNextPaymentDateByPeriod()
    {
        var sub = Sub(UserId, period: BillingPeriod.Month, next: "2026-11-20");
        _subscriptions.Items.Add(sub);

        var result = await _sut.CreateAsync(UserId, new PaymentRequest(sub.Id, 100m, D("2026-11-20")));

        Assert.Equal(sub.Id, result.SubscriptionId);
        Assert.Equal(D("2026-12-20"), sub.NextPaymentDate);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Create_SubscriptionNotFound_ThrowsAndSavesNothing()
    {
        var foreign = Sub(OtherUserId);
        _subscriptions.Items.Add(foreign);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CreateAsync(UserId, new PaymentRequest(foreign.Id, 100m, D("2026-11-13"))));

        Assert.Empty(_payments.Items);
        Assert.Equal(D("2026-11-20"), foreign.NextPaymentDate);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    // ---------- List ----------

    private (Subscription Sub, Payment P1, Payment P2, Payment P3) Seed()
    {
        var sub = Sub(UserId);
        _subscriptions.Items.Add(sub);
        var p1 = Pay(UserId, 100m, "2026-11-01", sub);
        var p2 = Pay(UserId, 200m, "2026-11-10");
        var p3 = Pay(UserId, 300m, "2026-11-20", sub);
        _payments.Items.AddRange(new[] { p1, p2, p3, Pay(OtherUserId, 999m, "2026-11-10") });
        return (sub, p1, p2, p3);
    }

    [Fact]
    public async Task List_NoFilters_ReturnsOnlyOwnPaymentsNewestFirst()
    {
        var (_, p1, p2, p3) = Seed();

        var result = await _sut.ListAsync(UserId, null, null, null);

        Assert.Equal(new[] { p3.Id, p2.Id, p1.Id }, result.Select(p => p.Id));
    }

    [Fact]
    public async Task List_DateRangeAndSubscriptionFilters()
    {
        var (sub, p1, p2, p3) = Seed();

        var from = await _sut.ListAsync(UserId, D("2026-11-05"), null, null);
        var to = await _sut.ListAsync(UserId, null, D("2026-11-10"), null);
        var bySub = await _sut.ListAsync(UserId, null, null, sub.Id);

        Assert.Equal(new[] { p3.Id, p2.Id }, from.Select(p => p.Id));
        Assert.Equal(new[] { p2.Id, p1.Id }, to.Select(p => p.Id));
        Assert.Equal(new[] { p3.Id, p1.Id }, bySub.Select(p => p.Id));
    }

    [Fact]
    public async Task List_FromEqualsTo_IsAllowed()
    {
        var (_, _, p2, _) = Seed();

        var result = await _sut.ListAsync(UserId, D("2026-11-10"), D("2026-11-10"), null);

        Assert.Equal(p2.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task List_FromAfterTo_ThrowsBadRequest() =>
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.ListAsync(UserId, D("2026-11-11"), D("2026-11-10"), null));

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_Own_RemovesAndSaves()
    {
        var p = Pay(UserId, 100m, "2026-11-01");
        _payments.Items.Add(p);

        await _sut.DeleteAsync(UserId, p.Id);

        Assert.Empty(_payments.Items);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Delete_AnotherUsersPayment_ThrowsNotFound()
    {
        var p = Pay(OtherUserId, 100m, "2026-11-01");
        _payments.Items.Add(p);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(UserId, p.Id));

        Assert.Single(_payments.Items);
    }
}
