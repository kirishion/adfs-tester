// SAML 2.0 Web Browser SSO. Nicht-interaktiv: AuthnRequest bilden +
// Endpoint-Erreichbarkeit. Interaktiv: kompletter SSO mit Auswertung der
// SAMLResponse (Signatur, Conditions, Audience, Claims). Optional:
// AuthnRequest signieren (SignRequest) und verschluesselte Assertions
// entschluesseln.

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;

namespace AdfsTester
{
    public static class SamlTester
    {
        public static TestRun Run(AppConfig cfg, AdfsMetadata md, TestDepth depth)
        {
            bool interactive = depth == TestDepth.Deep;
            var run = new TestRun("SAML 2.0");
            var sso = !string.IsNullOrEmpty(md.Saml2SsoRedirect) ? md.Saml2SsoRedirect : cfg.Saml2Endpoint;
            run.Info("SSO-Endpoint", sso);

            string issuer = cfg.SamlRpIdentifier;
            if (string.IsNullOrEmpty(issuer))
            {
                if (interactive)
                    run.Warn("RP-Identifier", "Kein SAML RP-Identifier (Issuer/Audience) gesetzt.",
                             "Entspricht 'RelyingPartyIdentifier' in eurer CNSamlProvider-Config.");
                else
                    run.Info("RP-Identifier", "Kein RP-Identifier gesetzt - Schnelltest prueft nur die Endpoint-Erreichbarkeit.");
            }

            string acs = cfg.RedirectUri;   // interaktiv faengt das Tool die Response lokal ab
            string authnRequest = BuildAuthnRequest(sso, issuer, acs);
            run.Info("AuthnRequest", "SAML 2.0 AuthnRequest erzeugt (Issuer/Audience = " + issuer + ").", authnRequest);

            string enc;
            try { enc = DeflateBase64Url(authnRequest); }
            catch (Exception ex) { run.Add(ErrorLogger.ToCheckResult("AuthnRequest kodieren", "Deflate/Base64", ex)); return run; }

            // Query zusammenbauen (ggf. signiert)
            string relayState = "adfs-tester";
            string query = "SAMLRequest=" + enc + "&RelayState=" + Uri.EscapeDataString(relayState);

            if (cfg.SamlSignRequest && !interactive)
                run.Info("AuthnRequest signieren", "Wird nur im 'Tiefen Test' angewandt (Schnelltest sendet ungezeichnet).");

            if (cfg.SamlSignRequest && interactive)
            {
                string err;
                var signCert = CertStore.Find(cfg.SamlSignStoreLocation, cfg.SamlSignStoreName, cfg.SamlSignThumbprint, true, out err);
                if (signCert == null)
                {
                    run.Error("AuthnRequest signieren", "Signaturzertifikat nicht ladbar.", err);
                    return run;
                }
                string sigAlgEnc = Uri.EscapeDataString(SamlCrypto.SigAlgRsaSha256);
                string signedOctets = query + "&SigAlg=" + sigAlgEnc;
                string sigErr;
                string sigB64 = SamlCrypto.SignRedirect(signedOctets, signCert, out sigErr);
                if (sigB64 == null) { run.Error("AuthnRequest signieren", "Signatur fehlgeschlagen.", sigErr); return run; }
                query = signedOctets + "&Signature=" + Uri.EscapeDataString(sigB64);
                run.Ok("AuthnRequest signieren", "Request mit Zertifikat " + signCert.Thumbprint + " signiert (RSA-SHA256).");
            }

            var url = sso + (sso.Contains("?") ? "&" : "?") + query;

            if (!interactive)
            {
                run.Info("Redirect-URL", ErrorLogger.Truncate(url, 1000));
                var r = HttpHelper.Get(url, cfg.TimeoutSeconds);
                if (!r.Transport) { run.Add(ErrorLogger.ToCheckResult("SAML SSO Endpoint", "GET " + sso, r.Error)); return run; }
                if (r.Body.IndexOf("MSIS", StringComparison.Ordinal) >= 0)
                    run.Error("ADFS-Fehler", "ADFS lieferte einen MSIS-Fehler.",
                              "RP-Identifier unbekannt oder SAML fuer die RP nicht aktiviert.", ErrorLogger.Truncate(r.Body, 2000));
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
                          "Die lokale Callback-URL (Verbindung-Tab) muss in ADFS als ACS/Redirect fuer die RP registriert sein.", cap.RawRequest);
                return run;
            }

