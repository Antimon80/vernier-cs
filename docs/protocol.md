# Kommunikationsprotokoll für die GoDirect Spektrometer

## Initialisierung
Beim Start der App wird eine Sequenz von Commands an das Gerät geschickt, die mit Ausnahme des ersten Command vom Gerät mit einer fixen Anzahl an Paketen mit je 64 Bytes (bzw. 8 Bytes beim Modell SpectroVis) beantwortet wird. Die neue App `SpectralAnalysis` schickt gegenüber der alten App `LoggerPro` eine vereinfachte Sequenz, das grundsätzliche Muster bleibt aber gleich. Nachfolgend wird die Sequenz der neuen App ausgewertet und für die eigene Implementierung übernommen.

| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x00 00 00 | --- | --- | unklar |
| 0x01 00 00 | 1 | Die ersten beiden Bytes sind immer gleich und spezifisch für das jeweilige Gerätemodell. | Validierung des angeschlossenen Modells |
| 0x02 00 00 | 128 (SpectroVis) bzw. 56 (alle übrigen Modelle) | u16-decodiert streng monoton ansteigende Zahlensequenz im Big-Endian-Format | Überprüfung der Linearität des CCD-Sensors, Validierung der Endianess |
| 0x04 28 00 | 1 | Der Command `0x04` setzt grundsätzlich die Integrationszeit, die nachfolgenden beiden Bytes codieren im Little-Endian-Format den Betrag in [ms]; in der Antwort des Geräts wird in den ersten beiden Bytes der Betrag in [ms] kopiert, alle nachfolgenden Bytes sind identisch zum letzten Paket der Antwort auf den Command `0x02 00 00`. | Bestätigung des eingehenden Commands |
| 0x40 00 00 | 64 (SpectroVis) bzw. 56 (alle übrigen Modelle) | Der Command `0x40` fragt grundsätzlich Messdaten vom Gerät an; diese werden paketweise im Big-Endian-Format als ADC-Counts (`0x 00 00 00 00` bis `0x ff ff ff ff`) geschickt. | Überprüfung, ob vom Gerät "sinnvoller" Dark-Noise zurück kommt &rarr; falls alle Werte 0 oder max. Counts, ist der CCD defekt |
| 0x04 5a 00 | 1 | Die Integrationszeit wird verändert, als Antwort ist in den ersten beiden Bytes wieder das Echo in [ms] codiert | Bestätigung des eingehenden Commands. |
| 0x40 00 00 | 64 (SpectroVis) bzw. 56 (alle übrigen Modelle) | Messdaten als ADC-Counts | Überprüfung, ob vom Gerät "sinnvoller" Dark-Noise zurückkommt. |
| 0x04 1e 00 | 1 | Die Integrationszeit wird verändert, als Antwort ist in den ersten beiden Bytes wieder das Echo in [ms] codiert. | Die Integrationszweit wird auf den Standardwert 30 [ms] gesetzt. |
| 0x41 00 00 | 1 | Der Command `0x41` steuert grundsätzlich das Verhalten der Weisslichtquelle; das Gerät bestätigt mit den ersten beiden Bytes der Antwort `0x 00 00`, dass die Lichtquelle **aus** ist | Überprüfung der Funktionalität der Weisslichtquelle |
| 0x42 00 00 | 1 | Der Command `0x42` steuert grundsätzlich das Verhalten der 405-nm-LED (sofern vorhanden); das Gerät bestätigt mit den ersten beiden Bytes der Antwort `0x 00 00`, dass die Lichtquelle **aus** ist | Überprüfung der Funktionalität der 405-nm-LED |
| 0x43 00 00 | 1 | Der Command `0x43` steuert grundsätzlich das Verhalten der 500-nm-LED (sofern vorhanden); das Gerät bestätigt mit den ersten beiden Bytes der Antwort `0x 00 00`, dass die Lichtquelle **aus** ist | Überprüfung der Funktionalität der 500-nm-LED |
| 0x41 01 00 | 1 | Der Command `0x41` steuert grundsätzlich das Verhalten der Weisslichtquelle; das Gerät bestätigt mit den ersten beiden Bytes der Antwort `0x 01 00`, dass die Lichtquelle **an** ist | Überprüfung der Funktionalität der Weisslichtquelle; Setzen des Standard-Betriebsmodus |
| 0x40 00 00 | 64 (SpectroVis) bzw. 56 (alle übrigen Modell) | Messdaten in ADC-Counts | Das Gerät muss deutlich höhere Counts als im Dark-Mode messen &rarr; Überprüfung der Funktionalität der Weisslichtquelle |
| ----- | ----- | ----- | ----- |

