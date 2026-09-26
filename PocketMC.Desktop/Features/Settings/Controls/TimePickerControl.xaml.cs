using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PocketMC.Desktop.Features.Settings.Controls
{
    public partial class TimePickerControl : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty SelectedTimeProperty =
            DependencyProperty.Register(
                nameof(SelectedTime),
                typeof(TimeSpan?),
                typeof(TimePickerControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedTimeChanged));

        public TimeSpan? SelectedTime
        {
            get => (TimeSpan?)GetValue(SelectedTimeProperty);
            set => SetValue(SelectedTimeProperty, value);
        }

        public IReadOnlyList<string> HoursList { get; } =
            Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();

        public IReadOnlyList<string> MinutesList { get; } =
            Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();

        private string _tempHour = "04";
        public string TempHour
        {
            get => _tempHour;
            set
            {
                if (_tempHour != value)
                {
                    _tempHour = value;
                    OnPropertyChanged(nameof(TempHour));
                }
            }
        }

        private string _tempMinute = "00";
        public string TempMinute
        {
            get => _tempMinute;
            set
            {
                if (_tempMinute != value)
                {
                    _tempMinute = value;
                    OnPropertyChanged(nameof(TempMinute));
                }
            }
        }

        public string SelectedHourText => (SelectedTime ?? new TimeSpan(4, 0, 0)).Hours.ToString("D2");
        public string SelectedMinuteText => (SelectedTime ?? new TimeSpan(4, 0, 0)).Minutes.ToString("D2");

        public event PropertyChangedEventHandler? PropertyChanged;

        public TimePickerControl()
        {
            InitializeComponent();
            FlyoutPopup.Opened += OnPopupOpened;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged += OnWindowMovedOrResized;
                window.SizeChanged += OnWindowMovedOrResized;
                window.Deactivated += OnWindowMovedOrResized;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged -= OnWindowMovedOrResized;
                window.SizeChanged -= OnWindowMovedOrResized;
                window.Deactivated -= OnWindowMovedOrResized;
            }
            FlyoutPopup.IsOpen = false;
        }

        private void OnWindowMovedOrResized(object? sender, EventArgs e)
        {
            if (FlyoutPopup.IsOpen)
            {
                FlyoutPopup.IsOpen = false;
            }
        }

        private void OnPopupOpened(object? sender, EventArgs e)
        {
            var time = SelectedTime ?? new TimeSpan(4, 0, 0);
            TempHour = time.Hours.ToString("D2");
            TempMinute = time.Minutes.ToString("D2");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                HoursListBox.ScrollIntoView(TempHour);
                MinutesListBox.ScrollIntoView(TempMinute);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnConfirmClicked(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TempHour, out int h) && int.TryParse(TempMinute, out int m))
            {
                SelectedTime = new TimeSpan(h, m, 0);
            }
            FlyoutPopup.IsOpen = false;
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            FlyoutPopup.IsOpen = false;
        }

        private static void OnSelectedTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimePickerControl picker)
            {
                picker.OnPropertyChanged(nameof(SelectedHourText));
                picker.OnPropertyChanged(nameof(SelectedMinuteText));
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
