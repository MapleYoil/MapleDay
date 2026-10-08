using System.Net;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class TelemetryTests : IDisposable
{
    [Fact]
    public void ExistingSettingsDefaultToEnabledAndOptOutSurvivesReload()
    {
        var previous=JsonSerializer.Deserialize<AppSettings>("{\"CloseToTray\":true}")!;
        Assert.True(previous.UsageAnalyticsEnabled);Assert.True(previous.AutomaticErrorReports);
        Assert.False(previous.AnonymousUsageAnalytics);
        previous.UsageAnalyticsEnabled=false;previous.AutomaticErrorReports=false;
        var restored=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(previous))!;
        Assert.False(restored.UsageAnalyticsEnabled);Assert.False(restored.AutomaticErrorReports);
    }
    private readonly string _root=Path.Combine(Path.GetTempPath(),"mapleday-telemetry-tests-"+Guid.NewGuid().ToString("N"));
    private sealed class Server : HttpMessageHandler
    {
        public List<(string Path,string Body)> Requests { get; }=[];
        public bool Fail { get; set; }
        public string? RejectProfiles { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("x-nxopen-api-key"));
            Requests.Add((request.RequestUri!.AbsolutePath,await request.Content!.ReadAsStringAsync(token)));
            if (RejectProfiles is { } message && Requests[^1].Body.Contains("\"profiles\""))
                return new(HttpStatusCode.BadRequest) { Content = System.Net.Http.Json.JsonContent.Create(new { error = message }) };
            return new(Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK);
        }
    }
    [Fact]
    public async Task StableInstallAndOldNewNicknameHashesSurviveRestartWithoutRawNames()
    {
        var server=new Server();using var http=new HttpClient(server){BaseAddress=new("https://test/api/mapleday/")};
        var path=Path.Combine(_root,"identity.dat");var queue=new DiagnosticQueue(Path.Combine(_root,"errors"));
        using(var client=new TelemetryClient(http,path))
        {
            await client.SendAsync(["최고캐릭터"],new(1,0,35,0),queue,true,default,anonymous:true);
            await client.SendAsync(["새로운최고"],new(1,0,35,0),queue,true,default,anonymous:true);
        }
        using var http2=new HttpClient(server){BaseAddress=new("https://test/api/mapleday/")};
        using(var client=new TelemetryClient(http2,path))await client.SendAsync(["새로운최고"],new(1,0,35,0),queue,true,default,anonymous:true);
        var bodies=server.Requests.Select(request=>JsonDocument.Parse(request.Body)).ToArray();
        Assert.Equal(bodies[0].RootElement.GetProperty("installation").GetString(),bodies[2].RootElement.GetProperty("installation").GetString());
        Assert.Equal(2,bodies[2].RootElement.GetProperty("aliases").GetArrayLength());
        Assert.All(server.Requests,request=>{Assert.DoesNotContain("최고캐릭터",request.Body);Assert.DoesNotContain("새로운최고",request.Body);});
        foreach(var body in bodies)body.Dispose();
    }
    [Fact]
    public async Task DefaultIncludesNicknameAndAnonymousToggleKeepsSameIdentityWithoutRawName()
    {
        var server=new Server();using var http=new HttpClient(server){BaseAddress=new("https://test/api/mapleday/")};
        using var client=new TelemetryClient(http,Path.Combine(_root,"identity.dat"));
        var queue=new DiagnosticQueue(Path.Combine(_root,"errors"));
        await client.SendAsync(["현재최고"],new(1,0,36,0),queue,true,default);
        await client.SendAsync(["현재최고"],new(1,0,36,0),queue,true,default,anonymous:true);
        using var first=JsonDocument.Parse(server.Requests[0].Body);using var second=JsonDocument.Parse(server.Requests[1].Body);
        Assert.Equal("현재최고",first.RootElement.GetProperty("nicknames")[0].GetString());
        Assert.False(first.RootElement.GetProperty("anonymous").GetBoolean());
        Assert.Empty(second.RootElement.GetProperty("nicknames").EnumerateArray());
        Assert.Equal(first.RootElement.GetProperty("installation").GetString(),second.RootElement.GetProperty("installation").GetString());
        Assert.Equal(first.RootElement.GetProperty("aliases")[0].GetString(),second.RootElement.GetProperty("aliases")[0].GetString());
    }
    [Fact]
    public async Task FailedErrorUploadRetainsQueueAndOptOutStopsUsageRequest()
    {
        var server=new Server{Fail=true};using var http=new HttpClient(server){BaseAddress=new("https://test/api/mapleday/")};
        using var client=new TelemetryClient(http,Path.Combine(_root,"identity.dat"));
        var queue=new DiagnosticQueue(Path.Combine(_root,"errors"));
        queue.Capture(new IOException("secret path"),"storage",new(1,0,35,0));
        await Assert.ThrowsAsync<HttpRequestException>(()=>client.SendAsync([],new(1,0,35,0),queue,false,default));
        Assert.Single(queue.Pending());Assert.Single(server.Requests);
        Assert.EndsWith("/errors",server.Requests[0].Path);Assert.DoesNotContain("secret path",server.Requests[0].Body);
        server.Fail=false;await client.SendAsync([],new(1,0,35,0),queue,false,default);
        Assert.Empty(queue.Pending());
    }
    [Fact]
    public async Task SharedProfileUsesHighestCharacterWhileAnonymousAndDisabledUsageNeverSendMetadata()
    {
        var server=new Server();using var http=new HttpClient(server){BaseAddress=new("https://test/api/mapleday/")};
        using var client=new TelemetryClient(http,Path.Combine(_root,"profile.dat"));
        var queue=new DiagnosticQueue(Path.Combine(_root,"errors"));var profile=new UsageCharacter("현재최고",291,"오로라");
        await client.SendAsync([profile.Nickname],new(1,0,37,0),queue,true,default,character:profile);
        using var shared=JsonDocument.Parse(server.Requests[0].Body);
        Assert.Equal(291,shared.RootElement.GetProperty("profiles")[0].GetProperty("level").GetInt32());
        Assert.Equal("오로라",shared.RootElement.GetProperty("profiles")[0].GetProperty("world").GetString());
        await client.SendAsync([profile.Nickname],new(1,0,37,0),queue,true,default,anonymous:true,character:profile);
        using var anonymous=JsonDocument.Parse(server.Requests[1].Body);
        Assert.False(anonymous.RootElement.TryGetProperty("profiles",out _));
        Assert.DoesNotContain(profile.Nickname,server.Requests[1].Body);Assert.DoesNotContain(profile.World,server.Requests[1].Body);
        await client.SendAsync([profile.Nickname],new(1,0,37,0),queue,false,default,character:profile);
        Assert.Equal(2,server.Requests.Count);
    }
    [Fact]
    public async Task LegacyServerRetriesNicknamePayloadButProfileValidationErrorsAreNotBypassed()
    {
        var server=new Server { RejectProfiles="허용되지 않은 집계 정보입니다." };
        using var http=new HttpClient(server){BaseAddress=new("https://test/api/mapleday/")};
        using var client=new TelemetryClient(http,Path.Combine(_root,"legacy-profile.dat"));
        var queue=new DiagnosticQueue(Path.Combine(_root,"errors"));var profile=new UsageCharacter("현재최고",291,"오로라");
        await client.SendAsync([profile.Nickname],new(1,0,37,0),queue,true,default,character:profile);
        Assert.Equal(2,server.Requests.Count);
        using var fallback=JsonDocument.Parse(server.Requests[1].Body);
        Assert.False(fallback.RootElement.TryGetProperty("profiles",out _));
        Assert.Equal(profile.Nickname,fallback.RootElement.GetProperty("nicknames")[0].GetString());
        server.RejectProfiles="집계 캐릭터 정보를 확인해주세요.";
        await Assert.ThrowsAsync<HttpRequestException>(()=>client.SendAsync([profile.Nickname],new(1,0,37,0),queue,true,default,character:profile));
        Assert.Equal(3,server.Requests.Count);
    }
    [Fact]
    public void QueueDeduplicatesAndDisableClearsPendingButSuspendKeepsCrashReports()
    {
        var queue=new DiagnosticQueue(Path.Combine(_root,"errors"));
        queue.Capture(new IOException("one"),"storage",new(1,0,35,0));
        queue.Capture(new IOException("two"),"storage",new(1,0,35,0));
        Assert.Single(queue.Pending());queue.Suspend();queue.SetEnabled(true);Assert.Single(queue.Pending());
        queue.SetEnabled(false);Assert.Empty(queue.Pending());queue.Capture(new IOException(),"storage",new(1,0,35,0));Assert.Empty(queue.Pending());
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
