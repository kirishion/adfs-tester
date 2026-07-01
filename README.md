# ADFS-Tester

Ein kleines Windows-Tool, um ADFS-Probleme einzugrenzen: erreicht der Client den
Dienst, stimmen die Zertifikate, und funktioniert die Anmeldung über das jeweilige
Protokoll? Geschrieben fürs Troubleshooting im Support, läuft als einzelne `.exe`
ohne Installation.

## Download

Fertige `AdfsTester.exe` unter [Releases](../../releases/latest).

Herunterladen, auf den betroffenen Server oder Client kopieren, doppelklicken.
Voraussetzung ist .NET Framework 4.5 oder neuer; das ist auf Windows 10/11 ohnehin
vorhanden. Es muss nichts weiter mitkopiert oder installiert werden.

## Was getestet wird

Zertifikate und Erreichbarkeit (ohne Anmeldung):

- TCP-Verbindung und TLS-Handshake (Protokoll, Cipher)
- Server-Zertifikat: Kette, Gültigkeit, Hostname, Sperrstatus
- Token-Signing- und -Encryption-Zertifikate aus den Federation-Metadaten,
  inklusive Warnung vor baldigem Ablauf
- OpenID-Connect-Discovery und JWKS

Anmelde-Flows:

- WS-Federation
- WS-Trust (Benutzername/Passwort, ohne Browser)
- SAML 2.0
- OAuth 2.0 (Client-Credentials, ROPC, Authorization Code)
- OpenID Connect (id_token gegen JWKS geprüft: Signatur, iss, aud, exp, nbf)

Jeder Befund wird farbig (OK / Hinweis / Warnung / Fehler) mit Klartext-Ursache und
Lösungsvorschlag angezeigt. Das Gesamtergebnis lässt sich als TXT oder HTML für ein
Ticket exportieren.

## Bedienung

Auf dem Tab **Verbindung** den ADFS-Host eintragen (z. B. `adfs.firma.tld`) und
**Metadata & Zertifikate laden**. Das prüft Erreichbarkeit und alle Zertifikate –
schon das deckt die meisten Störungen auf.

Jedes Protokoll hat einen eigenen Tab mit genau seinen Feldern (WS-Fed: Realm;
WS-Trust: RP-Identifier; SAML: RP-Identifier, optional Signieren/Encryption-Zert;
OIDC/OAuth: ClientId/Secret, Scope). Jedes Feld hat einen Tooltip, der erklärt, wo
der Wert in ADFS bzw. in der Anwendungs-Konfiguration steht.

Pro Tab (und für alle Protokolle zusammen) gibt es zwei Modi:

- **Schnelltest:** ohne Browser und ohne Zugangsdaten. Prüft nur Erreichbarkeit,
  Endpunkte, Zertifikate und ob die nötige Konfiguration vorhanden ist. Fehlende
  protokollspezifische Felder sind dabei nur Hinweise. Ideal für einen ersten Check.
- **Tiefer Test:** End-to-End. Führt die echte Anmeldung durch (System-Browser bei
  WS-Fed/SAML/OAuth-Code bzw. Zugangsdaten bei WS-Trust/ROPC), holt ein Token und
  prüft Signatur, Gültigkeit und Claims. Bei SAML zusätzlich: signierter
  AuthnRequest (SignRequest) und Entschlüsselung verschlüsselter Assertions.

Der Tiefe Test öffnet für die interaktiven Flows den Standard-Browser und fängt die
Antwort über einen lokalen Loopback-Listener ab (keine eingebettete Browser-Komponente).
Die Callback-URL vom Tab **Verbindung** muss dafür in ADFS registriert sein. Zum
Signieren bzw. Entschlüsseln bei SAML wird der private Schlüssel des jeweiligen
Zertifikats benötigt – das Tool also auf dem Server ausführen, wo dieser vorliegt.

## Selbst bauen

Kein Visual Studio nötig. Der Build nutzt den im Framework enthaltenen Compiler:

```
Build-AdfsTester.bat
```

Das erzeugt `AdfsTester.exe` im selben Ordner. Das App-Icon lässt sich optional vorab
mit `powershell -ExecutionPolicy Bypass -File MakeIcon.ps1` neu generieren.

## Tests

Offline-Selbsttest der Kernlogik (Base64Url, JWT, JSON, JWKS, verschlüsselte Config,
SAML-Auswertung, Report):

```
Tests\Build-SelfTest.bat
```

Rückgabewert 0 bedeutet, alle Tests sind durchgelaufen.

## Technisches

- WinForms, .NET Framework 4.5+, ausschließlich Framework-Bibliotheken (kein NuGet).
- Passwörter und Secrets werden lokal mit Windows-DPAPI (CurrentUser) verschlüsselt
  in der Config-Datei abgelegt.
- Das Tool liest und diagnostiziert nur; es ändert nichts an der ADFS-Konfiguration.

Aufbau und Designentscheidungen sind unter [docs/](docs/) beschrieben.

## Lizenz

[MIT](LICENSE)
