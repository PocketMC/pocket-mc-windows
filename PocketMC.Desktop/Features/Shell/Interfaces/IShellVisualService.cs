namespace PocketMC.Desktop.Features.Shell.Interfaces
{
    /// <summary>
    /// Manages the visual appearance of the application shell, including themes and performance-focused visual effects.
    /// </summary>
    public interface IShellVisualService
    {
        bool EnableMicaEffect { get; set; }
        void RequestMicaUpdate();
        void ApplyTheme(string theme);
    }
}
