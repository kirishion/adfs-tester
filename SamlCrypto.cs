// SAML-spezifische Krypto: Entschluesselung von EncryptedAssertion und
// Signieren des AuthnRequest fuer das HTTP-Redirect-Binding.
// Nutzt ausschliesslich Framework-APIs (System.Security.Cryptography.Xml).

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace AdfsTester
{
    public static class SamlCrypto
    {
        private const string XENC = "http://www.w3.org/2001/04/xmlenc#";

        // Entschluesselt alle EncryptedAssertion im Dokument in-place mit dem
        // privaten Schluessel des Zertifikats. Gibt true zurueck, wenn mindestens
        // eine Assertion entschluesselt wurde.
        public static bool DecryptInPlace(XmlDocument doc, X509Certificate2 cert, out string info)
        {
            info = "";
            var encList = doc.SelectNodes("//*[local-name()='EncryptedAssertion']");
            if (encList == null || encList.Count == 0) { info = "Keine EncryptedAssertion vorhanden."; return false; }

            RSA rsa = GetRsaPrivateKey(cert, out info);
            if (rsa == null) return false;

            using (rsa)
            {
                int done = 0;
                foreach (XmlNode ea in encList)
                {
                    var edEl = FindLocal(ea, "EncryptedData") as XmlElement;
                    var ekEl = FindLocal(ea, "EncryptedKey") as XmlElement;
                    if (edEl == null || ekEl == null) continue;

                    var keyCipher = FindLocal(ekEl, "CipherValue");
                    if (keyCipher == null) continue;
                    byte[] encKey = Convert.FromBase64String(Clean(keyCipher.InnerText));
                    var ekMethod = FindLocal(ekEl, "EncryptionMethod") as XmlElement;
                    // ADFS-Default ist RSA-OAEP; bei fehlendem EncryptionMethod daher OAEP annehmen.
                    bool oaep = ekMethod == null ||
                                ekMethod.GetAttribute("Algorithm").IndexOf("oaep", StringComparison.OrdinalIgnoreCase) >= 0;

                    byte[] sessionKey = EncryptedXml.DecryptKey(encKey, rsa, oaep);

                    var ed = new EncryptedData();
                    ed.LoadXml(edEl);
                    var sym = CreateSym(ed.EncryptionMethod != null ? ed.EncryptionMethod.KeyAlgorithm : null);
                    if (sym == null) { info = "Nicht unterstuetzter Datenverschluesselungs-Algorithmus."; return done > 0; }
                    using (sym)
                    {
                        sym.Key = sessionKey;
                        byte[] plain = new EncryptedXml(doc).DecryptData(ed, sym);
                        string xml = Encoding.UTF8.GetString(plain);
                        var frag = doc.CreateDocumentFragment();
                        frag.InnerXml = xml;
                        if (ea.ParentNode != null) { ea.ParentNode.ReplaceChild(frag, ea); done++; }
                    }
                }
                info = done + " Assertion(en) entschluesselt.";
                return done > 0;
            }
        }

        // Baut den Signaturteil fuer das HTTP-Redirect-Binding.
        // signedOctets = "SAMLRequest=..&RelayState=..&SigAlg=.." (bereits URL-kodiert, in dieser Reihenfolge).
        // Liefert den URL-kodierten Signature-Parameterwert.
        public static string SignRedirect(string signedOctets, X509Certificate2 cert, out string error)
        {
            error = "";
            RSA rsa = GetRsaPrivateKey(cert, out error);
            if (rsa == null) return null;

            using (rsa)
            try
            {
                var data = Encoding.UTF8.GetBytes(signedOctets);
                // GetRSAPrivateKey liefert CNG- wie CSP-Schluessel und unterstuetzt SHA256 zuverlaessig.
                byte[] sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return Convert.ToBase64String(sig);
            }
            catch (Exception ex)
            {
                error = "Signieren fehlgeschlagen: " + ex.Message +
                        " (Provider des Zertifikats unterstuetzt evtl. kein SHA256.)";
                return null;
            }
        }

        public const string SigAlgRsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

        // Liefert den privaten RSA-Schluessel des Zertifikats. GetRSAPrivateKey()
        // deckt CNG- (ADFS 2016+) und klassische CSP-Schluessel ab und unterstuetzt
        // SHA256 - anders als der veraltete cert.PrivateKey-Zugriff.
        private static RSA GetRsaPrivateKey(X509Certificate2 cert, out string error)
        {
            error = "";
            try
            {
                var rsa = cert.GetRSAPrivateKey();
                if (rsa == null) error = "Zertifikat hat keinen RSA-Privatschluessel im Store.";
                return rsa;
            }
            catch (Exception ex) { error = "Privater Schluessel nicht nutzbar: " + ex.Message; return null; }
        }

        private static SymmetricAlgorithm CreateSym(string alg)
        {
            if (string.IsNullOrEmpty(alg)) return new RijndaelManaged();
            if (alg.IndexOf("tripledes", StringComparison.OrdinalIgnoreCase) >= 0)
                return new TripleDESCryptoServiceProvider();
            if (alg.IndexOf("aes", StringComparison.OrdinalIgnoreCase) >= 0)
                return new RijndaelManaged();
            return null;
        }

        private static XmlNode FindLocal(XmlNode parent, string localName)
        {
            return parent.SelectSingleNode(".//*[local-name()='" + localName + "']");
        }

        private static string Clean(string s)
        {
            return s == null ? "" : s.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "").Trim();
        }
    }
}
