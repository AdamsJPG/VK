using System.Net;

namespace DataCompare.Engine.Reporting
{

    /// <summary>
    /// Builds the source/target connection banner shared by both HTML reports (grey Source panel on
    /// the left, orange Target panel on the right, each with a database icon) — a single place for the
    /// markup, icon, and CSS so <see cref="DataComparisonHtmlReportWriter"/> and <see
    /// cref="SchemaHtmlReportWriter"/> can't drift into two different-looking headers.
    /// </summary>
    public static class ReportBannerBuilder
    {
        // A simplified twin-cylinder database icon (stacked disks) — not pixel-identical to the app's
        // WPF geometry, but the same idea, since there's no reason to reproduce that path data for a
        // one-off HTML icon. currentColor so the same markup works on both the grey source panel and
        // the white icon needed against the orange target panel.
        private const string DatabaseIconSvg = """
            <svg viewBox="0 0 100 70" width="40" height="28" class="db-icon">
                <path d="M15,15 L15,55 A35,12 0 0,0 85,55 L85,15" fill="currentColor" />
                <ellipse cx="50" cy="15" rx="35" ry="12" fill="currentColor" />
                <ellipse cx="50" cy="35" rx="35" ry="12" fill="none" stroke="white" stroke-width="3" />
                <ellipse cx="50" cy="55" rx="35" ry="12" fill="none" stroke="white" stroke-width="3" />
            </svg>
            """;

        /// <summary>
        /// the shared inline CSS rules for the banner markup <see cref="Build"/> produces — embed this
        /// verbatim inside a report's own &lt;style&gt; block.
        /// </summary>
        public const string Css = """
            .banner { display: flex; border-radius: 4px; overflow: hidden; }
            .side { flex: 1; display: flex; align-items: center; gap: 12px; padding: 14px 20px; }
            .side.source { background: #F0F0F0; color: #222; }
            .side.target { background: #E8792A; color: #fff; justify-content: flex-end; text-align: right; }
            .side .db-icon { flex-shrink: 0; }
            .side.source .db-icon { color: #333; }
            .side.target .db-icon { color: #fff; }
            .side-label { font-weight: bold; font-size: 15px; }
            .side .server { font-size: 11px; opacity: 0.75; }
            .side .database { font-weight: 600; font-size: 13px; }
            """;

        /// <summary>
        /// builds the source/target banner markup for the given connection details.
        /// </summary>
        /// <param name="sourceServer">a System.String holding the name of the source server</param>
        /// <param name="sourceDatabase">a System.String holding the name of the source database</param>
        /// <param name="targetServer">a System.String holding the name of the target server</param>
        /// <param name="targetDatabase">a System.String holding the name of the target database</param>
        /// <returns>returns a System.String containing the HTML markup for the banner</returns>
        public static string Build(string sourceServer, string sourceDatabase, string targetServer, string targetDatabase) => $"""
            <div class="banner">
                <div class="side source">
                    {DatabaseIconSvg}
                    <div class="side-text">
                        <div class="side-label">Source</div>
                        <div class="server">{Encode(sourceServer)}</div>
                        <div class="database">{Encode(sourceDatabase)}</div>
                    </div>
                </div>
                <div class="side target">
                    <div class="side-text">
                        <div class="side-label">Target</div>
                        <div class="server">{Encode(targetServer)}</div>
                        <div class="database">{Encode(targetDatabase)}</div>
                    </div>
                    {DatabaseIconSvg}
                </div>
            </div>
            """;

        /// <summary>
        /// HTML-encodes a string of text for safe inclusion in the banner markup.
        /// </summary>
        /// <param name="text">a System.String holding the text to encode</param>
        /// <returns>returns a System.String containing the HTML-encoded text</returns>
        private static string Encode(string text) => WebUtility.HtmlEncode(text);
    }
}
