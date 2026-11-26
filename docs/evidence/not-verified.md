# Cosa NON è verificato (da provare in laboratorio o sul 40F vero)

Tutto ciò che segue è stato controllato solo sulla documentazione o non è stato controllato affatto. Va provato sul
FortiGate 40F vero (o in parte su una FortiGate-VM, vedi `docs/LAB-TEST.md`) prima di usare gli script in produzione.

## Dipende dal 40F reale o dal suo backup di fabbrica

| Cosa | Perché non è verificato | Come verificarlo |
|---|---|---|
| Configurazione di fabbrica del 40F (switch `lan` con lan1–lan3, server DHCP, oggetto indirizzo `lan`, policy di fabbrica, nome dello switch fisico `sw0`) | La fixture del 40F è **sintetica**: scritta a mano, non un backup reale | Caricare il backup di fabbrica vero come destinazione: Shiftwall legge interfacce, switch, `physical-switch`, profili e riferimenti da lì |
| Profili predefiniti sul 40F (`default` per antivirus, web filter, application control, IPS; `certificate-inspection`) | I nomi vengono dall'Administration Guide 8.0, la fixture li contiene solo come nomi | Con il backup di fabbrica Shiftwall controlla che esistano; senza, il report lo segnala (RB-071) |
| Creazione di uno switch hardware nuovo con `config system virtual-switch` / `set physical-switch` | Documentato per il 40F (SOC4), ma l'esempio della guida modifica uno switch esistente; la creazione di uno nuovo non è mai stata eseguita | Variante `recreated` sul 40F vero: controllare con `show system virtual-switch` e `show system interface internal` |
| Porte con `set mode static` e `set ip 0.0.0.0 0.0.0.0` prima di entrare in uno switch | Requisito documentato (Hardware switch, 40F fast path); comando mai eseguito | Return code 0 della parte `switch` |
| Interfaccia `internal` creata automaticamente dallo switch hardware nuovo, prima di `config system interface` / `edit "internal"` | Non descritto esplicitamente nella documentazione | Se la parte `management` dà errore su `edit "internal"`, segnalarlo |
| Ordine delle parti con la connessione di gestione (`management`, `factory-management`) | Logica verificata dai test, comportamento reale della sessione GUI durante lo script non verificato | Eseguire con il PC su una porta che resta in `lan` o dalla console |
| Script GUI: dimensione massima di un file e comportamento in caso di errore a metà | Non documentato nelle fonti trovate; Shiftwall tiene le parti piccole (max 800 righe) | Annotare i Return code di ogni parte |
| Fine riga e codifica accettate da "Run Script" | Shiftwall scrive LF e UTF-8 senza BOM; non trovata una fonte che lo fissi | Esecuzione delle parti sul 40F |
| `execute batch` | Esiste ma il comportamento non è documentato: Shiftwall non lo usa | — |

## Traduzioni con limiti noti

| Cosa | Stato |
|---|---|
| Fuso orario (`timezone` numerico 5.2 → nome 8.0) | Nessuna tabella ufficiale: da impostare a mano (M002) |
| Profili di sicurezza personalizzati (es. `Ufficio-Web`) | Sostituiti dal predefinito dello stesso tipo o policy disattivata: le impostazioni personalizzate vanno ricreate a mano |
| SSL VPN → IPsec dial-up | Segue la guida ufficiale, ma è "da rivedere": provare con FortiClient (IKEv2, EAP; LDAP richiede EAP-TTLS e FortiClient 7.4.3+) |
| Tunnel sito-sito con IKEv1/DH 5/3DES | Mantenuto identico per compatibilità con l'altra sede (RB-072): da aggiornare insieme all'altra sede |
| Dead peer detection, keep-alive e auto-negotiate del dial-up | Non aggiunti: nessuna fonte per valori diversi dai default 8.0 |
| `system accprofile` e profili personalizzati | Non ricostruiti: elencati come lavoro manuale |
| Password | Mai copiate (ENC): nuove password, da comunicare e cambiare al primo accesso; la password dell'admin con cui si esegue lo script non viene cambiata |

## Copertura delle prove

| Cosa | Stato |
|---|---|
| Fixture | Solo la fixture sintetica 110C 5.2 → 40F 8.0 (tre varianti di switch + variante "policy disattivata"). Le sei fixture aziendali su più versioni e la catena di versioni 5.2→…→8.0 non sono ancora fatte |
| Backup reale del 110C | Non ancora ricevuto: andrà in `tests/private/` solo anonimizzato, mai nel repository |
| Schema 5.2 | Estratto dal PDF della CLI Reference 5.2: 11 difetti noti dello schema (senza effetto sull'uscita), vedi `tools/cli-schema/README.md` |
| Interfaccia grafica | Verificata con test del view model e con gli screenshot della CI (Windows); nessun test automatico dei controlli WPF |
