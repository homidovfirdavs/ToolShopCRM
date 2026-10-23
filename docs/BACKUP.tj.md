# Backup-и худкори шабона (Windows)

Ин ҳуҷҷат барои соҳиби мағоза ва Admin аст. Дар он ҳеҷ рамзи воқеӣ навишта намешавад. Рамзҳоро ҳеҷ гоҳ ба Git, чат ё скриншот нафиристед.

## Чӣ сохта мешавад

| Қисм | Вазифа |
|---|---|
| Корбари PostgreSQL `toolshop_backup` | Танҳо хондан. Бо он маълумотро тағйир додан мумкин нест. |
| `%APPDATA%\postgresql\pgpass.conf` | Рамзи `toolshop_backup`-ро нигоҳ медорад. Файл берун аз репозиторий аст ва танҳо корбари Windows-и шумо дастрас дорад. |
| `backend\scripts\Backup-Scheduled.ps1` | Backup-и бесаволу ҷавоб. Файл: `backups\toolshop_crm-auto-<сана>.dump` + `.sha256`. Нигоҳдории 14 нусхаи охирин (на камтар аз 3). Танҳо файлҳои `-auto-` нест мешаванд. Нусхаҳои дастии шумо ҳеҷ гоҳ нест намешаванд. |
| `backend\scripts\Register-BackupTask.ps1` | Вазифаи Windows `ToolShopBackup`: ҳар рӯз соати 23:30. Агар компютер хомӯш буд, баъд аз рӯшан шудан иҷро мешавад. Рамзи Windows нигоҳ дошта намешавад. |

Натиҷа дар `backups\`: `backup.log` (сабт, бе рамз), `LAST-BACKUP-OK.txt` (охирин backup-и хуб) ва `BACKUP-FAILED.txt` (танҳо пас аз хато).

## Қадамҳо

### B1. Версияи PostgreSQL (танҳо хондан)

```powershell
& (Get-ChildItem "$env:ProgramFiles\PostgreSQL\*\bin\psql.exe" | Select-Object -Last 1).FullName -U postgres -h localhost -d postgres -c "SELECT version();"
```

`psql` рамзи `postgres`-ро худаш дар сатри пинҳон мепурсад. Версия бояд 14 ё боло бошад (`pg_read_all_data` аз 14 вуҷуд дорад).

### B2. Сохтани корбари `toolshop_backup`  (танҳо бо тасдиқи ошкори шумо)

Ин дар сервери PostgreSQL корбари нав месозад. Маълумоти бизнес ва схема тағйир намеёбад, миграция нест. Дар `psql` иҷро кунед:

```powershell
& (Get-ChildItem "$env:ProgramFiles\PostgreSQL\*\bin\psql.exe" | Select-Object -Last 1).FullName -U postgres -h localhost -d toolshop_crm
```

Дар сатри `toolshop_crm=#`:

```sql
CREATE ROLE toolshop_backup LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE CONNECTION LIMIT 2;
\password toolshop_backup
GRANT pg_read_all_data TO toolshop_backup;
GRANT CONNECT ON DATABASE toolshop_crm TO toolshop_backup;
\q
```

`\password` рамзи навро ду маротиба дар сатри пинҳон мепурсад. Рамзи дароз ва беназир интихоб кунед (дар ҷои дигар истифода нашуда бошад). Онро дар як ҷои бехатар нависед (парол-менеҷер).

### B3. `pgpass.conf` (шумо худатон месозед)

```powershell
New-Item -ItemType Directory -Force "$env:APPDATA\postgresql" | Out-Null
notepad "$env:APPDATA\postgresql\pgpass.conf"
```

Дар Notepad як сатр нависед (ба ҷои `<РАМЗ>` рамзи воқеиро) ва захира кунед:

```
localhost:5432:toolshop_crm:toolshop_backup:<РАМЗ>
```

Ин файл дар репозиторий нест ва ба Git намеравад.

### B4. Иҷрои дастии санҷишӣ

Пеш аз он `dotnet run` ва `npm run dev` набояд лозим бошанд (backup дар вақти кор ҳам мешавад, вале барои санҷиш беҳтар аст оромӣ).

