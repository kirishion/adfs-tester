// Konfiguration des ADFS-Test-Tools. Einfaches key=value-Textformat.
// Secrets (ClientSecret, Password) werden mit Windows DPAPI (CurrentUser)
// verschluesselt abgelegt.

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AdfsTester
{
    public sealed class AppConfig
    {
        // ---- ADFS-Basis ----
        // Host oder Basis-URL des ADFS. Beispiel: "adfs.firma.tld" oder
        // "https://adfs.firma.tld". Pfade werden automatisch angehaengt.
        public string AdfsHost = "adfs.firma.tld";

        // ---- WS-Federation / SAML (Relying Party) ----
        public string Realm = "https://app.firma.tld/";   // wtrealm / SAML Audience (SP-EntityID)
        public string Wreply = "";                          // optionales wreply / ACS-URL

        // ---- OAuth / OIDC ----
        public string ClientId = "";
        public string ClientSecret = "";
        public string RedirectUri = "http://localhost:8765/adfs-tester/";
        public string Scope = "openid";
        public string OAuthResource = "";   // ADFS-spezifisch: 'resource'-Parameter (RP-Identifier)

        // ---- Credentials fuer nicht-interaktive Flows (WS-Trust, ROPC) ----
        public string Username = "";
        public string Password = "";

        // ---- Optionen ----
        public int TimeoutSeconds = 30;
        public int CertWarnDays = 30;        // Warnung wenn Zertifikat < X Tage gueltig
        public bool VerifyServerCert = true; // false = TLS-Fehler ignorieren (nur Diagnose)

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
                    case "Realm": c.Realm = v; break;
                    case "Wreply": c.Wreply = v; break;
                    case "ClientId": c.ClientId = v; break;
                    case "ClientSecret": c.ClientSecret = SecretCrypto.Unprotect(v); break;
                    case "RedirectUri": c.RedirectUri = v; break;
                    case "Scope": c.Scope = v; break;
                    case "OAuthResource": c.OAuthResource = v; break;
                    case "Username": c.Username = v; break;
                    case "Password": c.Password = SecretCrypto.Unprotect(v); break;
                    case "TimeoutSeconds": c.TimeoutSeconds = ParseInt(v, 30); break;
                    case "CertWarnDays": c.CertWarnDays = ParseInt(v, 30); break;
                    case "VerifyServerCert": c.VerifyServerCert = ParseBool(v, true); break;
                }
            }
            return c;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ADFS-Test-Tool - Konfiguration");
            sb.AppendLine("# ClientSecret und Password sind mit Windows DPAPI (CurrentUser) verschluesselt.");
            sb.AppendLine("AdfsHost=" + AdfsHost);
            sb.AppendLine("Realm=" + Realm);
            sb.AppendLine("Wreply=" + Wreply);
            sb.AppendLine("ClientId=" + ClientId);
            sb.AppendLine("ClientSecret=" + SecretCrypto.Protect(ClientSecret));
            sb.AppendLine("RedirectUri=" + RedirectUri);
            sb.AppendLine("Scope=" + Scope);
            sb.AppendLine("OAuthResource=" + OAuthResource);
            sb.AppendLine("Username=" + Username);
            sb.AppendLine("Password=" + SecretCrypto.Protect(Password));
            sb.AppendLine("TimeoutSeconds=" + TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("CertWarnDays=" + CertWarnDays.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("VerifyServerCert=" + (VerifyServerCert ? "true" : "false"));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        // ---- Abgeleitete URLs (ADFS-Standardpfade) ----------------------------

        // Normalisierte Basis-URL: "https://<host>" ohne Trailing-Slash.
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

        public string FederationMetadataUrl
        { get { return BaseUrl + "/FederationMetadata/2007-06/FederationMetadata.xml"; } }

        public string OpenIdConfigurationUrl
        { get { return BaseUrl + "/adfs/.well-known/openid-configuration"; } }

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
