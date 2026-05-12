using Scryber.Drawing;
using Scryber.Html;

namespace Scryber.Styles.Parsing.Typed
{
    /// <summary>Stores the raw grid-template-columns string value for later parsing by the layout engine.</summary>
    public class CSSGridTemplateColumnsParser : CSSStyleAttributeParser<string>
    {
        public CSSGridTemplateColumnsParser() : base(CSSStyleItems.GridTemplateColumns, StyleKeys.GridTemplateColumnsKey) { }

        protected override bool DoSetStyleValue(Style onStyle, CSSStyleItemReader reader)
        {
            // Read each space-separated token and re-join — handles "1fr 2fr", "repeat(3, 1fr)", etc.
            var sb = new System.Text.StringBuilder();
            while (reader.ReadNextValue())
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(reader.CurrentTextValue);
            }
            var raw = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            this.SetValue(onStyle, raw);
            return true;
        }
    }

    /// <summary>Parses grid-auto-flow: row | column.</summary>
    public class CSSGridAutoFlowParser : CSSStyleAttributeParser<GridAutoFlow>
    {
        public CSSGridAutoFlowParser() : base(CSSStyleItems.GridAutoFlow, StyleKeys.GridAutoFlowKey) { }

        protected override bool DoSetStyleValue(Style onStyle, CSSStyleItemReader reader)
        {
            if (!reader.ReadNextValue())
                return false;

            GridAutoFlow flow;
            switch (reader.CurrentTextValue.ToLowerInvariant())
            {
                case "column": flow = GridAutoFlow.Column; break;
                case "row":    flow = GridAutoFlow.Row;    break;
                default:       return false;
            }
            this.SetValue(onStyle, flow);
            return true;
        }
    }

    /// <summary>Stores the raw grid-template-rows string value.</summary>
    public class CSSGridTemplateRowsParser : CSSStyleAttributeParser<string>
    {
        public CSSGridTemplateRowsParser() : base(CSSStyleItems.GridTemplateRows, StyleKeys.GridTemplateRowsKey) { }

        protected override bool DoSetStyleValue(Style onStyle, CSSStyleItemReader reader)
        {
            var sb = new System.Text.StringBuilder();
            while (reader.ReadNextValue())
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(reader.CurrentTextValue);
            }
            var raw = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            this.SetValue(onStyle, raw);
            return true;
        }
    }
}
