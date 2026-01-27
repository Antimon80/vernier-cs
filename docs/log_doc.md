# Dokumentation Wireshark Logging

## Initialisierung und Betriebsart
- Das Gerät wird beim Öffnen der App automatisch erkannt und initialisiert
- Das Gerät unterstützt die folgenden Betriebsarten:
  - `Absorption`: default nach der Initialisierung eingstellt
  - `Transmission`
  - `Fluoreszenz 405 nm`
  - `Fluoreszenz 500 nm`
  - `Intensität`: zeichnet mit Hilfe einer Glasfaser die Emission einer externen Lichtquelle auf
  - `Unkalibrierte Messwerte`: erfasst Rohdaten als Counts
- Details siehe: [init_mode.md](init_mode.md)
- Log-Daten: 
  - docs\wireshark\spectrovis\01\capture_files\init
    - Der Name es Unterverzeichnisses bezieht sich beim Wechsel des Betriebsmodus auf den Endzustand
  - docs\wireshark\spectrovis\01\json_files\init
  - docs\wireshark\spectrovis\01\json_files\change_mode
    - Der Name des übergeordneten Ordners bezieht sich auf den Endzustand
    - Der Name des Unterverzeichnisses bezieht sich auf den Ausgangszustand

## Konfiguration
- In jedem der verschiedenen Betriebsmodi sind die folgenden Messmodi möglich:
  - `Vollspektrum`: kontinuierliche Datenerfassung über den gesamten Wellenlängenbereich (380 - 950 nm)
  - `Zeitgesteuert`: zeitaufgelöste Datenerfassung  bei bestimmten Wellenlängen
  - `Ereignisse mit Tastatureingabe`: manuelle Erfassung einzelner Datenpunkte bei bestimmten Wellenlängen
- Details siehe [acquisition.md](acquisition.md)
- Log-Daten:
  - docs\wireshark\spectrovis\01\capture_files\change_acquisition
    - Der Name des ersten Unterverzeichnisses bezieht sich auf den Betriebsmodus, in dem die Zustandswechsel getrackt wurden
    - Der Name des zweiten Unterverzeichnisses bezieht sich beim Wechsel des Datenerfassungsmodus auf den Endzustand
  - docs\wireshark\spectrovis\01\json_files\change_acquisition
    - Der Name des ersten Unterverzeichnisses bezieht sich auf den Betriebsmodus, in dem die Zustandswechsel getrackt wurden
    - Der Name des zweiten Unterverzeichnisses bezieht sich beim Wechel des Datenerfassungsmodus auf den Endzustand
    - Der Name des dritten Unververzeichnisses bezieht sich beim Wechsel des Datenerfassungsmodus auf den Anfangszustand

## Kalibration und Datenerfassung
- Das Gerät muss in den Betriebsarten `Absorption` und `Transmission` vor Beginn der Datenerfassung kalibriert werden.
- Details siehe: [cal_meas.md](cal_meas.md)
- Log-Daten:
  - Kalibration:
    - docs\wireshark\spectrovis\01\capture_files\calibration
    - docs\wireshark\spectrovis\01\json_files\calibration
    - docs\wireshark\spectrovis\01\json_files\recalibration
    - Der Name des Unterverzeichnisses bezieht sich auf den Betriebsmodus, in dem das Gerät kalibriert wird.
  - Datenerfassung:

## Workflows
- Zur Rekonstruktion des Kommunikationsprotokolls werden verschiedene Workflows mit Wireshark getrackt. Detaillierte Beschreibungen siehe [workflow.md](workflow.md)
- Die zugehörigen Log-Daten sind in den nummerierten Unterordnern `02` und `03` in `docs\wireshark\spectrovis\` enthalten. Der Unterordner `04` enthält Mitschnitte einer Gerätesteuerung mit der neuen App `SpectralAnalysis` von Vernier.

## Fehlerfälle
- Bei der Testreihe zur Rekalibration des Geräts wird zwischenzeitlich die App `MS Teams` parallel zu LoggerPro und Wireshark geöffnet
- Infolge ändert sich willkürlich das Verhalten des Spektrometers
- Das Statusfenster `Spektrometer kalibrieren` zeigt bei `Integrationszeit der Abtastung` einen Wert von 522 ms an
- Unter `Spektrometer konfigurieren` wird eine `Erfassungszeit` von 122 ms angezeigt
- Beim Schliessen der App stellt die Weisslicht-LED nicht mehr ab
- Beim Wiederöffnen von LoggerPro wird das Gerät nicht mehr erkannt &rarr; das Spektrometer befindet sich offensichtlich in einem Fehlerzustand
- Das Betriebssystem muss neu gestartet werden
- Der Fehler lässt sich nicht reproduzieren