```powershell
cd C:\Users\Asus\ToolShopCRM\backend\scripts
powershell -ExecutionPolicy Bypass -File .\Backup-Scheduled.ps1
Get-Content ..\..\backups\LAST-BACKUP-OK.txt
```

Бояд `OK … (… MB) SHA-256 …` ва файли `LAST-BACKUP-OK.txt` пайдо шавад. Агар хато шавад, `backups\BACKUP-FAILED.txt` ва `backups\backup.log`-ро кушоед.

### B5. Вазифаи Windows

Бе нусхаи иловагӣ:

```powershell
powershell -ExecutionPolicy Bypass -File .\Register-BackupTask.ps1 -At 23:30
```

Бо нусхаи иловагӣ ба флеш/диск (ҳарфи дискро иваз кунед):

```powershell
powershell -ExecutionPolicy Bypass -File .\Register-BackupTask.ps1 -At 23:30 -CopyTo "E:\ToolShopBackups"
```

Санҷиш:

```powershell
Start-ScheduledTask -TaskName ToolShopBackup
Start-Sleep 20
(Get-ScheduledTaskInfo -TaskName ToolShopBackup).LastTaskResult
Get-Content ..\..\backups\backup.log -Tail 5
```

`LastTaskResult` = `0` маънои муваффақ аст.

### B6. Санҷиши барқарорӣ аз файли `-auto-` ва санҷиши хато

Барқарорӣ аз файли автоматӣ ба DB-и ягонбора (DB-и воқеӣ тағйир намеёбад):

```powershell
.\Test-BackupRestore.ps1 -ExistingBackup "..\..\backups\toolshop_crm-auto-<сана>.dump"
```

Рамзи `postgres`-ро скрипт дар сатри пинҳон мепурсад. Интизорӣ: `PASS`. Агар backup аз барнома кӯҳнатар бошад ва маълумот дар байн тағйир ёфта бошад, ҳисоби сатрҳо қонунӣ фарқ карда метавонад. Барои санҷиши тоза барномаро хомӯш кунед ва backup-и нав гиред.

Санҷиши хато (бояд хато нишон дода шавад, на хомӯшӣ):

```powershell
Rename-Item "$env:APPDATA\postgresql\pgpass.conf" pgpass.conf.off
powershell -ExecutionPolicy Bypass -File .\Backup-Scheduled.ps1
Get-Content ..\..\backups\BACKUP-FAILED.txt
Rename-Item "$env:APPDATA\postgresql\pgpass.conf.off" pgpass.conf
```

Бояд `FAILED:` ва файли `BACKUP-FAILED.txt` пайдо шавад. Баъд файлро ба номи пештара баргардонед (сатри охир).

## Хатарҳо ва маҳдудиятҳо

- `pgpass.conf` рамзро дар диск ҳамчун матн нигоҳ медорад. Бо ин рамз танҳо хондан мумкин аст, на тағйир додан.
- Backup-ҳо ҳешҳои рамзҳо ва калидҳои MFA-ро доранд, ки рамзгузорӣ нашудаанд. Флеш/диски берунӣ бояд хусусӣ бошад (беҳтараш BitLocker). Файлҳоро ба Git ё чат нафиристед.
- Агар компютер тамоми рӯз хомӯш бошад, он рӯз backup намешавад. Баъд аз рӯшан шудан иҷро мешавад.
- Backup дар ҳамон компютер нусхаи **берун аз компютер** нест. Бе `-CopyTo` ба диски дигар хатари вайрон шудани диск боқист. Нусхаи абрӣ ҳоло мавҷуд нест.
- Скрипт худаш барқарориро намесанҷад. Ҳар чанд ҳафта B6-ро такрор кунед.

## Бозгашт

```powershell
Unregister-ScheduledTask -TaskName ToolShopBackup -Confirm:$false
Remove-Item "$env:APPDATA\postgresql\pgpass.conf"
```

Дар `psql` (бо `postgres`): `DROP OWNED BY toolshop_backup; DROP ROLE toolshop_backup;`. Backup-ҳои мавҷуда дар `backups\` мемонанд.
