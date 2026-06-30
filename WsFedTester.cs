// WS-Federation (Passive Requestor Profile). Nicht-interaktiv: Erreichbarkeit
// des Signin-Endpoints + MSIS-Fehlererkennung. Interaktiv: kompletter
// Sign-In mit Auswertung des wresult-Tokens.

using System;

namespace AdfsTester
{
    public static class WsFedTester
    {
        public static TestRun Run(AppConfig cfg, AdfsMetadata md, bool interactive)
        {
            var run = new TestRun("WS-Federation");
            var endpoint = !string.IsNullOrEmpty(md.PassiveEndpoint) ? md.PassiveEndpoint : cfg.WsFedEndpoint;

            if (string.IsNullOrEmpty(cfg.Realm))
                run.Warn("Realm", "Kein Realm (wtrealm) konfiguriert.",
                         "Realm = RP-Identifier (Trust Identifier) der Anwendung in ADFS.");

            // wreply: bei interaktiv die lokale Redirect-URI, sonst konfiguriertes/leer
            string wreply = interactive ? cfg.RedirectUri : cfg.Wreply;
            var url = endpoint +
                      (endpoint.Contains("?") ? "&" : "?") +
                      "wa=wsignin1.0" +
                      "&wtrealm=" + Uri.EscapeDataString(cfg.Realm ?? "") +
                      (string.IsNullOrEmpty(wreply) ? "" : "&wreply=" + Uri.EscapeDataString(wreply)) +
                      "&wctx=adfs-tester";

            run.Info("Signin-URL", url);

            if (!interactive)
            {
                var r = HttpHelper.Get(url, cfg.TimeoutSeconds);
                if (!r.Transport)
                {
                    run.Add(ErrorLogger.ToCheckResult("WS-Fed Endpoint", "GET " + endpoint, r.Error));
                    return run;
                }

                ScanForMsis(run, r.Body);

                if (r.Status == 200)
                    run.Ok("WS-Fed Endpoint", "Endpoint erreichbar (HTTP 200) - Login-Seite oder Auto-Post wird ausgeliefert.");
                else if (r.Status >= 300 && r.Status < 400)
                    run.Ok("WS-Fed Endpoint", "Endpoint antwortet mit Redirect (HTTP " + r.Status + ") - Home-Realm-Discovery/IdP.");
                else
                    run.Warn("WS-Fed Endpoint", "Unerwarteter HTTP-Status " + r.Status + ".",
                             "Body in Rohdaten pruefen.", ErrorLogger.Truncate(r.Body, 1500));

                run.Info("Hinweis", "Fuer einen echten End-to-End-Test mit Token-Auswertung den interaktiven Modus verwenden.");
                return run;
            }

            // ---- Interaktiv ----
            run.Info("Interaktiv", "System-Browser wird geoeffnet. Bitte am ADFS anmelden ...");
            var cap = BrowserFlow.Capture(cfg.RedirectUri, url, Math.Max(cfg.TimeoutSeconds, 120));
            if (!cap.Success)
            {
                run.Error("WS-Fed Login", cap.Error, "Redirect-URI muss als wreply/Endpoint in ADFS erlaubt sein.", cap.RawRequest);
                return run;
            }

            string wresult;
            if (!cap.Params.TryGetValue("wresult", out wresult) || string.IsNullOrEmpty(wresult))
            {
                run.Error("WS-Fed Token", "Kein 'wresult' im Redirect empfangen.",
                          "Parameter: " + string.Join(", ", new System.Collections.Generic.List<string>(cap.Params.Keys).ToArray()),
                          cap.RawRequest);
                return run;
            }

            run.Ok("WS-Fed Login", "wresult empfangen (" + wresult.Length + " Zeichen).");
            SamlInspect.Inspect(run, wresult, md.PrimarySigningCert, cfg.Realm, "WS-Fed Token");
            return run;
        }

        private static void ScanForMsis(TestRun run, string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            int idx = body.IndexOf("MSIS", StringComparison.Ordinal);
            if (idx < 0) return;
            // MSIS-Code ausschneiden (z.B. MSIS7000)
            int end = idx;
            while (end < body.Length && (char.IsLetterOrDigit(body[end]))) end++;
            string code = body.Substring(idx, Math.Min(end - idx, 12));
            run.Error("ADFS-Fehler (" + code + ")", "ADFS lieferte einen MSIS-Fehler in der Antwort.",
                      InterpretMsis(code), ErrorLogger.Truncate(body, 2000));
        }

        private static string InterpretMsis(string code)
        {
            switch (code)
            {
                case "MSIS7000": return "Die uebermittelte Anfrage entspricht keiner registrierten Relying Party (wtrealm unbekannt).";
                case "MSIS7001": return "Keine gueltige Anforderung - wtrealm/Parameter pruefen.";
                case "MSIS7007": return "Die Anforderung enthielt kein gueltiges 'wtrealm' oder es ist keiner RP zugeordnet.";
                case "MSIS9622": return "Token-Verschluesselung/Konfigurationsproblem an der Relying Party.";
                case "MSIS3173": return "Anforderung erfordert Authentifizierung, die fehlschlug (z.B. MFA).";
                default: return "ADFS-Fehlercode " + code + " - siehe Microsoft-Dokumentation und ADFS-Eventlog (AD FS/Admin).";
            }
        }
    }
}
