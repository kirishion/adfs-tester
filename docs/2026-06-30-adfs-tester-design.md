# ADFS-Tester — Design / Spec

**Datum:** 2026-06-30
**Status:** Genehmigt (Design)
**Projektverzeichnis:** `ADFS_Testing/`

## Zweck

Ein eigenständiges Windows-Diagnosetool, um die Anbindung an einen ADFS-Server
zu testen — primär für den Support, abgedeckt werden aber sowohl der
**allgemeine Health-/Infrastruktur-Check** (Erreichbarkeit, Metadata,
Zertifikate) als auch der **end-to-end Auth-Flow** über alle relevanten
Protokolle. Das Tool zeigt möglichst **alle Fehlerquellen** mit konkreten
Lösungsempfehlungen an.

Leitsatz: **eine einzige `AdfsTester.exe`**, die sich ohne Installation und
ohne mitzukopierende Runtime/DLLs auf einen Server oder Client kopieren und
ausführen lässt.

## Rahmenbedingungen

- **Technologie:** WinForms, .NET Framework 4.5+ (auf jedem Windows 10/11
  vorhanden). Build via `csc.exe` + Batch — exakt nach dem Muster des
  bestehenden `OAUTH_Testing`-Tools.
- **Keine externen Libraries / kein NuGet.** Alle fünf Protokolle und die
  Krypto-/Zertifikatsprüfung werden ausschließlich mit Framework-Assemblies
  umgesetzt. Das erfüllt die Single-EXE-Anforderung und die
  Free-/Open-Source-Präferenz (keine kommerziellen Libs).
- **Nur lesend/diagnostisch** — das Tool verändert keine ADFS-Konfiguration.

### Framework-Assemblies pro Aufgabe

| Aufgabe | Assembly / Typen |
|---|---|
| WS-Trust | RST-SOAP-Envelope selbst bauen + `HttpWebRequest`-POST an `usernamemixed`, RSTR via `System.Xml` parsen (kein WCF/`WSTrustChannelFactory` — siehe Review-Hinweis) |
| SAML-/WS-Fed-XML-Signaturprüfung | `System.Security.Cryptography.Xml` (`SignedXml` mit `GetIdElement`-Override für Assertion-ID) |
| OIDC/OAuth JWT | manuelles Base64url-Decode + RS256-Verifikation via `System.Security.Cryptography`; Signing-Key bevorzugt aus JWKS-`x5c` (X509), Fallback `n`/`e` (kein `System.IdentityModel.Tokens.Jwt`-NuGet) |
| TLS / Zertifikate | `X509Chain`, `SslStream` (negotiated `SslProtocol`/`CipherAlgorithm`), `ServerCertificateValidationCallback`; TLS 1.3 auf .NET Framework nicht verfügbar → Info statt Fehler |
| Interaktiver Flow | `TcpListener` auf `127.0.0.1:<freier Port>` (kein Admin/URL-ACL nötig), `Process.Start` (System-Browser) |

## Abgedeckte Protokolle

Alle fünf, mit Priorität auf WS-Federation, SAML 2.0 und OpenID Connect/OAuth:

1. **WS-Federation** (passiv, Web-SSO — häufigster ADFS-Mechanismus)
2. **WS-Trust** (SOAP, nicht-interaktiv: Username/Passwort → SAML-Token)
3. **SAML 2.0** (Web Browser SSO Profile)
4. **OAuth 2.0** (Authorization Code, Client Credentials, ROPC)
5. **OpenID Connect** (Discovery, id_token, JWKS)

### Login-Strategie

Sowohl nicht-interaktiv als auch interaktiv — **ohne** eingebettete
Browser-Komponente (WebView2/WebBrowser-Control werden bewusst vermieden, da
sie die Single-EXE-/Kopier-Anforderung brechen bzw. moderne ADFS-Seiten
schlecht rendern):

- **Nicht-interaktiv** (schneller Support-Test): WS-Trust (User/Pwd → SAML),
  OAuth ROPC (User/Pwd → Token), Client Credentials. Endpoints, Metadata und
  Zertifikate werden ohnehin ohne Login getestet.
