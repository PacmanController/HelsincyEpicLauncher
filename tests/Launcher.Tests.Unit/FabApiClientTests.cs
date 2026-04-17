// Copyright (c) Helsincy. All rights reserved.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Application.Modules.FabLibrary.Contracts;
using Launcher.Infrastructure.FabLibrary;
using Launcher.Shared;
using Launcher.Tests.Unit.Helpers;

namespace Launcher.Tests.Unit;

/// <summary>
/// FabApiClient 单元测试。使用 MockHttpMessageHandler 控制 HTTP 响应，NSubstitute mock IAuthService。
/// </summary>
public sealed class FabApiClientTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MockHttpMessageHandler _handler = new();
    private readonly IAuthService _authService = Substitute.For<IAuthService>();
    private readonly FabApiClient _sut;

    public FabApiClientTests()
    {
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://www.fab.com/api"),
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("FabApi").Returns(httpClient);

        _authService.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Ok("test-access-token"));

        _sut = new FabApiClient(factory, _authService);
    }

    // ===== SearchAsync =====

    [Fact]
    public async Task SearchAsync_Success_ReturnsItems()
    {
        // Arrange
        var responseBody = new FabSearchResponse
        {
            Items = [new FabAssetDto { AssetId = "asset-1", Title = "Test Asset" }],
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
        };
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(responseBody, JsonOptions));

        var query = new FabSearchQuery { Keyword = "test", Page = 1, PageSize = 20 };

        // Act
        var result = await _sut.SearchAsync(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        result.Value.Items[0].AssetId.Should().Be("asset-1");
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_Unauthorized_ReturnsHttpError()
    {
        // Arrange
        _handler.EnqueueResponse(HttpStatusCode.Unauthorized, """{"error":"unauthorized"}""");

        var query = new FabSearchQuery { Keyword = "test" };

        // Act
        var result = await _sut.SearchAsync(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("FAB_HTTP_401");
        result.Error.CanRetry.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_ServerError_RetriesAndReturnsError()
    {
        // Arrange: Polly retries 3 times + original = 4 requests total
        _handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"error":"server error"}""");
        _handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"error":"server error"}""");
        _handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"error":"server error"}""");
        _handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"error":"server error"}""");

        var query = new FabSearchQuery { Keyword = "test" };

        // Act
        var result = await _sut.SearchAsync(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("FAB_HTTP_500");
        result.Error.CanRetry.Should().BeTrue();
        _handler.ReceivedRequests.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SearchAsync_CloudflareChallenge_ReturnsFriendlyBlockedError()
    {
        // Arrange
        const string challengeHtml = "<!DOCTYPE html><html><head><title>Just a moment...</title></head><body>Enable JavaScript and cookies to continue<script>window._cf_chl_opt={};</script></body></html>";
        _handler.EnqueueResponse(HttpStatusCode.Forbidden, challengeHtml);

        var query = new FabSearchQuery { Keyword = "test" };

        // Act
        var result = await _sut.SearchAsync(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("FAB_BROWSER_CHALLENGE_BLOCKED");
        result.Error.UserMessage.Should().Contain("浏览器验证拦截");
        result.Error.CanRetry.Should().BeFalse();
    }

    // ===== GetDetailAsync =====

    [Fact]
    public async Task GetDetailAsync_Success_ReturnsDetail()
    {
        // Arrange
        var detail = new FabAssetDetailDto
        {
            AssetId = "asset-detail-1",
            Title = "Detail Asset",
            Description = "A detailed description",
            Author = "TestAuthor",
            Price = 19.99m,
            Rating = 4.5,
            RatingCount = 100,
            DownloadSize = 1024 * 1024,
            LatestVersion = "2.0.0",
        };
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(detail, JsonOptions));

        // Act
        var result = await _sut.GetDetailAsync("asset-detail-1", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AssetId.Should().Be("asset-detail-1");
        result.Value.Title.Should().Be("Detail Asset");
        result.Value.Price.Should().Be(19.99m);
        result.Value.RatingCount.Should().Be(100);
    }

    // ===== GetDownloadInfoAsync =====

    [Fact]
    public async Task GetDownloadInfoAsync_Success_ReturnsDownloadInfo()
    {
        // Arrange
        var info = new FabDownloadInfoDto
        {
            AssetId = "asset-dl-1",
            DownloadUrl = "https://cdn.fab.com/download/asset-dl-1.zip",
            FileName = "asset-dl-1.zip",
            FileSize = 5 * 1024 * 1024,
            Version = "1.0.0",
        };
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(info, JsonOptions));

        // Act
        var result = await _sut.GetDownloadInfoAsync("asset-dl-1", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DownloadUrl.Should().Contain("cdn.fab.com");
        result.Value.FileSize.Should().Be(5 * 1024 * 1024);
    }

    // ===== GetOwnedAssetsAsync =====

    [Fact]
    public async Task GetOwnedAssetsAsync_Success_ReturnsItems()
    {
        // Arrange
        var response = new FabOwnedAssetsResponse
        {
            Items =
            [
                new FabAssetDto { AssetId = "owned-1", Title = "Owned Asset 1", IsOwned = true },
                new FabAssetDto { AssetId = "owned-2", Title = "Owned Asset 2", IsOwned = true },
            ],
        };
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(response, JsonOptions));

        // Act
        var result = await _sut.GetOwnedAssetsAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.Items.Should().AllSatisfy(item => item.IsOwned.Should().BeTrue());
    }

    // ===== Token 失败 =====

    [Fact]
    public async Task AnyMethod_TokenFailed_ReturnsAuthError()
    {
        // Arrange
        _authService.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Fail<string>(new Error
            {
                Code = "AUTH_TOKEN_EXPIRED",
                UserMessage = "Token expired",
                TechnicalMessage = "Token expired",
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            }));

        // Act
        var result = await _sut.SearchAsync(
            new FabSearchQuery { Keyword = "test" }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_TOKEN_EXPIRED");
    }

    // ===== 取消 =====

    [Fact]
    public async Task AnyMethod_Cancelled_ReturnsCancelledError()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await _sut.GetDetailAsync("any-asset", cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("FAB_CANCELLED");
    }

    public void Dispose() => _handler.Dispose();
}
