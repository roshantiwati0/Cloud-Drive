using CloudSSD.Desktop.ViewModels;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The PC's transfer history in groups by kind of file, not one mixed list.</summary>
public class TransferCategoriesTests
{
    [Theory]
    [InlineData("beach.PNG", TransferCategories.Photos)]
    [InlineData("IMG_2041.heic", TransferCategories.Photos)]
    [InlineData("trip.mp4", TransferCategories.Videos)]
    [InlineData("song.m4a", TransferCategories.Music)]
    [InlineData("guide.pdf", TransferCategories.Documents)]
    [InlineData("notes.txt", TransferCategories.Documents)]
    [InlineData("game.apk", TransferCategories.Apps)]
    [InlineData("backup.zip", TransferCategories.Apps)]
    [InlineData("1b638429cd1f4815.bin", TransferCategories.Others)]
    [InlineData("README", TransferCategories.Others)]
    [InlineData("dot.", TransferCategories.Others)]
    [InlineData(null, TransferCategories.Others)]
    public void Each_file_goes_to_its_kind(string? name, string expected) => Assert.Equal(expected, TransferCategories.Of(name));

    [Fact]
    public void The_groups_come_in_one_fixed_order_and_each_has_an_icon()
    {
        Assert.Equal(new[] { "Photos", "Videos", "Music", "Documents", "Apps & archives", "Others" }, TransferCategories.Order);
        Assert.Equal(TransferCategories.Order.Count, TransferCategories.Order.Select(TransferCategories.Icon).Distinct().Count());
        foreach (var c in TransferCategories.Order.Append("All types"))
        {
            Assert.True(CloudSSD.Desktop.LocalizationManager.HasTranslation("Hinglish", c), "Hinglish missing: " + c);
            Assert.True(CloudSSD.Desktop.LocalizationManager.HasTranslation("हिन्दी (Hindi)", c), "Hindi missing: " + c);
        }
    }
}
