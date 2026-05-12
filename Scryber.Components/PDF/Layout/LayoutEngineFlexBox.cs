using System;
using System.Collections.Generic;
using Scryber.Components;
using Scryber.Drawing;
using Scryber.Styles;

namespace Scryber.PDF.Layout
{
    public class LayoutEngineFlexBox : LayoutEnginePanel
    {
        // True while we are in row layout mode — gates the column-break injection.
        private bool _isRowMode;

        public LayoutEngineFlexBox(ContainerComponent container, IPDFLayoutEngine parent)
            : base(container, parent)
        {
        }

        protected override void DoLayoutBlockComponent(PDFPositionOptions position, PDFColumnOptions columnOptions)
        {
            var flex      = this.FullStyle.Flex;
            var direction = flex.Direction;

            if (direction == FlexDirection.Column || direction == FlexDirection.ColumnReverse)
            {
                var gap    = flex.Gap;
                var rowGap = this.FullStyle.IsValueDefined(StyleKeys.FlexRowGapKey) ? flex.RowGap : gap;
                if (rowGap.PointsValue > 0)
                    columnOptions = new PDFColumnOptions() { AlleyWidth = rowGap };

                _isRowMode = false;
                base.DoLayoutBlockComponent(position, columnOptions);
            }
            else
            {
                int childCount = CountVisibleChildren();
                if (childCount <= 0)
                {
                    _isRowMode = false;
                    base.DoLayoutBlockComponent(position, columnOptions);
                    return;
                }

                var gap    = flex.Gap;
                var colGap = this.FullStyle.IsValueDefined(StyleKeys.FlexColumnGapKey) ? flex.ColumnGap : gap;

                double containerW = position.Width.HasValue
                    ? position.Width.Value.PointsValue
                    : this.DocumentLayout.CurrentPage.LastOpenBlock()?.AvailableBounds.Width.PointsValue ?? 0;

                var widths = ComputeColumnWidths(childCount, containerW, colGap.PointsValue);

                var rowCols = new PDFColumnOptions()
                {
                    ColumnCount  = childCount,
                    AlleyWidth   = colGap,
                    ColumnWidths = widths
                };

                // Capture parent region so we can find the new block after layout.
                var parentBlock  = this.DocumentLayout.CurrentPage.LastOpenBlock();
                var parentRegion = parentBlock?.CurrentRegion;
                int priorCount   = parentRegion?.Contents.Count ?? 0;

                _isRowMode = true;
                base.DoLayoutBlockComponent(position, rowCols);
                _isRowMode = false;

                // Post-layout: apply align-items and justify-content.
                if (parentRegion != null && parentRegion.Contents.Count > priorCount)
                {
                    var flexBlock = parentRegion.Contents[parentRegion.Contents.Count - 1] as PDFLayoutBlock;
                    if (flexBlock != null && flexBlock.Columns.Length > 0)
                    {
                        var align   = flex.AlignItems;
                        var justify = flex.JustifyContent;

                        if (align != FlexAlignMode.Stretch && align != FlexAlignMode.FlexStart)
                            ApplyAlignItems(flexBlock, align);

                        if (justify != FlexJustify.FlexStart)
                            ApplyJustifyContent(flexBlock, justify);
                    }
                }
            }
        }

        /// <summary>
        /// Override DoLayoutChildren: in row mode, force a column break after each flex item
        /// so each child occupies exactly one column region.
        /// </summary>
        protected override void DoLayoutChildren(ComponentList children)
        {
            if (!_isRowMode)
            {
                base.DoLayoutChildren(children);
                return;
            }

            bool first = true;
            foreach (Component comp in children)
            {
                if (!comp.Visible) continue;

                bool isItem = IsFlexItem(comp);

                if (isItem && !first)
                {
                    PDFLayoutBlock block = this.DocumentLayout.CurrentPage.LastOpenBlock();
                    PDFLayoutRegion reg  = block.CurrentRegion;
                    bool newPage;
                    this.MoveToNextRegion(Unit.Zero, ref reg, ref block, out newPage);
                }

                this.DoLayoutAChild(comp);
                if (isItem) first = false;

                if (!this.ContinueLayout
                    || this.DocumentLayout.CurrentPage.IsClosed
                    || this.DocumentLayout.CurrentPage.CurrentBlock == null)
                    break;
            }
        }

        // -----------------------------------------------------------------------
        // Post-layout: align-items (cross-axis / Y in row mode)
        // -----------------------------------------------------------------------

        private static void ApplyAlignItems(PDFLayoutBlock flexBlock, FlexAlignMode align)
        {
            int colCount = flexBlock.Columns.Length;
            if (colCount < 2) return;

            // Find the tallest first child block across all columns (content height, not region height).
            double maxH = 0;
            for (int i = 0; i < colCount; i++)
            {
                double h = FirstChildHeight(flexBlock.Columns[i]);
                if (h > maxH) maxH = h;
            }

            if (maxH <= 0) return;

            for (int i = 0; i < colCount; i++)
            {
                var    col      = flexBlock.Columns[i];
                double childH   = FirstChildHeight(col);
                double diff     = maxH - childH;
                if (diff <= 0.5) continue; // Already at max height.

                double yOffset = align switch
                {
                    FlexAlignMode.FlexEnd => diff,
                    FlexAlignMode.Center  => diff / 2.0,
                    _                     => 0
                };

                if (yOffset <= 0) continue;

                // Offset every child block in this column.
                foreach (var item in col.Contents)
                {
                    if (item is PDFLayoutBlock child)
                    {
                        var b = child.TotalBounds;
                        b.Y = b.Y + new Unit(yOffset, PageUnits.Points);
                        child.TotalBounds = b;
                    }
                }
            }
        }

