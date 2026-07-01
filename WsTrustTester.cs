// WS-Trust 1.3 (nicht-interaktiv): baut eine RST-SOAP-Nachricht mit
// UsernameToken, POSTet sie an den usernamemixed-Endpoint und wertet die
// RSTR (bzw. einen SOAP-Fault) aus.

using System;
using System.Globalization;
using System.Xml;

namespace AdfsTester
{
    public static class WsTrustTester
    {
        public static TestRun Run(AppConfig cfg, AdfsMetadata md, TestDepth depth)
        {
            var run = new TestRun("WS-Trust");
            var endpoint = cfg.WsTrustUsernameMixed;
            run.Info("Endpoint", endpoint);

            // Schnelltest: nur Erreichbarkeit des usernamemixed-Endpoints, ohne Zugangsdaten.
            if (depth == TestDepth.Quick)
            {
                var probe = HttpHelper.Get(endpoint, cfg.TimeoutSeconds);
                if (!probe.Transport)
                    run.Add(ErrorLogger.ToCheckResult("WS-Trust Endpoint", "GET " + endpoint, probe.Error));
                else
                    run.Ok("WS-Trust Endpoint", "Endpoint erreichbar (HTTP " + probe.Status +
                           ") - usernamemixed vorhanden. GET ohne SOAP liefert erwartungsgemaess kein Token.");
                run.Info("Hinweis", "Fuer den echten Token-Bezug (Username/Passwort) den 'Tiefen Test' verwenden.");
                return run;
            }

            if (string.IsNullOrEmpty(cfg.Username) || string.IsNullOrEmpty(cfg.Password))
            {
                run.Warn("Credentials", "Kein Username/Passwort konfiguriert.",
                         "WS-Trust usernamemixed benoetigt Benutzername und Passwort (Tab 'Verbindung') fuer den Token-Bezug.");
                return run;
            }
            if (string.IsNullOrEmpty(cfg.WsTrustAppliesTo))
                run.Warn("AppliesTo", "Kein AppliesTo/RP-Identifier konfiguriert - ADFS lehnt das meist ab.",
                         "RP-Identifier auf dem WS-Trust-Tab setzen.");

            string envelope = BuildRst(endpoint, cfg.WsTrustAppliesTo, cfg.Username, cfg.Password);
            run.Info("SOAP-Request", "RST (trust/13, Issue, UsernameToken) erzeugt.", MaskPassword(envelope, cfg.Password));

            var r = HttpHelper.PostRaw(endpoint, envelope,
                "application/soap+xml; charset=utf-8; action=\"http://docs.oasis-open.org/ws-sx/ws-trust/200512/RST/Issue\"",
                null, cfg.TimeoutSeconds);

            if (!r.Transport)
            {
                run.Add(ErrorLogger.ToCheckResult("WS-Trust POST", "POST " + endpoint, r.Error));
                return run;
            }

            if (r.Status != 200)
            {
                // SOAP-Fault auswerten
                var fault = ParseFault(r.Body);
                run.Error("WS-Trust Antwort",
                          "HTTP " + r.Status + (fault != null ? " - " + fault : ""),
                          InterpretFault(fault),
                          ErrorLogger.Truncate(r.Body, 3000));
                return run;
            }

            run.Ok("WS-Trust Antwort", "HTTP 200 - RSTR empfangen (" + r.Body.Length + " Bytes).");
            SamlInspect.Inspect(run, r.Body, md.PrimarySigningCert, cfg.WsTrustAppliesTo, "WS-Trust Token");
            return run;
        }

        private static string BuildRst(string endpoint, string realm, string user, string pass)
        {
            string msgId = "urn:uuid:" + Guid.NewGuid().ToString();
            string tokId = "uuid-" + Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            string created = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            string expires = now.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            return
"<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://www.w3.org/2005/08/addressing\" xmlns:u=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">" +
"<s:Header>" +
"<a:Action s:mustUnderstand=\"1\">http://docs.oasis-open.org/ws-sx/ws-trust/200512/RST/Issue</a:Action>" +
"<a:MessageID>" + msgId + "</a:MessageID>" +
"<a:To s:mustUnderstand=\"1\">" + Xml(endpoint) + "</a:To>" +
"<o:Security s:mustUnderstand=\"1\" xmlns:o=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\">" +
"<u:Timestamp u:Id=\"_0\"><u:Created>" + created + "</u:Created><u:Expires>" + expires + "</u:Expires></u:Timestamp>" +
"<o:UsernameToken u:Id=\"" + tokId + "\">" +
"<o:Username>" + Xml(user) + "</o:Username>" +
"<o:Password>" + Xml(pass) + "</o:Password>" +
"</o:UsernameToken>" +
"</o:Security>" +
"</s:Header>" +
"<s:Body>" +
"<trust:RequestSecurityToken xmlns:trust=\"http://docs.oasis-open.org/ws-sx/ws-trust/200512\">" +
"<wsp:AppliesTo xmlns:wsp=\"http://schemas.xmlsoap.org/ws/2004/09/policy\">" +
"<a:EndpointReference><a:Address>" + Xml(realm) + "</a:Address></a:EndpointReference>" +
"</wsp:AppliesTo>" +
"<trust:KeyType>http://docs.oasis-open.org/ws-sx/ws-trust/200512/Bearer</trust:KeyType>" +
"<trust:RequestType>http://docs.oasis-open.org/ws-sx/ws-trust/200512/Issue</trust:RequestType>" +
"<trust:TokenType>urn:oasis:names:tc:SAML:2.0:assertion</trust:TokenType>" +
"</trust:RequestSecurityToken>" +
"</s:Body>" +
"</s:Envelope>";
        }

        private static string ParseFault(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.LoadXml(body);
                var reason = doc.SelectSingleNode("//*[local-name()='Reason']/*[local-name()='Text']") ??
                             doc.SelectSingleNode("//*[local-name()='faultstring']");
                var subcode = doc.SelectSingleNode("//*[local-name()='Subcode']/*[local-name()='Value']");
                var txt = reason != null ? reason.InnerText.Trim() : "";
                if (subcode != null) txt = subcode.InnerText.Trim() + ": " + txt;
                return txt.Length > 0 ? txt : null;
            }
            catch { return null; }
        }

        private static string InterpretFault(string fault)
        {
            if (string.IsNullOrEmpty(fault)) return "SOAP-Fault - Rohdaten pruefen.";
            if (fault.IndexOf("FailedAuthentication", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fault.IndexOf("ID3242", StringComparison.Ordinal) >= 0)
                return "Authentifizierung fehlgeschlagen: Username/Passwort falsch, Konto gesperrt/deaktiviert oder Extranet-Lockout.";
            if (fault.IndexOf("RequestFailed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Anforderung fehlgeschlagen: AppliesTo/Realm unbekannt oder usernamemixed-Endpoint nicht aktiviert.";
            return "SOAP-Fault: " + fault;
        }

        private static string MaskPassword(string s, string pass)
        {
            if (string.IsNullOrEmpty(pass)) return s;
            return s.Replace(Xml(pass), "********");
        }

        private static string Xml(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