#### Anmerkungen
- Die Commands `0x42 00 00` und `0x43 00 00` müssen nur für die Modelle `SpectroVisPlus` und `SpectroVisPlus (BLE)` geschickt werden, alle anderen Modelle besitzen keine LEDs zum Anregen von Fluoreszenz.
- Antworten auf den Request `0x02 00 00`:
  - 0x01 00 &rarr; SpectroVis
  - 0x02 00 &rarr; SpectroVisPlus
  - 0x16 00 &rarr; SpectroVisPlus (BLE)
  - 0x03 d5 &rarr; UV-Vis-Spectrometer
  - 0x04 00 &rarr; Emission-Spectrometer
- Auf dem UI der alten App `LoggerPro` wird nach der Initialisierung der Betriebsmodus `Absorbanz` als Standard für die Modelle `SpectroVis`, `SpectroVisPlus` und `UV-Vis-Spectrometer` angezeigt; tatsächlich ist das Gerät aber nicht kalibriert und kann nur rohe Messdaten schicken &rarr; muss auf dem neuen UI angezeigt werden.

## Wechsel des Betriebsmodus
Die Geräte können an sich nur rohe Messdaten bei einer bestimmten Integrationszeit schicken, Referenzdaten für die Betriebsmodi `Absorbanz` und `Transmission` werden softwareseitig gehalten. Nach der Initialisierung der Geräte ist standardmässig die Weisslichtquelle eingeschaltet (ausser beim Modell `Emission-Spectrometer`), ein Wechsel des Betriebsmodus ändert gerätigseitig also nur den Status der verfügbaren Lichtquellen.

### Absorbanz bzw. Transmission &rarr; Intensität

| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 64 (SpectroVis) bzw. 56 (alle übrigen Modelle) | Messdaten in ADC-Counts | Das Gerät muss deutlich **niedrigere** Counts als im Light-Mode messen |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms für die Betriebsmodi `Intensität` oder `unkalibrierte Rohdaten` gesetzt |

### Intensität &rarr; Absorbanz bzw. Transmission

| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x41 01 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 01 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 64 (SpectroVis) bzw. 56 (alle übrigen Modelle) | Messdaten in ADC-Counts | Das Gerät muss deutlich **höhere** Counts als im Dark-Mode messen |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Absorbanz bzw. Transmission &rarr; Fluoreszenz 405 nm
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 00 00 | 1 | Der Status der 405-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 01 00 | 1 | Der Status der 405-nm-LED wird mit `0x 01 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss deutlich **niedrigere** Counts als im Weisslicht-Modus messen |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Fluoreszenz 405 nm &rarr; Absorbanz bzw. Transmission
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 00 00 | 1 | Der Status der 405-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x41 01 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 01 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss deutlich **höhere** Counts als im Fluoreszenz-Modus messen |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Absorbanz bzw. Transmission &rarr; Fluoreszenz 500 nm
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 00 00 | 1 | Der Status der 500-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 01 00 | 1 | Der Status der 500-nm-LED wird mit `0x 01 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss deutlich **niedrigere** Counts als im Weisslicht-Modus messen |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Fluoreszenz 500 nm &rarr; Absorbanz bzw. Transmission
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 00 00 | 1 | Der Status der 500-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x41 01 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 01 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss deutlich **höhere** Counts als im Fluoreszenz-Modus messen |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Fluoreszenz 405 nm &rarr; Fluoreszenz 500 nm
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 00 00 | 1 | Der Status der 405-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 01 00 | 1 | Der Status der 500-nm-LED wird mit `0x 01 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss beim Wechsel zwischen den beiden Betriebsmodi etwa ähnlich hohe ADC-Counts zählen. |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Fluoreszenz 500 nm &rarr; Fluoreszenz 405 nm
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 00 00 | 1 | Der Status der 405-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 01 00 | 1 | Der Status der 500-nm-LED wird mit `0x 01 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss beim Wechsel zwischen den beiden Betriebsmodi etwa ähnlich hohe ADC-Counts zählen. |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Intensität &rarr; Fluoreszenz 405 nm
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 00 00 | 1 | Der Status der 405-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 01 00 | 1 | Der Status der 405-nm-LED wird mit `0x 00 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss beim Wechsel zwischen den beiden Betriebsmodi etwa ähnlich hohe ADC-Counts zählen. |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Fluoreszenz 405 nm &rarr; Intensität
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x42 00 00 | 1 | Der Status der 405-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss beim Wechsel zwischen den beiden Betriebsmodi etwa ähnlich hohe ADC-Counts zählen. |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Intensität &rarr; Fluoreszenz 500 nm
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 00 00 | 1 | Der Status der 500-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 01 00 | 1 | Der Status der 500-nm-LED wird mit `0x 00 00` als **an** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss beim Wechsel zwischen den beiden Betriebsmodi etwa ähnlich hohe ADC-Counts zählen. |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |

### Fluoreszenz 500 nm &rarr; Intensität
| Command | Anzahl Antwortpakete | Inhalt Antwortpakete | Interpretation |
| ----- | ----- | ----- | ----- |
| 0x41 00 00 | 1 | Der Status der Weisslichtquelle wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x43 00 00 | 1 | Der Status der 500-nm-LED wird mit `0x 00 00` als **aus** bestätigt. | Validierung des Status der Lichtquelle. |
| 0x40 00 00 | 56 | Messdaten in ADC-Counts | Das Gerät muss beim Wechsel zwischen den beiden Betriebsmodi etwa ähnlich hohe ADC-Counts zählen. |
| 0x04 1e 00 | 1 | Bestätigung der gesetzten Integrationszeit mit `1e 00`. | Die Integrationszeit wird auf den Standardwert von 30 ms gesetzt |



### Anmerkungen
- Für die Betriebsmodi `Absorbanz` und `Transmission` wird die passende Intgrationszeit dynamisch während der Kalibration des Geräts gesetzt &rarr; als Standard wird daher ein Wert von 30 ms angenommen. 
- Wird von den Betriebsmodi `Fluoreszenz` oder `Intensität` zu den Betriebsmodi `Absorbanz` oder `Transmission` gewechselt, muss auf jeden Fall eine Neukalibration des Geräts angefordert werden, da ansonsten die Erfassungszeit nicht mehr konsistent mit den Referenzdaten einer allfälligen früheren Kalibration ist.
- Das Modell `Emission-Spectrometer` kennt hardwareseitig nur einen einzigen Betriebsmodus; der Wechsel zwischen `Intensität` und `rohe Messdaten` erfolgt rein softwareseitig.
- Die Wechsel zu den bzw. von den Betriebsmodi `Fluoreszenz` sind nur für die Modelle `SpectroVisPlus` und `SpectroVisPlus (BLE)` relevant.


## Kalibration
Alle Modelle mit einer Weisslichtquelle müssen für die Betriebsmodi `Absorbanz` bzw. `Transmission` kalibriert werden.
