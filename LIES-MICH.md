# Dynamic Island für Windows – Version 1.4

Neu in 1.4: **RAM-Begrenzer** (Einstellungen → Verhalten → Arbeitsspeicher) und automatischer Bau auf GitHub.


Neu in 1.3:
- **Am Rand andocken:** nah an einen Rand ziehen → klebt bündig dran (Ecken dort gerade), geht zur Mitte hin auf
- **In den Rand eindrücken:** ganz in den Rand schieben → nur ein schmaler leuchtender Streifen bleibt. Klick drauf oder rausziehen = wieder da
- **Ruheanzeige:** Datum, Uhr, Akku, Leistung (CPU/RAM) oder Minimal – Mausrad über der Insel wechselt
- **Pause → Ruheanzeige:** pausierte Musik verschwindet nach einstellbarer Zeit
- **Neue Farben:** 14 abgestimmte Vorlagen, Farbpaletten (Neutral, Pastell, Kräftig, Tief) und „Überrasch mich“
- **Noch flüssiger:** Inhalt wird während Animationen als Bild zwischengespeichert

Eine Insel wie beim iPhone, die du überall auf den Bildschirm ziehen kannst.

## Bedienung

| Was | Wie |
|---|---|
| Aufklappen | Maus drüber (oder Klick – einstellbar) |
| Verschieben | Insel anklicken und ziehen, wohin du willst. Oben in der Mitte rastet sie ein |
| Menü | **Doppelklick** → Einstellungen · Zurück · Schliessen |
| Am Rand andocken | Nah an einen Bildschirmrand ziehen |
| Eindrücken | Ganz in den Rand schieben (Maus bis an den Bildschirmrand) |
| Rausholen | Streifen anklicken oder rausziehen |
| Ruheanzeige wechseln | Mausrad über der zugeklappten Insel |
| Rechtsklick | Einstellungen, Autostart, Position zurücksetzen, Neu starten, Beenden |
| Musik | Klick aufs Cover = Spotify öffnen · Balken klicken/ziehen = spulen |

Zugeklappt siehst du sofort, ob Musik läuft:
- **Läuft:** Equalizer-Balken hüpfen in deiner Akzentfarbe
- **Pausiert:** Balken liegen flach und grau, Titel und Cover werden blasser

Nahe am Rand geht die Insel automatisch zur Bildschirmmitte hin auf (z. B. unten → nach oben).

## Neu in 1.6

- **Fortschrittsbalken-Designs:** Google-Welle (Standard), Linie, Pixel-Tiere (Katze, Hund, Hase, Ente, Schildkröte, Schnecke), Pixel Art, Programmierer, Arbeit, Freizeit, Natur, Wissenschaft – auch mit eigener Farbe
- **Quelle wechseln:** In der offenen Insel oben rechts auf „•••“ → Spotify, Browser/Webplayer usw.
- **Lyrics** (optional, von lrclib.net) – unter dem Song oder in der zugeklappten Insel
- **Ruhezustand:** zugeklappt, halb offen oder ganz offen
- **MacBook-Notch:** in die Nähe von oben-Mitte ziehen → fliegt hin und klebt an
- **Stil „Cover“** und **Akzent vom Song-Cover**
- **Apps-Ordner** erstellen, benennen, sortieren; Dateien einfach auf die Insel ziehen
- **Aufgaben** mit Erinnerung, **Wetter** (open-meteo.com), **Kontrollzentrum** (Lautstärke, Helligkeit, WLAN, Bluetooth, Flugmodus, Kamera …)

## Einstellungen (Doppelklick → Einstellungen)

- **Aussehen:** Vorlagen, **Meine Designs** (eigene Designs mit Namen speichern, anwenden, löschen – nur lokal), Stil (Einfarbig / Verlauf / Liquid Glass / **Klarglas** mit Regler Wolkig ↔ Klar), Farben, Verlaufs-Winkel, Deckkraft, Akzentfarbe, Grösse, Rundung, Schatten, Schriftart
- **Verhalten:** Öffnen per Maus oder Klick, Verzögerungen, Animationstempo, Equalizer, Autostart, Vollbild ausblenden, Position zurücksetzen
- **Funktionen:** Musik, Apps, Tasten, Uhr ein-/ausschalten
- **Verbindungen:** Musikquelle – nur Spotify oder alle Apps (YouTube, Browser …)
- **Apps:** aus installierten Apps wählen (mit Suche), Programme/Ordner/Webseiten hinzufügen oder einfach ins Fenster ziehen
- **Tastenkürzel:** beliebte Windows-Kürzel antippen oder eigene aufnehmen (Knopf drücken, dann Tasten drücken)
- **Info:** Einstellungsdatei, Fehlerprotokoll, Neu starten, Beenden

Fast alles wird **sofort** an der Insel übernommen.

> Liquid Glass: Glas-Look mit Lichtkante und Spiegelung. Echter Blur hinter der Insel ist in Windows zusammen mit flüssigen Animationen leider nicht sauber machbar.

---

## Installieren / Updaten

