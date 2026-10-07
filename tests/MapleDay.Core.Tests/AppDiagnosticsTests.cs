using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class AppDiagnosticsTests
{
    [Fact]
    public void ReportsIncludeOnlyAllowlistedFieldsAndNeverExceptionMessagesOrData()
    {
        var error = new InvalidOperationException("live_secret https://host/private?api-key=secret C:\\Users\\Nickname");
        error.Data["api-key"] = "private-key";
        var report = DiagnosticReport.Create(error,"unhandled",new Version(1,0,35,0),true);
        var raw = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("live_secret",raw); Assert.DoesNotContain("private-key",raw);
        Assert.DoesNotContain("Nickname",raw); Assert.DoesNotContain("https:",raw);
        Assert.Equal("System.InvalidOperationException",report.ExceptionType);
        Assert.Equal(32,report.Id.Length); Assert.Equal(64,report.Fingerprint.Length);
    }

    [Fact]
    public void HighestLevelIgnoresSupportRepresentativeAndUsesStableTieBreak()
    {
        Assert.Equal("가",UsageIdentity.HighestNickname([("나",291),("가",291),("대표",287)]));
        Assert.Null(UsageIdentity.HighestNickname([]));
        Assert.Null(UsageIdentity.HighestNickname([("",300)]));
    }

    [Fact]
    public void NicknameHashIsNormalizedAndContainsNoRawNickname()
    {
        Assert.Equal(UsageIdentity.NicknameHash(" 가 "),UsageIdentity.NicknameHash("가"));
        Assert.Equal(UsageIdentity.NicknameHash("가"),UsageIdentity.NicknameHash("가"));
        Assert.Equal(64,UsageIdentity.NicknameHash("가").Length);
        Assert.NotEqual(UsageIdentity.NicknameHash("가"),UsageIdentity.NicknameHash("나"));
    }
}