- **Interaktiv** (realistisch): lokaler `TcpListener` auf
  `127.0.0.1:<freier Port>` (kein Admin/keine URL-Reservierung nötig), Start des
  **System-Browsers** zur ADFS-Login-Seite, Abfangen des Redirects
  (Code/SAMLResponse) durch Parsen der ersten HTTP-Request-Zeile, Auswertung.
  Mit Timeout-/Abbruch-Handling und einer freundlichen
  „Sie können das Fenster schließen"-Abschlussseite.

## Architektur & Projektstruktur

Alles unter dem eigenen Projektverzeichnis `ADFS_Testing/`:

```
ADFS_Testing/
  docs/
    2026-06-30-adfs-tester-design.md   // dieses Dokument
  Program.cs              // Entry point, STAThread, TLS-Setup, Global Exception Handler
  AppConfig.cs            // Config laden/speichern, DPAPI-verschlüsselte Secrets
  MainForm.cs             // WinForms-GUI: Tabs + MenuStrip + StatusStrip
  ErrorLogger.cs          // Zentrale Fehler-Interpretation (Socket, HTTP, TLS, XML, Krypto)
  MetadataClient.cs       // Federation-Metadata + OIDC-Discovery laden/parsen
  CertificateInspector.cs // SSL/TLS + Signing/Encryption-Zertifikate prüfen
  WsFedTester.cs          // WS-Federation passiv
  WsTrustTester.cs        // WS-Trust (nicht-interaktiv, User/Pwd → SAML)
  SamlTester.cs           // SAML 2.0 Web SSO + Signaturprüfung
  OidcTester.cs           // OpenID Connect / OAuth (Discovery, JWT, JWKS)
  BrowserFlow.cs          // Interaktiver Flow: localhost-Listener + System-Browser
  ReportBuilder.cs        // Export als .txt und .html
  Build-AdfsTester.bat    // csc.exe-Build, nur Framework-Refs
  MakeIcon.ps1            // Icon-Generierung (wie OAUTH_Testing)
  AdfsTester.ico          // generiertes Multi-Size-Icon
```

**Kernprinzip — Isolation:** Jeder `*Tester.cs` ist eine eigenständige,
testbare Einheit mit klarer Schnittstelle (`TestRun Run(AdfsConfig cfg)`), die
eine strukturierte Ergebnisliste zurückgibt. GUI und `ReportBuilder`
konsumieren nur diese Ergebnislisten und kennen die Protokoll-Interna nicht.

## Datenmodell

Das gemeinsame Ergebnis-Modell ist das Herzstück — „alle Fehlerquellen
anzeigen" hängt daran:

```csharp
enum Severity { Ok, Info, Warning, Error }

class CheckResult {
    string Step;            // z.B. "TLS-Zertifikat Hostname-Match"
    Severity Severity;
    string Detail;          // Ist-Wert / was wurde gefunden
    string Recommendation;  // konkreter Lösungshinweis bei Warning/Error
    string RawData;         // optional: Rohdaten (Cert-Dump, XML, JWT-Claims), aufklappbar
}

class TestRun {
    string Title;               // "WS-Federation", "OIDC", ...
    List<CheckResult> Checks;
    Severity Overall;           // schlechtester Einzelstatus
}
```

## GUI-Layout

WinForms wie OAUTH_Testing — MenuStrip (Datei/Extras/Hilfe), StatusStrip mit
Gesamtampel, Tabs mit Badges für das letzte Resultat:

- **Tab „Verbindung & Config"** — Eingaben: ADFS-Host/Base-URL,
  Relying-Party/Realm, Client-ID, Client-Secret (DPAPI), Redirect-URI, Scope,
  optional Username/Passwort für nicht-interaktive Flows.
  Buttons: „Metadata laden", „Alles testen".
- **Tab „Metadata & Zertifikate"** — Ergebnis von MetadataClient +
  CertificateInspector.
- **Tabs „WS-Federation" / „WS-Trust" / „SAML 2.0" / „OIDC / OAuth"** — je eine
  farbcodierte `CheckResult`-Tabelle + aufklappbares RawData-Feld.
