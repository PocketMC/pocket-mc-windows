using System;
using System.Windows.Media;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Features.Shell
{
    public sealed class ShellUIStateService : IShellUIStateService
    {
        private string? _breadcrumbCurrentText;
        public string? BreadcrumbCurrentText
        {
            get => _breadcrumbCurrentText;
            set { _breadcrumbCurrentText = value; OnStateChanged?.Invoke(); }
        }

        private bool _isBreadcrumbVisible;
        public bool IsBreadcrumbVisible
        {
            get => _isBreadcrumbVisible;
            set { _isBreadcrumbVisible = value; OnStateChanged?.Invoke(); }
        }

        private string? _titleBarTitle;
        public string? TitleBarTitle
        {
            get => _titleBarTitle;
            set { _titleBarTitle = value; OnStateChanged?.Invoke(); }
        }

        private string? _titleBarStatusText;
        public string? TitleBarStatusText
        {
            get => _titleBarStatusText;
            set { _titleBarStatusText = value; OnStateChanged?.Invoke(); }
        }

        private Brush? _titleBarStatusBrush;
        public Brush? TitleBarStatusBrush
        {
            get => _titleBarStatusBrush;
            set { _titleBarStatusBrush = value; OnStateChanged?.Invoke(); }
        }

        private bool _isTitleBarContextVisible;
        public bool IsTitleBarContextVisible
        {
            get => _isTitleBarContextVisible;
            set { _isTitleBarContextVisible = value; OnStateChanged?.Invoke(); }
        }

        private string? _globalHealthStatusText;
        public string? GlobalHealthStatusText
        {
            get => _globalHealthStatusText;
            set { _globalHealthStatusText = value; OnStateChanged?.Invoke(); }
        }

        private Brush? _globalHealthStatusBrush;
        public Brush? GlobalHealthStatusBrush
        {
            get => _globalHealthStatusBrush;
            set { _globalHealthStatusBrush = value; OnStateChanged?.Invoke(); }
        }

        public event Action? OnStateChanged;

        public void UpdateBreadcrumb(string? label)
        {
            BreadcrumbCurrentText = label;
            IsBreadcrumbVisible = !string.IsNullOrEmpty(label);
        }

        public void SetTitleBarContext(string? title, string? statusText, Brush? statusBrush)
        {
            TitleBarTitle = title;
            TitleBarStatusText = statusText;
            TitleBarStatusBrush = statusBrush;
            IsTitleBarContextVisible = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(statusText);
        }

        public void ClearTitleBarContext()
        {
            TitleBarTitle = null;
            TitleBarStatusText = null;
            IsTitleBarContextVisible = false;
        }
    }
}
