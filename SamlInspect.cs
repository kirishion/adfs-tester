// Gemeinsame Inspektion einer SAML-Assertion/-Response. Robust gegenueber
// SAML 1.1 (WS-Fed/WS-Trust) und SAML 2.0 durch local-name()-XPath.
// Prueft Signatur, Conditions (Zeit/Audience), Status und listet Claims.

using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;

namespace AdfsTester
{
    public static class SamlInspect
    {
        public static void Inspect(TestRun run, string xml, X509Certificate2 signingCert,
                                   string expectedAudience, string label)
        {
            XmlDocument doc;
            try
            {
                doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
                doc.LoadXml(xml);
            }
            catch (Exception ex)
            {
                run.Add(ErrorLogger.ToCheckResult(label + " parsen", "XML-Parse", ex));
                return;
            }

            // ---- samlp:Status (nur bei Response vorhanden) ----
            var statusCode = doc.SelectSingleNode("//*[local-name()='StatusCode']") as XmlElement;
            if (statusCode != null)
            {
                var val = statusCode.GetAttribute("Value");
                if (val.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0)
                    run.Ok(label + " Status", "StatusCode = Success.");
                else
                {
                    var msg = doc.SelectSingleNode("//*[local-name()='StatusMessage']");
                    run.Error(label + " Status", "StatusCode = " + val + (msg != null ? " (" + msg.InnerText + ")" : ""),
                              "ADFS hat die Anfrage abgelehnt. Status Requester = Fehler im Request (Realm/ACS/Signatur), Responder = serverseitig.");
                }
            }

            // ---- Assertion vorhanden? ----
            var assertion = doc.SelectSingleNode("//*[local-name()='Assertion']") as XmlElement;
            if (assertion == null)
            {
                run.Warn(label + " Assertion", "Keine Assertion gefunden (evtl. verschluesselt: EncryptedAssertion).",
                         "Falls EncryptedAssertion: Token-Decrypting-Zertifikat noetig - dieses Tool prueft verschluesselte Assertions nicht inhaltlich.");
                var enc = doc.SelectSingleNode("//*[local-name()='EncryptedAssertion']");
                if (enc != null)
                    run.Info(label + " EncryptedAssertion", "Assertion ist verschluesselt - Decryption-Zertifikat erforderlich.");
                return;
            }

            // ---- Signatur ----
            var sig = doc.SelectSingleNode("//*[local-name()='Signature']");
            if (sig == null)
            {
                run.Warn(label + " Signatur", "Keine XML-Signatur in der Antwort.",
                         "ADFS signiert Token normalerweise. Fehlt sie, ist die RP-/Trust-Konfiguration pruefenswert.");
            }
            else
            {
                string detail;
                bool ok = XmlSignature.Verify(doc, signingCert, out detail);
                if (ok) run.Ok(label + " Signatur", detail + " (gegen Token-Signing-Zertifikat verifiziert).");
                else run.Error(label + " Signatur", detail,
                               "Signatur passt nicht zum Signing-Zertifikat aus den Metadaten - falsches/rotiertes Zertifikat oder manipulierte Antwort.");
            }

            // ---- Conditions (Zeit) ----
            var cond = doc.SelectSingleNode("//*[local-name()='Conditions']") as XmlElement;
            if (cond != null)
            {
                CheckTime(run, label, cond.GetAttribute("NotBefore"), cond.GetAttribute("NotOnOrAfter"));

                // Audience (SAML2: Audience, SAML1.1: Audience unter AudienceRestrictionCondition)
                if (!string.IsNullOrEmpty(expectedAudience))
                {
                    bool found = false; string seen = "";
                    var auds = doc.SelectNodes("//*[local-name()='Audience']");
                    if (auds != null)
                        foreach (XmlNode a in auds)
                        {
                            seen += (seen.Length > 0 ? ", " : "") + a.InnerText.Trim();
                            if (string.Equals(a.InnerText.Trim(), expectedAudience.Trim(), StringComparison.OrdinalIgnoreCase))
                                found = true;
                        }
                    if (found) run.Ok(label + " Audience", "Audience passt zu Realm '" + expectedAudience + "'.");
                    else run.Error(label + " Audience",
                                   "Audience-Mismatch. Erwartet '" + expectedAudience + "', Token enthaelt: " + (seen.Length > 0 ? seen : "(keine)"),
                                   "Realm/Identifier im Tool muss exakt dem RP-Identifier in ADFS entsprechen.");
                }
            }

            // ---- NameID / Subject ----
            var nameId = doc.SelectSingleNode("//*[local-name()='NameID']") ??
                         doc.SelectSingleNode("//*[local-name()='NameIdentifier']");
            if (nameId != null) run.Info(label + " NameID", nameId.InnerText.Trim());

            // ---- Claims ----
            var attrs = doc.SelectNodes("//*[local-name()='Attribute']");
            var sb = new StringBuilder();
            int count = 0;
            if (attrs != null)
                foreach (XmlNode at in attrs)
                {
                    var el = at as XmlElement;
                    if (el == null) continue;
                    var name = el.GetAttribute("Name");
                    if (string.IsNullOrEmpty(name)) name = el.GetAttribute("AttributeName");
                    var valNodes = el.SelectNodes(".//*[local-name()='AttributeValue']");
                    var vals = new StringBuilder();
                    if (valNodes != null)
                        foreach (XmlNode v in valNodes)
                            vals.Append((vals.Length > 0 ? ", " : "") + v.InnerText.Trim());
                    sb.AppendLine(name + " = " + vals);
                    count++;
                }
            if (count > 0) run.Ok(label + " Claims", count + " Claim(s) im Token.", sb.ToString());
            else run.Warn(label + " Claims", "Keine Claims (Attribute) im Token.",
                          "Claim-Issuance-Rules der Relying Party in ADFS pruefen - leere Claims sind eine haeufige Fehlerquelle.");
        }

        private static void CheckTime(TestRun run, string label, string notBefore, string notOnOrAfter)
        {
            DateTime nb, na; var now = DateTime.UtcNow;
            bool hasNb = DateTime.TryParse(notBefore, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out nb);
            bool hasNa = DateTime.TryParse(notOnOrAfter, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out na);

            if (hasNb && now < nb.AddMinutes(-5))
                run.Error(label + " Gueltigkeit", "Token noch nicht gueltig (NotBefore " + nb.ToString("u") + ", jetzt " + now.ToString("u") + ").",
                          "Uhrzeit-Differenz (Clock-Skew) zwischen Client und ADFS - Zeitsynchronisation pruefen.");
            else if (hasNa && now > na.AddMinutes(5))
                run.Error(label + " Gueltigkeit", "Token abgelaufen (NotOnOrAfter " + na.ToString("u") + ", jetzt " + now.ToString("u") + ").",
                          "Token-Lifetime zu kurz oder Clock-Skew - Zeitsynchronisation pruefen.");
            else
                run.Ok(label + " Gueltigkeit", "Zeitfenster gueltig" +
                       (hasNa ? " (bis " + na.ToString("u") + ")" : "") + ".");
        }
    }
}
