// SAML 2.0 Web Browser SSO. Nicht-interaktiv: AuthnRequest bilden +
// Endpoint-Erreichbarkeit. Interaktiv: kompletter SSO mit Auswertung der
// SAMLResponse (Signatur, Conditions, Audience, Claims).

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AdfsTester
{
    public static class SamlTester
    {
        public static TestRun Run(AppConfig cfg, AdfsMetadata md, bool interactive)
        {
            var run = new TestRun("SAML 2.0");
            var sso = !string.IsNullOrEmpty(md.Saml2SsoRedirect) ? md.Saml2SsoRedirect : cfg.Saml2Endpoint;
            run.Info("SSO-Endpoint", sso);

            string acs = cfg.RedirectUri;
            string authnRequest = BuildAuthnRequest(sso, cfg.Realm, acs);
            run.Info("AuthnRequest", "SAML 2.0 AuthnRequest erzeugt (Issuer/Audience = " + cfg.Realm + ").", authnRequest);

            string encoded;
            try { encoded = DeflateBase64Url(authnRequest); }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult("AuthnRequest kodieren", "Deflate/Base64", ex));
                return run;
            }

            var url = sso + (sso.Contains("?") ? "&" : "?") + "SAMLRequest=" + encoded + "&RelayState=adfs-tester";

            if (!interactive)
            {
                run.Info("Redirect-URL", ErrorLogger.Truncate(url, 1000));
                var r = HttpHelper.Get(url, cfg.TimeoutSeconds);
                if (!r.Transport)
                {
                    run.Add(ErrorLogger.ToCheckResult("SAML SSO Endpoint", "GET " + sso, r.Error));
                    return run;
                }
                if (r.Body.IndexOf("MSIS", StringComparison.Ordinal) >= 0)
                    run.Error("ADFS-Fehler", "ADFS lieferte einen MSIS-Fehler.",
                              "wtrealm/Issuer unbekannt oder SAML fuer die RP nicht aktiviert.", ErrorLogger.Truncate(r.Body, 2000));
                else if (r.Status == 200 || (r.Status >= 300 && r.Status < 400))
                    run.Ok("SAML SSO Endpoint", "Endpoint erreichbar (HTTP " + r.Status + ") - Login-Seite wird ausgeliefert.");
                else
                    run.Warn("SAML SSO Endpoint", "Unerwarteter HTTP-Status " + r.Status + ".", null, ErrorLogger.Truncate(r.Body, 1500));

                run.Info("Hinweis", "Token-Auswertung (Signatur/Claims) erfolgt nur im interaktiven Modus.");
                return run;
            }

            // ---- Interaktiv ----
            run.Info("Interaktiv", "System-Browser wird geoeffnet. Bitte am ADFS anmelden ...");
            var cap = BrowserFlow.Capture(cfg.RedirectUri, url, Math.Max(cfg.TimeoutSeconds, 120));
            if (!cap.Success)
            {
                run.Error("SAML Login", cap.Error,
                          "AssertionConsumerService-URL (= Redirect-URI) muss in ADFS fuer die RP registriert sein.", cap.RawRequest);
                return run;
            }

            string samlResponse;
            if (!cap.Params.TryGetValue("SAMLResponse", out samlResponse) || string.IsNullOrEmpty(samlResponse))
            {
                run.Error("SAML Token", "Keine 'SAMLResponse' im Redirect empfangen.",
                          "Empfangene Parameter: " + string.Join(", ", new System.Collections.Generic.List<string>(cap.Params.Keys).ToArray()),
                          cap.RawRequest);
                return run;
            }

            string xml;
            try { xml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse.Trim())); }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult("SAMLResponse dekodieren", "Base64", ex));
                return run;
            }

            run.Ok("SAML Login", "SAMLResponse empfangen und dekodiert (" + xml.Length + " Zeichen).");
            SamlInspect.Inspect(run, xml, md.PrimarySigningCert, cfg.Realm, "SAMLResponse");
            return run;
        }

        private static string BuildAuthnRequest(string destination, string issuer, string acs)
        {
            string id = "_" + Guid.NewGuid().ToString("N");
            string instant = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            return
"<samlp:AuthnRequest xmlns:samlp=\"urn:oasis:names:tc:SAML:2.0:protocol\" " +
"ID=\"" + id + "\" Version=\"2.0\" IssueInstant=\"" + instant + "\" " +
"Destination=\"" + Xml(destination) + "\" " +
"AssertionConsumerServiceURL=\"" + Xml(acs) + "\" " +
"ProtocolBinding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST\">" +
"<saml:Issuer xmlns:saml=\"urn:oasis:names:tc:SAML:2.0:assertion\">" + Xml(issuer) + "</saml:Issuer>" +
"<samlp:NameIDPolicy AllowCreate=\"true\" Format=\"urn:oasis:names:tc:SAML:2.0:nameid-format:unspecified\"/>" +
"</samlp:AuthnRequest>";
        }

        // HTTP-Redirect-Binding: raw DEFLATE -> Base64 -> URL-encode
        private static string DeflateBase64Url(string xml)
        {
            var bytes = Encoding.UTF8.GetBytes(xml);
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    ds.Write(bytes, 0, bytes.Length);
                return Uri.EscapeDataString(Convert.ToBase64String(ms.ToArray()));
            }
        }

        private static string Xml(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
