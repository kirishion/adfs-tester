// Laedt Zertifikate aus dem Windows-Zertifikatspeicher per Thumbprint.
// Wird fuer SAML genutzt: Signieren des AuthnRequest und Entschluesseln von
// EncryptedAssertions (jeweils privater Schluessel noetig).

using System;
using System.Security.Cryptography.X509Certificates;

namespace AdfsTester
{
    public static class CertStore
    {
        // Sucht das Zertifikat und liefert eine verstaendliche Fehlermeldung,
        // falls es fehlt oder keinen privaten Schluessel hat (needPrivateKey).
        public static X509Certificate2 Find(string location, string storeName, string thumbprint,
                                            bool needPrivateKey, out string error)
        {
            error = "";
            thumbprint = AppConfig.CleanThumb(thumbprint);
            if (string.IsNullOrEmpty(thumbprint)) { error = "Kein Thumbprint angegeben."; return null; }

            StoreLocation loc = string.Equals(location, "CurrentUser", StringComparison.OrdinalIgnoreCase)
                ? StoreLocation.CurrentUser : StoreLocation.LocalMachine;
            StoreName name;
            if (!TryParseStoreName(storeName, out name))
            {
                error = "Unbekannter Store-Name '" + storeName + "' (erlaubt: My, Root, CA, TrustedPeople ...).";
                return null;
            }

            try
            {
                var store = new X509Store(name, loc);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                try
                {
                    var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                    if (found.Count == 0)
                    {
                        error = "Kein Zertifikat mit Thumbprint " + thumbprint + " in " + loc + "\\" + name + " gefunden.";
                        return null;
                    }
                    var cert = found[0];
                    if (needPrivateKey && !cert.HasPrivateKey)
                    {
                        error = "Zertifikat gefunden, aber ohne privaten Schluessel in " + loc + "\\" + name +
                                ". Signieren/Entschluesseln benoetigt den privaten Schluessel (Tool auf dem Server mit dem Key ausfuehren).";
                        return null;
                    }
                    return cert;
                }
                finally { store.Close(); }
            }
            catch (Exception ex)
            {
                error = "Zugriff auf " + loc + "\\" + storeName + " fehlgeschlagen: " + ex.Message +
                        (loc == StoreLocation.LocalMachine ? " (LocalMachine erfordert evtl. Adminrechte.)" : "");
                return null;
            }
        }

        private static bool TryParseStoreName(string s, out StoreName name)
        {
            name = StoreName.My;
            if (string.IsNullOrEmpty(s)) return true;
            return Enum.TryParse(s, true, out name);
        }
    }
}
