using System;
using System.Runtime.InteropServices;
using Xunit;

namespace PocketMC.E2E.Tests.Infrastructure
{
    public class UnixTheoryAttribute : TheoryAttribute
    {
        public UnixTheoryAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Skip = "This test runs only on Unix-like operating systems (Linux/OSX).";
            }
        }
    }
}
