using System.Net;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Thumbnails: a slow first read from the cloud is tried again later, only a file without a picture is remembered.</summary>
public class MobileBridgeThumbTests
{
    [Fact]
    public Task A_thumbnail_that_timed_out_is_tried_again_not_remembered_as_missing() => BridgeHarness.With(async env =>
    {
        await File.WriteAllBytesAsync(Path.Combine(env.Cloud, "photo.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 });
        int calls = 0;
        bool slow = true;
        env.Bridge.ThumbTimeout = TimeSpan.FromMilliseconds(300);
        env.Bridge.ThumbnailFactory = async (path, w, ct) =>
        {
            calls++;
            if (slow) await Task.Delay(TimeSpan.FromSeconds(5), ct);   // the photo is still coming from the cloud
            return new byte[] { 1, 2, 3 };
        };
        var url = "/api/fs/thumb?path=" + Uri.EscapeDataString("cloudssd:photo.jpg") + "&w=160";

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await env.Get(url)).StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(env.Host.Paths.DataRoot, "thumbs"), "*.none", SearchOption.AllDirectories));

        slow = false;
        var ok = await env.Get(url);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3 }, await ok.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, calls);
    });

    [Fact]
    public Task A_file_without_a_picture_is_remembered_for_a_while() => BridgeHarness.With(async env =>
    {
        await File.WriteAllBytesAsync(Path.Combine(env.Cloud, "broken.jpg"), new byte[] { 0, 1, 2 });
        int calls = 0;
        env.Bridge.ThumbnailFactory = (path, w, ct) => { calls++; return Task.FromResult<byte[]?>(null); };
        var url = "/api/fs/thumb?path=" + Uri.EscapeDataString("cloudssd:broken.jpg") + "&w=160";

        Assert.Equal(HttpStatusCode.NotFound, (await env.Get(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Get(url)).StatusCode);
        Assert.Equal(1, calls);   // not made again within the day
    });
}
