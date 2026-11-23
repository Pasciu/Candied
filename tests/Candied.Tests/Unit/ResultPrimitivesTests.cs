using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Unit;

public class ResultPrimitivesTests
{
    [Fact]
    public void Ok_Result_Should_Expose_Value_And_Success()
    {
        var result = Result.Ok<string, string>("success-data");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("success-data", result.Value);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void Fail_Result_Should_Expose_Error_And_Failure()
    {
        var result = Result.Fail<string, string>("error-message");

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal("error-message", result.Error);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Implicit_Conversion_From_Value_Works()
    {
        Result<int, string> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Implicit_Conversion_From_Error_Works()
    {
        Result<int, string> result = "failed";

        Assert.True(result.IsFailure);
        Assert.Equal("failed", result.Error);
    }
}
