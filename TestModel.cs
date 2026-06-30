// Gemeinsames Ergebnis-Modell fuer alle Tester.
// Jeder *Tester liefert einen TestRun mit einer Liste von CheckResult-Eintraegen.
// GUI und ReportBuilder konsumieren ausschliesslich dieses Modell.

using System;
using System.Collections.Generic;

namespace AdfsTester
{
    public enum Severity { Ok = 0, Info = 1, Warning = 2, Error = 3 }

    public sealed class CheckResult
    {
        public string Step;            // z.B. "TLS-Zertifikat Hostname-Match"
        public Severity Severity;
        public string Detail;          // Ist-Wert / was wurde gefunden
        public string Recommendation;  // konkreter Loesungshinweis bei Warning/Error
        public string RawData;         // optional: Rohdaten (Cert-Dump, XML, JWT-Claims)

        public CheckResult(string step, Severity sev, string detail,
                           string recommendation = null, string rawData = null)
        {
            Step = step ?? "";
            Severity = sev;
            Detail = detail ?? "";
            Recommendation = recommendation ?? "";
            RawData = rawData ?? "";
        }
    }

    public sealed class TestRun
    {
        public string Title;
        public readonly List<CheckResult> Checks = new List<CheckResult>();

        public TestRun(string title) { Title = title ?? ""; }

        public Severity Overall
        {
            get
            {
                var worst = Severity.Ok;
                foreach (var c in Checks)
                    if (c.Severity > worst) worst = c.Severity;
                return worst;
            }
        }

        // Bequeme Add-Helfer ----------------------------------------------------
        public CheckResult Ok(string step, string detail, string raw = null)
        { return Add(new CheckResult(step, Severity.Ok, detail, null, raw)); }

        public CheckResult Info(string step, string detail, string raw = null)
        { return Add(new CheckResult(step, Severity.Info, detail, null, raw)); }

        public CheckResult Warn(string step, string detail, string rec = null, string raw = null)
        { return Add(new CheckResult(step, Severity.Warning, detail, rec, raw)); }

        public CheckResult Error(string step, string detail, string rec = null, string raw = null)
        { return Add(new CheckResult(step, Severity.Error, detail, rec, raw)); }

        public CheckResult Add(CheckResult c) { Checks.Add(c); return c; }

        public int CountOf(Severity s)
        {
            int n = 0;
            foreach (var c in Checks) if (c.Severity == s) n++;
            return n;
        }
    }
}
