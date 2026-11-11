using System.Linq;
using Newtonsoft.Json.Linq;
using vs2026_plugin.Services;
using Xunit;

namespace vs2026_plugin.Tests
{
    /// <summary>Plain-http server URLs send the token in clear (xygeni/xygeni-product-backlog#1438).</summary>
    public class InsecureUrlTests
    {
        [Theory]
        [InlineData("localhost")] [InlineData("LOCALHOST")] [InlineData("app.localhost")] [InlineData("127.0.0.1")]
        [InlineData("127.1.2.3")] [InlineData("::1")] [InlineData("[::1]")] [InlineData("0:0:0:0:0:0:0:1")]
        public void LoopbackHostsNeverLeaveTheMachine(string host) => Assert.True(InsecureUrl.IsLoopbackHost(host));

        [Theory]
        [InlineData("xygeni.local")] [InlineData("10.0.0.5")] [InlineData("localhost.evil.com")] [InlineData("127.evil.com")]
        [InlineData("127.0.0")] [InlineData("")] [InlineData(null)]
        public void OtherHostsAreNotLoopback(string host) => Assert.False(InsecureUrl.IsLoopbackHost(host));

        [Fact]
        public void HttpToAnyHostButLoopbackIsRefusedUnlessAllowed()
        {
            Assert.Null(InsecureUrl.Problem("https://api.xygeni.io", false));
            Assert.Null(InsecureUrl.Problem("http://localhost:8086/deps-doctor-service", false));
            Assert.Equal(InsecureUrl.Refused, InsecureUrl.Problem("http://xygeni.corp.local/deps-doctor-service", false));
            Assert.Equal(InsecureUrl.Refused, InsecureUrl.Problem("HTTP://api.xygeni.io", false));
            Assert.Null(InsecureUrl.Problem("http://xygeni.corp.local", true));
        }

        [Fact]
        public void AnHttpStringThatDoesNotParseFailsClosed()
        {
            Assert.True(InsecureUrl.IsInsecureRemote("http://[bad"));
            Assert.False(InsecureUrl.IsInsecureRemote("not a url"));
        }
    }

    /// <summary>Published MCP server version and checksum (#2381, #2382).</summary>
    public class McpVersionTests
    {
        private static readonly string Hash = new string('a', 64);

        [Fact]
        public void VersionsCompareNumerically()
        {
            Assert.True(McpVersion.IsNewer("1.10.0", "1.9.3"));
            Assert.False(McpVersion.IsNewer("1.2.0", "1.2.0"));
            Assert.True(McpVersion.IsNewer("1.2.0", null));
            Assert.False(McpVersion.IsNewer("not a version", "1.0.0"));
        }

        [Fact]
        public void ChecksumFileNeedsSixtyFourHexCharacters()
        {
            Assert.Equal(Hash, McpVersion.FromChecksumFile(Hash.ToUpperInvariant() + "  xygeni-mcp-server.jar\n"));
            Assert.Null(McpVersion.FromChecksumFile(new string('b', 63)));
            Assert.Null(McpVersion.FromChecksumFile("<Error>AccessDenied</Error>"));
        }

        [Fact]
        public void AnInstalledJarIsNeverReplacedWithoutAValidChecksum()
        {
            foreach (bool installed in new[] { true, false })
            {
                Assert.Equal(McpVersion.ChecksumAction.Verify, McpVersion.GetChecksumAction(200, Hash, installed));
                Assert.Equal(McpVersion.ChecksumAction.Skip, McpVersion.GetChecksumAction(200, null, installed));
                Assert.Equal(McpVersion.ChecksumAction.Skip, McpVersion.GetChecksumAction(null, null, installed));
                Assert.Equal(McpVersion.ChecksumAction.Skip, McpVersion.GetChecksumAction(503, null, installed));
            }
            Assert.Equal(McpVersion.ChecksumAction.Skip, McpVersion.GetChecksumAction(403, null, true));
            Assert.Equal(McpVersion.ChecksumAction.Skip, McpVersion.GetChecksumAction(404, null, true));
            Assert.Equal(McpVersion.ChecksumAction.InstallUnverified, McpVersion.GetChecksumAction(403, null, false));
            Assert.Equal(McpVersion.ChecksumAction.InstallUnverified, McpVersion.GetChecksumAction(404, null, false));
        }
    }