            string samlResponse;
            if (!cap.Params.TryGetValue("SAMLResponse", out samlResponse) || string.IsNullOrEmpty(samlResponse))
            {
                run.Error("SAML Token", "Keine 'SAMLResponse' im Redirect empfangen.",
                          "Empfangene Parameter: " + cap.KeysCsv(), cap.RawRequest);
                return run;
            }

            string xml;
            try { xml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse.Trim())); }
            catch (Exception ex) { run.Add(ErrorLogger.ToCheckResult("SAMLResponse dekodieren", "Base64", ex)); return run; }
            run.Ok("SAML Login", "SAMLResponse empfangen und dekodiert (" + xml.Length + " Zeichen).");

            // ggf. verschluesselte Assertion entschluesseln
            XmlDocument doc;
            try { doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true }; doc.LoadXml(xml); }
            catch (Exception ex) { run.Add(ErrorLogger.ToCheckResult("SAMLResponse parsen", "XML", ex)); return run; }

            bool hasEncrypted = doc.SelectSingleNode("//*[local-name()='EncryptedAssertion']") != null;
            if (hasEncrypted)
            {
                if (string.IsNullOrEmpty(cfg.SamlDecryptThumbprint))
                    run.Warn("Verschluesselte Assertion", "Response enthaelt eine EncryptedAssertion, aber kein Entschluesselungs-Zertifikat konfiguriert.",
                             "Encryption-Cert-Thumbprint auf dem SAML-Tab setzen (entspricht EncryptionCertThumbprint eurer Config).");
                else
                {
                    string cerr;
                    var decCert = CertStore.Find(cfg.SamlDecryptStoreLocation, cfg.SamlDecryptStoreName, cfg.SamlDecryptThumbprint, true, out cerr);
                    if (decCert == null)
                        run.Error("Verschluesselte Assertion", "Entschluesselungs-Zertifikat nicht ladbar.", cerr);
                    else
                    {
                        try
                        {
                            string info;
                            bool ok = SamlCrypto.DecryptInPlace(doc, decCert, out info);
                            if (ok) run.Ok("Verschluesselte Assertion", "Entschluesselt: " + info);
                            else run.Error("Verschluesselte Assertion", "Entschluesselung nicht moeglich: " + info,
                                           "Passt das Encryption-Zertifikat (privater Schluessel) zur ADFS-Konfiguration?");
                        }
                        catch (Exception ex)
                        {
                            run.Add(ErrorLogger.ToCheckResult("Verschluesselte Assertion", "Entschluesselung", ex));
                        }
                    }
                }
            }

            SamlInspect.Inspect(run, doc.OuterXml, md.PrimarySigningCert, issuer, "SAMLResponse");

            // erwarteter Claim
            if (!string.IsNullOrEmpty(cfg.SamlExpectedClaim))
                CheckExpectedClaim(run, doc, cfg.SamlExpectedClaim);

            return run;
        }

        private static void CheckExpectedClaim(TestRun run, XmlDocument doc, string expected)
        {
            var attrs = doc.SelectNodes("//*[local-name()='Attribute']");
            if (attrs != null)
                foreach (XmlNode at in attrs)
                {
                    var el = at as XmlElement;
                    if (el == null) continue;
                    var name = el.GetAttribute("Name");
                    if (string.IsNullOrEmpty(name)) name = el.GetAttribute("AttributeName");
                    if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("/" + expected, StringComparison.OrdinalIgnoreCase) ||
                        name.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var v = el.SelectSingleNode(".//*[local-name()='AttributeValue']");
                        run.Ok("Erwarteter Claim", "'" + expected + "' vorhanden" + (v != null ? " = " + v.InnerText.Trim() : "") + ".");
                        return;
                    }
                }
            run.Error("Erwarteter Claim", "Claim '" + expected + "' NICHT im Token gefunden.",
                      "Claim-Issuance-Rules der RP in ADFS pruefen (entspricht 'Claim' in eurer CNSamlProvider-Config, z.B. upn).");
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
