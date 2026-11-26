using CloudSSD.Desktop;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The list behind the bell (top right of the PC app): kept, counted, and a click goes to its place.</summary>
[Collection("NotificationCenter")]   // one static list: these tests do not run side by side
public class NotificationCenterTests
{
    [Fact]
    public void Notes_are_kept_newest_first_counted_and_saved_across_a_restart()
    {
        var dir = Directory.CreateTempSubdirectory("cssd_notes_").FullName;
        var file = Path.Combine(dir, "notifications.json");
        try
        {
            NotificationCenter.Load(file);
            NotificationCenter.Clear();
            NotificationCenter.Add("Redmi 12 asks for access", "Browse the drives", "access:r1:dev1", "access");
            NotificationCenter.Add("Files delivered", "Tab received 2 file(s).", "page:Devices", "files");
            NotificationCenter.Add("RAM auto mode", "Moved 3 idle app(s)…");
            Assert.Equal(new[] { "RAM auto mode", "Files delivered", "Redmi 12 asks for access" }, NotificationCenter.Items.Select(i => i.Title));
            Assert.Equal(3, NotificationCenter.Unread);

            // A request answered in its own window: its line is read.
            NotificationCenter.MarkRead("access:r1:dev1");
            Assert.Equal(2, NotificationCenter.Unread);

            // After a restart the list is still there, read state and all.
            NotificationCenter.Load(file);
            Assert.Equal(3, NotificationCenter.Items.Count);
            Assert.True(NotificationCenter.Items.Single(i => i.Target == "access:r1:dev1").Read);
            Assert.Equal("", NotificationCenter.Items.Single(i => i.Kind == "access").Icon);

            NotificationCenter.MarkAllRead();
            Assert.Equal(0, NotificationCenter.Unread);
            NotificationCenter.Clear();
            NotificationCenter.Load(file);
            Assert.Empty(NotificationCenter.Items);
        }
        finally
        {
            NotificationCenter.Clear();
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_click_opens_the_place_and_only_the_last_100_stay()
    {
        var dir = Directory.CreateTempSubdirectory("cssd_notes_").FullName;
        var before = NotificationCenter.Opener;
        try
        {
            NotificationCenter.Load(Path.Combine(dir, "notifications.json"));
            NotificationCenter.Clear();
            var opened = new List<string>();
            NotificationCenter.Opener = t => { opened.Add(t); return true; };

            NotificationCenter.Add("Redmi 12 paired with this PC", "Choose what it may do", "perm:dev1", "access");
            NotificationCenter.Add("Clipboard from Redmi 12", "hello");      // nowhere to go
            var withPlace = NotificationCenter.Items.Single(i => i.Target == "perm:dev1");
            var without = NotificationCenter.Items.Single(i => i.Target == null);
            Assert.True(withPlace.CanOpen);
            Assert.False(without.CanOpen);

            Assert.True(NotificationCenter.Open(withPlace));
            Assert.False(NotificationCenter.Open(without));                  // read, but goes nowhere
            Assert.Equal(new[] { "perm:dev1" }, opened);
            Assert.True(withPlace.Read && without.Read);

            for (int i = 0; i < NotificationCenter.MaxItems + 15; i++) NotificationCenter.Add("n" + i, "");
            Assert.Equal(NotificationCenter.MaxItems, NotificationCenter.Items.Count);
            Assert.Equal("n" + (NotificationCenter.MaxItems + 14), NotificationCenter.Items[0].Title);

            NotificationCenter.RefreshTimes();
            Assert.All(NotificationCenter.Items, i => Assert.False(string.IsNullOrEmpty(i.When)));
        }
        finally
        {
            NotificationCenter.Opener = before;
            NotificationCenter.Clear();
            Directory.Delete(dir, true);
        }
    }
}
