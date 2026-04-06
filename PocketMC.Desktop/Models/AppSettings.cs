using System;

namespace PocketMC.Desktop.Models
{
    public class AppSettings
    {
        public string? AppRootPath { get; set; }
        public string? PlayitConfigDirectory { get; set; }
        public bool HasCompletedFirstLaunch { get; set; }
    }
}
