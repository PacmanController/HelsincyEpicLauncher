// Copyright (c) Helsincy. All rights reserved.

using System.Net;
using Launcher.Infrastructure.Auth;
using Launcher.Tests.Unit.Helpers;

namespace Launcher.Tests.Unit;

/// <summary>
/// authorization_code grant 执行器单元测试。
/// </summary>
public sealed class AuthorizationCodeGrantExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithAuthorizationCodeInput_ShouldSendEg1WithoutRedirectUri()
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

        var sut = new AuthorizationCodeGrantExecutor(factory, options);

        // Act
        var result = await sut.ExecuteAsync(EpicLoginResult.FromAuthorizationCodeInput("code-123"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccountId.Should().Be("account-1");

        var request = handler.ReceivedRequests.Should().ContainSingle().Which;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be(EpicTokenEndpoint.TokenUrl);
        request.Headers.Authorization!.Scheme.Should().Be("Basic");

        var body = handler.ReceivedRequestBodies.Should().ContainSingle().Which;
        body.Should().Contain("grant_type=authorization_code");
        body.Should().Contain("code=code-123");
        body.Should().Contain("token_type=eg1");
        body.Should().NotContain("redirect_uri=");
    }

    [Fact]
    public async Task ExecuteAsync_WithLoopbackCallback_ShouldSendRedirectUriWithoutEg1()
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

        var sut = new AuthorizationCodeGrantExecutor(factory, options);

        // Act
        var result = await sut.ExecuteAsync(
            EpicLoginResult.FromLoopbackCallback("code-123", "http://localhost:6780/callback"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        handler.ReceivedRequests.Should().ContainSingle();
        var body = handler.ReceivedRequestBodies.Should().ContainSingle().Which;
        body.Should().Contain("grant_type=authorization_code");
        body.Should().Contain("code=code-123");
        body.Should().Contain("redirect_uri=http%3A%2F%2Flocalhost%3A6780%2Fcallback");
        body.Should().NotContain("token_type=eg1");
    }
}