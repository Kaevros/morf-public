using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Widgates
{
    internal sealed class AdaptiveChoiceGrid : UniformGrid
    {
        internal int MaximumColumns = 3;
        internal double MinimumTileWidth = 110;
        protected override Size MeasureOverride(Size availableSize)
        {
            Columns = double.IsInfinity(availableSize.Width) ? MaximumColumns
                : Math.Max(1, Math.Min(MaximumColumns, (int)(availableSize.Width / MinimumTileWidth)));
            return base.MeasureOverride(availableSize);
        }
    }
}
