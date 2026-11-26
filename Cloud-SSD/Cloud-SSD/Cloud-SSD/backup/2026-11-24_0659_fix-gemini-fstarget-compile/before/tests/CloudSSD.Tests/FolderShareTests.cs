using System.Net;
using System.Text;
using CloudSSD.App;
using CloudSSD.Core;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Sending a folder (a project with many files): the tree is kept, build output left out, paths made safe.</summary>
public class FolderShareTests
{
    private static string Project(string parent)
    {
        var root = Path.Combine(parent, "MyApp");
        void Put(string rel, int size) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, new byte[size]); }
        Put("README.md", 10);
        Put(@"src\index.js", 20);
        Put(@"src\lib\index.js", 30);          // the same name in another folder
        Put(@"node_modules\left\index.js", 1000);
        Put(@"node_modules\left\package.json", 500);
        Put(@".git\HEAD", 40);
        Put(@"bin\Debug\app.dll", 2000);
        Put(@"src\Thumbs.db", 5);
        return root;
    }

    [Fact]
    public void A_project_is_walked_with_its_tree_and_without_what_a_build_makes_again()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cssd-walk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = Project(dir);
            var (files, skipped) = ProjectFilter.Walk(root, false, 5000);
            Assert.Equal(["MyApp/README.md", "MyApp/src/index.js", "MyApp/src/lib/index.js"],
                files.Select(f => f.Folder + "/" + f.Name).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(5, skipped.Files);                       // 2 in node_modules, HEAD, app.dll, Thumbs.db
            Assert.Equal(1000 + 500 + 40 + 2000 + 5, skipped.Bytes);
            Assert.Equal([".git", "bin", "node_modules"], skipped.Folders);

            var (all, none) = ProjectFilter.Walk(root, true, 5000);
            Assert.Equal(8, all.Count);
            Assert.Equal(0, none.Files);

            Assert.Equal(2, ProjectFilter.Walk(root, false, 2).Files.Count);   // at most so many; the rest counted as left out
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("MyApp/src", "MyApp/src")]
    [InlineData(@"MyApp\src\lib", "MyApp/src/lib")]
    [InlineData("../../Windows/System32", "Windows/System32")]
    [InlineData("C:/Users/x", "Users/x")]
    [InlineData("/etc/./passwd/", "etc/passwd")]
    [InlineData("..", "")]
    [InlineData("", "")]
    public void A_folder_from_another_device_cannot_point_outside(string given, string safe) => Assert.Equal(safe, ProjectFilter.SafeFolder(given));

    [Fact]
    public void A_second_folder_with_the_same_name_gets_a_number()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cssd-free-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            Assert.Equal("MyApp", ProjectFilter.FreeFolderName(dir, "MyApp"));
            Directory.CreateDirectory(Path.Combine(dir, "MyApp"));
            Assert.Equal("MyApp (2)", ProjectFilter.FreeFolderName(dir, "MyApp"));
            Directory.CreateDirectory(Path.Combine(dir, "MyApp (2)"));
            Assert.Equal("MyApp (3)", ProjectFilter.FreeFolderName(dir, "MyApp"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public Task The_PC_offers_a_folder_and_the_phone_sees_each_file_with_its_place() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-fold-01", "&kind=phone");
        var root = Project(env.Temp);
        var loose = Path.Combine(env.Temp, "notes.txt");
        await File.WriteAllTextAsync(loose, "hi");
        var dev = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Pixel");
        var offer = env.Bridge.SendToDevice(dev, [root, loose])!;
        Assert.Equal(4, offer.Items.Count);
        Assert.Equal(["MyApp"], offer.TopFolders);

        var o = (await env.Json(await env.Get("/api/share/inbox", token))).GetProperty("offers").EnumerateArray().Single();
        Assert.Equal("MyApp", o.GetProperty("folders")[0].GetString());
        Assert.Equal(5, o.GetProperty("skipped").GetProperty("files").GetInt32());
        var items = o.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("folder").GetString(), i.GetProperty("name").GetString())).ToList();
        Assert.Contains(("MyApp/src/lib", "index.js"), items);
        Assert.Contains(("MyApp/src", "index.js"), items);
        Assert.Contains(("", "notes.txt"), items);
        Assert.DoesNotContain(env.Temp, o.GetRawText().Replace("\\\\", "\\"));     // where it is on the PC stays on the PC

        // Each file downloads by itself (and resumes like any other).
        var lib = offer.Items.Single(i => i.Folder == "MyApp/src/lib");
        var got = await env.Get($"/api/share/file?id={offer.Id}&item={lib.Id}", token);
        Assert.Equal(30, (await got.Content.ReadAsByteArrayAsync()).Length);

        // Everything, when asked for.
        Assert.Equal(9, env.Bridge.SendToDevice(dev, [root, loose], everything: true)!.Items.Count);
    });

    [Fact]
    public Task A_phone_sends_a_folder_to_another_phone_and_files_with_the_same_name_do_not_meet() => BridgeHarness.With(async env =>
    {
        var from = await env.PairNew("Pixel", "inst-fold-02", "&kind=phone");
        var to = await env.PairNew("Tab", "inst-fold-03", "&kind=tablet");
        var tab = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Tab").Id;
        // A project is thousands of files: the list goes in the body (an address cannot hold it).
        var list = "[" + string.Join(",", Enumerable.Range(0, 300).Select(i => $"{{\"name\":\"f{i}.txt\",\"size\":1,\"folder\":\"Big/part{i % 7}\"}}"))
            + ",{\"name\":\"index.js\",\"size\":3,\"folder\":\"Big/src\"},{\"name\":\"index.js\",\"size\":4,\"folder\":\"../../Big/src/lib\"}]";
        var r = await env.PostBytes("/api/share/offer?to=" + tab, Encoding.UTF8.GetBytes(list), from);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var offer = (await env.Json(r)).GetProperty("offer");
        Assert.Equal(302, offer.GetProperty("items").GetArrayLength());     // more than the 200 loose files allowed
        var items = offer.GetProperty("items").EnumerateArray().ToList();
        var a = items.Single(i => i.GetProperty("folder").GetString() == "Big/src");
        var b = items.Single(i => i.GetProperty("folder").GetString() == "Big/src/lib");   // ".." gone
        var id = offer.GetProperty("id").GetString()!;

        foreach (var (item, size) in new[] { (a, 3), (b, 4) })
        {
            var up = await env.PostBytes($"/api/share/upload?id={id}&item={item.GetProperty("id").GetString()}&uploadId={Guid.NewGuid():N}&offset=0&total={size}", new byte[size], from);
            Assert.True((await env.Json(up)).GetProperty("complete").GetBoolean());
        }
        var o = env.Bridge.Shares.Get(id)!;
        var relay = env.Bridge.Shares.RelayFolder(o);
        Assert.Equal(3, new FileInfo(Path.Combine(relay, "Big", "src", "index.js")).Length);
        Assert.Equal(4, new FileInfo(Path.Combine(relay, "Big", "src", "lib", "index.js")).Length);
    });

    [Fact]
    public void Too_many_files_are_refused()
    {
        var clockless = Enumerable.Range(0, ShareStore.MaxFolderItems + 1).Select(i => ($"f{i}", 1L, "A")).ToList();
        var store = new ShareStore(Path.Combine(Path.GetTempPath(), "cssd-many-" + Guid.NewGuid().ToString("N")), () => DateTimeOffset.Now);
        var (o, error) = store.OfferFromDevice(new PairedDevice { Id = "a", Name = "A" }, new PairedDevice { Id = "b", Name = "B" }, clockless);
        Assert.Null(o);
        Assert.Contains(ShareStore.MaxFolderItems.ToString(), error);
    }
}
