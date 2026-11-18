# Migration guide

*Italiano più sotto.*

This guide moves the configuration of one FortiGate to a different model with Shiftwall. It follows the manual
procedure in the [FortiOS 8.0.0 Best Practices][bp] (*Migrating a configuration manually*): same firmware on both units,
rename the interfaces, replace the `config-version` value of the header, restore on the new unit and check.

> Shiftwall is not affiliated with Fortinet. Modified configurations are not supported by Fortinet TAC: always test in a
> lab before production.

## Before you start

1. **Same FortiOS version on both units.** Shiftwall renames interfaces and adapts the header; it does not translate
   configuration syntax between versions. Upgrade the old or the new unit first (Fortinet's
   [upgrade tool](https://docs.fortinet.com/upgrade-tool/fortigate) gives the path). If the versions differ, the
   pre-checks stop you (MIG-001).
2. **Full backup of the old unit**, in plain text: an encrypted backup cannot be read, and a backup with masked
   passwords would not work on the new unit (MIG-013). `ENC` values in a normal backup are fine: Shiftwall copies them
   unchanged.
3. **Factory-default backup of the new unit** (recommended): back it up before changing anything. Its interface names
   and header are the most reliable target. Without it you can pick the model from the database, but you will have to
   fix the header by hand and some data may be marked *unverified*.
4. If `private-data-encryption` is enabled on the old unit, set the same key on the new one before restoring, or plan
   to re-enter the secrets (MIG-008).

## The five steps

Everything is a simulation until you press **Save configuration** in the last step.

| Step | What you do | What Shiftwall checks |
|---|---|---|
| 1 Load | Drop or browse the source backup and the factory-default backup of the target (or choose *Model from the database* and the FortiOS version). | File type, FortiOS version and build, number of lines and ports. |
| 2 Pre-checks | Read the list. Errors block the next step; warnings go into the report and the checklist. | Firmware, disk, VDOMs, switches, masked passwords, partial backups, unverified data. |
| 3 Mapping | Press **Map by name**, then pick or drag a target port for every source port in use. Save the mapping as a template to reuse it. | Used ports that are not mapped, ports mapped twice, ports missing on the target. |
| 4 Transform | Review the changes and the side-by-side differences. | Balanced blocks, references to interfaces missing on the target, `ENC` values unchanged. |
| 5 Report and save | Save the new configuration to a new file, export the HTML report or print it, and go through the checklist. | The original file is never overwritten. |

For several sites with the same models and mapping, use **Batch conversion** with a saved template (try it in
simulation first).

## After saving

1. Open the checklist in the report and work through every item (switches, disk, licences and FortiGuard, secrets).
2. Restore the new file on the new unit **in a lab**, following Fortinet's procedure in the [Best Practices][bp].
3. Check interfaces, routing, policies, VPNs and administrator access before production.

---

# Guida alla migrazione

Questa guida sposta la configurazione di un FortiGate su un modello diverso con Shiftwall. Segue la procedura manuale
delle [FortiOS 8.0.0 Best Practices][bp] (*Migrating a configuration manually*): stesso firmware sui due apparati,
rinomina delle interfacce, sostituzione del valore `config-version` nell'header, ripristino sul nuovo apparato e
verifica.

> Shiftwall non è affiliato a Fortinet. Le config modificate non sono supportate dal TAC Fortinet: testare sempre in
> laboratorio prima della produzione.

## Prima di iniziare

1. **Stessa versione di FortiOS sui due apparati.** Shiftwall rinomina le interfacce e adatta l'header; non traduce la
   sintassi tra versioni diverse. Aggiorna prima il vecchio o il nuovo apparato (il percorso lo indica
   l'[upgrade tool](https://docs.fortinet.com/upgrade-tool/fortigate) di Fortinet). Se le versioni differiscono, i
   controlli ti fermano (MIG-001).
2. **Backup completo del vecchio apparato**, in chiaro: un backup cifrato non è leggibile e un backup con password
   mascherate non funzionerebbe sul nuovo (MIG-013). I valori `ENC` di un backup normale vanno bene: Shiftwall li copia
   identici.
3. **Backup di default del nuovo apparato** (consigliato): fallo prima di modificare qualsiasi cosa. I suoi nomi di
   interfaccia e il suo header sono la destinazione più affidabile. Senza, puoi scegliere il modello dal database, ma
   dovrai correggere l'header a mano e alcuni dati potrebbero risultare *non verificati*.
4. Se sul vecchio apparato è attivo `private-data-encryption`, imposta la stessa chiave sul nuovo prima del ripristino,
   oppure prevedi di reinserire i segreti (MIG-008).

## I cinque passi

È tutto una simulazione finché non premi **Salva configurazione** nell'ultimo passo.

| Passo | Cosa fai | Cosa controlla Shiftwall |
|---|---|---|
| 1 Carica | Trascina o sfoglia il backup sorgente e il backup di default della destinazione (oppure scegli *Modello dal database* e la versione FortiOS). | Tipo di file, versione e build FortiOS, numero di righe e porte. |
| 2 Controlli | Leggi l'elenco. Gli errori bloccano il passo successivo; gli avvisi finiscono nel report e nella checklist. | Firmware, disco, VDOM, switch, password mascherate, backup parziali, dati non verificati. |
| 3 Mappatura | Premi **Mappa per nome**, poi scegli o trascina una porta di destinazione per ogni porta sorgente usata. Salva la mappatura come template per riusarla. | Porte usate non mappate, porte mappate due volte, porte assenti sulla destinazione. |
| 4 Trasformazione | Controlla le modifiche e le differenze affiancate. | Blocchi bilanciati, riferimenti a interfacce assenti sulla destinazione, valori `ENC` invariati. |
| 5 Report e salvataggio | Salva la nuova configurazione in un nuovo file, esporta il report HTML o stampalo e segui la checklist. | Il file originale non viene mai sovrascritto. |

Per più sedi con gli stessi modelli e la stessa mappatura usa **Conversione in blocco** con un template salvato
(provala prima in simulazione).

## Dopo il salvataggio

1. Apri la checklist nel report e verifica ogni voce (switch, disco, licenze e FortiGuard, segreti).
2. Ripristina il nuovo file sul nuovo apparato **in laboratorio**, seguendo la procedura Fortinet nelle [Best Practices][bp].
3. Verifica interfacce, routing, policy, VPN e accesso amministrativo prima della produzione.

[bp]: https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/32eccc66-2e05-11f1-b31d-02356ffb40d9/FortiOS-8.0.0-Best_Practices.pdf
