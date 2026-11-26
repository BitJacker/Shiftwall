# Copertura dei test

Misurata il 2026-11-26 sul commit finale di questo lavoro, con lo strumento Microsoft `dotnet-coverage` 18.12.0 usato solo
in locale (nessuna dipendenza aggiunta al progetto). I test sono stati eseguiti su Linux con .NET 10.0.404. Le righe sono
quelle del codice scritto a mano: il codice generato dal compilatore (`obj/`, regex generate) è escluso.

| Progetto | Righe coperte | Copertura righe | Copertura rami (cobertura, codice generato incluso) |
|---|---|---|---|
| Shiftwall.Core (parser, inventario) | 840 / 889 | 94,5% | 75,4% |
| Shiftwall.Features (migrazione, ricostruzione, toolbox) | 5479 / 5928 | 92,4% | 81,8% |
| — di cui `Features/Rebuild` (motore di ricostruzione, switch, profili) | 3227 / 3473 | 92,9% | — |
| Shiftwall.Lan (unico modulo con rete) | 178 / 197 | 90,4% | 74,3% |
| Shiftwall.Presentation (view model del wizard e della toolbox) | 1839 / 2447 | 75,2% | 63,3% |
| tools/update-db (strumento per aggiornare il database, non incluso nell'exe) | 161 / 635 | 25,4% | 26,9% |
| Shiftwall.App (viste WPF) | non misurato | — | — |

Shiftwall.App è WPF: si compila e si avvia solo su Windows. È verificato dalla CI su Windows (avvio, una schermata per
pagina, tutti i passi del wizard di ricostruzione nelle tre varianti: vedi `screenshots/`), non da test di copertura.

## Numero di test

| Progetto di test | Test |
|---|---|
| Shiftwall.Architecture.Tests (nessuna rete fuori da Lan, dipendenze tra progetti) | 10 |
| Shiftwall.Core.Tests | 137 |
| Shiftwall.Features.Tests | 203 |
| Shiftwall.Presentation.Tests | 526 |
| Shiftwall.Lan.Tests | 27 |
| Shiftwall.UpdateDb.Tests | 25 |
| **Totale** | **928** |

Tutti verdi. La CI esegue gli stessi test su Linux e su Windows a ogni push.
