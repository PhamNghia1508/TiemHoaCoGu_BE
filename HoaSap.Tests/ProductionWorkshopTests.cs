using System.ComponentModel.DataAnnotations;
using HoaSap.BLL.Workshops;
using Xunit;

namespace HoaSap.Tests;

public sealed class ProductionWorkshopTests
{
    [Fact]
    public void BookingRequest_IdempotencyKey_IsOptionalAndCappedAt100Characters()
    {
        var request = new BookingRequest { TicketCount = 1, CustomerName = "A", CustomerPhone = "0901234567", IdempotencyKey = new string('x', 101) };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, true));
        Assert.Contains(results, x => x.MemberNames.Contains(nameof(BookingRequest.IdempotencyKey)));
    }

    [Fact]
    public void ManualPaymentRequest_RejectsAmountAbovePracticalBound()
    {
        var request = new ManualPaymentRequest { TransactionCode = " TX ", PaymentMethod = "Cash", Amount = 1_000_000_000_001m };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, true));
    }
}