- **Tab „Report"** — Gesamtübersicht + Export-Buttons (.txt / .html).

Farbcodierung pro Zeile: grün=Ok, blau=Info, gelb=Warning, rot=Error.
StatusStrip-Gesamtampel wird rot, sobald irgendwo ein Error auftritt.

## Was jeder Tester prüft

**MetadataClient** (Basis für alle):
- WS-Fed/SAML: `/FederationMetadata/2007-06/FederationMetadata.xml` laden &
  parsen → Endpoints, Signing-Certs, unterstützte Protokolle.
- OIDC: `/adfs/.well-known/openid-configuration` + `jwks_uri`.

**CertificateInspector:**
- TLS-Handshake je Endpoint via `SslStream`: TLS-Version, Cipher, Server-Cert.
- `X509Chain`-Validierung: Kette vollständig?, Root vertrauenswürdig?,
  Self-Signed?, Hostname-Match (SAN/CN), Gültigkeit, Revocation (CRL/OCSP).
- Token-Signing-/Encryption-Certs aus Metadata: Ablaufdatum, Thumbprint,
  Warnung < 30 Tage / Error wenn abgelaufen.

**WsFedTester:** Passive-Requestor-URL (`wa=wsignin1.0`),
Endpoint-Erreichbarkeit + Redirect-Antwort; interaktiv via BrowserFlow →
zurückkommende SAML-Assertion-Signatur gegen Signing-Cert verifizieren, Claims
auflisten.

**WsTrustTester** (nicht-interaktiv): RST-SOAP-Envelope (`trust/13`,
`Issue`-Action) mit UsernameToken bauen, an `/adfs/services/trust/13/usernamemixed`
POSTen, RSTR parsen → SAML-Token-Typ, Claims, Signatur, Lifetime. Rohes
SOAP-Request/Response landet im RawData-Feld.

**SamlTester:** AuthnRequest bauen (optional signiert), interaktiv via
BrowserFlow; SAMLResponse: XML-Signatur via `SignedXml` gegen Signing-Cert,
Conditions (NotBefore/NotOnOrAfter, Audience), Destination, InResponseTo,
Status-Code.

**OidcTester:** Discovery laden; Client-Credentials/ROPC nicht-interaktiv und
Authorization-Code interaktiv via BrowserFlow. JWT (id_token/access_token):
Header (alg, kid), Signatur RS256 gegen JWKS-Key (kid-Match), Claims
(iss, aud, exp, nbf, iat), exp-Gültigkeit.

**BrowserFlow:** `TcpListener` auf freiem `127.0.0.1`-Port, System-Browser via
`Process.Start`, Redirect mit Code/SAMLResponse durch Parsen der ersten
HTTP-Request-Zeile abfangen, minimale HTTP-Antwort (Abschlussseite) senden,
Ergebnis an Tester zurück. Timeout + Abbruch-Handling.

## Fehlerquellen-Katalog

Zentral im `ErrorLogger` interpretiert; jede erkannte Fehlerquelle wird als
`CheckResult` mit konkreter Empfehlung ausgegeben.

**Netzwerk / Erreichbarkeit:** DNS nicht auflösbar · Port 443 zu /
Connection refused · Timeout · Firewall/Proxy blockiert · Proxy verlangt Auth ·
ADFS-Proxy (WAP) vs. interner ADFS · Load-Balancer.

**TLS / SSL-Zertifikat:** abgelaufen · Self-Signed/Root nicht vertraut ·
Hostname-Mismatch (SAN/CN) · Zwischenzertifikat fehlt · TLS-Version deaktiviert
(1.0/1.1) · Cipher-Mismatch · SNI · Client-Zertifikat erforderlich
(`/adfs/services/trust/13/certificate`).

**Token-Zertifikate (häufigste ADFS-Ausfälle):** Token-Signing abgelaufen ·
läuft bald ab (<30 Tage) · AutoCertificateRollover hat getauscht, RP nicht
aktualisiert · Cert in Metadata ≠ tatsächlich signierendes Cert ·
Token-Decrypting abgelaufen · revoziert (CRL/OCSP).

