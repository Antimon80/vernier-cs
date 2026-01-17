# Modus und Parameter der Datenerfassung

## change_acquisition
### absorbance
#### event (Endzustand)
##### init (Anfangszustand)
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Die Betriebsart des Spektrometers ist nach der Initialisierung default auf `Absorbanz`
- Die Default-Einstellung für die Betriebsart sind gesetzt
- Der Modus der Datenerfassung wird ausgehend vom Zustand nach der Initialisierung auf `Ereignisse mit Tastatureingabe` geändert
- Die Default-Wellenlänge ereignisgesteuerten Modus ist immer eine andere (ca. 418 - 425 nm)
##### full
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Der Modus der Datenerfassung wird ausgehnd vom Zustand nach der Initialisierung im Betriebsmodus `Absorbanz` auf `Ereignisse mit Tastatureingabe` geändert
- Der Modus der Datenerfassung wird wieder auf `Vollspektrum` geändert
- Vom Modus `Vollspektrum` wird wieder zu `Ereignisse mit Tastatureingabe` gewechselt
##### time_resolved
- Es werden keine Pakete geschickt
- Hardwareseitig ist `zeitgesteuert` und `Ereignisse mit Tastatureingabe` der gleiche Datenerfassungsmodus

#### full (Endzustand)
##### time_resolved (Anfangszustand)
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Der Modus der Datenerfassung wird ausgehnd vom Zustand nach der Initialisierung im Betriebsmodus `Absorbanz` auf `zeitgesteuert` geändert
- Der Modus der Datenerfassung wird wieder auf `Vollspektrum` geändert
##### event
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Der Modus der Datenerfassung wird ausgehnd vom Zustand nach der Initialisierung im Betriebsmodus `Absorbanz` auf `Ereignisse mit Tastatureingabe` geändert
- Der Modus der Datenerfassung wird wieder auf `Vollspektrum` geändert

#### time_resolved (Endzustand)
##### init (Anfangszustand)
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Die Betriebsart des Spektrometers ist nach der Initialisierung default auf `Absorbanz`
- Die Default-Einstellung für die Betriebsart sind gesetzt
- Der Modus der Datenerfassung wird ausgehend vom Zustand nach der Initialisierung auf `zeitgesteuert` geändert
- Die Default-Wellenlänge im zeitgesteuerten Modus ist immer eine andere (ca. 418 - 425 nm)
##### full
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Der Modus der Datenerfassung wird ausgehnd vom Zustand nach der Initialisierung im Betriebsmodus `Absorbanz` auf `zeitgesteuert` geändert
- Der Modus der Datenerfassung wird wieder auf `Vollspektrum` geändert
- Vom Modus `Vollspektrum` wird wieder zu `zeitgesteuert` gewechselt
##### event
- Es werden keine Pakete geschickt
- Hardwareseitig ist `zeitgesteuert` und `Ereignisse mit Tastatureingabe` der gleiche Datenerfassungsmodus

### fluorescence_405nm
#### time_resolved (Endzustand)
##### full (Anfangszustand)
- Nach Wechsel in den zeitgesteuerten Modus werden kontinuierlich Pakete verschickt (Polling)

## acquisition_param
### absorbance
#### full
#### event
#### time_resolved

## Allgemeine Beobachtungen
- Keine Pakete werden gesendet für:
  - Änderung der Dauer der Zeiterfassung
  - Umstellen auf `fortlaufende Datenerfassung`
  - Änderung der Abtastrate: die Abtastrate beträgt immer 0.8879 pt/s
  - Setzen der Option `10 nm Band` im zeit- oder ereignisgesteuertem Modus
- `Benachbarte Wellenlängen kombinieren` im zeit- oder ereignisgesteuertem Modus: Es werden zwar Pakete gesendet (gleiche Anzahl und Sequenz wie beim Setzen des Modus), die Option bleibt aber nicht gesetzt