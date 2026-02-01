# Kalibration und Datenerfassung

## calibration
Kalibration:
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Betriebsart `Absorption` oder `Transmission` ist gewählt
- `Erfassungszeit` ist default auf 15 ms
- Im Hauptmenü `Kalibrieren` wählen
- Weisslicht-LED stellt ab und wieder an
- Vorwärmzeit kann übersprungen werden
- Kalibration abschliessen
- `Erfassungszeit` ändert sich auf 118 ms oder 122 ms
- LoggerPro schliessen

Rekalibration:
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Betriebsart `Absorption` oder `Transmission` ist gewählt
- Spektrometer ist bereits kalibriert
- Spektrometer erneut kalibrieren
- Rekalibration schliesst mit dem gleichen Zustand wie die initiale Kalibration ab

## measurement
- Spektrometer ist angeschlossen, LoggerPro ist gestartet
- Betriebsmodus ist gewählt
- Erfassungsmodus ist gewählt
- Im Betriebsmodus `Absorption` oder `Transmission` ist das Spektrometer kalibriert
- Eine Messung wird gestartet
- Die Messung wird gestoppt

## Allgemeine Beobachtungen
- Abhängig davon, auf welchen Wert bei der Kalibrierung des Geräts die `Erfassungszeit` gesetzt wird, werden unterschiedlich viele `out`-Pakete geschickt &rarr; 46 bei 118 ms, 42 bei 112 ms
- Die Anzahl der empfangenen `in`-Frames ist unabhängig vom gewählten Datenerfassungmodus immer gleich
- In der neuen App `Spectral Analysis` muss bereits beim Öffnen der App der Betriebsmodus und der Datenerfassungsmodus gewählt werden; ändert man dann in den Datenerfassungsmodi `zeitgesteuert` oder `ereignisgesteuert` die Wellenlänge, bei der die Datenpunkte aufgezeichnet werden sollen, werden keine `out`-Pakete geschickt
- Die Datenerfassungsmodi `zeitgesteuert` und `ereignisgesteuert` existieren wahrscheinlich gar nicht auf Seite der Hardware, das Gerät scheint bei Anforderung von Messdaten immer Vollspektren zu schicken
- In der neuen App `Spectral Analysis` befindet sich das Gerät nach der Initialisierung dauerhaft im Messmodus; Klicken des Buttons `Erfassen` auf dem UI startet lediglich eine Aufzeichnung der gesammelten Messdaten