        private static double FirstChildHeight(PDFLayoutRegion col)
        {
            foreach (var item in col.Contents)
            {
                if (item is PDFLayoutBlock b)
                    return b.TotalBounds.Height.PointsValue;
            }
            return 0;
        }

        // -----------------------------------------------------------------------
        // Post-layout: justify-content (main-axis / X in row mode)
        // -----------------------------------------------------------------------

        private static void ApplyJustifyContent(PDFLayoutBlock flexBlock, FlexJustify justify)
        {
            int colCount = flexBlock.Columns.Length;
            if (colCount < 1) return;

            double containerW = flexBlock.TotalBounds.Width.PointsValue;
            double totalColW  = 0;
            for (int i = 0; i < colCount; i++)
                totalColW += flexBlock.Columns[i].TotalBounds.Width.PointsValue;

            double leftover = containerW - totalColW;
            if (leftover < 1.0) return; // Items fill the container — nothing to distribute.

            double startOffset = 0;
            double gapBetween  = 0;

            switch (justify)
            {
                case FlexJustify.FlexEnd:
                    startOffset = leftover;
                    break;
                case FlexJustify.Center:
                    startOffset = leftover / 2.0;
                    break;
                case FlexJustify.SpaceBetween:
                    if (colCount > 1)
                        gapBetween = leftover / (colCount - 1);
                    else
                        startOffset = leftover / 2.0; // single item: centre
                    break;
                case FlexJustify.SpaceAround:
                    double aroundUnit = leftover / colCount;
                    startOffset = aroundUnit / 2.0;
                    gapBetween  = aroundUnit;
                    break;
            }

            double xOffset = startOffset;
            for (int i = 0; i < colCount; i++)
            {
                if (xOffset >= 0.5)
                {
                    var col    = flexBlock.Columns[i];
                    var bounds = col.TotalBounds;
                    bounds.X   = bounds.X + new Unit(xOffset, PageUnits.Points);
                    col.TotalBounds = bounds;
                }
                xOffset += gapBetween;
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static bool IsFlexItem(IComponent child)
            => child is IContainerComponent && child is Component c && c.Visible;

        private int CountVisibleChildren()
        {
            var container = this.Component as IContainerComponent;
            if (container == null || !container.HasContent) return 0;
            int count = 0;
            foreach (var child in container.Content)
                if (IsFlexItem(child)) count++;
            return count;
        }

        /// <summary>
        /// Computes per-column width fractions.
        /// When any item has flex-grow > 0 the fractions are proportional to grow values.
        /// When all items have flex-grow = 0 the fractions are derived from their explicit
        /// widths (width or flex-basis) if set, so that justify-content can redistribute
        /// any leftover space post-layout.
        /// </summary>
        private ColumnWidths ComputeColumnWidths(int count, double containerWidthPts, double alleyPts)
        {
            var container = this.Component as IContainerComponent;
            if (container == null || !container.HasContent) return ColumnWidths.Empty;

            double[] grows       = new double[count];
            double   totalGrow   = 0.0;
            bool     anyPositive = false;
            int      i           = 0;

            foreach (var child in container.Content)
            {
                if (!IsFlexItem(child)) continue;
                if (i >= count) break;

                double grow = 1.0;
                if (child is IStyledComponent sc && sc.Style != null
                    && sc.Style.IsValueDefined(StyleKeys.FlexGrowKey))
                    grow = sc.Style.GetValue(StyleKeys.FlexGrowKey, 1.0);

                grows[i] = grow;
                totalGrow += grow;
                if (grow > 0) anyPositive = true;
                i++;
            }

            // Any grow > 0: use proportional grow ratios (existing behaviour).
            if (anyPositive && totalGrow > 0)
            {
                double[] pct = new double[count];
                for (int j = 0; j < count; j++)
                    pct[j] = grows[j] / totalGrow;
                return new ColumnWidths(pct);
            }

            // All grow = 0: try to read explicit widths from child styles.
            double effectiveW = containerWidthPts - alleyPts * (count - 1);
            if (effectiveW <= 0) return ColumnWidths.Empty;

            double[] fractions = new double[count];
            bool     anySet    = false;
            i = 0;

            foreach (var child in container.Content)
            {
                if (!IsFlexItem(child)) continue;
                if (i >= count) break;

                if (child is IStyledComponent sc && sc.Style != null)
                {
                    Unit w = Unit.Zero;
                    if (sc.Style.IsValueDefined(StyleKeys.SizeWidthKey))
                        w = sc.Style.Size.Width;
                    else if (sc.Style.IsValueDefined(StyleKeys.FlexBasisKey) && !sc.Style.Flex.BasisAuto)
                        w = sc.Style.Flex.Basis;

                    if (w.PointsValue > 0)
                    {
                        fractions[i] = w.PointsValue / effectiveW;
                        anySet = true;
                    }
                }
                i++;
            }

            return anySet ? new ColumnWidths(fractions) : ColumnWidths.Empty;
        }
    }
}
