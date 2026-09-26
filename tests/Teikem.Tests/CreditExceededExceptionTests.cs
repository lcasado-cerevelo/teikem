using Teikem.Domain.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 3 (ajuste C): 422 credit_exceeded con errors {limit, exposure, newAmount, available} en formato 0.00 invariante.</summary>
public class CreditExceededExceptionTests
{
    [Fact]
    public void Carries_the_four_credit_keys_status_and_code()
    {
        var ex = new CreditExceededException(new CreditCheck(10m, 0m, 26.25m), 10m);
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("credit_exceeded", ex.Code);
        Assert.Equal(new[] { "10.00" }, ex.Errors!["limit"]);
        Assert.Equal(new[] { "0.00" }, ex.Errors!["exposure"]);
        Assert.Equal(new[] { "26.25" }, ex.Errors!["newAmount"]);
        Assert.Equal(new[] { "10.00" }, ex.Errors!["available"]);
        Assert.Equal(CreditRules.Message(new CreditCheck(10m, 0m, 26.25m)), ex.Message);
    }
}
