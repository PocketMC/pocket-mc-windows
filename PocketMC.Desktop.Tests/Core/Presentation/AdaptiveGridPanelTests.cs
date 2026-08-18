using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Core.Presentation;
using Xunit;

namespace PocketMC.Desktop.Tests.Core.Presentation
{
    public sealed class AdaptiveGridPanelTests
    {
        [Fact]
        public void Measure_WithZeroWidth_ReturnsZeroSize()
        {
            RunInSta(() =>
            {
                var panel = new AdaptiveGridPanel
                {
                    MinColumnWidth = 340,
                    HorizontalSpacing = 16,
                    VerticalSpacing = 16
                };

                panel.Measure(new Size(0, 0));

                Assert.Equal(0, panel.DesiredSize.Width);
                Assert.Equal(0, panel.DesiredSize.Height);
            });
        }

        [Fact]
        public void Measure_WithMultipleChildren_CalculatesColumnsAndHeight()
        {
            RunInSta(() =>
            {
                var panel = new AdaptiveGridPanel
                {
                    MinColumnWidth = 340,
                    HorizontalSpacing = 16,
                    VerticalSpacing = 16
                };

                for (int i = 0; i < 6; i++)
                {
                    var border = new Border { Width = 340, Height = 200 };
                    panel.Children.Add(border);
                }

                // Available width 1100: (1100 + 16) / (340 + 16) = 1116 / 356 = 3 columns
                // 6 items across 3 columns = 2 rows
                // Total height: 200 * 2 + 16 (spacing) = 416
                panel.Measure(new Size(1100, 800));

                Assert.Equal(1100, panel.DesiredSize.Width);
                Assert.Equal(416, panel.DesiredSize.Height);
            });
        }

        [Fact]
        public void Measure_WithNarrowWidth_CalculatesSingleColumn()
        {
            RunInSta(() =>
            {
                var panel = new AdaptiveGridPanel
                {
                    MinColumnWidth = 340,
                    HorizontalSpacing = 16,
                    VerticalSpacing = 16
                };

                for (int i = 0; i < 3; i++)
                {
                    var border = new Border { Height = 150 };
                    panel.Children.Add(border);
                }

                // Available width 360 -> 1 column -> 3 rows of 150 + 2*16 = 482
                panel.Measure(new Size(360, 1000));

                Assert.Equal(360, panel.DesiredSize.Width);
                Assert.Equal(482, panel.DesiredSize.Height);
            });
        }

        [Fact]
        public void Arrange_PositionsItemsInGridColumns()
        {
            RunInSta(() =>
            {
                var panel = new AdaptiveGridPanel
                {
                    MinColumnWidth = 340,
                    HorizontalSpacing = 16,
                    VerticalSpacing = 16
                };

                var b1 = new Border { Height = 100 };
                var b2 = new Border { Height = 100 };
                var b3 = new Border { Height = 100 };
                var b4 = new Border { Height = 100 };

                panel.Children.Add(b1);
                panel.Children.Add(b2);
                panel.Children.Add(b3);
                panel.Children.Add(b4);

                // Width 728: (728 + 16) / (340 + 16) = 744 / 356 = 2 columns
                // Item width: (728 - 16) / 2 = 356
                panel.Measure(new Size(728, 600));
                panel.Arrange(new Rect(0, 0, 728, 600));

                Assert.Equal(356, b1.RenderSize.Width);
                Assert.Equal(356, b2.RenderSize.Width);
            });
        }

        private static void RunInSta(Action action)
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null)
            {
                throw new Exception("STA test thread threw exception", exception);
            }
        }
    }
}
