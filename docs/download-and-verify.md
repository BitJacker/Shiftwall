# Download and verify Shiftwall

*Italiano più sotto.*

Shiftwall is a single portable file, `Shiftwall.exe` (Windows 10/11, x64). It needs no installer and no
administrator rights.

## 1. Check the SHA256 hash

Every release publishes `SHA256SUMS.txt` next to the exe. The hash proves that the file you downloaded is exactly the
one built by the project's CI.

In PowerShell, in the download folder:

```powershell
Get-FileHash .\Shiftwall.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

The `Hash` value printed by `Get-FileHash` must be equal to the first value in `SHA256SUMS.txt` (PowerShell prints
it in upper case, the file in lower case: case does not matter). If they differ, do not run the file: download it again
from the official release page.

To compare automatically:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt).Split(' ')[0]
(Get-FileHash .\Shiftwall.exe -Algorithm SHA256).Hash -eq $expected.ToUpperInvariant()
```

`True` means the file is intact.

## 2. The SmartScreen warning

The exe is **not code-signed**. On first run Microsoft Defender SmartScreen shows **"Windows protected your PC"**,
because the file has no signature and no download reputation yet. After checking the hash, choose **More info** and
then **Run anyway**. Your organisation's policy may prevent this: in that case ask your IT administrator.

Microsoft explains this behaviour in
[SmartScreen reputation for Windows app developers](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)
(unsigned files show "Windows protected your PC" and the user must choose "Run anyway").

## 3. What Shiftwall writes on your PC

- Your settings (theme, language, LAN option, confirmed certificate fingerprints): `%APPDATA%\Shiftwall\settings.json`.
- An imported model database, only if you import one: `%APPDATA%\Shiftwall\database`.
- An error log with exception types and code locations only, never configuration content:
  `%LOCALAPPDATA%\Shiftwall\logs`.
- The files you save, where you choose.
- The .NET runtime of a single-file app unpacks its native components (no data of yours) into `%TEMP%\.net` on first
  run ([.NET single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview#native-libraries)).

Configurations are processed in memory. Shiftwall makes no network connections, except the optional LAN feature
(disabled by default) when you press *Download*.

---

# Scaricare e verificare Shiftwall

Shiftwall è un unico file portable, `Shiftwall.exe` (Windows 10/11, x64), senza installer e senza diritti di
amministratore.

## 1. Verifica l'hash SHA256

Ogni release pubblica `SHA256SUMS.txt` accanto all'exe. In PowerShell, nella cartella del download:

```powershell
Get-FileHash .\Shiftwall.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Il valore `Hash` deve coincidere con il primo valore di `SHA256SUMS.txt` (maiuscole/minuscole non contano). Se è
diverso, non avviare il file e scaricalo di nuovo dalla pagina ufficiale delle release. Confronto automatico:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt).Split(' ')[0]
(Get-FileHash .\Shiftwall.exe -Algorithm SHA256).Hash -eq $expected.ToUpperInvariant()
```

`True` significa che il file è integro.

## 2. L'avviso di SmartScreen

L'exe **non è firmato digitalmente**. Al primo avvio Microsoft Defender SmartScreen mostra l'avviso
**"Windows protected your PC"**: il file non ha firma né reputazione di download. Dopo aver verificato l'hash, scegli
**More info** e poi **Run anyway** (in Windows in italiano le voci compaiono tradotte). Se la policy aziendale lo impedisce, rivolgiti
all'amministratore IT. Fonte: [SmartScreen reputation for Windows app developers](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).

## 3. Cosa scrive Shiftwall sul tuo PC

- Impostazioni (tema, lingua, opzione LAN, impronte dei certificati confermati): `%APPDATA%\Shiftwall\settings.json`.
- Un database modelli importato, solo se lo importi: `%APPDATA%\Shiftwall\database`.
- Un log degli errori con solo tipo di eccezione e punti del codice, mai contenuti delle config: `%LOCALAPPDATA%\Shiftwall\logs`.
- I file che salvi, dove scegli tu.
- Il runtime .NET di un'app single-file estrae i suoi componenti nativi (nessun tuo dato) in `%TEMP%\.net` al primo avvio.

Le configurazioni sono elaborate in memoria. Shiftwall non apre connessioni di rete, tranne la funzione LAN
facoltativa (disattivata di default) quando premi *Scarica*.
