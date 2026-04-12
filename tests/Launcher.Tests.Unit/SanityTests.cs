// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Tests.Unit;

/// <summary>
/// 验证测试框架可用性的占位测试
/// </summary>
public class SanityTests
{
    [Fact]
    public void TestFramework_ShouldWork()
    {
        // 验证 xUnit + FluentAssertions 正常工作
        true.Should().BeTrue();
    }
}
