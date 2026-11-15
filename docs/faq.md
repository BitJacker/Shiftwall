# FAQ

*Italiano più sotto.*

**Does Shiftwall send my configurations anywhere?**
No. It works offline and processes files in memory. The only network feature is the optional *Backup from LAN* page,
off by default, which connects only to the private IP address you enter. See [SECURITY.md](../SECURITY.md#privacy-policy).

**Does it decrypt passwords or pre-shared keys?**
No. `ENC` values are copied byte for byte and checked after the transform: if one changed, the result is marked
unusable (MIG-043).

**Can it migrate between different FortiOS versions?**
No. Upgrade one of the units first so that both run the same version; Shiftwall stops you otherwise (MIG-001).

**Why should I load the factory-default backup of the new unit?**
Its interface names and header are exact for that unit. Database data comes from official documents, but some fields
(for example default switch membership) are not documented publicly and are marked *unverified*.

**My model is not in the database.**
Load the factory-default backup of the target: the database is not needed then. To add the model, open a
["New FortiGate model" issue](https://github.com/BitJacker/shiftwall/issues/new/choose) with official sources.

**Does it compute upgrade paths?**
No. Fortinet publishes them through its [upgrade tool](https://docs.fortinet.com/upgrade-tool/fortigate); the
*Upgrade planning* page opens it instead of guessing.

**Is the result supported by Fortinet?**
No. Modified configurations are not supported by Fortinet TAC: always test in a lab before production.

**Windows says "Windows protected your PC".**
The exe is not code-signed. Check the SHA256 hash first, then choose *More info* → *Run anyway*. See
[download-and-verify.md](download-and-verify.md).

**Which files does Shiftwall write?**
Settings, an error log without configuration content, the files you save, and the .NET runtime components in
`%TEMP%\.net`. Details in [download-and-verify.md](download-and-verify.md#3-what-shiftwall-writes-on-your-pc).

**Can I share an anonymized configuration?**
Yes: the *Anonymize config* page replaces public IP addresses (private ones too, if you choose), host names,
user names, e-mail addresses, comments and secrets (`ENC` values, passwords, PEM blocks) with consistent aliases. Keep the reversible map and its
password to yourself. Always check the result before sharing it.

---

# FAQ (italiano)

**Shiftwall invia le mie configurazioni da qualche parte?**
No. Funziona offline ed elabora i file in memoria. L'unica funzione di rete è la pagina facoltativa *Backup da LAN*,
disattivata di default, che si collega solo all'IP privato che inserisci. Vedi [SECURITY.md](../SECURITY.md#privacy-policy).

**Decifra password o chiavi pre-condivise?**
No. I valori `ENC` sono copiati byte per byte e verificati dopo la trasformazione: se uno cambiasse, il risultato viene
segnalato come inutilizzabile (MIG-043).

**Può migrare tra versioni diverse di FortiOS?**
No. Aggiorna prima uno dei due apparati in modo che abbiano la stessa versione; altrimenti Shiftwall ti ferma (MIG-001).

**Perché caricare il backup di default del nuovo apparato?**
I suoi nomi di interfaccia e il suo header sono esatti per quell'apparato. I dati del database vengono da documenti
ufficiali, ma alcuni campi (per esempio i membri degli switch di default) non sono documentati pubblicamente e sono
segnati come *non verificati*.

**Il mio modello non è nel database.**
Carica il backup di default della destinazione: in quel caso il database non serve. Per aggiungere il modello apri una
issue ["New FortiGate model"](https://github.com/BitJacker/shiftwall/issues/new/choose) con le fonti ufficiali.

**Calcola i percorsi di upgrade?**
No. Fortinet li pubblica con il suo [upgrade tool](https://docs.fortinet.com/upgrade-tool/fortigate); la pagina
*Pianifica upgrade* lo apre invece di tirare a indovinare.

**Il risultato è supportato da Fortinet?**
No. Le config modificate non sono supportate dal TAC Fortinet: testare sempre in laboratorio prima della produzione.

**Windows mostra "Windows protected your PC".**
L'exe non è firmato. Verifica prima l'hash SHA256, poi scegli *More info* → *Run anyway* (in Windows in italiano le
voci compaiono tradotte). Vedi [download-and-verify.md](download-and-verify.md).

**Quali file scrive Shiftwall?**
Impostazioni, un log degli errori senza contenuti delle config, i file che salvi e i componenti del runtime .NET in
`%TEMP%\.net`. Dettagli in [download-and-verify.md](download-and-verify.md#3-cosa-scrive-shiftwall-sul-tuo-pc).

**Posso condividere una configurazione anonimizzata?**
Sì: la pagina *Anonimizza config* sostituisce IP pubblici (anche privati, se lo scegli), hostname, nomi utente,
indirizzi e-mail, commenti e segreti (valori `ENC`, password, blocchi PEM) con alias coerenti. Tieni per te la mappa reversibile
e la sua password. Controlla sempre il risultato prima di condividerlo.
