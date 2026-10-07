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
        previous.UsageAnalyticsEnabled=false;previous.AutomaticErrorReports=false;
        var restored=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(previous))!;
        Assert.False(restored.UsageAnalyticsEnabled);Assert.False(restored.AutomaticErrorReports);
    }
    private readonly string _root=Path.Combine(Path.GetTempPath(),"mapleday-telemetry-tests-"+Guid.NewGuid().ToString("N"));
    private sealed class Server : HttpMessageHandler
    {
        public List<(string Path,string Body)> Requests { get; }=[];
        public bool Fail { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("x-nxopen-api-key"));
            Requests.Add((request.RequestUri!.AbsolutePath,await request.Content!.ReadAsStringAsync(token)));
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
            await client.SendAsync(["최고캐릭터"],new(1,0,35,0),queue,true,default);
            await client.SendAsync(["새로운최고"],new(1,0,35,0),queue,true,default);
        }
        using var http2=new HttpClient(server){BaseAddress=new("https://test/api/mapleday/")};
        using(var client=new TelemetryClient(http2,path))await client.SendAsync(["새로운최고"],new(1,0,35,0),queue,true,default);
        var bodies=server.Requests.Select(request=>JsonDocument.Parse(request.Body)).ToArray();
        Assert.Equal(bodies[0].RootElement.GetProperty("installation").GetString(),bodies[2].RootElement.GetProperty("installation").GetString());
        Assert.Equal(2,bodies[2].RootElement.GetProperty("aliases").GetArrayLength());
        Assert.All(server.Requests,request=>{Assert.DoesNotContain("최고캐릭터",request.Body);Assert.DoesNotContain("새로운최고",request.Body);});
        foreach(var body in bodies)body.Dispose();
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
