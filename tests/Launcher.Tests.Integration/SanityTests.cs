// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Tests.Integration;

/// <summary>
/// 集成测试框架验证
/// </summary>
public class SanityTests
{
    [Fact]
    public void IntegrationTestFramework_ShouldWork()
    {
        true.Should().BeTrue();
    }
}
