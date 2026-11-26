# Conteggi sorgente → destinazione

Generato dai test (`EvidenceTests`) sulla fixture sintetica `110c-5.2-office.fgtcfg` (FortiGate 110C, FortiOS 5.2) verso `40f-8.0-target-synthetic.fgtcfg` (FortiGate 40F, FortiOS 8.0). Ogni riga: oggetti nella sorgente = ricostruiti + convertiti + da fare a mano; nessun oggetto sparisce senza motivo.

## Variante `lan`: internal → lan (switch esistente), wan2 → lan3

| Oggetto | Sorgente | Ricostruiti | Convertiti | A mano | Differenza spiegata |
|---|---|---|---|---|---|
| `system interface` | 6 | 5 | 1 | 0 | `ssl.root` convertita nel tunnel IPsec dial-up `dialup_vpn` |
| `system zone` | 1 | 1 | 0 | 0 | — |
| `system global` | 1 | 1 | 0 | 0 | `system global`: 'timezone' non esiste nella versione di destinazione e non ha un equivalente: 5.2 uses a numeric time zone index; 8.0 expects a time zone database name (e.g. Europe/Rome). No official table maps one to the other: set it by hand. |
| `system dns` | 1 | 1 | 0 | 0 | — |
| `firewall address` | 6 | 5 | 1 | 0 | indirizzo del pool SSL VPN convertito nell'intervallo del dial-up |
| `firewall addrgrp` | 1 | 1 | 0 | 0 | — |
| `firewall service category` | 2 | 2 | 0 | 0 | — |
| `firewall service custom` | 7 | 7 | 0 | 0 | `Gestionale-8443`: 'visibility' non esiste nella versione di destinazione e non ha un equivalente: Documented in the old CLI Reference, absent from the new one. |
| `firewall service group` | 1 | 1 | 0 | 0 | — |
| `firewall schedule recurring` | 1 | 1 | 0 | 0 | — |
| `firewall vip` | 1 | 1 | 0 | 0 | — |
| `firewall ippool` | 1 | 1 | 0 | 0 | — |
| `user ldap` | 1 | 1 | 0 | 0 | `AD-Ufficio`: 'password' richiede una password della categoria 'ldap-radius' (i valori ENC non si copiano mai). Sceglila nel passaggio delle password. |
| `user local` | 2 | 2 | 0 | 0 | — |
| `user group` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase1-interface` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase2-interface` | 1 | 1 | 0 | 0 | — |
| `router static` | 2 | 2 | 0 | 0 | — |
| `firewall policy` | 6 | 5 | 1 | 0 | policy SSL VPN convertita in policy del tunnel dial-up<br>`1`: Profili di sicurezza non migrati, da ricreare e assegnare: webfilter-profile Ufficio-Web |
| `system admin` | 2 | 1 | 0 | 1 | `admin`: 'admin' è l'amministratore con cui esegui lo script: lo script non lo modifica. Per cambiargli la password la CLI chiede quella attuale (Administration Guide, Default administrator password): fallo a mano alla fine. |
| `system dhcp server` | 2 | 2 | 0 | 0 | — |
| `system accprofile` | 1 | 0 | 0 | 1 | `system accprofile`: La sezione 'system accprofile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |
| `vpn ssl settings` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `vpn ssl web portal` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `webfilter profile` | 1 | 0 | 0 | 1 | `webfilter profile`: La sezione 'webfilter profile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |

## Variante `recreated`: internal ricreato con lan1 + lan2 (switch hardware), wan2 → lan (resta con lan3)