**Zeit / Clock-Skew:** Uhrzeit-Differenz Client↔ADFS → Assertion
`NotBefore`/`NotOnOrAfter`, JWT `nbf`/`exp` schlagen fehl · Token-Lifetime zu
kurz.

**Metadata:** Endpoint 404 (Federation-Metadata deaktiviert) · XML malformed ·
entityID-Mismatch · Protokoll für RP nicht aktiviert (Endpoint fehlt).

**WS-Federation:** `wtrealm` matcht keinen RP-Trust · RP-Trust deaktiviert ·
`wreply`/`wctx` falsch · MSIS-Fehlercodes (MSIS7000/7001/9622 …) erkannt &
übersetzt.

**WS-Trust:** Endpoint-Binding deaktiviert (windows/usernamemixed) · MEX nicht
erreichbar · falsche Credentials · Extranet-Lockout · falsches Binding.

**SAML 2.0:** Signatur fehlt/ungültig · unsignierte Response wo Signatur
verlangt · Audience (SP-EntityID) Mismatch · Destination-Mismatch ·
`InResponseTo`/Replay · NameID-Format · Conditions abgelaufen · Status ≠
Success (Requester/Responder) · verschlüsselte Assertion nicht entschlüsselbar.

**OIDC / OAuth:** `invalid_client` (client_id/secret) · `redirect_uri`-Mismatch
(häufigster OAuth-Fehler) · Scope unbekannt/nicht erlaubt ·
`response_type`/`grant_type` nicht erlaubt · PKCE verlangt aber fehlt ·
`invalid_grant` (ROPC-Creds/Code abgelaufen) · `nonce`-Mismatch · `kid` nicht
in JWKS · `alg=none`/Algorithm-Confusion · `aud`-Mismatch · `iss`-Mismatch
(http/https, Trailing-Slash) · Consent/MFA erforderlich.

**Authentifizierung / Konto:** Konto gesperrt/deaktiviert · Passwort abgelaufen
· Extranet-Smart-Lockout · MFA erforderlich.

**ADFS-Konfiguration allgemein:** RP-Identifier-Mismatch · Claims-Rules liefern
erwartete Claims nicht (leere Claims) · Endpoint-Pfade je ADFS-Version
unterschiedlich (2012R2/2016/2019) · Forms- vs. Windows-Auth am Endpoint.

## Fehlerbehandlung

Kein Test wirft ungefangen — jeder `*Tester.Run()` fängt alles und übersetzt es
via `ErrorLogger` in `CheckResult`-Einträge statt zu crashen. Ein
fehlschlagender Test blockiert die anderen nicht („Alles testen" läuft alle
Protokolle durch, auch wenn eines scheitert). Globaler Exception-Handler im
`Program.cs` als letztes Netz.

## Build

`Build-AdfsTester.bat` analog zu `OAUTH_Testing/Build-TestImapOauth.bat`:
`csc.exe` (`Framework64\v4.0.30319` mit Fallback auf `Framework`),
`/target:winexe /optimize+ /platform:anycpu`, Icon einbetten falls vorhanden,
nur Framework-Referenzen (`System.dll`, `System.Drawing.dll`,
`System.Windows.Forms.dll`, `System.Core.dll`, `System.Security.dll`,
`System.Web.dll`, `System.Xml.dll`, `System.Security.dll`).
Ergebnis: **eine einzige `AdfsTester.exe`** ohne mitzukopierende Abhängigkeiten.
(`System.IdentityModel`/`System.ServiceModel` entfallen, da WS-Trust per rohem
SOAP-POST umgesetzt wird.)

## Verifikation

- Format-Parsing, JWT-Decode und Zertifikatsketten-Logik lassen sich gegen
  öffentlich erreichbare Discovery-/Metadata-Endpunkte prüfen (ohne echten
  ADFS).
- Der reale End-to-End-Test erfolgt manuell gegen den ADFS der Umgebung.

## Nicht-Ziele (YAGNI)

- Keine eingebettete Browser-Komponente.
- Kein Kerberos/WIA-Negotiate-Flow.
- Keine Modifikation der ADFS-Config (nur lesend/diagnostisch).
- Kein Dauer-Monitoring/Service — reines On-Demand-Tool.
