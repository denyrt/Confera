using Confera.Application.Common;
using Confera.Application.Reports;

namespace Confera.Application.Tests;

public sealed class ResultTests
{
    [Fact]
    public void PayloadResultsExposeOnlyTheActiveBranch()
    {
        var value = new object();
        var success = Result<object, ReportError>.Success(value);
        Assert.True(success.IsSuccess);
        Assert.Same(value, success.Value);
        Assert.Throws<InvalidOperationException>(() => success.Error);

        // The default enum value is still an active error, not a missing payload.
        var failure = Result<object, ReportError>.Failure(ReportError.InvalidPeriod);
        Assert.False(failure.IsSuccess);
        Assert.Equal(ReportError.InvalidPeriod, failure.Error);
        Assert.Throws<InvalidOperationException>(() => failure.Value);
    }

    [Fact]
    public void ResultsWithoutPayloadDistinguishSuccessFromFailure()
    {
        var success = Result<string>.Success();
        Assert.True(success.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => success.Error);

        var failure = Result<string>.Failure("unavailable");
        Assert.False(failure.IsSuccess);
        Assert.Equal("unavailable", failure.Error);
    }

    [Fact]
    public void FactoriesRejectMissingRequiredPayloads()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string, string>.Success(null!));
        Assert.Throws<ArgumentNullException>(() => Result<string, string>.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result<string>.Failure(null!));
    }
}