1. Läuft die Insel schon? → **Doppelklick → Schliessen**.
2. ZIP entpacken. Bei einem Update: den Ordner **`src`** im alten DynamicIsland-Ordner durch den neuen ersetzen.
   (Deine Einstellungen in `App\einstellungen.json` bleiben erhalten.)
3. Im Explorer den Ordner öffnen, in dem `src` liegt → oben in die Adresszeile `powershell.exe` → Enter.
4. Diesen Befehl einfügen und Enter (baut die App in den Ordner „DynamicIsland“ in deinem Benutzerordner – nicht in OneDrive):
   ```
   dotnet publish src\DynamicIsland.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $HOME\DynamicIsland\App
   ```
5. Wenn es fertig ist:
   ```
   start $HOME\DynamicIsland\App\DynamicIsland.exe
   ```

(Beim allerersten Mal braucht es vorher das .NET SDK: `winget install Microsoft.DotNet.SDK.8`)

---

## Zum Teilen: eine .exe, die sofort läuft

Damit Freunde die App einfach herunterladen und doppelklicken können (ohne .NET, ohne Befehle):

1. Im Ordner mit `src` → Adresszeile → `powershell.exe` → Enter
2. Einfügen und Enter:
   ```
   dotnet publish src\DynamicIsland.csproj -p:PublishProfile=Teilen
   ```
3. Fertig: Im neuen Ordner **`Teilen`** liegt **`DynamicIsland.exe`** (ca. 60–80 MB, weil .NET mit drin ist).
4. Diese eine Datei verschicken (z. B. über OneDrive-/Google-Drive-Link – für WhatsApp ist sie zu gross).

Beim ersten Öffnen zeigt Windows wahrscheinlich „Der Computer wurde durch Windows geschützt“ → **Weitere Informationen → Trotzdem ausführen**.
Das kommt, weil die App nicht mit einem (kostenpflichtigen) Zertifikat signiert ist – sie ist trotzdem sicher.

Die Einstellungen landen jeweils neben der .exe (`einstellungen.json`) – also am besten die .exe in einen eigenen Ordner legen, nicht direkt in Downloads.

---

## Noch einfacher teilen: GitHub baut die .exe automatisch

1. Auf github.com ein neues Repository erstellen (z. B. „DynamicIsland“).
2. Den ganzen Ordner hochladen („Add file → Upload files“) – inklusive des versteckten Ordners `.github`.
   (Tipp: GitHub Desktop macht das am einfachsten.)
3. GitHub baut die App jetzt selbst: Reiter **Actions** → neuester Lauf → unten **DynamicIsland** herunterladen.
4. Für einen festen Link zum Teilen: rechts **Releases → Create a new release** → Tag `v1.4` → veröffentlichen.
   Nach 2–3 Minuten hängt dort `DynamicIsland.exe` – diesen Link können Freunde öffnen, herunterladen, doppelklicken.

---

## RAM-Begrenzer

Einstellungen → Verhalten → **Arbeitsspeicher (RAM)**
- Die Insel misst selbst: **Minimum** (was sie in Ruhe wirklich braucht) und **Maximum** (ab da bringt mehr nichts).
- Der Schieberegler geht nur zwischen diesen zwei Werten.
- Tiefer = weniger RAM, aber Windows lagert mehr aus → die Insel kann etwas träger reagieren.
- Solange das Einstellungsfenster offen ist, gilt keine Grenze. Gemessen wird nach dem Schliessen.

---

## Eigene Funktionen (Module)

1. In `src\Modules\` die Datei **`BeispielModul.cs`** kopieren und umbenennen.
2. Klassennamen ändern und `Enabled => true` setzen.
3. Neu bauen (Schritt 4 oben) → neuer Tab ist da und erscheint auch unter Einstellungen → Funktionen.

Farben, die sich dem Design anpassen: `.Themed(TextBlock.ForegroundProperty, "IslandSubText")`
(Schlüssel: `IslandText`, `IslandSubText`, `IslandAccent`, `IslandChip`, `IslandHover`, `IslandTrack`, `IslandPlaceholder`)

---

## Leistung

- Zugeklappt wartet die Insel nur auf Meldungen von Windows (Songwechsel, Pause …).
- Equalizer: nur wenn Musik läuft und er sichtbar ist, mit 30 statt 60 Bildern/s. Abschaltbar.
- Fortschrittsbalken: bewegt sich nur, wenn der Musik-Tab offen ist.
- Uhr: 1 Update pro Minute. Vollbild-Check: alle 1,5 s ein kurzer Blick.
- Verschieben macht Windows selbst → so flüssig wie ein normales Fenster.

## Probleme?

- Fehler stehen in `App\fehler.log` (oder Einstellungen → Info → Fehlerprotokoll) → an Claude schicken.
- Insel weg? Task-Manager → „DynamicIsland“ beenden und `App\DynamicIsland.exe` neu starten.
- Insel ausserhalb vom Bildschirm? Rechtsklick → „Oben in die Mitte setzen“ (oder Einstellungen → Verhalten).
- Deinstallieren: Einstellungen → Verhalten → „Mit Windows starten“ aus → Schliessen → Ordner löschen.
