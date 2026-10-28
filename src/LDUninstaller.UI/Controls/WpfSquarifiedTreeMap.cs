using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LDUninstaller.Core.Algorithms;
using LDUninstaller.Core.Models;

namespace LDUninstaller.UI.Controls
{
    public class WpfSquarifiedTreeMap : FrameworkElement
    {
        private class RenderableBlock
        {
            public TreeMapItem<ApplicationUninstallerEntry> Item { get; set; } = null!;
            public Rect Rect { get; set; }
            public Brush Brush { get; set; } = null!;
            public FormattedText? TitleText { get; set; }
            public Point TitlePos { get; set; }
            public FormattedText? SubText { get; set; }
            public Point SubPos { get; set; }
        }

        private List<TreeMapItem<ApplicationUninstallerEntry>> _items = new();
        private List<RenderableBlock> _renderBlocks = new();
        private TreeMapItem<ApplicationUninstallerEntry>? _hoveredItem;
        public event Action<ApplicationUninstallerEntry>? OnItemSelected;

        private static readonly Brush[] Palette;
        private static readonly Pen BorderPen;
        private static readonly Pen HoverPen;
        private static readonly Brush BackdropBrush;
        private static readonly Brush EmptyTextBrush;
        private static readonly Brush SubTextBrush;
        private static readonly Typeface TitleTypeface;
        private static readonly Typeface SubTypeface;

        static WpfSquarifiedTreeMap()
        {
            Palette = new Brush[]
            {
                new SolidColorBrush(Color.FromRgb(37, 99, 235)),   // Modern Blue
                new SolidColorBrush(Color.FromRgb(16, 185, 129)),  // Emerald
                new SolidColorBrush(Color.FromRgb(139, 92, 246)),  // Violet
                new SolidColorBrush(Color.FromRgb(245, 158, 11)),  // Amber
                new SolidColorBrush(Color.FromRgb(14, 165, 233)),  // Sky
                new SolidColorBrush(Color.FromRgb(239, 68, 68)),   // Rose
                new SolidColorBrush(Color.FromRgb(99, 102, 241)),  // Indigo
                new SolidColorBrush(Color.FromRgb(20, 184, 166))   // Teal
            };

            foreach (var b in Palette) b.Freeze();

            BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 15, 23, 42)), 1.5);
            BorderPen.Freeze();

            HoverPen = new Pen(Brushes.White, 2.0);
            HoverPen.Freeze();

            BackdropBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            BackdropBrush.Freeze();

            EmptyTextBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            EmptyTextBrush.Freeze();

            SubTextBrush = new SolidColorBrush(Color.FromArgb(200, 240, 240, 240));
            SubTextBrush.Freeze();

            TitleTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            SubTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        }

        public WpfSquarifiedTreeMap()
        {
            ClipToBounds = true;
            MouseMove += WpfSquarifiedTreeMap_MouseMove;
            MouseLeave += (s, e) => { _hoveredItem = null; InvalidateVisual(); };
            MouseLeftButtonDown += WpfSquarifiedTreeMap_MouseLeftButtonDown;
        }

        public void SetData(IEnumerable<ApplicationUninstallerEntry> entries)
        {
            var valid = entries
                .Where(e => e.EstimatedSize.Bytes > 1024 * 1024) // > 1 MB
                .Select(e => new TreeMapItem<ApplicationUninstallerEntry>(e, e.EstimatedSize.Bytes))
                .ToList();

            _items = valid;
            Recalculate();
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Recalculate();
            InvalidateVisual();
        }

        // ZERO-ALLOCATION RENDER PIPELINE:
        // Layout and FormattedText objects are pre-generated during size/data change.
        // OnRender performs 0 allocations, eliminating GC pauses during mouse hover.
        private void Recalculate()
        {
            if (ActualWidth <= 10 || ActualHeight <= 10 || _items.Count == 0)
            {
                _renderBlocks.Clear();
                return;
            }

            var bounds = new System.Drawing.RectangleF(2, 2, (float)ActualWidth - 4, (float)ActualHeight - 4);
            _items = SquarifiedTreeMap.CalculateLayout(_items, bounds);

            var newBlocks = new List<RenderableBlock>(_items.Count);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            int index = 0;

            foreach (var item in _items)
            {
                var b = item.Bounds;
                if (b.Width < 2 || b.Height < 2) continue;

                var block = new RenderableBlock
                {
                    Item = item,
                    Rect = new Rect(b.X, b.Y, b.Width, b.Height),
                    Brush = Palette[index % Palette.Length]
                };
                index++;

                if (b.Width > 55 && b.Height > 28)
                {
                    string title = item.Value?.DisplayName ?? "Uygulama";
                    string sub = item.Value?.EstimatedSize.ToString() ?? "";
                    double maxW = Math.Max(10, b.Width - 12);

                    try
                    {
                        var ft = new FormattedText(
                            title,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            TitleTypeface,
                            11,
                            Brushes.White,
                            dpi)
                        {
                            MaxTextWidth = maxW,
                            MaxTextHeight = 16,
                            Trimming = TextTrimming.CharacterEllipsis
                        };
                        block.TitleText = ft;
                        block.TitlePos = new Point(b.X + 6, b.Y + 5);

                        if (b.Height > 44)
                        {
                            var ftSub = new FormattedText(
                                sub,
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                SubTypeface,
                                9.5,
                                SubTextBrush,
                                dpi)
                            {
                                MaxTextWidth = maxW,
                                MaxTextHeight = 14,
                                Trimming = TextTrimming.CharacterEllipsis
                            };
                            block.SubText = ftSub;
                            block.SubPos = new Point(b.X + 6, b.Y + 22);
                        }
                    }
                    catch { }
                }

                newBlocks.Add(block);
            }

            _renderBlocks = newBlocks;
        }

        private void WpfSquarifiedTreeMap_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            var hit = _items.FirstOrDefault(i =>
                p.X >= i.Bounds.X && p.X <= i.Bounds.Right &&
                p.Y >= i.Bounds.Y && p.Y <= i.Bounds.Bottom);

            if (hit != _hoveredItem)
            {
                _hoveredItem = hit;
                ToolTip = _hoveredItem != null
                    ? $"{_hoveredItem.Value.DisplayName}\nBoyut: {_hoveredItem.Value.EstimatedSize}\nYayıncı: {_hoveredItem.Value.Publisher}"
                    : null;
                InvalidateVisual();
            }
        }

        private void WpfSquarifiedTreeMap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_hoveredItem != null)
            {
                OnItemSelected?.Invoke(_hoveredItem.Value);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            // Draw dark backdrop
            dc.DrawRectangle(BackdropBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

            if (_renderBlocks.Count == 0)
            {
                var ftEmpty = new FormattedText(
                    "Görselleştirilecek disk verisi yok veya boyutlar hesaplanıyor...",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    TitleTypeface,
                    13,
                    EmptyTextBrush,
                    1.0);
                dc.DrawText(ftEmpty, new Point((ActualWidth - ftEmpty.Width) / 2, (ActualHeight - ftEmpty.Height) / 2));
                return;
            }

            // Zero allocations during render
            foreach (var block in _renderBlocks)
            {
                bool isHovered = block.Item == _hoveredItem;
                dc.DrawRoundedRectangle(block.Brush, isHovered ? HoverPen : BorderPen, block.Rect, 4, 4);

                if (block.TitleText != null)
                {
                    dc.DrawText(block.TitleText, block.TitlePos);
                }

                if (block.SubText != null)
                {
                    dc.DrawText(block.SubText, block.SubPos);
                }
            }
        }
    }
}
