using VideoRental.Rentals.Domain.Constants;
using VideoRental.Rentals.Domain.Entities;

namespace VideoRental.Rentals.Tests;

/// <summary>
/// Тести доменних правил, що не потребують ні бази, ні моків:
/// карта допустимих переходів статусів і обчислювані властивості сутностей.
/// </summary>
public class RentalStatusTests
{
    [Theory]
    [InlineData(RentalStatus.Submitted,          RentalStatus.DiscsConfirmed,    true)]
    [InlineData(RentalStatus.Submitted,          RentalStatus.AwaitingValidation, true)]
    [InlineData(RentalStatus.Submitted,          RentalStatus.Cancelled,          true)]
    [InlineData(RentalStatus.AwaitingValidation, RentalStatus.DiscsConfirmed,    true)]
    [InlineData(RentalStatus.DiscsConfirmed,    RentalStatus.PickedUp,             true)]
    [InlineData(RentalStatus.DiscsConfirmed,    RentalStatus.Cancelled,          true)]
    [InlineData(RentalStatus.PickedUp,             RentalStatus.Returned,           true)]
    public void AllowedTransitions_AreAccepted(string from, string to, bool expected)
        => Assert.Equal(expected, RentalStatus.CanTransition(from, to));

    [Theory]
    // фільми вже на руках — скасувати не можна
    [InlineData(RentalStatus.PickedUp,    RentalStatus.Cancelled)]
    // видати можна лише після підтвердження дисків
    [InlineData(RentalStatus.Submitted, RentalStatus.PickedUp)]
    // завершені статуси — термінальні
    [InlineData(RentalStatus.Returned,  RentalStatus.PickedUp)]
    [InlineData(RentalStatus.Returned,  RentalStatus.Cancelled)]
    [InlineData(RentalStatus.Cancelled, RentalStatus.PickedUp)]
    [InlineData(RentalStatus.Cancelled, RentalStatus.DiscsConfirmed)]
    public void ForbiddenTransitions_AreRejected(string from, string to)
        => Assert.False(RentalStatus.CanTransition(from, to));

    [Fact]
    public void All_ContainsExactlySixStatuses()
        => Assert.Equal(6, RentalStatus.All.Count);

    [Fact]
    public void IsKnown_RejectsUnknownAndNull()
    {
        Assert.False(RentalStatus.IsKnown("Shipped"));
        Assert.False(RentalStatus.IsKnown(null));
        Assert.True(RentalStatus.IsKnown(RentalStatus.PickedUp));
    }

    [Fact]
    public void EveryStatus_HasTransitionMapEntry()
    {
        foreach (var status in RentalStatus.All)
            Assert.True(RentalStatus.AllowedTransitions.ContainsKey(status),
                        $"немає запису в карті переходів для статусу {status}");
    }

    // ---------------------------------------------- обчислювані властивості ---

    [Fact]
    public void RentalItem_DailyCharge_SubtractsDiscount()
    {
        var item = new RentalItem { DailyRate = 4.25m, Units = 3, Discount = 1.75m };

        // 4.25 * 3 - 1.75 = 11.00
        Assert.Equal(11.00m, item.DailyCharge);
    }

    [Fact]
    public void Rental_IsOverdue_OnlyWhenPickedUpAndPastDue()
    {
        var due = new DateOnly(2026, 11, 10);
        var afterDue = new DateOnly(2026, 11, 20);
        var beforeDue = new DateOnly(2026, 11, 01);

        Assert.True(new Rental { Status = RentalStatus.PickedUp, DueDate = due }.IsOverdue(afterDue));
        Assert.False(new Rental { Status = RentalStatus.PickedUp, DueDate = due }.IsOverdue(beforeDue));

        // уже повернена або скасована оренда простроченою не вважається
        Assert.False(new Rental { Status = RentalStatus.Returned, DueDate = due }.IsOverdue(afterDue));
        Assert.False(new Rental { Status = RentalStatus.Cancelled, DueDate = due }.IsOverdue(afterDue));
        Assert.False(new Rental { Status = RentalStatus.Submitted, DueDate = due }.IsOverdue(afterDue));
    }

    [Fact]
    public void SqlErrorCodes_MapToDocumentedValues()
    {
        // ці номери кидають збережувані процедури з Лаб. 01
        Assert.Equal(50001, SqlErrorCodes.NotFound);
        Assert.Equal(50002, SqlErrorCodes.InvalidTransition);
        Assert.Equal(50003, SqlErrorCodes.NotEnoughDiscs);
        Assert.Equal(50004, SqlErrorCodes.BusinessRule);
    }
}