| Oggetto | Sorgente | Ricostruiti | Convertiti | A mano | Differenza spiegata |
|---|---|---|---|---|---|
| `system interface` | 6 | 5 | 1 | 0 | `ssl.root` convertita nel tunnel IPsec dial-up `dialup_vpn` |
| `system zone` | 1 | 1 | 0 | 0 | — |
| `system global` | 1 | 1 | 0 | 0 | `system global`: 'timezone' non esiste nella versione di destinazione e non ha un equivalente: 5.2 uses a numeric time zone index; 8.0 expects a time zone database name (e.g. Europe/Rome). No official table maps one to the other: set it by hand. |
| `system dns` | 1 | 1 | 0 | 0 | — |
| `firewall address` | 6 | 5 | 1 | 0 | indirizzo del pool SSL VPN convertito nell'intervallo del dial-up |
| `firewall addrgrp` | 1 | 1 | 0 | 0 | — |
| `firewall service category` | 2 | 2 | 0 | 0 | — |
| `firewall service custom` | 7 | 7 | 0 | 0 | `Gestionale-8443`: 'visibility' non esiste nella versione di destinazione e non ha un equivalente: Documented in the old CLI Reference, absent from the new one. |
| `firewall service group` | 1 | 1 | 0 | 0 | — |
| `firewall schedule recurring` | 1 | 1 | 0 | 0 | — |
| `firewall vip` | 1 | 1 | 0 | 0 | — |
| `firewall ippool` | 1 | 1 | 0 | 0 | — |
| `user ldap` | 1 | 1 | 0 | 0 | `AD-Ufficio`: 'password' richiede una password della categoria 'ldap-radius' (i valori ENC non si copiano mai). Sceglila nel passaggio delle password. |
| `user local` | 2 | 2 | 0 | 0 | — |
| `user group` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase1-interface` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase2-interface` | 1 | 1 | 0 | 0 | — |
| `router static` | 2 | 2 | 0 | 0 | — |
| `firewall policy` | 6 | 5 | 1 | 0 | policy SSL VPN convertita in policy del tunnel dial-up<br>`1`: Profili di sicurezza non migrati, da ricreare e assegnare: webfilter-profile Ufficio-Web |
| `system admin` | 2 | 1 | 0 | 1 | `admin`: 'admin' è l'amministratore con cui esegui lo script: lo script non lo modifica. Per cambiargli la password la CLI chiede quella attuale (Administration Guide, Default administrator password): fallo a mano alla fine. |
| `system dhcp server` | 2 | 2 | 0 | 0 | — |
| `system accprofile` | 1 | 0 | 0 | 1 | `system accprofile`: La sezione 'system accprofile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |
| `vpn ssl settings` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `vpn ssl web portal` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `webfilter profile` | 1 | 0 | 0 | 1 | `webfilter profile`: La sezione 'webfilter profile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |

## Variante `split`: internal spezzato: lan1 con l'indirizzo, lan2 senza; wan2 → lan (resta con lan3)

| Oggetto | Sorgente | Ricostruiti | Convertiti | A mano | Differenza spiegata |
|---|---|---|---|---|---|
| `system interface` | 6 | 5 | 1 | 0 | `ssl.root` convertita nel tunnel IPsec dial-up `dialup_vpn` |
| `system zone` | 1 | 1 | 0 | 0 | — |
| `system global` | 1 | 1 | 0 | 0 | `system global`: 'timezone' non esiste nella versione di destinazione e non ha un equivalente: 5.2 uses a numeric time zone index; 8.0 expects a time zone database name (e.g. Europe/Rome). No official table maps one to the other: set it by hand. |
| `system dns` | 1 | 1 | 0 | 0 | — |
| `firewall address` | 6 | 5 | 1 | 0 | indirizzo del pool SSL VPN convertito nell'intervallo del dial-up |
| `firewall addrgrp` | 1 | 1 | 0 | 0 | — |
| `firewall service category` | 2 | 2 | 0 | 0 | — |
| `firewall service custom` | 7 | 7 | 0 | 0 | `Gestionale-8443`: 'visibility' non esiste nella versione di destinazione e non ha un equivalente: Documented in the old CLI Reference, absent from the new one. |
| `firewall service group` | 1 | 1 | 0 | 0 | — |
| `firewall schedule recurring` | 1 | 1 | 0 | 0 | — |
| `firewall vip` | 1 | 1 | 0 | 0 | — |
| `firewall ippool` | 1 | 1 | 0 | 0 | — |
| `user ldap` | 1 | 1 | 0 | 0 | `AD-Ufficio`: 'password' richiede una password della categoria 'ldap-radius' (i valori ENC non si copiano mai). Sceglila nel passaggio delle password. |
| `user local` | 2 | 2 | 0 | 0 | — |
| `user group` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase1-interface` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase2-interface` | 1 | 1 | 0 | 0 | — |
| `router static` | 2 | 2 | 0 | 0 | — |
| `firewall policy` | 6 | 5 | 1 | 0 | policy SSL VPN convertita in policy del tunnel dial-up<br>`1`: Profili di sicurezza non migrati, da ricreare e assegnare: webfilter-profile Ufficio-Web |
| `system admin` | 2 | 1 | 0 | 1 | `admin`: 'admin' è l'amministratore con cui esegui lo script: lo script non lo modifica. Per cambiargli la password la CLI chiede quella attuale (Administration Guide, Default administrator password): fallo a mano alla fine. |
| `system dhcp server` | 2 | 2 | 0 | 0 | — |
| `system accprofile` | 1 | 0 | 0 | 1 | `system accprofile`: La sezione 'system accprofile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |
| `vpn ssl settings` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `vpn ssl web portal` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `webfilter profile` | 1 | 0 | 0 | 1 | `webfilter profile`: La sezione 'webfilter profile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |

