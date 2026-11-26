# Esito delle verifiche indipendenti

Tre controlli sugli script delle tre varianti (110C 5.2 → 40F 8.0, fixture sintetiche). Il revisore indipendente è un
agente nuovo che **non ha scritto il codice e non lo ha letto**: ha ricevuto solo l'ingresso (backup sintetico del 110C
e configurazione di fabbrica sintetica del 40F) e l'uscita (script GUI e terminale), e ha controllato ogni riga sulla
CLI Reference 8.0 e sull'Administration Guide 8.0.

| Verifica | Trovati | Corretti | Rimasti |
|---|---|---|---|
| Revisore indipendente (riga per riga sulla CLI Reference 8.0) | 13 | 7 | 6 |
| migration-verifier (riferimenti, conteggi, ordine, password, casi d'errore) | 18 | 16 | 2 |
| cli-file-linter (formato, codifica, schema 8.0; 109 file, 2254 righe) | 0 errori, 12 avvisi | 6 | 6 |

## Revisore indipendente

Esito generale: **nessuna riga verrebbe rifiutata dalla FortiOS 8.0**. Ordine corretto (ogni riferimento definito prima
dell'uso, porte tolte dallo switch prima di essere riusate), default 5.2 → 8.0 scritti correttamente, dial-up quasi
identico all'esempio ufficiale "FortiClient as dialup client".

| # | Problema | Gravità | Stato |
|---|---|---|---|
| 1 | La parte 1 toglie lan1/lan2 dallo switch `lan`: se il PC è su quelle porte, la connessione cade subito | alta | **Corretto**: guida `load-scripts.md` (dove cade la connessione) e avviso RB-014/RB-068 |
| 2 | Stesso rischio nella variante `lan` se il PC è su lan3 | media | **Corretto** (come #1) |
| 3 | Dopo il cambio di subnet serve un IP statico sul PC per l'ultima parte | media | **Corretto**: istruzione esplicita in `load-scripts.md` e negli avvisi RB-021/RB-068 |
| 4 | La guida diceva che la connessione cade alla parte `management`, nelle varianti cade a `factory-management` | bassa | **Corretto** |
| 5 | `user ldap` senza `set password` | media | **Voluto**: la password LDAP deve essere quella vera del server AD, Shiftwall non la inventa. Si scrive nel passaggio delle password (categoria LDAP/RADIUS); senza, l'oggetto è nel lavoro manuale (RB-011) |
| 6 | Utenti LDAP sul dial-up IKEv2: servono EAP-TTLS, server LDAPS, FortiClient 7.4.3+ ed EMS | alta | **Corretto**: nuovo avviso RB-076 con la fonte (Administration Guide 8.0.2, Add LDAP user authentication) |
| 7 | Web mode del portale perso senza avviso | bassa | **Corretto**: nuovo avviso RB-077 (Release Notes 8.0.2) |
| 8 | `Ufficio-Web` sostituito da `webfilter-profile "default"`: comportamento diverso | media | **Rimasto, per scelta**: è l'opzione (a) richiesta; RB-069 lo segnala; con l'opzione (b) la policy esce disattivata |
| 9 | Policy con profili senza `ssl-ssh-profile` (8.0: `no-inspection`) | media | **Rimasto**: la 5.2 non ha un default (p. 99), nessuna fonte su cosa facesse davvero; in `not-verified.md` |
| 10 | DPD del tunnel sito-sito: intervallo 8.0 non specificato | bassa | **Rimasto**: nessuna fonte; in `not-verified.md` |
| 11 | wan2 → DHCP con `defaultgw disable`: la policy ospiti può non uscire | bassa | **Rimasto, fedele alla sorgente** (5.2 p. 552); avviso RB-052 già presente |
| 12 | `lan` usata come uplink conserva `role lan` | bassa (estetico) | Rimasto |
| 13 | `timezone` non migrato | bassa | Rimasto: nessuna tabella ufficiale, lavoro manuale (M002) |

Dubbi del revisore senza fonte ufficiale (tutti in `not-verified.md`): id dei server DHCP di fabbrica del 40F reale,
comportamento SSL della 5.2, contenuto del web filter `default` 8.0, completamento di Run Script dopo la caduta della
sessione, intervallo DPD 8.0.

## migration-verifier

| # | Problema | Gravità | Stato |
|---|---|---|---|
| 1 | Password generata una per categoria: la PSK del dial-up (data agli utenti) era uguale a quella del tunnel sito-sito | alta | **Corretto**: una password nuova per ogni oggetto; avviso RB-073 se una password scritta a mano è condivisa |
| 2 | Uno switch esistente ricostruito senza le porte originali veniva svuotato prima delle aggiunte | alta | **Corretto**: deve restare almeno una porta originale (RB-013) |
| 3 | Profili rimossi da una regola (`dlp-sensor`, `spamfilter-profile`) persi in silenzio, policy attiva | alta | **Corretto**: contati come persi → policy disattivata (RB-070) |
| 4 | Porte aggiunte a uno switch hardware esistente senza controllo del chip | media | **Corretto** |
| 5 | VLAN nuova su una porta membro di uno switch o con ID già usato | media | **Corretto** (RB-064, RB-074) |
| 6 | Switch nuovo con il nome di una zona/VLAN/tunnel della sorgente | media | **Corretto** (RB-012) |
| 7 | Policy 5.2 con profili ma `utm-status` non attivo usciva con i profili attivi | media | **Corretto**: default 5.2 `disable` (p. 100), profili non scritti (RB-075) |
| 8 | Ispezione SSL implicita | media | **Rimasto** (come #9 del revisore) |
| 9 | Fixture con soli profili `default`: `deep-inspection` diventava `certificate-inspection` | media | **Corretto**: la fixture contiene tutti i profili precaricati documentati |
| 10 | DHCP ricostruito riusava l'id appena cancellato; commento del packager impreciso | media | **Corretto** |
| 11 | D010 segnata "da confermare" | media | **Corretto**: verificato sul PDF 5.2, p. 552 (default `disable`) |
| 12 | Test dei chip con un target incoerente | bassa | **Corretto** |
| 13 | Porta contesa elencata comunque nel secondo switch | bassa | **Corretto** |
| 14 | Porta di split uguale alla porta principale | bassa | **Corretto** |
| 15 | RB-021 incoerente quando l'interfaccia di gestione è nuova | bassa | **Corretto** |
| 16 | RB-025 contraddiceva RB-069 | bassa | **Corretto** |
| 17 | Split: VLAN figlie solo sulla porta principale, non detto | bassa | **Corretto** (RB-056) |
| 18 | DH 14 indicato come debole | bassa | **Rimasto, verificato**: l'Administration Guide 8.0 (Phase 1 configuration) elenca il gruppo 14 tra quelli insicuri per la Security Rating Insights |

## cli-file-linter

0 errori. Avvisi corretti: punto di caduta della connessione (6 avvisi, guida aggiornata). Avvisi rimasti: carattere
`à` nel commento di un indirizzo della fixture (3+3; UTF-8 ammesso da "CLI basics"), nome `sw0` dello switch fisico
(dalla fixture sintetica: con il backup vero Shiftwall legge quello reale).

Tutti i problemi corretti hanno un test di regressione (`VerifierFindingsTests`, `ReviewFindingsTests`, `ProfileTests`,
`SwitchPlanTests`).
