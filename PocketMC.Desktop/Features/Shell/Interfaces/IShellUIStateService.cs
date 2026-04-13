using System;
using System.Windows.Media;

namespace PocketMC.Desktop.Features.Shell.Interfaces
{
    /// <summary>
    /// Represents the global UI state for the application shell, including breadcrumbs and health status.
    /// </summary>
    public interface IShellUIStateService
    {
        string? BreadcrumbCurrentText { get; set; }
        bool IsBreadcrumbVisible { get; set; }

        string? TitleBarTitle { get; set; }
        string? TitleBarStatusText { get; set; }
        Brush? TitleBarStatusBrush { get; set; }
        bool IsTitleBarContextVisible { get; set; }

        string? GlobalHealthStatusText { get; set; }
        Brush? GlobalHealthStatusBrush { get; set; }

        event Action? OnStateChanged;

        void UpdateBreadcrumb(string? label);
        void SetTitleBarContext(string? title, string? statusText, Brush? statusBrush);
        void ClearTitleBarContext();
    }
}
