using TwitchVodDownloader.Core.Util;

namespace TwitchVodDownloader.Core.Tests;

public class FileNamesTests
{
    [Theory]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("  trailing dots... ", "trailing dots")]
    [InlineData("CON", "_CON")]
    [InlineData("???", "___")]
    [InlineData("", "download")]
    public void Sanitizes(string input, string expected) => Assert.Equal(expected, FileNames.Sanitize(input));

    [Fact]
    public void Truncates_long_names() => Assert.Equal(180, FileNames.Sanitize(new string('x', 500)).Length);
}
