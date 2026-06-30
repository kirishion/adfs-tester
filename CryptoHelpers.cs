// Krypto- und Format-Helfer ohne externe Libraries:
//  - Base64Url (JWT/JWKS)
//  - JSON via System.Web.Script.Serialization.JavaScriptSerializer
//  - JWT-Decode + RS256/384/512-Signaturpruefung gegen JWKS (x5c oder n/e)
//  - SAML-XML-Signaturpruefung (SignedXml mit GetIdElement-Override)
//  - X509-Zertifikat-Formatierung

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace AdfsTester
{
    public static class B64
    {
        public static byte[] DecodeUrl(string s)
        {
            if (s == null) s = "";
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        public static string DecodeUrlToString(string s)
        {
            return Encoding.UTF8.GetString(DecodeUrl(s));
        }
    }

    public static class Json
    {
        private static readonly JavaScriptSerializer Ser =
            new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

        public static Dictionary<string, object> Parse(string json)
        {
            var o = Ser.DeserializeObject(json) as Dictionary<string, object>;
            return o ?? new Dictionary<string, object>();
        }

        public static object ParseAny(string json) { return Ser.DeserializeObject(json); }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return v.ToString();
            return "";
        }

        public static string Pretty(object o, int indent = 0)
        {
            var sb = new StringBuilder();
            Write(sb, o, indent);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object o, int indent)
        {
            string pad = new string(' ', indent * 2);
            var dict = o as Dictionary<string, object>;
            if (dict != null)
            {
                sb.AppendLine("{");
                int i = 0;
                foreach (var kv in dict)
                {
                    sb.Append(pad + "  \"" + kv.Key + "\": ");
                    Write(sb, kv.Value, indent + 1);
                    sb.AppendLine(++i < dict.Count ? "," : "");
                }
                sb.Append(pad + "}");
                return;
            }
            var arr = o as object[];
            if (arr != null)
            {
                sb.Append("[");
                for (int j = 0; j < arr.Length; j++)
                {
                    Write(sb, arr[j], indent + 1);
                    if (j < arr.Length - 1) sb.Append(", ");
                }
                sb.Append("]");
                return;
            }
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { sb.Append("\"" + o + "\""); return; }
            sb.Append(o.ToString());
        }
    }

    public sealed class JwtParts
    {
        public Dictionary<string, object> Header;
        public Dictionary<string, object> Payload;
        public byte[] Signature;
        public string SigningInput;   // ASCII "header.payload"
        public string Alg;
        public string Kid;
        public string RawHeaderJson;
        public string RawPayloadJson;
    }

    public static class JwtHelper
    {
        public static JwtParts Decode(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) throw new FormatException("JWT hat nicht das Format header.payload.signature");
            var p = new JwtParts();
            p.RawHeaderJson = B64.DecodeUrlToString(parts[0]);
            p.RawPayloadJson = B64.DecodeUrlToString(parts[1]);
            p.Header = Json.Parse(p.RawHeaderJson);
            p.Payload = Json.Parse(p.RawPayloadJson);
            p.Signature = parts.Length >= 3 ? B64.DecodeUrl(parts[2]) : new byte[0];
            p.SigningInput = parts[0] + "." + parts[1];
            p.Alg = Json.Str(p.Header, "alg");
            p.Kid = Json.Str(p.Header, "kid");
            return p;
        }

        // Verifiziert die RS256/384/512-Signatur gegen das uebergebene Zertifikat.
        public static bool VerifyRsa(JwtParts p, X509Certificate2 cert, out string detail)
        {
            detail = "";
            if (cert == null) { detail = "Kein Signaturzertifikat verfuegbar."; return false; }
            string hashName;
            switch ((p.Alg ?? "").ToUpperInvariant())
            {
                case "RS256": hashName = "SHA256"; break;
                case "RS384": hashName = "SHA384"; break;
                case "RS512": hashName = "SHA512"; break;
                default:
                    detail = "Nicht unterstuetzter/asymmetrischer alg-Wert: " + p.Alg;
                    return false;
            }
            try
            {
                var pub = cert.PublicKey.Key as RSACryptoServiceProvider;
                if (pub == null) { detail = "Zertifikat enthaelt keinen RSA-Schluessel."; return false; }
                // In Provider importieren der SHA2 sicher unterstuetzt (PROV_RSA_AES = 24).
                var rsa = new RSACryptoServiceProvider();
                rsa.ImportParameters(pub.ExportParameters(false));
                var data = Encoding.ASCII.GetBytes(p.SigningInput);
                bool ok = rsa.VerifyData(data, hashName, p.Signature);
                detail = ok ? "Signatur gueltig (" + p.Alg + ")" : "Signatur UNGUELTIG (" + p.Alg + ")";
                return ok;
            }
            catch (Exception ex)
            {
                detail = "Signaturpruefung fehlgeschlagen: " + ex.Message;
                return false;
            }
        }
    }

    // Repraesentiert einen JWKS-Key (nur was wir brauchen).
    public sealed class JwksKey
    {
        public string Kid;
        public string X5c;   // erstes Zertifikat der Kette (Base64, NICHT Url)
        public string N;     // Modulus (Base64Url)
        public string E;     // Exponent (Base64Url)

        public X509Certificate2 ToCertificate()
        {
            if (!string.IsNullOrEmpty(X5c))
                return new X509Certificate2(Convert.FromBase64String(X5c));
            if (!string.IsNullOrEmpty(N) && !string.IsNullOrEmpty(E))
            {
                var rsa = new RSACryptoServiceProvider();
                rsa.ImportParameters(new RSAParameters { Modulus = B64.DecodeUrl(N), Exponent = B64.DecodeUrl(E) });
                // Kein Zertifikat, nur Schluessel -> Pseudo-Cert nicht moeglich; Aufrufer nutzt N/E direkt.
                return null;
            }
            return null;
        }
    }

    public static class Jwks
    {
        public static List<JwksKey> Parse(string json)
        {
            var list = new List<JwksKey>();
            var root = Json.Parse(json);
            object keysObj;
            if (!root.TryGetValue("keys", out keysObj)) return list;
            var keys = keysObj as object[];
            if (keys == null) return list;
            foreach (var k in keys)
            {
                var kd = k as Dictionary<string, object>;
                if (kd == null) continue;
                var jk = new JwksKey
                {
                    Kid = Json.Str(kd, "kid"),
                    N = Json.Str(kd, "n"),
                    E = Json.Str(kd, "e")
                };
                object x5cObj;
                if (kd.TryGetValue("x5c", out x5cObj))
                {
                    var arr = x5cObj as object[];
                    if (arr != null && arr.Length > 0 && arr[0] != null) jk.X5c = arr[0].ToString();
                }
                list.Add(jk);
            }
            return list;
        }
    }

    // SignedXml-Variante die Elemente per ID-/AssertionID-Attribut aufloest
    // (SAML-Assertions referenzieren ihre eigene ID in der Signatur).
    internal sealed class SamlSignedXml : SignedXml
    {
        public SamlSignedXml(XmlDocument doc) : base(doc) { }
        public SamlSignedXml(XmlElement el) : base(el) { }

        public override XmlElement GetIdElement(XmlDocument doc, string id)
        {
            var el = base.GetIdElement(doc, id);
            if (el != null) return el;
            foreach (var attr in new[] { "ID", "AssertionID", "Id", "id" })
            {
                var nodes = doc.SelectNodes("//*[@" + attr + "='" + id + "']");
                if (nodes != null && nodes.Count > 0) return nodes[0] as XmlElement;
            }
            return null;
        }
    }

    public static class XmlSignature
    {
        // Verifiziert die erste ds:Signature im Dokument gegen das Zertifikat.
        public static bool Verify(XmlDocument doc, X509Certificate2 cert, out string detail)
        {
            detail = "";
            try
            {
                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
                var sigNode = doc.SelectSingleNode("//ds:Signature", nsmgr) as XmlElement;
                if (sigNode == null) { detail = "Keine XML-Signatur (ds:Signature) gefunden."; return false; }

                var signed = new SamlSignedXml(doc);
                signed.LoadXml(sigNode);
                bool ok = (cert != null)
                    ? signed.CheckSignature(cert, true)
                    : signed.CheckSignature();
                detail = ok ? "XML-Signatur gueltig." : "XML-Signatur UNGUELTIG.";
                return ok;
            }
            catch (Exception ex)
            {
                detail = "XML-Signaturpruefung fehlgeschlagen: " + ex.Message;
                return false;
            }
        }
    }

    public static class CertFormat
    {
        public static string Describe(X509Certificate2 c)
        {
            if (c == null) return "(kein Zertifikat)";
            var sb = new StringBuilder();
            sb.AppendLine("Subject     : " + c.Subject);
            sb.AppendLine("Issuer      : " + c.Issuer);
            sb.AppendLine("Thumbprint  : " + c.Thumbprint);
            sb.AppendLine("Seriennr.   : " + c.SerialNumber);
            sb.AppendLine("Gueltig ab  : " + c.NotBefore.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Gueltig bis : " + c.NotAfter.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Sig-Alg     : " + c.SignatureAlgorithm.FriendlyName);
            try { sb.AppendLine("Key-Alg     : " + c.PublicKey.Oid.FriendlyName + " " + KeySize(c) + " Bit"); }
            catch { }
            return sb.ToString();
        }

        private static int KeySize(X509Certificate2 c)
        {
            try { var rsa = c.PublicKey.Key as RSACryptoServiceProvider; return rsa != null ? rsa.KeySize : 0; }
            catch { return 0; }
        }
    }
}
