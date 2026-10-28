using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LDUninstaller.Core.Algorithms
{
    public class TreeMapItem<T>
    {
        public T Value { get; set; }
        public double Weight { get; set; }
        public RectangleF Bounds { get; set; }

        public TreeMapItem(T value, double weight)
        {
            Value = value;
            Weight = weight;
        }
    }

    public static class SquarifiedTreeMap
    {
        public static List<TreeMapItem<T>> CalculateLayout<T>(IEnumerable<TreeMapItem<T>> items, RectangleF bounds)
        {
            var itemList = items.Where(x => x.Weight > 0).OrderByDescending(x => x.Weight).ToList();
            if (itemList.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
                return itemList;

            double totalWeight = itemList.Sum(x => x.Weight);
            if (totalWeight <= 0)
                return itemList;

            double totalArea = bounds.Width * bounds.Height;
            var normalized = itemList.Select(i => new Element<T>(i, (i.Weight / totalWeight) * totalArea)).ToList();

            LayoutSub(normalized, bounds);
            return itemList;
        }

        private class Element<T>
        {
            public TreeMapItem<T> Item { get; }
            public double Area { get; set; }

            public Element(TreeMapItem<T> item, double area)
            {
                Item = item;
                Area = area;
            }
        }

        private static void LayoutSub<T>(List<Element<T>> elements, RectangleF bounds)
        {
            if (elements.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
                return;

            if (elements.Count == 1)
            {
                elements[0].Item.Bounds = bounds;
                return;
            }

            var row = new List<Element<T>>();
            var remaining = new List<Element<T>>(elements);

            double side = Math.Min(bounds.Width, bounds.Height);
            if (side <= 0) return;

            while (remaining.Count > 0)
            {
                var candidate = remaining[0];
                var testRow = new List<Element<T>>(row) { candidate };

                if (row.Count == 0 || Worst(row, side) >= Worst(testRow, side))
                {
                    row.Add(candidate);
                    remaining.RemoveAt(0);
                }
                else
                {
                    break;
                }
            }

            // Layout row
            double rowArea = row.Sum(x => x.Area);
            RectangleF remainingBounds;

            if (bounds.Width >= bounds.Height)
            {
                // Vertical strip
                float rowWidth = (float)(rowArea / bounds.Height);
                float currentY = bounds.Y;

                foreach (var el in row)
                {
                    float h = (float)(el.Area / rowWidth);
                    el.Item.Bounds = new RectangleF(bounds.X, currentY, rowWidth, h);
                    currentY += h;
                }

                remainingBounds = new RectangleF(bounds.X + rowWidth, bounds.Y, Math.Max(0, bounds.Width - rowWidth), bounds.Height);
            }
            else
            {
                // Horizontal strip
                float rowHeight = (float)(rowArea / bounds.Width);
                float currentX = bounds.X;

                foreach (var el in row)
                {
                    float w = (float)(el.Area / rowHeight);
                    el.Item.Bounds = new RectangleF(currentX, bounds.Y, w, rowHeight);
                    currentX += w;
                }

                remainingBounds = new RectangleF(bounds.X, bounds.Y + rowHeight, bounds.Width, Math.Max(0, bounds.Height - rowHeight));
            }

            if (remaining.Count > 0)
            {
                LayoutSub(remaining, remainingBounds);
            }
        }

        private static double Worst<T>(List<Element<T>> row, double side)
        {
            if (row.Count == 0) return double.MaxValue;
            double s2 = side * side;
            double sum = row.Sum(x => x.Area);
            if (sum == 0 || s2 == 0) return double.MaxValue;

            double max = row.Max(x => x.Area);
            double min = row.Min(x => x.Area);

            return Math.Max((s2 * max) / (sum * sum), (sum * sum) / (s2 * min));
        }
    }
}
