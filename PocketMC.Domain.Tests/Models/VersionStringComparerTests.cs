using System;
using System.Collections.Generic;
using System.Linq;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Domain.Tests.Models
{
    public class VersionStringComparerTests
    {
        [Fact]
        public void Compare_ShouldCorrectlySortNumericVersions()
        {
            var comparer = new VersionStringComparer();

            // 21.1.230 should be greater than 21.1.99
            Assert.True(comparer.Compare("21.1.230", "21.1.99") > 0);
            Assert.True(comparer.Compare("21.1.99", "21.1.230") < 0);

            // 21.1.99 should be greater than 21.1.9
            Assert.True(comparer.Compare("21.1.99", "21.1.9") > 0);

            // Same versions should be equal
            Assert.Equal(0, comparer.Compare("21.1.230", "21.1.230"));
        }

        [Fact]
        public void Compare_ShouldCorrectlySortVersionsWithDifferentPartCounts()
        {
            var comparer = new VersionStringComparer();

            // 21.1.0 vs 21.1
            Assert.Equal(0, comparer.Compare("21.1.0", "21.1"));
            
            // 21.1.1 vs 21.1
            Assert.True(comparer.Compare("21.1.1", "21.1") > 0);
        }

        [Fact]
        public void Compare_ShouldHandleBetaAndExperimentalSuffixes()
        {
            var comparer = new VersionStringComparer();

            // Stable (no suffix) should be greater than beta
            Assert.True(comparer.Compare("20.2.86", "20.2.86-beta") > 0);
            Assert.True(comparer.Compare("20.2.86-beta", "20.2.86") < 0);

            // Suffix string comparison (e.g. beta vs alpha, or beta.2 vs beta.1)
            Assert.True(comparer.Compare("20.2.86-beta.2", "20.2.86-beta.1") > 0);
            Assert.True(comparer.Compare("20.2.86-alpha", "20.2.86-beta") < 0);
        }

        [Fact]
        public void Compare_ShouldHandleNullOrEmpty()
        {
            var comparer = new VersionStringComparer();

            Assert.Equal(0, comparer.Compare(null, null));
            Assert.True(comparer.Compare("21.1.230", null) > 0);
            Assert.True(comparer.Compare(null, "21.1.230") < 0);
            Assert.True(comparer.Compare("21.1.230", "") > 0);
        }

        [Fact]
        public void OrderByDescending_ShouldPutLatestVersionsFirst()
        {
            var comparer = new VersionStringComparer();
            var versions = new List<string>
            {
                "21.1.9",
                "21.1.230",
                "21.1.99",
                "21.1.100",
                "21.1.230-beta",
                "21.1"
            };

            var sorted = versions.OrderByDescending(v => v, comparer).ToList();

            var expected = new List<string>
            {
                "21.1.230",
                "21.1.230-beta",
                "21.1.100",
                "21.1.99",
                "21.1.9",
                "21.1" // 21.1 equals 21.1.0 which is less than 21.1.9
            };

            Assert.Equal(expected, sorted);
        }
    }
}
