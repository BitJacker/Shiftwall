# Prove del lavoro (solo dati sintetici)

Tutto quello che c'è in questa cartella viene dalle fixture **sintetiche** `110c-5.2-office.fgtcfg` (FortiGate 110C,
FortiOS 5.2) e `40f-8.0-target-synthetic.fgtcfg` (FortiGate 40F, FortiOS 8.0), scritte a mano per i test. Nessun dato
reale. Le password negli script sono sostituite da `********`.

| # | Prova | File |
|---|---|---|
| 1 | Script finali per GUI e per terminale, nelle tre varianti dello switch `internal` (mappato su `lan`, ricreato con lan1+lan2, spezzato) | `scripts/lan/`, `scripts/recreated/`, `scripts/split/` (ognuna con `gui/` e `terminal/`) |
| 2 | Conteggi sorgente → destinazione per tipo di oggetto e per variante, con la differenza spiegata riga per riga | [`counts.md`](counts.md) |
| 3 | Validazione: comandi generati, validati sullo schema 8.0, riferimenti controllati, errori | [`validation.md`](validation.md) |
| 4 | Regole di traduzione usate, con fonte e test che le coprono | [`rules.md`](rules.md) |
| 5 | Esito della revisione indipendente (e del migration-verifier e del linter): trovati, corretti, rimasti | [`review.md`](review.md) |
| 6 | Copertura dei test per progetto e numero di test | [`coverage.md`](coverage.md) |
| 7 | Screenshot di tutti i passi del wizard, nelle tre varianti, presi dalla CI su Windows | `screenshots/` |
| 8 | Cosa NON è verificato e va provato in laboratorio | [`not-verified.md`](not-verified.md) |

I file 1–4 sono generati dal test `EvidenceTests`, che fallisce se non sono aggiornati (per rigenerarli:
`SHIFTWALL_WRITE_EVIDENCE=1 dotnet test tests/Shiftwall.Features.Tests`). Gli screenshot vengono dall'artifact
`smoke-test` della CI (finestra di circa 1030×750 pixel, cioè uno schermo piccolo).
