# Modus und Parameter der Datenerfassung

## change_acquisition
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Die Betriebsart des Spektrometers ist nach der Initialisierung default auf `Absorbanz`
- Die Default-Einstellungen für die Betriebsart sind gesetzt und werden nicht verändert
- Der Datenerfassungsmodus ist default auf `Vollspektrum` gesetzt
- Betriebsmodus `Absorbanz`:
  - Der Modus der Datenerfassung wird ausgehend vom Zustand nach der Initialisierung auf `Ereignisse mit Tastatureingabe` bzw. `Zeitgesteuert` geändert
  - Der Modus der Datenerfassung wird wieder zurück auf `Vollspektrum` geändert
  - Der Modus der Datenerfassung wird erneut auf `Ereignisse mit Tastatureingabe` bzw. `Zeitgesteuert` geändert
- Übrige Betriebsmodi:
  - Zunächst wird der Betriebsmodus geändert
  - Dann wird der Modus der Datenerfassung von `Vollspektrum` auf `Ereignisse mit Tastatureingabe` bzw. `Zeitgesteuert` geändert
  - Der Modus der Datenerfassung wird wieder zurück auf `Vollspektrum` geändert

## Allgemeine Beobachtungen
- Die Default-Wellenlänge nach dem Wechsel in die Betriebsmodi `Zeitgesteuert` bzw. `Ereignisse mit Tastatureingabe` ist immer eine andere (ca. 418 - 425 nm)
- Keine Pakete werden gesendet für:
  - Änderung der Dauer der Zeiterfassung
  - Umstellen auf `fortlaufende Datenerfassung`
  - Änderung der Abtastrate: die Abtastrate beträgt immer 0.8879 pt/s
  - Setzen der Option `10 nm Band` im zeit- oder ereignisgesteuertem Modus
- `Benachbarte Wellenlängen kombinieren` im zeit- oder ereignisgesteuertem Modus: Es werden zwar Pakete gesendet (gleiche Anzahl und Sequenz wie beim Setzen des Modus), die Option bleibt aber nicht gesetzt
- Beim Setzen der Datenerfassungsmodi `zeitgesteuert` und `ereignisgesteuert` setzt eine Art Ping-Modus ein &rarr; die App fordert periodisch Messdaten an, allerdings mit einer kleineren Frequenz als wenn eine Messung gestartet wird
- In der neuen App `Spectral Analysis` setzt die Anforderung von Messdaten unmittelbar nach der Initialisierung des Geräts und unabhängig vom gewählten Betriebs-/Datenerfassungsmodus ein; die `out`-Pakete werden mit der gleichen Frequenz wie für eine Messung geschickt