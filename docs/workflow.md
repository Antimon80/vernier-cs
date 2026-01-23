# Tracking mehrerer Zustandswechsel während einer Session

Um zu Reksontruieren, welche Bytes im HID-Payload spezifisch für eine Session sind, wurde der folgende Workflow mit Hilfe von Wireshark getrackt:

1. Das Spektrometer ist angschlossen, LoggerPro wird gestartet &rarr; `Initialisierung` &rarr; `init\out_1`
2. Im default gesetzten Betriebsmodus `Absorbanz` wird das Gerät kalibriert &rarr; `calibration\absorbance\out_1`
3. Im default gesetzten Datenerfassungsmodus `Vollspektrum` wird eine Messung gestartet &rarr; `measurement\absorbance\full_spectrum\out_1`
4. Der Datenerfassungsmodus wird auf `zeitgesteuert` gewechselt; dabei wird default eine Wellenlänge gesetzt, bei der die Daten erfasst werden (wurde nicht notiert, wahrscheinlich 413.9 nm) &rarr; `change_acquisition\absorbance\time_resolved\full_spectrum\out_1` (die letzten 5 Pakete sind Pings)
5. Es wird eine Messung gestartet &rarr; `measurement\absorbance\time_resolved\out_1`(die ersten 5 Pakete sind Pings)
6. Der Datenerfassungsmodus wird auf `Vollspektrum` geändert &rarr; `change_acquisition\absorbance\full_spectrum\time_resolved\out_1` (die ersten 5 Pakete sind Pings)
7. Es wird eine Messung gestartet &rarr; `measurement\absorbance\full_spectrum\out_2`
8. Der Datenerfassungsmodus wird auf `ereignisgsteuert` geändert, der Betriebsmodus ist immer noch `Absorbanz`; default wird dabei eine Wellenlänge von 413.9 nm zur Datenerfassung gesetzt &rarr; `change_acquisition\absorbance\event_triggered\full_spectrum\out_1` (die letzten 5 Pakete sind Pings)
9. Eine Messung wird gestartet &rarr; `measurement\absorbance\event_triggered\out_1` (die erten 5 Pakte sind Pings)
10. Die Wellenlänge zur Datenerfassung wird auf 520.3 nm geändert &rarr; `acquisition_param\absorbance\event_triggered\out_1` (die letzten 5 Pakete sind Pings)
11. Eine Messung wird gestartet &rarr; `measurement\absorbance\event_triggered\out_2` (die erten 5 Pakte sind Pings)
12. Der Datenerfassungmodus wird auf `Vollspektrum` geändert, Betriebsmodus ist immer noch `Absorbanz` &rarr; `change_acquisition\absorbance\full_spectrum\event_triggered\out_1`
13. Eine Messung wird gestartet &rarr; `measurement\absorbance\full_spectrum\out_3`
14. Der Betriebsmodus wird auf `Fluoreszenz 405 nm` geändert &rarr; `change_mode\fluorescence_405nm\absorbance\out_1`
15. Im Datenerfassungsmodus `Vollspektrum` wird eine Messung gestartet &rarr; `measurement\fluorescence_405nm\full_spectrum\out_1`
16. Der Datenerfassungsmodus wird zu `zeitgesteuert` geändert &rarr; die Wellenlänge zur Datenerfassung wird default auf 394.1 nm gesetzt &rarr; `change_acquisition\fluorescence_405nm\time_resolved\full_spectrum\out_1` (die letzten 5 Pakete sind Pings)
17. Es wird eine Messung gestartet &rarr; `measurement\fluorescence_405nm\time_resolved\out_1`
18. Der Datenerfassungsmodus wird auf `Vollspektrum` geändert &rarr; `change_acquisition\fluorescence_405nm\full_spectrum\time_resolved\out_1`
19. Es wird eine Messung gestartet &rarr; `measurement\fluorescence_405nm\full_spectrum\out_2`
20. Der Betriebsmodus wird auf `Absorbanz` geändert &rarr; `change_mode\absorbance\fluorescence_405nm\out_1`
21. Es wird eine Messung gestartet &rarr; `measurement\absorbance\full_spectrum\out_4`
22. Das Gerät wird rekalibriert &rarr; Erfassungszeit ist 122 ms &rarr; `recalibration\absorbance\out_1`
23. Im Betriebsmodus `Absorbanz` und Datenerfassungsmodus `Vollspektrum` wird eine Messung gestartet &rarr; `measurement\absorbance\full_spectrum\out_5`
24. Der Datenerfassungsmodus wird zu `zeitgesteuert` geändert &rarr; 413.9 nm Wellenlänge &rarr; `change_acquisition\absorbance\time_resolved_full_spectrum\out_2` (die letzten 4 Pakete sind Pings)
25. Es wird eine Messung gestartet &rarr; `measurement\absorbance\time_resolved\out_2`
26. Die Wellenlänge zur Datenerfassung wird auf 520.3 nm geändert &rarr; `acquisition_param\absorbance\time_resolved\out_1`
27. Es wird eine Messung gestartet &rarr; `measurement\absorbance\time_resoveld\out_3` (die ersten 5 Pakete sind Pings)
28. Der Betriebsmodus wird zu `Fluoreszenz 405 nm` geändert, der Datenerfassungsmodus ist immer noch `zeitgesteuert` &rarr; `change_mode\fluorescence_405nm\absorbance\out_2` (die ersten und die letzten 5 Pakete sind Pings)
29. Es wird eine Messung gestartet; dabei hat sich die zuvor eingestellt Erfassungszeit von 10 s auf 200 s geändert &rarr; `measurement\fluorescence_405nm\time_resolved\out_2` (die ersten 5 Pakete sind Pings)
30. Der Betriebsmodus wird zu `Absorbanz` geändert &rarr; `change_mode\absorbance\fluorescence_405nm\out_2`
31. Es wird eine Messung gestartet &rarr; `measurement\absorbance\time_resolved\out_3` (die ersten 5 Pakete sind Pings)
32. Der Datenerfassungsmodus wird zu `Vollspektrum` geändert &rarr; `change_acquisition\absorbance\full_spectrum\time_resolved\out_2`
33. Es wird eine Messung gestartet &rarr; `measurement\absorbance\full_spectrum\out_6`
34. Das Gerät wird rekalibriert &rarr; `recalibration\absorbance\out_2`
35. Im Betriebsmodus `Absorbanz` und Datenerfassungsmodus `Vollspektrum` wird eine Messung gestartet &rarr; `measurement\absorbance\full_spectrum\out_7`
36. Der Datenerfassungsmodus wird zu `ereignisgesteuert` geändert; default wird zur Datenerfassung jetzt eine Wellenlänge von 887.4 nm gesetzt &rarr; `change_acquisition\absorbance\event_triggered\full_spectrum\out_2` (die letzten 5 Pakete sind Pings)
37. Es wird eine Messung gestartet &rarr; `measurement\absorbance\event_triggered\out_3`
38. Die Wellenlänge zur Datenerfassung wird auf 520.3 nm geändert &rarr; `acquisition_param\absorbance\event_triggered\out_2`
39. Es wird eine Messung gestartet &rarr; `measurement\absorbance\event_triggered\out_4`
40. LoggerPro wird geschlossen &rarr; `close\out_1`

## Allgemeine Beobachtungen
- Sobald das Gerät einmal kalibriert ist, setzt im zeit- und ereignisgesteuerten Datenerfassungsmodus eine Art Ping-Pong-Mechanismus ein, d. h. die App sendet kontinuierlich Pakete mit dem Opcode `40`
- Die Frequenz des Ping-Pong-Mechanismus ist konstant aber deutlich kleiner als während der eigentlichen Datenerfassung
- Die Frequenz der Datenerfassung ist im Betriebsmodus `Fluoreszenz 405 nm` deutlich höher als im Betriebsmodus `Absorbanz` und es werden mehr Antworten vom Spektrophotometer gesammelt bis die App ein neues `out`-Paket sendet