using Teikem.Domain.Security;
using Xunit;

namespace Teikem.Tests;

public class UserPreferenceKeysTests
{
    [Theory]
    [InlineData("sales.entryMode", "wizard", true)]
    [InlineData("sales.entryMode", "full", true)]
    [InlineData("sales.entryMode", "otro", false)]
    [InlineData("warehouse.last", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("warehouse.last", "no-es-guid", false)]
    [InlineData("otra.clave", "wizard", false)]
    public void Valida_clave_y_valor(string key, string value, bool ok) => Assert.Equal(ok, UserPreferenceKeys.IsValid(key, value));

    [Fact]
    public void Solo_las_claves_permitidas_existen()
    {
        Assert.True(UserPreferenceKeys.IsKnown("sales.entryMode"));
        Assert.True(UserPreferenceKeys.IsKnown("warehouse.last"));
        Assert.False(UserPreferenceKeys.IsKnown("cualquiera"));
    }
}
