// Konfiguration des ADFS-Test-Tools. Einfaches key=value-Textformat.
// Felder sind nach Protokoll gruppiert (Prefix Saml/WsFed/WsTrust/OAuth),
// gemeinsame Felder ohne Prefix. Secrets werden mit Windows-DPAPI
// (CurrentUser) verschluesselt abgelegt.

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AdfsTester
{
    public sealed class AppConfig
    {
        // ---- Gemeinsam ----
        public string AdfsHost = "adfs.firma.tld";
        // Lokale Callback-URL fuer interaktive Tests (SAML-ACS + OAuth redirect_uri).
        public string RedirectUri = "http://localhost:8765/adfs-tester/";
        // Testbenutzer fuer nicht-interaktive Flows (WS-Trust, OAuth ROPC).
        public string Username = "";
        public string Password = "";
        public int TimeoutSeconds = 30;
        public int CertWarnDays = 30;
        public bool VerifyServerCert = true;

        // ---- WS-Federation ----
        public string WsFedRealm = "";      // wtrealm
        public string WsFedReply = "";      // wreply (optional)

        // ---- WS-Trust ----
        public string WsTrustAppliesTo = ""; // RP-Identifier (AppliesTo)

        // ---- SAML 2.0 ----
        public string SamlRpIdentifier = "";       // RelyingPartyIdentifier (Issuer/Audience)
        public bool SamlSignRequest = false;        // AuthnRequest signieren
        public string SamlSignStoreLocation = "LocalMachine";
        public string SamlSignStoreName = "My";
        public string SamlSignThumbprint = "";
        public string SamlDecryptStoreLocation = "LocalMachine";
        public string SamlDecryptStoreName = "My";
        public string SamlDecryptThumbprint = "";   // fuer verschluesselte Assertions
        public string SamlExpectedClaim = "";       // optional: dieser Claim muss vorhanden sein

        // ---- OAuth / OIDC ----
        public string ClientId = "";
        public string ClientSecret = "";
        public string Scope = "openid";
        public string OAuthResource = "";

        public static AppConfig Load(string path, Action<string> warn = null)
        {
            var c = new AppConfig();
            if (!File.Exists(path)) return c;
            int n = 0;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                n++;
                var line = raw.TrimEnd();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    if (warn != null) warn("[WARN] Config Zeile " + n + " ohne '=' ignoriert: " + line);
                    continue;
                }
                var k = line.Substring(0, eq).Trim();
                var v = line.Substring(eq + 1);
                switch (k)
                {
                    case "AdfsHost": c.AdfsHost = v; break;
                    case "RedirectUri": c.RedirectUri = v; break;
                    case "Username": c.Username = v; break;
                    case "Password": c.Password = SecretCrypto.Unprotect(v); break;
                    case "TimeoutSeconds": c.TimeoutSeconds = ParseInt(v, 30); break;
                    case "CertWarnDays": c.CertWarnDays = ParseInt(v, 30); break;
                    case "VerifyServerCert": c.VerifyServerCert = ParseBool(v, true); break;

                    case "WsFedRealm": c.WsFedRealm = v; break;
                    case "WsFedReply": c.WsFedReply = v; break;

                    case "WsTrustAppliesTo": c.WsTrustAppliesTo = v; break;

                    case "SamlRpIdentifier": c.SamlRpIdentifier = v; break;
                    case "SamlSignRequest": c.SamlSignRequest = ParseBool(v, false); break;
                    case "SamlSignStoreLocation": c.SamlSignStoreLocation = v; break;
                    case "SamlSignStoreName": c.SamlSignStoreName = v; break;
                    case "SamlSignThumbprint": c.SamlSignThumbprint = CleanThumb(v); break;
                    case "SamlDecryptStoreLocation": c.SamlDecryptStoreLocation = v; break;
                    case "SamlDecryptStoreName": c.SamlDecryptStoreName = v; break;
                    case "SamlDecryptThumbprint": c.SamlDecryptThumbprint = CleanThumb(v); break;
                    case "SamlExpectedClaim": c.SamlExpectedClaim = v; break;

                    case "ClientId": c.ClientId = v; break;
                    case "ClientSecret": c.ClientSecret = SecretCrypto.Unprotect(v); break;
                    case "Scope": c.Scope = v; break;
                    case "OAuthResource": c.OAuthResource = v; break;
                }
            }
            return c;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ADFS-Test-Tool - Konfiguration");
            sb.AppendLine("# Password und ClientSecret sind mit Windows DPAPI (CurrentUser) verschluesselt.");
            sb.AppendLine();
            sb.AppendLine("# --- Gemeinsam ---");
            sb.AppendLine("AdfsHost=" + AdfsHost);
            sb.AppendLine("RedirectUri=" + RedirectUri);
            sb.AppendLine("Username=" + Username);
            sb.AppendLine("Password=" + SecretCrypto.Protect(Password));
            sb.AppendLine("TimeoutSeconds=" + TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("CertWarnDays=" + CertWarnDays.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("VerifyServerCert=" + (VerifyServerCert ? "true" : "false"));
            sb.AppendLine();
            sb.AppendLine("# --- WS-Federation ---");
            sb.AppendLine("WsFedRealm=" + WsFedRealm);
            sb.AppendLine("WsFedReply=" + WsFedReply);
            sb.AppendLine();
            sb.AppendLine("# --- WS-Trust ---");
            sb.AppendLine("WsTrustAppliesTo=" + WsTrustAppliesTo);
            sb.AppendLine();
            sb.AppendLine("# --- SAML 2.0 ---");
            sb.AppendLine("SamlRpIdentifier=" + SamlRpIdentifier);
            sb.AppendLine("SamlSignRequest=" + (SamlSignRequest ? "true" : "false"));
            sb.AppendLine("SamlSignStoreLocation=" + SamlSignStoreLocation);
            sb.AppendLine("SamlSignStoreName=" + SamlSignStoreName);
            sb.AppendLine("SamlSignThumbprint=" + SamlSignThumbprint);
            sb.AppendLine("SamlDecryptStoreLocation=" + SamlDecryptStoreLocation);
            sb.AppendLine("SamlDecryptStoreName=" + SamlDecryptStoreName);
            sb.AppendLine("SamlDecryptThumbprint=" + SamlDecryptThumbprint);
            sb.AppendLine("SamlExpectedClaim=" + SamlExpectedClaim);
            sb.AppendLine();
            sb.AppendLine("# --- OAuth / OIDC ---");
            sb.AppendLine("ClientId=" + ClientId);
            sb.AppendLine("ClientSecret=" + SecretCrypto.Protect(ClientSecret));
            sb.AppendLine("Scope=" + Scope);
            sb.AppendLine("OAuthResource=" + OAuthResource);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        // ---- Abgeleitete URLs (ADFS-Standardpfade) ----
        public string BaseUrl
        {
            get
            {
                var h = (AdfsHost ?? "").Trim();
                if (h.Length == 0) return "";
                if (!h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !h.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    h = "https://" + h;
                return h.TrimEnd('/');
            }
        }

        public string FederationMetadataUrl { get { return BaseUrl + "/FederationMetadata/2007-06/FederationMetadata.xml"; } }
        public string OpenIdConfigurationUrl { get { return BaseUrl + "/adfs/.well-known/openid-configuration"; } }
        public string WsFedEndpoint { get { return BaseUrl + "/adfs/ls/"; } }
        public string Saml2Endpoint { get { return BaseUrl + "/adfs/ls/"; } }
        public string OAuthAuthorizeEndpoint { get { return BaseUrl + "/adfs/oauth2/authorize"; } }
        public string OAuthTokenEndpoint { get { return BaseUrl + "/adfs/oauth2/token"; } }
        public string WsTrustUsernameMixed { get { return BaseUrl + "/adfs/services/trust/13/usernamemixed"; } }

        private static int ParseInt(string v, int def)
        {
            int r;
            return int.TryParse((v ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static bool ParseBool(string v, bool def)
        {
            v = (v ?? "").Trim();
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1") return true;
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) || v == "0") return false;
            return def;
        }

        public static string CleanThumb(string t)
        {
            if (t == null) return "";
            var sb = new StringBuilder();
            foreach (var ch in t) if (Uri.IsHexDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            return sb.ToString();
        }
    }

    internal static class SecretCrypto
    {
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plain);
                var enc = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return "DPAPI:" + Convert.ToBase64String(enc);
            }
            catch { return ""; }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith("DPAPI:")) return stored;
            try
            {
                var enc = Convert.FromBase64String(stored.Substring("DPAPI:".Length));
                var bytes = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
    }
}
