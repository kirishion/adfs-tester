// Erzeugt exportierbare Reports (TXT/HTML) aus den TestRun-Ergebnissen.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AdfsTester
{
    public static class ReportBuilder
    {
        public static string BuildText(IEnumerable<TestRun> runs, AppConfig cfg, string generatedAt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================================");
            sb.AppendLine(" ADFS-Tester - Diagnosebericht");
            sb.AppendLine(" Erstellt : " + generatedAt);
            sb.AppendLine(" ADFS-Host: " + cfg.BaseUrl);
            sb.AppendLine(" Realm    : " + cfg.Realm);
            sb.AppendLine(" ClientId : " + cfg.ClientId);
            sb.AppendLine("==================================================================");
            sb.AppendLine();

            foreach (var run in runs)
            {
                sb.AppendLine("------------------------------------------------------------------");
                sb.AppendLine("# " + run.Title + "   [Gesamt: " + run.Overall + "]");
                sb.AppendLine("  OK=" + run.CountOf(Severity.Ok) + " Info=" + run.CountOf(Severity.Info) +
                              " Warnung=" + run.CountOf(Severity.Warning) + " Fehler=" + run.CountOf(Severity.Error));
                sb.AppendLine("------------------------------------------------------------------");
                foreach (var c in run.Checks)
                {
                    sb.AppendLine("[" + Tag(c.Severity) + "] " + c.Step);
                    sb.AppendLine("        " + c.Detail);
                    if (!string.IsNullOrEmpty(c.Recommendation))
                        sb.AppendLine("    -> " + c.Recommendation);
                    if (!string.IsNullOrEmpty(c.RawData))
                    {
                        sb.AppendLine("    --- Rohdaten ---");
                        foreach (var line in c.RawData.Replace("\r", "").Split('\n'))
                            sb.AppendLine("      " + line);
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string BuildHtml(IEnumerable<TestRun> runs, AppConfig cfg, string generatedAt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html lang='de'><head><meta charset='utf-8'>");
            sb.AppendLine("<title>ADFS-Tester Diagnosebericht</title><style>");
            sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;background:#f6f6f9}");
            sb.AppendLine("h1{font-size:20px} h2{font-size:16px;margin-top:28px;border-bottom:2px solid #ddd;padding-bottom:4px}");
            sb.AppendLine(".meta{background:#fff;border:1px solid #e0e0e0;border-radius:8px;padding:12px 16px;margin-bottom:16px}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;background:#fff} td,th{border:1px solid #e3e3e3;padding:6px 9px;vertical-align:top;text-align:left;font-size:13px}");
            sb.AppendLine("th{background:#f0f0f4}");
            sb.AppendLine(".Ok{color:#107c10;font-weight:bold}.Info{color:#0067c0}.Warning{color:#b7791f;font-weight:bold}.Error{color:#c42b1c;font-weight:bold}");
            sb.AppendLine("tr.r-Error{background:#fdf0ef}tr.r-Warning{background:#fdf8ee}");
            sb.AppendLine("pre{margin:4px 0 0;white-space:pre-wrap;font-size:11px;color:#444;max-height:240px;overflow:auto;background:#fafafa;border:1px solid #eee;padding:6px}");
            sb.AppendLine(".badge{display:inline-block;padding:2px 8px;border-radius:10px;font-size:12px;color:#fff}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>ADFS-Tester &ndash; Diagnosebericht</h1>");
            sb.AppendLine("<div class='meta'>");
            sb.AppendLine("Erstellt: <b>" + H(generatedAt) + "</b><br>ADFS-Host: <b>" + H(cfg.BaseUrl) + "</b><br>");
            sb.AppendLine("Realm: <b>" + H(cfg.Realm) + "</b><br>ClientId: <b>" + H(cfg.ClientId) + "</b></div>");

            foreach (var run in runs)
            {
                sb.AppendLine("<h2>" + H(run.Title) + " <span class='badge' style='background:" + Color(run.Overall) + "'>" + run.Overall + "</span></h2>");
                sb.AppendLine("<table><tr><th style='width:60px'>Status</th><th style='width:25%'>Schritt</th><th>Detail / Empfehlung</th></tr>");
                foreach (var c in run.Checks)
                {
                    sb.AppendLine("<tr class='r-" + c.Severity + "'>");
                    sb.AppendLine("<td class='" + c.Severity + "'>" + c.Severity + "</td>");
                    sb.AppendLine("<td>" + H(c.Step) + "</td>");
                    sb.Append("<td>" + H(c.Detail));
                    if (!string.IsNullOrEmpty(c.Recommendation))
                        sb.Append("<br><i>&#8594; " + H(c.Recommendation) + "</i>");
                    if (!string.IsNullOrEmpty(c.RawData))
                        sb.Append("<pre>" + H(c.RawData) + "</pre>");
                    sb.AppendLine("</td></tr>");
                }
                sb.AppendLine("</table>");
            }
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Tag(Severity s)
        {
            switch (s)
            {
                case Severity.Ok: return "OK  ";
                case Severity.Info: return "INFO";
                case Severity.Warning: return "WARN";
                default: return "FEHL";
            }
        }

        private static string Color(Severity s)
        {
            switch (s)
            {
                case Severity.Ok: return "#107c10";
                case Severity.Info: return "#0067c0";
                case Severity.Warning: return "#b7791f";
                default: return "#c42b1c";
            }
        }

        private static string H(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
