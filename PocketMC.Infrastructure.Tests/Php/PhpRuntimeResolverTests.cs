using System;
using System.IO;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Php;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Php
{
    public sealed class PhpRuntimeResolverTests
    {
        [Theory]
        [InlineData("5.15.0", "8.2")]
        [InlineData("5.0.0", "8.2")]
        [InlineData("v5.12.1", "8.2")]
        [InlineData("4.23.0", "8.0")]
        [InlineData("4.0.0", "8.0")]
        [InlineData("v4.1.0", "8.0")]
        [InlineData("6.0.0", "8.3")]
        [InlineData("v6.1.0", "8.3")]
        [InlineData("", "8.2")]
        [InlineData(null, "8.2")]
        public void GetRequiredPhpVersion_MapsPocketMineVersionsCorrectly(string? pocketMineVersion, string expectedPhpVersion)
        {
            Assert.Equal(expectedPhpVersion, PhpRuntimeResolver.GetRequiredPhpVersion(pocketMineVersion));
        }

        [Fact]
        public void GetBundledPhpVersions_ContainsAllSupportedVersions()
        {
            var versions = PhpRuntimeResolver.GetBundledPhpVersions();
            Assert.Contains("8.0", versions);
            Assert.Contains("8.1", versions);
            Assert.Contains("8.2", versions);
            Assert.Contains("8.3", versions);
        }

        [Fact]
        public void GetDefinition_ReturnsValidMetadata_ForSupportedVersions()
        {
            var def82 = PhpRuntimeResolver.GetDefinition("8.2");
            Assert.NotNull(def82);
            Assert.Equal("pm5-php-8.2-latest", def82!.Tag);
            Assert.False(string.IsNullOrWhiteSpace(def82.FallbackDownloadUrl));

            var def80 = PhpRuntimeResolver.GetDefinition("8.0");
            Assert.NotNull(def80);
            Assert.Equal("pm4-php-8.0-latest", def80!.Tag);
        }
    }
}
