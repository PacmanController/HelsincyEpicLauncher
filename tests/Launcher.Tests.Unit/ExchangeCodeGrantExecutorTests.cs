// Copyright (c) Helsincy. All rights reserved.

using System.Net;
using Launcher.Infrastructure.Auth;
using Launcher.Tests.Unit.Helpers;

namespace Launcher.Tests.Unit;

/// <summary>
/// exchange_code grant 执行器单元测试。
/// </summary>
public sealed class ExchangeCodeGrantExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithExchangeCodeInput_ShouldSendExchangeCodeGrant()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """
            {
              "access_token": "access-123456789012",
              "refresh_token": "refresh-123456789012",
              "expires_in": 3600,
              "account_id": "account-1",
              "displayName": "Test User"
            }
            """);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("EpicAuth").Returns(new HttpClient(handler));

        var options = new EpicOAuthOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
        };

        var sut = new ExchangeCodeGrantExecutor(factory, options);

        // Act
        var result = await sut.ExecuteAsync(EpicLoginResult.FromExchangeCodeInput("exchange-code-123"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccountId.Should().Be("account-1");

        handler.ReceivedRequests.Should().ContainSingle();
        var body = handler.ReceivedRequestBodies.Should().ContainSingle().Which;
        body.Should().Contain("grant_type=exchange_code");
        body.Should().Contain("exchange_code=exchange-code-123");
        body.Should().Contain("token_type=eg1");
        body.Should().NotContain("redirect_uri=");
    }
}