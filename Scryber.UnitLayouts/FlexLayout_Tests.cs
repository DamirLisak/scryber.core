using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scryber.Components;
using Scryber.Drawing;
using Scryber.PDF;
using Scryber.PDF.Layout;
using Scryber.Styles;

namespace Scryber.UnitLayouts
{
    [TestClass()]
    public class FlexLayout_Tests
    {
        private const string TestCategory = "Layout-Flex";

        // Page and container sizes chosen to give clean integer column widths.
        private const double PageW = 600;
        private const double PageH = 800;

        private PDFLayoutDocument _layout;

        private void Doc_LayoutComplete(object sender, LayoutEventArgs args)
        {
            _layout = args.Context.GetLayout<PDFLayoutDocument>();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static Document CreateDoc(out Page pg)
        {
            var doc = new Document();
            pg = new Page();
            pg.Style.PageStyle.Width  = PageW;
            pg.Style.PageStyle.Height = PageH;
            pg.Style.Padding.All = 0;
            doc.Pages.Add(pg);
            return doc;
        }

        private static Panel CreateFlexContainer(Page pg, FlexDirection dir = FlexDirection.Row, double width = PageW)
        {
            var panel = new Panel();
            panel.Style.Position.DisplayMode = DisplayMode.FlexBox;
            panel.Style.Flex.Direction       = dir;
            panel.Width = width;
            // Visible outer border so the container boundary is clear in the PDF.
            panel.Style.Border.LineStyle = LineType.Solid;
            panel.Style.Border.Width     = 1;
            panel.Style.Border.Color     = new Color(0, 0, 0);
            pg.Contents.Add(panel);
            return panel;
        }

        /// <summary>Adds a flex child Panel with an optional text label and border.</summary>
        private static Panel AddChild(Panel parent, double? height = null, double grow = 1.0,
                                      string label = null, Color? borderColor = null)
        {
            var child = new Div();
            child.Style.Flex.Grow = grow;
            child.Style.Padding.All = 4;
            child.Style.Border.LineStyle = LineType.Solid;
            child.Style.Border.Width     = 1;
            child.Style.Border.Color     = borderColor ?? new Color(0, 255, 0);
            if (height.HasValue)
                child.Height = height.Value;
            if (!string.IsNullOrEmpty(label))
                child.Contents.Add(new Label { Text = label });
            parent.Contents.Add(child);
            return child;
        }

        private static PDFLayoutBlock FindBlockOwner(PDFLayoutRegion region, Component owner)
        {
            foreach (var item in region.Contents)
            {
                if (item is PDFLayoutBlock b && b.Owner == owner)
                    return b;
            }
            return null;
        }

        private static PDFLayoutBlock FindFlexBlock(PDFLayoutRegion region)
        {
            foreach (var item in region.Contents)
            {
                if (item is PDFLayoutBlock b && b.Columns.Length > 1)
                    return b;
                if (item is PDFLayoutBlock nested)
                {
                    foreach (var col in nested.Columns)
                    {
                        var found = FindFlexBlock(col);
                        if (found != null) return found;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Recursively collects all text characters from a layout region,
        /// searching into nested blocks and lines.
        /// </summary>
        private static string CollectText(PDFLayoutRegion region)
        {
            var sb = new StringBuilder();
            AppendText(region, sb);
            return sb.ToString();
        }

        private static void AppendText(PDFLayoutRegion region, StringBuilder sb)
        {
            foreach (var item in region.Contents)
            {
                if (item is PDFLayoutLine line)
                {
                    foreach (var run in line.Runs)
                        if (run is PDFTextRunCharacter tc)
                            sb.Append(tc.Characters);
                }
                else if (item is PDFLayoutBlock block)
                {
                    foreach (var col in block.Columns)
                        AppendText(col, sb);
                }
            }
        }

        // -----------------------------------------------------------------------
        // FlexRow — basic two-child layout
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_TwoChildren_SideBySide()
        {
            var doc    = CreateDoc(out var pg);
            var panel  = CreateFlexContainer(pg);
            var child0 = AddChild(panel, height: 50, grow: 1, label: "Col 0");
            var child1 = AddChild(panel, height: 50, grow: 1, label: "Col 1");

            using (var ms = DocStreams.GetOutputStream("Flex_Row_TwoChildren_Equal.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(_layout, "Layout should not be null");
            Assert.AreEqual(1, _layout.AllPages.Count);

            var lpg        = _layout.AllPages[0];
            var pageRegion = lpg.ContentBlock.Columns[0];
            var panelBlock = pageRegion.Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock, "Panel layout block should not be null");

            // Flex row creates one column per child.
            Assert.AreEqual(2, panelBlock.Columns.Length, "Flex row with 2 children should produce 2 columns");

            var col0 = panelBlock.Columns[0];
            var col1 = panelBlock.Columns[1];

            // Each column should be half the container width.
            double expected = PageW / 2.0;
            Assert.AreEqual(expected, col0.TotalBounds.Width.PointsValue, 0.5, "Column 0 width should be half the container");
            Assert.AreEqual(expected, col1.TotalBounds.Width.PointsValue, 0.5, "Column 1 width should be half the container");

            // Column 0 starts at X=0; column 1 starts immediately after.
            Assert.AreEqual(0.0,      col0.TotalBounds.X.PointsValue, 0.5, "Column 0 should start at X=0");
            Assert.AreEqual(expected, col1.TotalBounds.X.PointsValue, 0.5, "Column 1 should start at X=half-width");

            // Each column should contain the matching child's layout block.
            Assert.IsNotNull(FindBlockOwner(col0, child0), "Column 0 should contain child0's layout block");
            Assert.IsNotNull(FindBlockOwner(col1, child1), "Column 1 should contain child1's layout block");

            // Each column should contain its label text.
            StringAssert.Contains(CollectText(col0), "Col 0", "Column 0 should contain label text");
            StringAssert.Contains(CollectText(col1), "Col 1", "Column 1 should contain label text");
        }

        // -----------------------------------------------------------------------
        // FlexRow — gap between children
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_Gap_ReducesColumnWidths()
        {
            const double gap = 20.0;

            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.Gap = new Unit(gap, PageUnits.Points);
            AddChild(panel, height: 50, grow: 1, label: "A");
            AddChild(panel, height: 50, grow: 1, label: "B");

            using (var ms = DocStreams.GetOutputStream("Flex_Row_Gap.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length, "Gap test should still produce 2 columns");

            // With one alley of 20pt, available = 600-20 = 580; each column = 290.
            double expected = (PageW - gap) / 2.0;
            Assert.AreEqual(expected, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 0.5, "Column 0 width with gap");
            Assert.AreEqual(expected, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 0.5, "Column 1 width with gap");

            // Text still present on each side of the gap.
            StringAssert.Contains(CollectText(panelBlock.Columns[0]), "A", "Column 0 text");
            StringAssert.Contains(CollectText(panelBlock.Columns[1]), "B", "Column 1 text");
        }

        // -----------------------------------------------------------------------
        // FlexRow — proportional grow
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_GrowRatio_DistributesSpace()
        {
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            AddChild(panel, height: 50, grow: 1, label: "Narrow");
            AddChild(panel, height: 50, grow: 2, label: "Wide");

            using (var ms = DocStreams.GetOutputStream("Flex_Row_GrowRatio.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            // grow=1 → 1/3 of 600 = 200; grow=2 → 2/3 of 600 = 400.
            Assert.AreEqual(200.0, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 0.5, "Column 0 (grow=1) should be 200pt");
            Assert.AreEqual(400.0, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 0.5, "Column 1 (grow=2) should be 400pt");

            StringAssert.Contains(CollectText(panelBlock.Columns[0]), "Narrow", "Narrow column text");
            StringAssert.Contains(CollectText(panelBlock.Columns[1]), "Wide",   "Wide column text");
        }

        // -----------------------------------------------------------------------
        // FlexRow — three children
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_ThreeChildren_EqualWidth()
        {
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            AddChild(panel, height: 50, label: "One");
            AddChild(panel, height: 50, label: "Two");
            AddChild(panel, height: 50, label: "Three");

            using (var ms = DocStreams.GetOutputStream("Flex_Row_ThreeChildren.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);

            Assert.AreEqual(3, panelBlock.Columns.Length, "Three children should produce three columns");

            double expected = PageW / 3.0;
            string[] labels = { "One", "Two", "Three" };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(expected, panelBlock.Columns[i].TotalBounds.Width.PointsValue, 0.5,
                    $"Column {i} should be 200pt");
                StringAssert.Contains(CollectText(panelBlock.Columns[i]), labels[i],
                    $"Column {i} should contain its label");
            }
        }

        // -----------------------------------------------------------------------
        // FlexColumn — children stacked vertically
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexColumn_TwoChildren_Stacked()
        {
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg, FlexDirection.Column);
            AddChild(panel, height: 50, label: "Row A");
            AddChild(panel, height: 80, label: "Row B");

            using (var ms = DocStreams.GetOutputStream("Flex_Column_TwoChildren.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);

            // Column direction uses a single column (standard block stacking).
            Assert.AreEqual(1, panelBlock.Columns.Length, "Flex column direction should have a single region");

            var region = panelBlock.Columns[0];
            var block0 = region.Contents[0] as PDFLayoutBlock;
            var block1 = region.Contents[1] as PDFLayoutBlock;
            Assert.IsNotNull(block0, "First child block should not be null");
            Assert.IsNotNull(block1, "Second child block should not be null");

            // Second child should start below the first.
            Assert.IsTrue(block1.TotalBounds.Y > block0.TotalBounds.Y,
                "Second child Y should be greater than first child Y");

            // First child is 50pt tall (explicit height).
            Assert.AreEqual(50.0, block0.TotalBounds.Height.PointsValue, 0.5,
                "First child height should be 50pt");

            // Text content present in each stacked child.
            StringAssert.Contains(CollectText(block0.Columns[0]), "Row A", "First child text");
            StringAssert.Contains(CollectText(block1.Columns[0]), "Row B", "Second child text");
        }

        // -----------------------------------------------------------------------
        // FlexColumn — children fill full width
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexColumn_Children_FullWidth()
        {
            var doc   = CreateDoc(out var pg);
            var panel1 = CreateFlexContainer(pg, FlexDirection.Column);
            var panel2 = CreateFlexContainer(pg, FlexDirection.Column);
            AddChild(panel1, height: 50, label: "Full A");
            AddChild(panel2, height: 50, label: "Full B");

            using (var ms = DocStreams.GetOutputStream("Flex_Column_FullWidth.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);

            Assert.AreEqual(1, panelBlock.Columns.Length, "Flex column should have one region");

            // The column region itself should span the full container width.
            var colRegion = panelBlock.Columns[0];
            Assert.AreEqual(PageW, colRegion.TotalBounds.Width.PointsValue, 0.5,
                "Flex-column region should span the full container width");

            // First labels present.
            var allText = CollectText(colRegion);
            StringAssert.Contains(allText, "Full A", "First child label");
            
            
            panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[1] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            
            Assert.AreEqual(1, panelBlock.Columns.Length, "Flex column should have one region");
            colRegion = panelBlock.Columns[0];
            Assert.AreEqual(PageW, colRegion.TotalBounds.Width.PointsValue, 0.5,
                "Flex-column region should span the full container width");
            
            allText = CollectText(colRegion);
            StringAssert.Contains(allText, "Full B", "Second child label");
        }

        // -----------------------------------------------------------------------
        // FlexRow — default grow=1 when not explicitly set
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_DefaultGrow_EqualColumns()
        {
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);

            // Add children without explicitly setting grow (default = 1)
            var child0 = new Panel();
            child0.Height = 50;
            child0.Style.Padding.All = 4;
            child0.Style.Border.LineStyle = LineType.Solid;
            child0.Style.Border.Width     = 1;
            child0.Style.Border.Color     = new Color(100, 100, 100);
            child0.Contents.Add(new Label { Text = "Default A" });
            panel.Contents.Add(child0);

            var child1 = new Panel();
            child1.Height = 50;
            child1.Style.Padding.All = 4;
            child1.Style.Border.LineStyle = LineType.Solid;
            child1.Style.Border.Width     = 1;
            child1.Style.Border.Color     = new Color(100, 100, 100);
            child1.Contents.Add(new Label { Text = "Default B" });
            panel.Contents.Add(child1);

            using (var ms = DocStreams.GetOutputStream("Flex_Row_DefaultGrow.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length, "Two children without grow should still produce 2 equal columns");

            double expected = PageW / 2.0;
            Assert.AreEqual(expected, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 0.5, "Column 0 equal width");
            Assert.AreEqual(expected, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 0.5, "Column 1 equal width");

            StringAssert.Contains(CollectText(panelBlock.Columns[0]), "Default A", "Column 0 text");
            StringAssert.Contains(CollectText(panelBlock.Columns[1]), "Default B", "Column 1 text");
        }

        // -----------------------------------------------------------------------
        // FlexRow — display:flex recognised from CSS string
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_CSSParsed_DisplayFlex()
        {
            // Build the document from inline CSS to verify the CSS parser path.
            var src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; flex-direction:row; width:600pt; border:1pt solid #000;"">
    <div style=""flex-grow:1; height:50pt; border:1pt solid #888; padding:4pt;"">CSS Left</div>
    <div style=""flex-grow:1; height:50pt; border:1pt solid #888; padding:4pt;"">CSS Right</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src),
                                           ParseSourceType.DynamicContent) as Document;
            Assert.IsNotNull(doc, "Parsed document should not be null");

            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_CSS_DisplayFlex.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout, "Layout should not be null after CSS parse");
            Assert.AreEqual(1, layout.AllPages.Count);

            // Recursively find the flex container block (has 2 columns for the 2 flex children).
            var contentBlock = layout.AllPages[0].ContentBlock;
            var flexBlock = FindFlexBlock(contentBlock.Columns[0]);

            Assert.IsNotNull(flexBlock, "Should find a block with 2 columns (flex row) in the layout tree");
            Assert.AreEqual(2, flexBlock.Columns.Length, "CSS flex-direction:row should produce 2 columns");

            // Both text strings present in their respective columns.
            StringAssert.Contains(CollectText(flexBlock.Columns[0]), "CSS Left",  "Left column text");
            StringAssert.Contains(CollectText(flexBlock.Columns[1]), "CSS Right", "Right column text");
        }

        // -----------------------------------------------------------------------
        // FlexRow — empty container does not throw
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_EmptyContainer_DoesNotThrow()
        {
            var doc   = CreateDoc(out var pg);
            var panel = new Panel();
            panel.Style.Position.DisplayMode = DisplayMode.FlexBox;
            panel.Width  = PageW;
            panel.Height = 50;
            panel.Style.Border.LineStyle = LineType.Solid;
            panel.Style.Border.Width     = 1;
            panel.Style.Border.Color     = new Color(0, 0, 0);
            pg.Contents.Add(panel);

            using (var ms = DocStreams.GetOutputStream("Flex_Row_Empty.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(_layout, "Layout should complete without throwing for empty flex container");
            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock, "Empty flex container should still produce a layout block");
        }

        // -----------------------------------------------------------------------
        // FlexRow — single child takes full width
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_SingleChild_FullWidth()
        {
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            AddChild(panel, height: 50, grow: 1, label: "Only Child");

            using (var ms = DocStreams.GetOutputStream("Flex_Row_SingleChild.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(1, panelBlock.Columns.Length, "Single child should produce one column");
            Assert.AreEqual(PageW, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 0.5,
                "Single-child flex row column should span the full container width");

            StringAssert.Contains(CollectText(panelBlock.Columns[0]), "Only Child", "Single child text");
        }

        // -----------------------------------------------------------------------
        // align-items
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_AlignItems_FlexStart_NoOffset()
        {
            // flex-start (the default) — shorter child should start at Y=0, same as taller child.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems = FlexAlignMode.FlexStart;
            AddChild(panel, height: 50, label: "Short");
            AddChild(panel, height: 80, label: "Tall");

            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_FlexStart.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            var shortBlock = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var tallBlock  = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(shortBlock);
            Assert.IsNotNull(tallBlock);

            Assert.AreEqual(0.0, shortBlock.TotalBounds.Y.PointsValue, 0.5,
                "flex-start: short child should start at Y=0");
            Assert.AreEqual(0.0, tallBlock.TotalBounds.Y.PointsValue, 0.5,
                "flex-start: tall child should start at Y=0");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_AlignItems_FlexEnd_ShortChildStartsAtBottom()
        {
            // flex-end — shorter child should be pushed down so its bottom aligns with taller child.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems = FlexAlignMode.FlexEnd;
            AddChild(panel, height: 50, label: "Short");  // col 0
            AddChild(panel, height: 80, label: "Tall");   // col 1

            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_FlexEnd.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            var shortBlock = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var tallBlock  = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(shortBlock);
            Assert.IsNotNull(tallBlock);

            // Short child (50pt) in a row of 80pt — offset = 80 - 50 = 30pt.
            Assert.AreEqual(30.0, shortBlock.TotalBounds.Y.PointsValue, 0.5,
                "flex-end: short child Y should be (maxH - childH) = 30pt");
            Assert.AreEqual(0.0, tallBlock.TotalBounds.Y.PointsValue, 0.5,
                "flex-end: tallest child should stay at Y=0");

            // Text present in each column.
            StringAssert.Contains(CollectText(panelBlock.Columns[0]), "Short");
            StringAssert.Contains(CollectText(panelBlock.Columns[1]), "Tall");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_AlignItems_Center_ShortChildIsCentered()
        {
            // center — shorter child should be pushed down by half the height difference.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems = FlexAlignMode.Center;
            AddChild(panel, height: 50, label: "Short");  // col 0
            AddChild(panel, height: 80, label: "Tall");   // col 1

            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_Center.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            var shortBlock = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var tallBlock  = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(shortBlock);
            Assert.IsNotNull(tallBlock);

            // Short child (50pt) in a row of 80pt — offset = (80 - 50) / 2 = 15pt.
            Assert.AreEqual(15.0, shortBlock.TotalBounds.Y.PointsValue, 0.5,
                "center: short child Y should be (maxH - childH) / 2 = 15pt");
            Assert.AreEqual(0.0, tallBlock.TotalBounds.Y.PointsValue, 0.5,
                "center: tallest child should stay at Y=0");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_AlignItems_EqualHeights_NoOffset()
        {
            // When all children have equal height, align-items has no visible effect.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems = FlexAlignMode.FlexEnd;
            AddChild(panel, height: 60, label: "A");
            AddChild(panel, height: 60, label: "B");

            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_EqualHeights.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            var block0 = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var block1 = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            Assert.AreEqual(0.0, block0.TotalBounds.Y.PointsValue, 0.5, "Equal-height child 0 should have Y=0");
            Assert.AreEqual(0.0, block1.TotalBounds.Y.PointsValue, 0.5, "Equal-height child 1 should have Y=0");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_AlignItems_CSSParsed_Center()
        {
            // Verify align-items:center is correctly parsed from an inline CSS string.
            var src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; align-items:center; width:600pt; border:1pt solid #000000;"">
    <div style=""height:50pt; flex-grow:1; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">Short</div>
    <div style=""height:80pt; flex-grow:1; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">Tall</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src),
                                           ParseSourceType.DynamicContent) as Document;
            Assert.IsNotNull(doc);

            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_CSS_Center.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout);
            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock, "Should find flex row block");
            Assert.AreEqual(2, flexBlock.Columns.Length);

            var shortBlock = flexBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var tallBlock  = flexBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(shortBlock);
            Assert.IsNotNull(tallBlock);

            // center: offset = (80 - 50) / 2 = 15pt
            Assert.AreEqual(15.0, shortBlock.TotalBounds.Y.PointsValue, 0.5,
                "CSS align-items:center should offset short child by 15pt");
            Assert.AreEqual(0.0, tallBlock.TotalBounds.Y.PointsValue, 0.5,
                "CSS align-items:center: tall child stays at Y=0");
        }

        // -----------------------------------------------------------------------
        // justify-content (only has effect when items do not fill the container,
        // i.e. flex-grow:0 with explicit widths)
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_JustifyContent_FlexStart_NoShift()
        {
            // flex-start is the default — items sit at the left edge.
            var src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:flex-start; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src),
                                           ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_FlexStart.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout);
            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);

            // flex-start: col0 stays at X=0, col1 immediately follows.
            Assert.AreEqual(0.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5,
                "flex-start: column 0 should start at X=0");
            Assert.AreEqual(100.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5,
                "flex-start: column 1 should start at X=100 (right after col 0)");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_JustifyContent_FlexEnd_ItemsShiftRight()
        {
            // flex-end — items pack to the right. Two 100pt items in 600pt → leftover 400pt.
            var src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:flex-end; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src),
                                           ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_FlexEnd.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout);
            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);

            // leftover = 600 - (100 + 100) = 400. Both cols shift right by 400.
            Assert.AreEqual(400.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5,
                "flex-end: column 0 should start at X=400");
            Assert.AreEqual(500.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5,
                "flex-end: column 1 should start at X=500");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_JustifyContent_Center_ItemsCentered()
        {
            // center — items are centred. Two 100pt items in 600pt → leftover 400 → shift by 200.
            var src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:center; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src),
                                           ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_Center.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout);
            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);

            // shift = 400 / 2 = 200. col0: 0+200=200, col1: 100+200=300.
            Assert.AreEqual(200.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5,
                "center: column 0 should start at X=200");
            Assert.AreEqual(300.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5,
                "center: column 1 should start at X=300");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_JustifyContent_SpaceBetween_SpacesDistributed()
        {
            // space-between — first item at left, last at right, equal gaps between.
            // Two 100pt items in 600pt → leftover 400 → one gap of 400 between them.
            var src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:space-between; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src),
                                           ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_SpaceBetween.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout);
            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);

            // col0 stays at X=0; col1 shifts right by gap=400 → 100+400=500.
            Assert.AreEqual(0.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5,
                "space-between: column 0 should stay at X=0");
            Assert.AreEqual(500.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5,
                "space-between: column 1 should be at X=500 (last item at right edge)");
        }

        [TestCategory(TestCategory)]
        [TestMethod()]
        public void FlexRow_JustifyContent_GrowItems_NoEffect()
        {
            // With flex-grow > 0, items fill the container so justify-content has no effect.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.JustifyContent = FlexJustify.FlexEnd;
            AddChild(panel, height: 50, grow: 1, label: "A");
            AddChild(panel, height: 50, grow: 1, label: "B");

            using (var ms = DocStreams.GetOutputStream("Flex_Justify_GrowNoEffect.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            // Items fill the container — columns stay at their original positions.
            Assert.AreEqual(0.0, panelBlock.Columns[0].TotalBounds.X.PointsValue, 0.5,
                "Grow items: col 0 should start at X=0 (justify-content is no-op)");
            Assert.AreEqual(PageW / 2.0, panelBlock.Columns[1].TotalBounds.X.PointsValue, 0.5,
                "Grow items: col 1 should start at X=300 (justify-content is no-op)");
        }

        // -----------------------------------------------------------------------
        // justify-content — three-item tests give more interesting gap arithmetic
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_JustifyContent_SpaceBetween_ThreeItems()
        {
            // space-between: first at left edge, last at right edge, equal gap between.
            // 3 × 100pt in 600pt → leftover=300, 2 gaps → 150pt each.
            // col0=0, col1=0+100+150=250, col2=250+100+150=500.
            const string src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:space-between; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0FFE8;"">C</div>
  </div>
</body>
</html>";
            using var doc = Document.Parse(new System.IO.StringReader(src), ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_SpaceBetween_3.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);
            Assert.AreEqual(3, flexBlock.Columns.Length);

            Assert.AreEqual(  0.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5, "space-between 3: col0 at left edge");
            Assert.AreEqual(250.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5, "space-between 3: col1 at 250");
            Assert.AreEqual(500.0, flexBlock.Columns[2].TotalBounds.X.PointsValue, 0.5, "space-between 3: col2 at right edge");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_JustifyContent_SpaceAround_TwoItems()
        {
            // space-around: equal space around each item (half-gap at edges).
            // 2 × 100pt in 600pt → leftover=400, aroundUnit=200, startOffset=100, gap=200.
            // col0=0+100=100, col1=100+200=300 (original col1 X=100, +300 offset=400?)
            // Re-check engine: xOffset starts at 100, col0.X = 0+100=100.
            // xOffset += 200 → 300. col1.X = 100 + 300 = 400.
            const string src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:space-around; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
  </div>
</body>
</html>";
            using var doc = Document.Parse(new System.IO.StringReader(src), ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_SpaceAround_2.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);
            Assert.AreEqual(2, flexBlock.Columns.Length);

            // aroundUnit = 400/2 = 200, startOffset = 100, gapBetween = 200.
            // col0 original X=0 → 0+100=100. col1 original X=100 → 100+300=400.
            Assert.AreEqual(100.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5, "space-around 2: col0 at 100 (half-gap)");
            Assert.AreEqual(400.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5, "space-around 2: col1 at 400");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_JustifyContent_SpaceAround_ThreeItems()
        {
            // space-around: 3 × 100pt in 600pt → leftover=300, aroundUnit=100, startOffset=50, gap=100.
            // col0 original X=0   → 0+50=50.
            // col1 original X=100 → 100+150=250.
            // col2 original X=200 → 200+250=450.
            const string src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:space-around; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0FFE8;"">C</div>
  </div>
</body>
</html>";
            using var doc = Document.Parse(new System.IO.StringReader(src), ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_SpaceAround_3.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);
            Assert.AreEqual(3, flexBlock.Columns.Length);

            Assert.AreEqual( 50.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5, "space-around 3: col0 at 50");
            Assert.AreEqual(250.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5, "space-around 3: col1 at 250");
            Assert.AreEqual(450.0, flexBlock.Columns[2].TotalBounds.X.PointsValue, 0.5, "space-around 3: col2 at 450");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_JustifyContent_Center_ThreeItems()
        {
            // center: 3 × 100pt in 600pt → leftover=300, startOffset=150.
            // All columns shift +150. col0=150, col1=250, col2=350.
            const string src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:center; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0FFE8;"">C</div>
  </div>
</body>
</html>";
            using var doc = Document.Parse(new System.IO.StringReader(src), ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_Center_3.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);
            Assert.AreEqual(3, flexBlock.Columns.Length);

            Assert.AreEqual(150.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5, "center 3: col0 at 150");
            Assert.AreEqual(250.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5, "center 3: col1 at 250");
            Assert.AreEqual(350.0, flexBlock.Columns[2].TotalBounds.X.PointsValue, 0.5, "center 3: col2 at 350");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_JustifyContent_SpaceEvenly_ThreeItems()
        {
            // space-evenly: equal spacing before, between, and after every item.
            // 3 × 100pt in 600pt → leftover=300, 4 gaps → 75pt each.
            // col0: 0+75=75.  col1: 100+150=250.  col2: 200+225=425.
            const string src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; justify-content:space-evenly; width:600pt; border:1pt solid #000000;"">
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
    <div style=""width:100pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0FFE8;"">C</div>
  </div>
</body>
</html>";
            using var doc = Document.Parse(new System.IO.StringReader(src), ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_Justify_SpaceEvenly_3.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);
            Assert.AreEqual(3, flexBlock.Columns.Length);

            Assert.AreEqual( 75.0, flexBlock.Columns[0].TotalBounds.X.PointsValue, 0.5, "space-evenly: col0 at 75");
            Assert.AreEqual(250.0, flexBlock.Columns[1].TotalBounds.X.PointsValue, 0.5, "space-evenly: col1 at 250");
            Assert.AreEqual(425.0, flexBlock.Columns[2].TotalBounds.X.PointsValue, 0.5, "space-evenly: col2 at 425");
        }

        // -----------------------------------------------------------------------
        // align-items — three children with mixed heights
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_AlignItems_FlexEnd_ThreeChildren_MixedHeights()
        {
            // Three children: 30pt, 50pt, 80pt. Row height = 80pt.
            // flex-end: each child Y-offset = 80 - childHeight.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems = FlexAlignMode.FlexEnd;
            AddChild(panel, height: 40, grow: 1, label: "Short",  borderColor: new Color(200,  80,  80));
            AddChild(panel, height: 60, grow: 1, label: "Medium", borderColor: new Color( 80, 200,  80));
            AddChild(panel, height: 80, grow: 1, label: "Tall",   borderColor: new Color( 80,  80, 200));

            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_FlexEnd_3.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(3, panelBlock.Columns.Length);

            var shortB  = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var mediumB = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            var tallB   = panelBlock.Columns[2].Contents[0] as PDFLayoutBlock;

            Assert.AreEqual(40.0, shortB.TotalBounds.Y.PointsValue,  0.5, "flex-end: short (30) offset = 80-30 = 50");
            Assert.AreEqual(20.0, mediumB.TotalBounds.Y.PointsValue, 0.5, "flex-end: medium (50) offset = 80-50 = 30");
            Assert.AreEqual( 0.0, tallB.TotalBounds.Y.PointsValue,   0.5, "flex-end: tall (80) offset = 0");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_AlignItems_Center_ThreeChildren_MixedHeights()
        {
            // Three children: 30pt, 50pt, 80pt. Row height = 80pt.
            // center: each child Y-offset = (80 - childHeight) / 2.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems = FlexAlignMode.Center;
            AddChild(panel, height: 30, grow: 1, label: "Short",  borderColor: new Color(200,  80,  80));
            AddChild(panel, height: 50, grow: 1, label: "Medium", borderColor: new Color( 80, 200,  80));
            AddChild(panel, height: 80, grow: 1, label: "Tall",   borderColor: new Color( 80,  80, 200));

            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_Center_3.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(3, panelBlock.Columns.Length);

            var shortB  = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var mediumB = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            var tallB   = panelBlock.Columns[2].Contents[0] as PDFLayoutBlock;

            Assert.AreEqual(25.0, shortB.TotalBounds.Y.PointsValue,  0.5, "center: short (30) offset = (80-30)/2 = 25");
            Assert.AreEqual(15.0, mediumB.TotalBounds.Y.PointsValue, 0.5, "center: medium (50) offset = (80-50)/2 = 15");
            Assert.AreEqual( 0.0, tallB.TotalBounds.Y.PointsValue,   0.5, "center: tall (80) offset = 0");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_AlignItems_Stretch_BehavesLikeFlexStart()
        {
            // Stretch (=0) is explicitly skipped in the engine — treated same as FlexStart.
            // Documents current behaviour: items keep their natural heights.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems = FlexAlignMode.Stretch;
            AddChild(panel, height: 40, grow: 1, label: "Short", borderColor: new Color(200, 80, 80));
            AddChild(panel, height: 80, grow: 1, label: "Tall",  borderColor: new Color( 80, 80, 200));

            using (var ms = DocStreams.GetOutputStream("Flex_AlignItems_Stretch.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            var shortB = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var tallB  = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;

            // Current behaviour: no Y offset applied (same as flex-start).
            Assert.AreEqual(0.0, shortB.TotalBounds.Y.PointsValue, 0.5, "stretch (skip): short child stays at Y=0");
            Assert.AreEqual(0.0, tallB.TotalBounds.Y.PointsValue,  0.5, "stretch (skip): tall child stays at Y=0");
        }

        // -----------------------------------------------------------------------
        // Mixed grow ratios
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_MixedGrow_FixedPlusOneAndTwo()
        {
            // A: width=100pt, grow=0 (fixed). B: grow=1. C: grow=2.
            // Available=600. Fixed=100. Remaining=500, total fr=3.
            // B gets 500*(1/3)≈166.67pt, C gets 500*(2/3)≈333.33pt.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            var fixedItem = AddChild(panel, height: 50, grow: 0, label: "Fixed",  borderColor: new Color(200,  80,  80));
            AddChild(panel, height: 50, grow: 1, label: "Grow×1", borderColor: new Color( 80, 200,  80));
            AddChild(panel, height: 50, grow: 2, label: "Grow×2", borderColor: new Color( 80,  80, 200));

            // Give Fixed item an explicit width so grow=0 anchors correctly.
            fixedItem.Width = 100;

            using (var ms = DocStreams.GetOutputStream("Flex_MixedGrow_0_1_2.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(3, panelBlock.Columns.Length);

            var colA = panelBlock.Columns[0];
            var colB = panelBlock.Columns[1];
            var colC = panelBlock.Columns[2];

            Assert.AreEqual(100.0, colA.TotalBounds.Width.PointsValue, 1.0, "Fixed item width = 100pt");
            Assert.AreEqual(500.0 / 3.0, colB.TotalBounds.Width.PointsValue, 2.0, "Grow×1 width ≈ 166.67pt");
            Assert.AreEqual(1000.0 / 3.0, colC.TotalBounds.Width.PointsValue, 2.0, "Grow×2 width ≈ 333.33pt");

            // B starts immediately after A.
            Assert.AreEqual(100.0, colB.TotalBounds.X.PointsValue, 1.0, "Grow×1 starts at X=100");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_GrowRatioOne_ThreeEqualChildren()
        {
            // Three grow:1 items in 600pt → each gets exactly 200pt.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            AddChild(panel, height: 50, grow: 1, label: "A", borderColor: new Color(200,  80,  80));
            AddChild(panel, height: 50, grow: 1, label: "B", borderColor: new Color( 80, 200,  80));
            AddChild(panel, height: 50, grow: 1, label: "C", borderColor: new Color( 80,  80, 200));

            using (var ms = DocStreams.GetOutputStream("Flex_Grow_3Equal.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(3, panelBlock.Columns.Length);

            Assert.AreEqual(200.0, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 1.0, "A width = 200pt");
            Assert.AreEqual(200.0, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 1.0, "B width = 200pt");
            Assert.AreEqual(200.0, panelBlock.Columns[2].TotalBounds.Width.PointsValue, 1.0, "C width = 200pt");
            Assert.AreEqual(  0.0, panelBlock.Columns[0].TotalBounds.X.PointsValue, 0.5, "A at X=0");
            Assert.AreEqual(200.0, panelBlock.Columns[1].TotalBounds.X.PointsValue, 0.5, "B at X=200");
            Assert.AreEqual(400.0, panelBlock.Columns[2].TotalBounds.X.PointsValue, 0.5, "C at X=400");
        }

        // -----------------------------------------------------------------------
        // gap + align combined
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_Gap_AlignCenter_Combined()
        {
            // Gap reduces available width; align-items centers shorter items vertically.
            // 3 items, 20pt gap → container=600, gap cost=2×20=40, working=560.
            // grow:1 each → each column≈186.67pt.
            // Heights: 40, 80, 60. Max=80. Center offsets: 20, 0, 10.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.AlignItems  = FlexAlignMode.Center;
            panel.Style.Flex.ColumnGap   = 20;
            AddChild(panel, height: 40, grow: 1, label: "Short",  borderColor: new Color(200,  80,  80));
            AddChild(panel, height: 80, grow: 1, label: "Tall",   borderColor: new Color( 80, 200,  80));
            AddChild(panel, height: 60, grow: 1, label: "Medium", borderColor: new Color( 80,  80, 200));

            using (var ms = DocStreams.GetOutputStream("Flex_Gap_AlignCenter.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(3, panelBlock.Columns.Length);

            var shortB  = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var tallB   = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            var mediumB = panelBlock.Columns[2].Contents[0] as PDFLayoutBlock;

            // Center offsets: (80-40)/2=20, (80-80)/2=0, (80-60)/2=10.
            Assert.AreEqual(20.0, shortB.TotalBounds.Y.PointsValue,  1.0, "Short (40pt) centered: Y=20");
            Assert.AreEqual( 0.0, tallB.TotalBounds.Y.PointsValue,   1.0, "Tall (80pt) centered: Y=0");
            Assert.AreEqual(10.0, mediumB.TotalBounds.Y.PointsValue, 1.0, "Medium (60pt) centered: Y=10");
        }

        // -----------------------------------------------------------------------
        // flex-direction: column with align-items
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexColumn_AlignItems_Center_NarrowItems()
        {
            // Column direction: items stack vertically.
            // align-items in column mode aligns along the X axis (horizontal centering).
            // Each item has an explicit width narrower than the container.
            // NOTE: current engine aligns Y offsets post-layout; column-mode alignment
            //       is not yet implemented — this test documents the existing no-op behaviour.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg, FlexDirection.Column);
            panel.Style.Flex.AlignItems = FlexAlignMode.Center;
            var itemA = AddChild(panel, height: 40, grow: 0, label: "Narrow A", borderColor: new Color(200, 80, 80));
            var itemB = AddChild(panel, height: 40, grow: 0, label: "Narrow B", borderColor: new Color(80, 200, 80));
            itemA.Width = 200;
            itemB.Width = 200;

            using (var ms = DocStreams.GetOutputStream("Flex_Column_AlignCenter.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            // Items should stack (column mode) — verify they both rendered.
            Assert.IsNotNull(_layout);
            var pageRegion = _layout.AllPages[0].ContentBlock.Columns[0];
            Assert.IsTrue(pageRegion.Contents.Count > 0, "Column flex should produce layout blocks");
        }

        // -----------------------------------------------------------------------
        // Visual showcase — four items, mixed sizes, space-around + align-center
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_FourChildren_SpaceAround_AlignCenter_Visual()
        {
            // Visual stress test: four items of different heights, space-around justify,
            // center align — produces a PDF that clearly shows both axes working together.
            // 4 × 80pt items in 600pt → leftover=280, aroundUnit=70, startOffset=35, gap=70.
            // Heights: 30, 70, 50, 90. Max=90.
            // X positions: col0=0+35=35, col1=80+105=185, col2=160+175=335, col3=240+245=485.
            // Y offsets (center): (90-30)/2=30, (90-70)/2=10, (90-50)/2=20, 0.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Flex.JustifyContent = FlexJustify.SpaceAround;
            panel.Style.Flex.AlignItems     = FlexAlignMode.Center;
            var i0 = AddChild(panel, height: 30, grow: 0, label: "30pt",  borderColor: new Color(200,  80,  80));
            var i1 = AddChild(panel, height: 70, grow: 0, label: "70pt",  borderColor: new Color( 80, 200,  80));
            var i2 = AddChild(panel, height: 50, grow: 0, label: "50pt",  borderColor: new Color( 80,  80, 200));
            var i3 = AddChild(panel, height: 90, grow: 0, label: "90pt",  borderColor: new Color(200, 160,   0));
            i0.Width = i1.Width = i2.Width = i3.Width = 80;

            using (var ms = DocStreams.GetOutputStream("Flex_Visual_SpaceAround_AlignCenter_4.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(4, panelBlock.Columns.Length);

            // X positions (space-around).
            Assert.AreEqual( 35.0, panelBlock.Columns[0].TotalBounds.X.PointsValue, 1.0, "col0 X = 35");
            Assert.AreEqual(185.0, panelBlock.Columns[1].TotalBounds.X.PointsValue, 1.0, "col1 X = 185");
            Assert.AreEqual(335.0, panelBlock.Columns[2].TotalBounds.X.PointsValue, 1.0, "col2 X = 335");
            Assert.AreEqual(485.0, panelBlock.Columns[3].TotalBounds.X.PointsValue, 1.0, "col3 X = 485");

            // Y positions (center).
            var b0 = panelBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            var b1 = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock;
            var b2 = panelBlock.Columns[2].Contents[0] as PDFLayoutBlock;
            var b3 = panelBlock.Columns[3].Contents[0] as PDFLayoutBlock;
            Assert.AreEqual(30.0, b0.TotalBounds.Y.PointsValue, 1.0, "30pt item centered: Y=(90-30)/2=30");
            Assert.AreEqual(10.0, b1.TotalBounds.Y.PointsValue, 1.0, "70pt item centered: Y=(90-70)/2=10");
            Assert.AreEqual(20.0, b2.TotalBounds.Y.PointsValue, 1.0, "50pt item centered: Y=(90-50)/2=20");
            Assert.AreEqual( 0.0, b3.TotalBounds.Y.PointsValue, 1.0, "90pt item (tallest): Y=0");
        }

        // -----------------------------------------------------------------------
        // Container padding — columns must fit inside the padded content area
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_ContainerPadding_ReducesColumnWidth()
        {
            // Container: 600pt wide, 20pt padding all sides → content area = 560pt.
            // Two grow:1 items → each column = 280pt.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Padding.All = 20;
            AddChild(panel, height: 50, grow: 1, label: "A", borderColor: new Color(200, 80, 80));
            AddChild(panel, height: 50, grow: 1, label: "B", borderColor: new Color(80, 80, 200));

            using (var ms = DocStreams.GetOutputStream("Flex_ContainerPadding_TwoItems.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            // Content width = 600 - 2×20 = 560 → each column = 280pt.
            double expected = (PageW - 40.0) / 2.0;
            Assert.AreEqual(expected, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 1.0,
                "Column 0 width should be (600-40)/2 = 280pt (padding deducted)");
            Assert.AreEqual(expected, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 1.0,
                "Column 1 width should be (600-40)/2 = 280pt (padding deducted)");
        }

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_ContainerPadding_WithGap()
        {
            // Container: 600pt, padding:20 all, column-gap:10. Three grow:1 items.
            // Content width = 560pt; gap cost = 2×10=20pt; working = 540pt → each col = 180pt.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Padding.All     = 20;
            panel.Style.Flex.ColumnGap  = 10;
            AddChild(panel, height: 50, grow: 1, label: "A", borderColor: new Color(200,  80,  80));
            AddChild(panel, height: 50, grow: 1, label: "B", borderColor: new Color( 80, 200,  80));
            AddChild(panel, height: 50, grow: 1, label: "C", borderColor: new Color( 80,  80, 200));

            using (var ms = DocStreams.GetOutputStream("Flex_ContainerPadding_Gap_3.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(3, panelBlock.Columns.Length);

            double expected = (PageW - 40.0 - 20.0) / 3.0; // (560-20)/3 = 180
            Assert.AreEqual(expected, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 1.0,
                "Column width = (600-pad×2-gap×2)/3 = 180pt");
            Assert.AreEqual(expected, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 1.0,
                "Column 1 width = 180pt");
            Assert.AreEqual(expected, panelBlock.Columns[2].TotalBounds.Width.PointsValue, 1.0,
                "Column 2 width = 180pt");
        }

        // -----------------------------------------------------------------------
        // Item margins — document how margins interact with flex columns
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_ItemMargins_ColumnWidthUnchanged()
        {
            // Item margins are applied inside the allocated column, not taken from column width.
            // Two grow:1 items each with margin:10pt in a 600pt container.
            // Column widths are still 300pt each; margin reduces the inner content area only.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            var itemA = AddChild(panel, height: 50, grow: 1, label: "A", borderColor: new Color(200, 80, 80));
            var itemB = AddChild(panel, height: 50, grow: 1, label: "B", borderColor: new Color(80, 80, 200));
            itemA.Style.Margins.All = 10;
            itemB.Style.Margins.All = 10;

            using (var ms = DocStreams.GetOutputStream("Flex_ItemMargins_TwoItems.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(2, panelBlock.Columns.Length);

            // Column widths are computed from grow ratios, not affected by item margins.
            Assert.AreEqual(300.0, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 1.0,
                "Item margins do not reduce column width: col0 = 300pt");
            Assert.AreEqual(300.0, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 1.0,
                "Item margins do not reduce column width: col1 = 300pt");
        }

        // -----------------------------------------------------------------------
        // Multiple rows — three stacked flex containers (simulating a row-by-row layout)
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexPage_ThreeRows_StackedContainers()
        {
            // Three flex rows stacked on the page, each with margin-bottom for row spacing.
            // Row 0: 3 equal columns, 40pt tall.
            // Row 1: 2 columns (grow:1 + grow:2), 60pt tall.
            // Row 2: 4 equal columns with space-between justify, 50pt tall.
            var doc = CreateDoc(out var pg);

            // Row 0 — 3 equal
            var row0 = CreateFlexContainer(pg);
            row0.Style.Margins.Bottom = 10;
            AddChild(row0, height: 40, grow: 1, label: "R0C0", borderColor: new Color(200,  80,  80));
            AddChild(row0, height: 40, grow: 1, label: "R0C1", borderColor: new Color( 80, 200,  80));
            AddChild(row0, height: 40, grow: 1, label: "R0C2", borderColor: new Color( 80,  80, 200));

            // Row 1 — asymmetric grow
            var row1 = CreateFlexContainer(pg);
            row1.Style.Margins.Bottom = 10;
            AddChild(row1, height: 60, grow: 1, label: "R1C0 (1fr)", borderColor: new Color(160, 120,  80));
            AddChild(row1, height: 60, grow: 2, label: "R1C1 (2fr)", borderColor: new Color( 80, 120, 160));

            // Row 2 — space-between, 4 fixed-width items
            var row2 = CreateFlexContainer(pg);
            row2.Style.Flex.JustifyContent = FlexJustify.SpaceBetween;
            var r2c0 = AddChild(row2, height: 50, grow: 0, label: "R2C0", borderColor: new Color(120, 200, 120));
            var r2c1 = AddChild(row2, height: 50, grow: 0, label: "R2C1", borderColor: new Color(120, 120, 200));
            var r2c2 = AddChild(row2, height: 50, grow: 0, label: "R2C2", borderColor: new Color(200, 120, 120));
            var r2c3 = AddChild(row2, height: 50, grow: 0, label: "R2C3", borderColor: new Color(200, 200, 120));
            r2c0.Width = r2c1.Width = r2c2.Width = r2c3.Width = 100;

            using (var ms = DocStreams.GetOutputStream("Flex_ThreeRows_Stacked.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(_layout);
            var pageRegion = _layout.AllPages[0].ContentBlock.Columns[0];
            Assert.AreEqual(3, pageRegion.Contents.Count, "Page should have 3 flex row blocks");

            var r0Block = pageRegion.Contents[0] as PDFLayoutBlock;
            var r1Block = pageRegion.Contents[1] as PDFLayoutBlock;
            var r2Block = pageRegion.Contents[2] as PDFLayoutBlock;

            Assert.AreEqual(3, r0Block.Columns.Length, "Row 0: 3 columns");
            Assert.AreEqual(2, r1Block.Columns.Length, "Row 1: 2 columns");
            Assert.AreEqual(4, r2Block.Columns.Length, "Row 2: 4 columns");

            // Row 0: each column = 200pt.
            Assert.AreEqual(200.0, r0Block.Columns[0].TotalBounds.Width.PointsValue, 1.0, "R0 col0 = 200pt");
            Assert.AreEqual(200.0, r0Block.Columns[1].TotalBounds.Width.PointsValue, 1.0, "R0 col1 = 200pt");
            Assert.AreEqual(200.0, r0Block.Columns[2].TotalBounds.Width.PointsValue, 1.0, "R0 col2 = 200pt");

            // Row 1: grow:1 → 200pt, grow:2 → 400pt.
            Assert.AreEqual(200.0, r1Block.Columns[0].TotalBounds.Width.PointsValue, 1.0, "R1 col0 (1fr) = 200pt");
            Assert.AreEqual(400.0, r1Block.Columns[1].TotalBounds.Width.PointsValue, 1.0, "R1 col1 (2fr) = 400pt");

            // Row 2: space-between — 4 × 100pt items in 600pt → leftover=200, gap=200/3≈66.7.
            // col0 at X=0, col1 at X≈166.7, col2 at X≈333.3, col3 at X=500.
            Assert.AreEqual(  0.0, r2Block.Columns[0].TotalBounds.X.PointsValue, 1.0, "R2 col0 at left");
            Assert.AreEqual(500.0, r2Block.Columns[3].TotalBounds.X.PointsValue, 1.0, "R2 col3 at right edge");

            // Row 1 starts below row 0 (+ margin).
            Assert.IsTrue(r1Block.TotalBounds.Y.PointsValue > r0Block.TotalBounds.Y.PointsValue + 40.0,
                "Row 1 starts below row 0");
        }

        // -----------------------------------------------------------------------
        // Multi-column with padding — real-world card-grid layout
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_FourColumns_PaddedContainer_AlignCenter()
        {
            // 4 columns in a padded container: padding=15pt, gap=10pt.
            // Content width = 600-30=570. Gap cost = 3×10=30. Working = 540pt → each col = 135pt.
            // Items have mixed heights → align:center vertically offsets shorter items.
            var doc   = CreateDoc(out var pg);
            var panel = CreateFlexContainer(pg);
            panel.Style.Padding.All    = 15;
            panel.Style.Flex.ColumnGap = 10;
            panel.Style.Flex.AlignItems = FlexAlignMode.Center;

            AddChild(panel, height: 80, grow: 1, label: "Tall\n80pt",   borderColor: new Color(200,  80,  80));
            AddChild(panel, height: 40, grow: 1, label: "Short\n40pt",  borderColor: new Color( 80, 200,  80));
            AddChild(panel, height: 60, grow: 1, label: "Medium\n60pt", borderColor: new Color( 80,  80, 200));
            AddChild(panel, height: 80, grow: 1, label: "Tall\n80pt",   borderColor: new Color(200, 160,   0));

            using (var ms = DocStreams.GetOutputStream("Flex_4Col_PaddedContainer.pdf"))
            {
                doc.LayoutComplete += Doc_LayoutComplete;
                doc.SaveAsPDF(ms);
            }

            var panelBlock = _layout.AllPages[0].ContentBlock.Columns[0].Contents[0] as PDFLayoutBlock;
            Assert.IsNotNull(panelBlock);
            Assert.AreEqual(4, panelBlock.Columns.Length);

            double expected = (PageW - 30.0 - 30.0) / 4.0; // (570-30)/4 = 135pt
            Assert.AreEqual(expected, panelBlock.Columns[0].TotalBounds.Width.PointsValue, 1.5, "Col 0 = 135pt");
            Assert.AreEqual(expected, panelBlock.Columns[1].TotalBounds.Width.PointsValue, 1.5, "Col 1 = 135pt");
            Assert.AreEqual(expected, panelBlock.Columns[2].TotalBounds.Width.PointsValue, 1.5, "Col 2 = 135pt");
            Assert.AreEqual(expected, panelBlock.Columns[3].TotalBounds.Width.PointsValue, 1.5, "Col 3 = 135pt");

            // Center: tallest=80, medium=60 → offset=10; short=40 → offset=20.
            var b1 = panelBlock.Columns[1].Contents[0] as PDFLayoutBlock; // short
            var b2 = panelBlock.Columns[2].Contents[0] as PDFLayoutBlock; // medium
            Assert.AreEqual(20.0, b1.TotalBounds.Y.PointsValue, 1.0, "Short (40) centered: Y=(80-40)/2=20");
            Assert.AreEqual(10.0, b2.TotalBounds.Y.PointsValue, 1.0, "Medium (60) centered: Y=(80-60)/2=10");
        }

        // -----------------------------------------------------------------------
        // CSS-parsed multi-row layout — realistic "card grid" scenario
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexPage_CSSParsed_MultiRowLayout()
        {
            // Two flex rows parsed from CSS. Each row has different column counts and heights.
            // Row 1: 3 equal columns. Row 2: 2 columns (1fr + 2fr).
            // Container padding:10pt, gap:8pt.
            const string src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <!-- Row 1: three equal columns -->
  <div style=""display:flex; width:600pt; padding:10pt; gap:8pt; border:1pt solid #000000; margin-bottom:8pt;"">
    <div style=""flex-grow:1; height:60pt; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">R1 A</div>
    <div style=""flex-grow:1; height:60pt; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">R1 B</div>
    <div style=""flex-grow:1; height:60pt; padding:4pt; border:1pt solid #646464; background-color:#D0FFE8;"">R1 C</div>
  </div>
  <!-- Row 2: asymmetric grow -->
  <div style=""display:flex; width:600pt; padding:10pt; gap:8pt; border:1pt solid #000000;"">
    <div style=""flex-grow:1; height:80pt; padding:4pt; border:1pt solid #646464; background-color:#FFD0E8;"">R2 Narrow (1fr)</div>
    <div style=""flex-grow:2; height:80pt; padding:4pt; border:1pt solid #646464; background-color:#E8D0FF;"">R2 Wide (2fr)</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src), ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_CSSParsed_MultiRow.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout);
            var pageRegion = layout.AllPages[0].ContentBlock.Columns[0];

            // Find the two flex row blocks.
            var row0Block = FindFlexBlock(pageRegion);
            Assert.IsNotNull(row0Block, "Row 0 block must exist");
            Assert.AreEqual(3, row0Block.Columns.Length, "Row 0: 3 columns");

            // Row 0: content=580pt, gap=2×8=16 → working=564 → each col≈188pt.
            double row0ColW = (600.0 - 20.0 - 16.0) / 3.0;
            Assert.AreEqual(row0ColW, row0Block.Columns[0].TotalBounds.Width.PointsValue, 2.0,
                "Row 0 col width = (580-16)/3 ≈ 188pt");

            // Row 1: content=580, gap=8 → working=572 → 1fr=190.67, 2fr=381.33.
            // Find the second flex row block by looking for a 2-column block.
            PDFLayoutBlock row1Block = null;
            foreach (var item in pageRegion.Contents)
            {
                if (item is PDFLayoutBlock b && b != row0Block && b.Columns.Length == 2)
                { row1Block = b; break; }
            }
            Assert.IsNotNull(row1Block, "Row 1 (2-column) block must exist");
            double row1Working = 600.0 - 20.0 - 8.0; // 572
            Assert.AreEqual(row1Working / 3.0, row1Block.Columns[0].TotalBounds.Width.PointsValue, 2.0,
                "Row 1 col0 (1fr) ≈ 190.67pt");
            Assert.AreEqual(row1Working * 2.0 / 3.0, row1Block.Columns[1].TotalBounds.Width.PointsValue, 2.0,
                "Row 1 col1 (2fr) ≈ 381.33pt");
        }

        // -----------------------------------------------------------------------
        // flex-wrap: wrap — documents current no-wrap (overflow) behaviour
        // -----------------------------------------------------------------------

        [TestCategory(TestCategory), TestMethod()]
        public void FlexRow_WrapNotImplemented_OverflowItemsShrinkToFit()
        {
            // CSS flex-wrap:wrap is not yet implemented.
            // When grow:0 items collectively exceed the container width, the engine
            // scales all item widths down proportionally so fractions sum to 1.0,
            // rather than throwing. All items still appear in one row.
            // 3 × 150pt in a 300pt container → total=450 > 300; each item shrinks to 100pt.
            const string src = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<body style=""margin:0; padding:0;"">
  <div style=""display:flex; flex-wrap:wrap; width:300pt; border:1pt solid #000000;"">
    <div style=""width:150pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0E8FF;"">A</div>
    <div style=""width:150pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#FFE8D0;"">B</div>
    <div style=""width:150pt; height:50pt; flex-grow:0; padding:4pt; border:1pt solid #646464; background-color:#D0FFE8;"">C</div>
  </div>
</body>
</html>";

            using var doc = Document.Parse(new System.IO.StringReader(src), ParseSourceType.DynamicContent) as Document;
            PDFLayoutDocument layout = null;
            using (var ms = DocStreams.GetOutputStream("Flex_WrapOverflow.pdf"))
            {
                doc.LayoutComplete += (s, e) => layout = e.Context.GetLayout<PDFLayoutDocument>();
                doc.SaveAsPDF(ms);
            }

            Assert.IsNotNull(layout);
            var flexBlock = FindFlexBlock(layout.AllPages[0].ContentBlock.Columns[0]);
            Assert.IsNotNull(flexBlock);

            // All 3 items in one row (no wrapping).
            Assert.AreEqual(3, flexBlock.Columns.Length, "flex-wrap:wrap not implemented — items stay in one row");

            // Each item shrinks proportionally: 150/(150+150+150) × 300 = 100pt.
            Assert.AreEqual(100.0, flexBlock.Columns[0].TotalBounds.Width.PointsValue, 1.0,
                "Overflow item A shrinks to 100pt (150/450 × 300)");
            Assert.AreEqual(100.0, flexBlock.Columns[1].TotalBounds.Width.PointsValue, 1.0,
                "Overflow item B shrinks to 100pt");
            Assert.AreEqual(100.0, flexBlock.Columns[2].TotalBounds.Width.PointsValue, 1.0,
                "Overflow item C shrinks to 100pt");
        }
    }
}
