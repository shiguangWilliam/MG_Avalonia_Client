using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Unit tests for HttpResult&lt;T&gt; / HttpError. These do not touch the network —
/// they verify the typed result API behaves correctly for callers' branching logic.
/// </summary>
public sealed class HttpResultTests
{
    [Fact]
    public void Success_HasValue_NullError()
    {
        HttpResult<string> result = HttpResult<string>.Ok("hello");

        result.Success.Should().BeTrue();
        result.Value.Should().Be("hello");
        result.Error.Should().BeNull();
        result.TryGetValue(out string? value).Should().BeTrue();
        value.Should().Be("hello");
    }

    [Fact]
    public void Failure_HasError_DefaultValue()
    {
        HttpResult<string> result = HttpResult<string>.Err(HttpError.Network("timeout"));

        result.Success.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().NotBeNull();
        result.Error!.Kind.Should().Be(HttpErrorKind.Network);
        result.TryGetValue(out _).Should().BeFalse();
    }

    [Fact]
    public void NetworkError_Factory_SetsKind()
    {
        HttpError err = HttpError.Network("DNS failed", code: "dns");

        err.Kind.Should().Be(HttpErrorKind.Network);
        err.Code.Should().Be("dns");
        err.Message.Should().Be("DNS failed");
    }

    [Fact]
    public void BusinessError_Factory_SetsKind()
    {
        HttpError err = HttpError.Business("http-503", "Server returned 503");

        err.Kind.Should().Be(HttpErrorKind.Business);
        err.Code.Should().Be("http-503");
        err.Message.Should().Be("Server returned 503");
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        HttpResult<int> result = HttpResult<int>.Ok(7);

        HttpResult<string> mapped = result.Map(x => x.ToString());

        mapped.Success.Should().BeTrue();
        mapped.Value.Should().Be("7");
    }

    [Fact]
    public void Map_OnFailure_PreservesError()
    {
        HttpError original = HttpError.Business("http-404", "not found");
        HttpResult<int> result = HttpResult<int>.Err(original);

        HttpResult<string> mapped = result.Map(x => x.ToString());

        mapped.Success.Should().BeFalse();
        mapped.Error.Should().BeSameAs(original);
    }

    [Fact]
    public void HttpError_ToString_IsUseful()
    {
        var err = HttpError.Business("http-500", "internal error");

        err.ToString().Should().Contain("Business");
        err.ToString().Should().Contain("http-500");
        err.ToString().Should().Contain("internal error");
    }

    [Fact]
    public void CnCNetHttp_TryDownloadString_NullUrl_ReturnsBusinessError()
    {
        HttpResult<string> result = CnCNetHttp.TryDownloadString("");

        result.Success.Should().BeFalse();
        result.Error!.Kind.Should().Be(HttpErrorKind.Business);
        result.Error.Code.Should().Be("empty-url");
    }

    [Fact]
    public void CnCNetHttp_TryDownloadBytes_NullUrl_ReturnsBusinessError()
    {
        HttpResult<byte[]> result = CnCNetHttp.TryDownloadBytes("   ");

        result.Success.Should().BeFalse();
        result.Error!.Kind.Should().Be(HttpErrorKind.Business);
        result.Error.Code.Should().Be("empty-url");
    }
}
