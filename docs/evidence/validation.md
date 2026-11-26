# Report di validazione

Ogni file generato (GUI e terminale) è riletto riga per riga: ogni `config` deve esistere nello schema CLI 8.0, ogni `set` deve avere un parametro dello schema e un valore valido per il suo tipo. I valori di tipo indirizzo, maschera o intervallo hanno solo il controllo di presenza: il loro formato lo verifica FortiOS (lo schema non lo descrive).

| Variante | File | Righe di comando | `config`/`edit`/`delete` validati | `set` validati sullo schema 8.0 | di cui con controllo del valore | Riferimenti controllati | Errori lint | Errori bloccanti |
|---|---|---|---|---|---|---|---|---|
| `lan` | GUI (16 file) | 349 | 84/84 | 183/183 (100%) | 141 | 64 | 0 | 0 |
| `lan` | terminale (19 file) | 351 | 85/85 | 183/183 (100%) | 141 | 64 | 0 | 0 |
| `recreated` | GUI (17 file) | 373 | 94/94 | 188/188 (100%) | 144 | 64 | 0 | 0 |
| `recreated` | terminale (20 file) | 375 | 95/95 | 188/188 (100%) | 144 | 64 | 0 | 0 |
| `split` | GUI (17 file) | 352 | 86/86 | 183/183 (100%) | 141 | 64 | 0 | 0 |
| `split` | terminale (20 file) | 354 | 87/87 | 183/183 (100%) | 141 | 64 | 0 | 0 |

I file sono in `docs/evidence/scripts/<variante>/gui` e `.../terminal`, con le password sostituite da `********`.
