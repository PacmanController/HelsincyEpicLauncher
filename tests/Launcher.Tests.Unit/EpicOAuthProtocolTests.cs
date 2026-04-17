// Copyright (c) Helsincy. All rights reserved.

using System.Collections.Specialized;
using Launcher.Infrastructure.Auth;

namespace Launcher.Tests.Unit;

/// <summary>
/// Epic OAuth 协议辅助逻辑单元测试。
/// </summary>
public sealed class EpicOAuthProtocolTests
{
    [Fact]
    public void BuildAuthorizeUrl_Should_IncludeRedirectUriAndState()
    {
        // Arrange
        const string authorizeUrl = "https://www.epicgames.com/id/authorize";

        // Act
        var result = EpicOAuthProtocol.BuildAuthorizeUrl(
            authorizeUrl,
            "client-id",
            "http://localhost:6780/callback",
            "state-123");

        // Assert
        result.Should().Contain("client_id=client-id");
        result.Should().Contain("response_type=code");
        result.Should().Contain(Uri.EscapeDataString("http://localhost:6780/callback"));
        result.Should().Contain("state=state-123");
    }

    [Fact]
    public void BuildAuthorizationCodeLoginUrl_Should_TargetEpicRedirectEndpoint()
    {
        // Act
        var result = EpicOAuthProtocol.BuildAuthorizationCodeLoginUrl("client-id");

        // Assert
        result.Should().Be("https://www.epicgames.com/id/login?redirectUrl=https%3A//www.epicgames.com/id/api/redirect%3FclientId%3Dclient-id%26responseType%3Dcode");
    }

    [Fact]
    public void ExtractAuthorizationCode_WithPlainCode_Should_Succeed()
    {
        // Act
        var result = EpicOAuthProtocol.ExtractAuthorizationCode("code-123");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("code-123");
    }

    [Fact]
    public void ExtractAuthorizationCode_WithJsonPayload_Should_Succeed()
    {
        // Act
        var result = EpicOAuthProtocol.ExtractAuthorizationCode("{\"authorizationCode\":\"code-123\"}");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("code-123");
    }

    [Fact]
    public void ExtractAuthorizationCode_WithJsonRedirectUrl_Should_Succeed()
    {
        // Act
        var result = EpicOAuthProtocol.ExtractAuthorizationCode("{\"redirectUrl\":\"https://localhost/launcher/authorized?code=code-123\"}");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("code-123");
    }

    [Fact]
    public void ExtractAuthorizationCode_WithRedirectUrl_Should_Succeed()
    {
        // Act
        var result = EpicOAuthProtocol.ExtractAuthorizationCode("https://localhost/launcher/authorized?code=code-123");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("code-123");
    }

    [Fact]
    public void ExtractAuthorizationCode_WithJsonMissingCode_Should_Fail()
    {
        // Act
        var result = EpicOAuthProtocol.ExtractAuthorizationCode("{\"foo\":\"bar\"}");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_AUTHORIZATION_CODE_INVALID");
    }

    [Fact]
    public void ParseCallback_WithCodeAndMatchingState_Should_Succeed()
    {
        // Arrange
        var query = new NameValueCollection
        {
            ["code"] = "code-123",
            ["state"] = "state-123",
        };

        // Act
        var result = EpicOAuthProtocol.ParseCallback("/callback", query, "/callback", "state-123");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Code.Should().Be("code-123");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ParseCallback_WithProviderError_Should_ReturnHelpfulError()
    {
        // Arrange
        var query = new NameValueCollection
        {
            ["state"] = "state-123",
            ["error"] = "invalid_redirect_url",
            ["error_description"] = "redirect URL isn't valid for this client",
        };

        // Act
        var result = EpicOAuthProtocol.ParseCallback("/callback", query, "/callback", "state-123");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_CALLBACK_PROVIDER_ERROR");
        result.Error.UserMessage.Should().Contain("回调地址配置无效");
    }

    [Fact]
    public void ParseCallback_WithStateMismatch_Should_Fail()
    {
        // Arrange
        var query = new NameValueCollection
        {
            ["code"] = "code-123",
            ["state"] = "state-other",
        };

        // Act
        var result = EpicOAuthProtocol.ParseCallback("/callback", query, "/callback", "state-123");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_CALLBACK_STATE_INVALID");
    }

    [Fact]
    public void ParseCallback_WithUnexpectedPath_Should_Fail()
    {
        // Arrange
        var query = new NameValueCollection
        {
            ["code"] = "code-123",
            ["state"] = "state-123",
        };

        // Act
        var result = EpicOAuthProtocol.ParseCallback("/unexpected", query, "/callback", "state-123");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_CALLBACK_PATH_INVALID");
    }
}