## Variante `profiles-disabled`: come 'lan', con l'opzione "Crea la policy disattivata" per i profili

| Oggetto | Sorgente | Ricostruiti | Convertiti | A mano | Differenza spiegata |
|---|---|---|---|---|---|
| `system interface` | 6 | 5 | 1 | 0 | `ssl.root` convertita nel tunnel IPsec dial-up `dialup_vpn` |
| `system zone` | 1 | 1 | 0 | 0 | — |
| `system global` | 1 | 1 | 0 | 0 | `system global`: 'timezone' non esiste nella versione di destinazione e non ha un equivalente: 5.2 uses a numeric time zone index; 8.0 expects a time zone database name (e.g. Europe/Rome). No official table maps one to the other: set it by hand. |
| `system dns` | 1 | 1 | 0 | 0 | — |
| `firewall address` | 6 | 5 | 1 | 0 | indirizzo del pool SSL VPN convertito nell'intervallo del dial-up |
| `firewall addrgrp` | 1 | 1 | 0 | 0 | — |
| `firewall service category` | 2 | 2 | 0 | 0 | — |
| `firewall service custom` | 7 | 7 | 0 | 0 | `Gestionale-8443`: 'visibility' non esiste nella versione di destinazione e non ha un equivalente: Documented in the old CLI Reference, absent from the new one. |
| `firewall service group` | 1 | 1 | 0 | 0 | — |
| `firewall schedule recurring` | 1 | 1 | 0 | 0 | — |
| `firewall vip` | 1 | 1 | 0 | 0 | — |
| `firewall ippool` | 1 | 1 | 0 | 0 | — |
| `user ldap` | 1 | 1 | 0 | 0 | `AD-Ufficio`: 'password' richiede una password della categoria 'ldap-radius' (i valori ENC non si copiano mai). Sceglila nel passaggio delle password. |
| `user local` | 2 | 2 | 0 | 0 | — |
| `user group` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase1-interface` | 1 | 1 | 0 | 0 | — |
| `vpn ipsec phase2-interface` | 1 | 1 | 0 | 0 | — |
| `router static` | 2 | 2 | 0 | 0 | — |
| `firewall policy` | 6 | 5 | 1 | 0 | policy SSL VPN convertita in policy del tunnel dial-up<br>`1`: Profili di sicurezza non migrati, da ricreare e assegnare: webfilter-profile Ufficio-Web |
| `system admin` | 2 | 1 | 0 | 1 | `admin`: 'admin' è l'amministratore con cui esegui lo script: lo script non lo modifica. Per cambiargli la password la CLI chiede quella attuale (Administration Guide, Default administrator password): fallo a mano alla fine. |
| `system dhcp server` | 2 | 2 | 0 | 0 | — |
| `system accprofile` | 1 | 0 | 0 | 1 | `system accprofile`: La sezione 'system accprofile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |
| `vpn ssl settings` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `vpn ssl web portal` | 1 | 0 | 1 | 0 | impostazioni SSL VPN convertite nel tunnel IPsec dial-up |
| `webfilter profile` | 1 | 0 | 0 | 1 | `webfilter profile`: La sezione 'webfilter profile' (1 oggetti) non viene ricostruita: controllala e ricrea quello che serve. |

