using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CloudSSD.App;
using CloudSSD.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>A running bridge on a free loopback port, its host and folders, and helpers to call it.</summary>
internal sealed record BridgeEnv(HttpClient Http, string Base, AppHost Host, LocalMobileBridgeService Bridge, string Temp, string Cloud, string Uploads)
{
    public Task<HttpResponseMessage> Get(string url, string? token = BridgeHarness.Shared) =>
        Http.GetAsync(Base + url + (token is null ? "" : (url.Contains('?') ? "&" : "?") + "token=" + token));

    public Task<HttpResponseMessage> Post(string url, string? token = BridgeHarness.Shared) =>
        Http.PostAsync(Base + url + (token is null ? "" : (url.Contains('?') ? "&" : "?") + "token=" + token), null);

    /// <summary>POST with a body (an upload).</summary>
    public Task<HttpResponseMessage> PostBytes(string url, byte[] body, string? token = BridgeHarness.Shared)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return Http.PostAsync(Base + url + (token is null ? "" : (url.Contains('?') ? "&" : "?") + "token=" + token), content);
    }

    public async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Pairs a new device with the six digits shown on the PC and returns its own key.</summary>
    public async Task<string> PairNew(string name, string installId, string extra = "")
    {
        var r = await Http.PostAsync($"{Base}/api/pair?code={Bridge.PairingCode}&name={name}&installId={installId}{extra}", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).GetProperty("token").GetString()!;
    }
}

/// <summary>Starts a bridge for one test: shared key <see cref="Shared"/> (still accepted), a temp cloud drive and uploads folder.</summary>
internal static class BridgeHarness
{
    public const string Shared = "0123456789abcdef0123456789abcdef";

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static async Task With(Func<BridgeEnv, Task> body, Action<LocalMobileBridgeService>? setup = null)
    {
        int port = FreePort();
        var temp = Path.Combine(Path.GetTempPath(), "cpx_sec_test_" + Guid.NewGuid().ToString("N"));
        var host = AppHost.Create(new AppPaths(temp));
        var b = host.Settings.MobileBridge;
        b.Port = port;
        b.PairingToken = Shared;
        b.LegacyTokenAccepted = true;
        var cloud = Directory.CreateDirectory(Path.Combine(temp, "cloud")).FullName;
        var uploads = Directory.CreateDirectory(Path.Combine(temp, "uploads")).FullName;
        b.UploadDirectory = uploads;
        var bridge = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance)
        {
            CloudRootsOverride = () => [new CloudRoot(AppHost.CombinedMountId, cloud, false, false, temp, "All Accounts")]
        };
        setup?.Invoke(bridge);
        try
        {
            bridge.Start();
            // The free port can be taken by another test's bridge before this one binds it (tests run in parallel).
            // Start then only logs the failure, and this test would talk to the other bridge: take another port.
            for (int i = 0; i < 10 && !bridge.IsRunning; i++)
            {
                port = FreePort();
                b.Port = port;
                bridge.Start();
            }
            Assert.True(bridge.IsRunning, "No free port for the test bridge");
            using var http = new HttpClient();
            await body(new BridgeEnv(http, $"http://127.0.0.1:{port}", host, bridge, temp, cloud, uploads));
        }
        finally
        {
            await bridge.DisposeAsync();
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
