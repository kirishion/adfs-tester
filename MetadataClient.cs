// Laedt und parst die ADFS-Metadaten:
//  - WS-Fed/SAML Federation-Metadata (XML)
//  - OpenID-Connect Discovery (.well-known) + JWKS
// Liefert eine AdfsMetadata-Struktur, die andere Tester wiederverwenden.

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace AdfsTester
{
    public sealed class AdfsMetadata
    {
        public string EntityId = "";
        public readonly List<X509Certificate2> SigningCerts = new List<X509Certificate2>();
        public readonly List<X509Certificate2> EncryptionCerts = new List<X509Certificate2>();
        public string PassiveEndpoint = "";        // WS-Fed Passive Requestor
        public string Saml2SsoRedirect = "";        // SAML2 SingleSignOnService (HTTP-Redirect)

        // OIDC
        public bool OpenIdLoaded;
        public string Issuer = "";
        public string AuthorizeEndpoint = "";
        public string TokenEndpoint = "";
        public string JwksUri = "";
        public readonly List<JwksKey> JwksKeys = new List<JwksKey>();
        public object[] GrantTypesSupported;
        public object[] ResponseTypesSupported;

        public X509Certificate2 PrimarySigningCert
        { get { return SigningCerts.Count > 0 ? SigningCerts[0] : null; } }
    }

    public static class MetadataClient
    {
        private const string MD = "urn:oasis:names:tc:SAML:2.0:metadata";
        private const string DS = "http://www.w3.org/2000/09/xmldsig#";
        private const string FED = "http://docs.oasis-open.org/wsfed/federation/200706";
        private const string WSA = "http://www.w3.org/2005/08/addressing";

        public static AdfsMetadata Load(TestRun run, AppConfig cfg)
        {
            var md = new AdfsMetadata();
            LoadFederation(run, cfg, md);
            LoadOpenId(run, cfg, md);
            return md;
        }

        private static void LoadFederation(TestRun run, AppConfig cfg, AdfsMetadata md)
        {
            var url = cfg.FederationMetadataUrl;
            var r = HttpHelper.Get(url, cfg.TimeoutSeconds);
            if (!r.Transport)
            {
                run.Add(ErrorLogger.ToCheckResult("Federation-Metadata laden", "GET " + url, r.Error));
                return;
            }
            if (r.Status != 200)
            {
                run.Error("Federation-Metadata laden",
                          "HTTP " + r.Status + " " + r.StatusText + " von " + url,
                          r.Status == 404
                            ? "Pfad/Host falsch oder Federation-Metadata-Endpoint deaktiviert."
                            : "Antwort-Body in den Rohdaten pruefen.",
                          ErrorLogger.Truncate(r.Body, 2000));
                return;
            }

            XmlDocument doc;
            try
            {
                doc = new XmlDocument { XmlResolver = null };
                doc.LoadXml(r.Body);
            }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult("Federation-Metadata parsen", "XML-Parse", ex));
                return;
            }

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("md", MD); ns.AddNamespace("ds", DS);
            ns.AddNamespace("fed", FED); ns.AddNamespace("wsa", WSA);

            var rootEl = doc.DocumentElement;
            if (rootEl != null) md.EntityId = rootEl.GetAttribute("entityID");
            run.Ok("Federation-Metadata laden",
                    "Geladen (" + r.Body.Length + " Bytes), entityID: " + (md.EntityId.Length > 0 ? md.EntityId : "(keines)"));

            // Zertifikate je use=signing/encryption
            var keyNodes = doc.SelectNodes("//md:KeyDescriptor", ns);
            int signing = 0, encryption = 0, unspecified = 0;
            if (keyNodes != null)
            {
                foreach (XmlNode kd in keyNodes)
                {
                    var certNode = kd.SelectSingleNode(".//ds:X509Certificate", ns);
                    if (certNode == null) continue;
                    X509Certificate2 cert;
                    try { cert = new X509Certificate2(Convert.FromBase64String(CleanBase64(certNode.InnerText))); }
                    catch { continue; }
                    var kdEl = kd as XmlElement;
                    var use = kdEl != null ? kdEl.GetAttribute("use") : "";
                    if (use.Equals("signing", StringComparison.OrdinalIgnoreCase)) { md.SigningCerts.Add(cert); signing++; }
                    else if (use.Equals("encryption", StringComparison.OrdinalIgnoreCase)) { md.EncryptionCerts.Add(cert); encryption++; }
                    else { md.SigningCerts.Add(cert); unspecified++; } // ohne use -> beide Rollen
                }
            }
            if (signing + encryption + unspecified == 0)
                run.Warn("Token-Zertifikate", "Keine KeyDescriptor-Zertifikate in den Metadaten gefunden.",
                          "Metadaten unvollstaendig? RP-Trust/IdP-Konfiguration pruefen.");
            else
                run.Ok("Token-Zertifikate",
                        signing + " Signing-, " + encryption + " Encryption-Zertifikat(e)" +
                        (unspecified > 0 ? ", " + unspecified + " ohne 'use'" : "") + " gefunden.");

            // Endpoints
            var passive = doc.SelectSingleNode("//fed:PassiveRequestorEndpoint/wsa:EndpointReference/wsa:Address", ns);
            if (passive != null) { md.PassiveEndpoint = passive.InnerText.Trim(); run.Ok("WS-Fed Endpoint", md.PassiveEndpoint); }

            var saml = doc.SelectSingleNode(
                "//md:IDPSSODescriptor/md:SingleSignOnService[@Binding='urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect']", ns) as XmlElement;
            if (saml != null) { md.Saml2SsoRedirect = saml.GetAttribute("Location"); run.Ok("SAML2 SSO Endpoint", md.Saml2SsoRedirect); }

            // Token-Zertifikate inhaltlich pruefen
            int i = 1;
            foreach (var c in md.SigningCerts)
                CertificateInspector.InspectTokenCert(run, "Token-Signing-Zertifikat #" + (i++), c, cfg.CertWarnDays);
            i = 1;
            foreach (var c in md.EncryptionCerts)
                CertificateInspector.InspectTokenCert(run, "Token-Encryption-Zertifikat #" + (i++), c, cfg.CertWarnDays);
        }

        private static void LoadOpenId(TestRun run, AppConfig cfg, AdfsMetadata md)
        {
            var url = cfg.OpenIdConfigurationUrl;
            var r = HttpHelper.Get(url, cfg.TimeoutSeconds);
            if (!r.Transport)
            {
                run.Warn("OpenID-Discovery laden", "Nicht erreichbar: " + (r.Error != null ? r.Error.Message : "?"),
                         "Falls OAuth/OIDC nicht genutzt wird, ist das unkritisch. Sonst Endpoint pruefen.",
                         r.Error != null ? ErrorLogger.BuildRaw("GET " + url, r.Error) : null);
                return;
            }
            if (r.Status != 200)
            {
                run.Warn("OpenID-Discovery laden", "HTTP " + r.Status + " von " + url,
                         "OAuth/OIDC evtl. nicht aktiviert (ADFS 2012R2 hat eingeschraenkten OAuth-Support).",
                         ErrorLogger.Truncate(r.Body, 1500));
                return;
            }

            try
            {
                var d = Json.Parse(r.Body);
                md.OpenIdLoaded = true;
                md.Issuer = Json.Str(d, "issuer");
                md.AuthorizeEndpoint = Json.Str(d, "authorization_endpoint");
                md.TokenEndpoint = Json.Str(d, "token_endpoint");
                md.JwksUri = Json.Str(d, "jwks_uri");
                object gt, rt;
                if (d.TryGetValue("grant_types_supported", out gt)) md.GrantTypesSupported = gt as object[];
                if (d.TryGetValue("response_types_supported", out rt)) md.ResponseTypesSupported = rt as object[];
                run.Ok("OpenID-Discovery laden",
                        "issuer: " + md.Issuer + " | authorize/token/jwks vorhanden: " +
                        (md.AuthorizeEndpoint.Length > 0) + "/" + (md.TokenEndpoint.Length > 0) + "/" + (md.JwksUri.Length > 0) + ".",
                        Json.Pretty(d));
            }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult("OpenID-Discovery parsen", "JSON-Parse", ex));
                return;
            }

            // JWKS laden
            if (md.JwksUri.Length > 0)
            {
                var jr = HttpHelper.Get(md.JwksUri, cfg.TimeoutSeconds);
                if (jr.Transport && jr.Status == 200)
                {
                    try
                    {
                        md.JwksKeys.AddRange(Jwks.Parse(jr.Body));
                        run.Ok("JWKS laden", md.JwksKeys.Count + " Signatur-Schluessel geladen.", ErrorLogger.Truncate(jr.Body, 2000));
                    }
                    catch (Exception ex) { run.Add(ErrorLogger.ToCheckResult("JWKS parsen", "JSON-Parse", ex)); }
                }
                else
                {
                    run.Warn("JWKS laden", "JWKS nicht ladbar (HTTP " + jr.Status + ").",
                             "id_token-Signaturpruefung nicht moeglich ohne JWKS.");
                }
            }
        }

        private static string CleanBase64(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "").Trim();
        }
    }
}
