using System.Reflection;

internal static class ThumbProbe
{
    /// <summary>Runs the PC app's own thumbnail maker on one file and says what happens (size, or the exception).</summary>
    public static int Run(string path)
    {
        var t = typeof(CloudSSD.Desktop.Views.DevicesView).Assembly.GetType("CloudSSD.Desktop.MobileThumbnailer")!;
        foreach (var name in new[] { "Make", "Picture" })
        {
            var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                var r = m.Invoke(null, new object[] { path, 160 });
                Console.WriteLine($"{name}: " + (r is byte[] b ? b.Length + " bytes" : r?.ToString() ?? "null"));
            }
            catch (TargetInvocationException e)
            {
                Console.WriteLine($"{name} threw: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}");
            }
        }
        return 0;
    }
}