    /// <summary>The closed list of scanner options and the command line it ends up in (#415).</summary>
    public class ScanOptionsTests
    {
        [Theory]
        [InlineData("--upload")] [InlineData("-o")] [InlineData("--run")] [InlineData("--set-baseline")] [InlineData("--exclude-add=a")]
        public void WhatTheExtensionManagesIsRefused(string option) => Assert.False(ScanOptions.Validate(option).Ok);

        [Theory]
        [InlineData("--mute=x\" \"--set-baseline")]
        [InlineData("--custom-detectors-dir=x\" --upload -o C:\\p \"y")]
        [InlineData("--standard=a\u0001b")]
        public void AQuoteOrControlCharacterIsRefused(string option) => Assert.False(ScanOptions.Validate(option).Ok);

        [Fact]
        public void APathMayHaveSpaces() => Assert.True(ScanOptions.Validate("--mute=my mutes/.xygeni.mute.yml").Ok);

        [Theory]
        [InlineData("/etc/detectors")] [InlineData("\\\\server\\share")] [InlineData("C:\\conf")]
        [InlineData("~/detectors")] [InlineData("../other")] [InlineData("conf/../../x")]
        public void PathsTheScannerReadsStayInsideTheProject(string path)
        {
            Assert.False(ScanOptions.IsPathInsideProject(path));
            Assert.False(ScanOptions.Validate("--custom-detectors-dir=" + path).Ok);
        }

        [Fact]
        public void ARelativePathOrANamedMuteProfileIsAccepted()
        {
            Assert.True(ScanOptions.Validate("--conf=conf/custom.yml").Ok);
            Assert.True(ScanOptions.Validate("--mute=#team-profile").Ok);
        }
    }

    /// <summary>.xygeni\ide.json, shared by the four plugins and committed (#415).</summary>
    public class ScanConfigTests
    {
        [Fact]
        public void KeysThisVersionDoesNotKnowSurviveASave()
        {
            string json = ScanConfig.Defaults().WithScanType("quality", false)
                .ToJson("{\"scans\":[\"sast\"],\"future\":{\"x\":1}}");
            var root = JObject.Parse(json);
            Assert.Equal(1, (int)root["future"]["x"]);
            Assert.NotEqual(new[] { "sast" }, root["scans"].Values<string>().ToArray());
            Assert.Equal(ScanConfig.Defaults().ToJson(), ScanConfig.Defaults().ToJson("not json"));
        }

        [Fact]
        public void AnExcludeThatCouldBreakTheArgumentIsDropped()
        {
            var config = ScanConfig.Parse("{\"excludes\":[\"vendor/**\",\"x\\\" --upload \\\"y\"]}");
            Assert.Equal(new[] { "vendor/**" }, config.Excludes.ToArray());
        }
    }

    /// <summary>ProcessStartInfo.Arguments quoting, read back by CommandLineToArgvW.</summary>
    public class CommandLineArgumentsTests
    {
        [Theory]
        [InlineData("scan", "\"scan\"")]
        [InlineData("", "\"\"")]
        [InlineData("C:\\a b\\x.ps1", "\"C:\\a b\\x.ps1\"")]
        // A trailing backslash would escape the closing quote and merge the next argument.
        [InlineData("--exclude-add=build\\", "\"--exclude-add=build\\\\\"")]
        // A quote is escaped instead of closing the argument.
        [InlineData("--mute=x\" \"--set-baseline", "\"--mute=x\\\" \\\"--set-baseline\"")]
        [InlineData("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
        public void EachArgumentIsReadBackIntact(string arg, string expected) => Assert.Equal(expected, CommandLineArguments.Quote(arg));
    }
}
