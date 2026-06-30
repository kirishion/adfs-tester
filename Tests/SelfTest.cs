// Deterministischer Selbsttest der Kernlogik (ohne Netzwerk/ohne echtes ADFS).
// Build: Build-SelfTest.bat  ->  SelfTest.exe  (Exit 0 = alle Tests gruen)
//
// Deckt ab: Base64Url, JWT-Decode, JSON-Parsing, JWKS-x5c->X509,
// AppConfig DPAPI-Roundtrip, SAML-Response-Inspektion (XPath/Conditions/
// Audience/Claims), ReportBuilder. Die XML-Signaturpruefung (SignedXml) und
// die echten Protokoll-Flows erfordern ein echtes ADFS und sind hier bewusst
// nicht abgedeckt (siehe AdfsSelfTest gegen Live-OIDC fuer die Online-Pfade).

using System;
using System.IO;
using AdfsTester;

internal static class SelfTest
{
    static int _pass, _fail;

    static void Check(string name, bool ok, string extra = "")
    {
        if (ok) { _pass++; Console.WriteLine("  PASS  " + name); }
        else { _fail++; Console.WriteLine("  FAIL  " + name + (extra.Length > 0 ? "  -> " + extra : "")); }
    }

    static void Main()
    {
        Console.WriteLine("ADFS-Tester Selbsttest (offline)\n");

        // 1) Base64Url Roundtrip
        var data = System.Text.Encoding.UTF8.GetBytes("Hällo-ADFS_/+=");
        string b64u = Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var back = B64.DecodeUrl(b64u);
        Check("B64.DecodeUrl Roundtrip", System.Text.Encoding.UTF8.GetString(back) == "Hällo-ADFS_/+=");

        // 2) JWT-Decode (Header/Payload/Claims)
        string h = "eyJhbGciOiJSUzI1NiIsImtpZCI6ImFiYzEyMyJ9";          // {"alg":"RS256","kid":"abc123"}
        string pl = "eyJpc3MiOiJodHRwczovL2EudGxkIiwiYXVkIjoiY2xpIiwiZXhwIjoyMH0"; // {"iss":"https://a.tld","aud":"cli","exp":20}
        var jwt = JwtHelper.Decode(h + "." + pl + ".sig");
        Check("JWT alg", jwt.Alg == "RS256");
        Check("JWT kid", jwt.Kid == "abc123");
        Check("JWT iss-Claim", Json.Str(jwt.Payload, "iss") == "https://a.tld");
        Check("JWT aud-Claim", Json.Str(jwt.Payload, "aud") == "cli");

        // 3) JSON-Parsing
        var d = Json.Parse("{\"a\":\"x\",\"n\":5,\"arr\":[1,2],\"o\":{\"k\":\"v\"}}");
        Check("Json.Str einfach", Json.Str(d, "a") == "x");
        Check("Json.Str verschachtelt vorhanden", d.ContainsKey("o"));
        Check("Json.Pretty nicht leer", Json.Pretty(d).Length > 0);

        // 4) JWKS-Parsing (kid, n/e, x5c-Erfassung). Die x5c->X509-Konvertierung
        //    ist durch den Online-Test gegen echte JWKS abgedeckt.
        var keys = Jwks.Parse("{\"keys\":[{\"kid\":\"k1\",\"kty\":\"RSA\",\"n\":\"AQAB\",\"e\":\"AQAB\",\"x5c\":[\"ZHVtbXk=\"]}," +
                              "{\"kid\":\"k2\",\"kty\":\"RSA\",\"n\":\"abc\",\"e\":\"AQAB\"}]}");
        Check("JWKS Key-Anzahl", keys.Count == 2, keys.Count.ToString());
        Check("JWKS kid", keys.Count == 2 && keys[0].Kid == "k1" && keys[1].Kid == "k2");
        Check("JWKS x5c erfasst", keys.Count == 2 && keys[0].X5c == "ZHVtbXk=");
        Check("JWKS n/e erfasst", keys.Count == 2 && keys[1].N == "abc" && keys[1].E == "AQAB");

        // 5) AppConfig DPAPI-Roundtrip
        string cfgFile = Path.Combine(Path.GetTempPath(), "adfs_selftest_cfg.txt");
        try
        {
            var cfg = new AppConfig { AdfsHost = "adfs.test", ClientSecret = "GeHeim!§$", Password = "pw123" };
            cfg.Save(cfgFile);
            string raw = File.ReadAllText(cfgFile);
            Check("Config: Secret nicht im Klartext", !raw.Contains("GeHeim!"));
            Check("Config: DPAPI-Marker vorhanden", raw.Contains("DPAPI:"));
            var loaded = AppConfig.Load(cfgFile);
            Check("Config: ClientSecret Roundtrip", loaded.ClientSecret == "GeHeim!§$");
            Check("Config: Password Roundtrip", loaded.Password == "pw123");
            Check("Config: BaseUrl Normalisierung", loaded.BaseUrl == "https://adfs.test");
        }
        finally { try { File.Delete(cfgFile); } catch { } }

        // 6) SAML-Response-Inspektion (unsigniert): Status/Audience/Claims/Conditions
        var run = new TestRun("SAML-Test");
        SamlInspect.Inspect(run, SampleSamlResponse(), null, "https://app.test/", "SAML");
        Check("SAML: Status Success erkannt", HasOk(run, "Status"));
        Check("SAML: Audience-Match erkannt", HasOk(run, "Audience"));
        Check("SAML: Claims extrahiert", HasOk(run, "Claims"));
        Check("SAML: Zeitfenster gueltig", HasOk(run, "Gueltigkeit"));
        Check("SAML: fehlende Signatur als Warnung", HasSeverity(run, "Signatur", Severity.Warning));

        // 7) SAML-Audience-Mismatch -> Fehler
        var run2 = new TestRun("SAML-Mismatch");
        SamlInspect.Inspect(run2, SampleSamlResponse(), null, "https://falsch.test/", "SAML");
        Check("SAML: Audience-Mismatch als Fehler", HasSeverity(run2, "Audience", Severity.Error));

        // 8) ReportBuilder
        string txt = ReportBuilder.BuildText(new[] { run }, new AppConfig(), "2026-06-30 10:00:00");
        string html = ReportBuilder.BuildHtml(new[] { run }, new AppConfig(), "2026-06-30 10:00:00");
        Check("Report TXT enthaelt Titel", txt.Contains("SAML-Test"));
        Check("Report HTML wohlgeformt", html.Contains("<table") && html.Contains("</html>"));

        Console.WriteLine("\n----------------------------------------");
        Console.WriteLine("Ergebnis: " + _pass + " PASS, " + _fail + " FAIL");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }

    static bool HasOk(TestRun r, string stepContains) { return HasSeverity(r, stepContains, Severity.Ok); }

    static bool HasSeverity(TestRun r, string stepContains, Severity sev)
    {
        foreach (var c in r.Checks)
            if (c.Step.IndexOf(stepContains, StringComparison.OrdinalIgnoreCase) >= 0 && c.Severity == sev) return true;
        return false;
    }

    static string SampleSamlResponse()
    {
        return
"<samlp:Response xmlns:samlp=\"urn:oasis:names:tc:SAML:2.0:protocol\" xmlns:saml=\"urn:oasis:names:tc:SAML:2.0:assertion\">" +
"<samlp:Status><samlp:StatusCode Value=\"urn:oasis:names:tc:SAML:2.0:status:Success\"/></samlp:Status>" +
"<saml:Assertion>" +
"<saml:Issuer>https://adfs.test/adfs/services/trust</saml:Issuer>" +
"<saml:Subject><saml:NameID>user@test</saml:NameID></saml:Subject>" +
"<saml:Conditions NotBefore=\"2000-01-01T00:00:00Z\" NotOnOrAfter=\"2999-01-01T00:00:00Z\">" +
"<saml:AudienceRestriction><saml:Audience>https://app.test/</saml:Audience></saml:AudienceRestriction>" +
"</saml:Conditions>" +
"<saml:AttributeStatement>" +
"<saml:Attribute Name=\"email\"><saml:AttributeValue>user@test</saml:AttributeValue></saml:Attribute>" +
"<saml:Attribute Name=\"role\"><saml:AttributeValue>admin</saml:AttributeValue></saml:Attribute>" +
"</saml:AttributeStatement>" +
"</saml:Assertion></samlp:Response>";
    }
}
