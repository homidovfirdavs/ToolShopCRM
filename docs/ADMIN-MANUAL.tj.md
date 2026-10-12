# Дастури администратор (ToolShopCRM)

Барои шахсе, ки барномаро насб, танзим ва нигоҳубин мекунад. Дастури кассир/анбордор: [USER-MANUAL.tj.md](USER-MANUAL.tj.md).

> Қоидаи асосӣ: **паролҳо ва калидҳо ҳеҷ гоҳ дар код, git, чат ё скриншот набояд бошанд.** Дар ин ҳуҷҷат ҷойи онҳо бо `<...>` ишора шудааст.

## 1. Нақшҳо ва иҷозатҳо

Чор нақш: **Admin**, **Manager**, **Cashier**, **Warehouse**. Иҷозатҳо дар `ToolShop.Domain/Authorization/Permissions.cs` муайян шудаанд; backend ҳамеша ҳакам аст (пинҳон кардани тугма дар UI ҳимоя нест).

| Иҷозат | Admin | Manager | Cashier | Warehouse |
|---|:-:|:-:|:-:|:-:|
| Products.View (дидани каталог) | ✔ | ✔ | ✔ | ✔ |
| Products.Create / Update | ✔ | ✔ | | |
| Products.Delete (ғайрифаъол кардан) | ✔ | ✔* | | |
| Sales.View (фармоиш, муштарӣ, пардохт, қарз) | ✔ | ✔ | ✔ | |
| Sales.Create (касса) | ✔ | ✔ | ✔ | |
| Sales.Cancel / Sales.Return | ✔ | ✔ | | |
| Inventory.View | ✔ | ✔ | | ✔ |
| Inventory.Adjust (ворид/баровард/ислоҳ) | ✔ | ✔ | | ✔ |
| Finance.View / Finance.Profit.View (Дашборд, Молия) | ✔ | ✔ | | |
| Reports.View | ✔ | ✔ | | |
| Expenses.View / Create / Update | ✔ | ✔ | | |
| Expenses.Void (бекор кардани хароҷот) | ✔ | | | |
| Users.Manage | ✔ | | | |

\* Дар контроллери маҳсулот «ғайрифаъол кардан» ба Admin ва Manager иҷозат дорад, вале дар рӯйхати иҷозатҳои Manager `Products.Delete` нест. Ин ноустувории хурд аст; ҳоло рафтор бо контроллер муайян мешавад.

Амалҳои мушаххас:
- Пардохтро бекор кардан (Void) ва дидани «Фармоишҳои таърихии walk-in»: Admin, Manager.
- Тасдиқи харид: Admin, Manager. Пешнависи харид: Admin, Manager, Warehouse.
- Ғайрифаъол кардани муштарӣ: Admin, Manager.
- Фурӯши анҷомёфтаро бекор кардан мумкин нест; **пурра баргардонидан** (`Sales.Return`): Admin, Manager. Cashier ва Warehouse наметавонанд.

### Идоракунии корбарон (танҳо Администратор)

Саҳифаи **«Корбарон»** (меню «Идоракунӣ») танҳо барои Администратор (иҷозати `Users.Manage`) кушода мешавад. Дар он:

- **Корбари нав:** логин, номи пурра, email, нақш (Admin, Manager, Cashier, Warehouse) ва рамзи аввалия (камаш 8 аломат: ҳарфи калон, ҳарфи хурд, рақам).
- **Таҳрир:** номи пурра, email, нақш. Иваз кардани нақш сессияҳои корбарро қатъ мекунад (бояд дубора ворид шавад).
- **Рамзи нав:** рамзи корбарро иваз мекунад, қуфли ҳисобро мебардорад ва ҳамаи сессияҳои ӯро мебандад. Рамз дар аудит сабт намешавад.
- **Ғайрифаъол/Фаъол кардан:** корбари ғайрифаъол фавран дастрасиро гум мекунад (токени access низ рад мешавад) ва вуруд намекунад. Корбар **нест намешавад**; таърихи амалҳояш мемонад.

Қоидаҳо (backend месанҷад): худро ғайрифаъол кардан ё нақши худро иваз кардан мумкин нест; администратори фаъоли охиринро ғайрифаъол кардан ё аз нақш гирифтан мумкин нест; логин ва email такроршаванда нестанд. Ҳар амал дар `AuditLogs` сабт мешавад (`UserCreated`, `UserUpdated`, `UserRoleChanged`, `UserDeactivated`, `UserActivated`, `UserPasswordReset`).

## 2. Танзимот

Қиматҳо ба .NET User Secrets (маҳаллӣ) ё тағйирёбандаҳои муҳит (сервер) дода мешаванд. Дар тағйирёбандаҳои муҳит `:` ба `__` иваз мешавад (`Jwt__Key`).

| Калид | Маъно | Эзоҳ |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | пайвастшавӣ ба PostgreSQL | ҳатмӣ; парол дар он |
| `Jwt:Key` | калиди имзо | ҳатмӣ; **камаш 32 байт** берун аз Development; тасодуфӣ, дароз |
| `Jwt:Issuer`, `Jwt:Audience` | эмитент ва шунаванда | ҳатмӣ |
| `Jwt:AccessTokenMinutes` | умри access token | пешфарз 15 |
| `AdminUser:Username/Email/Password` | администратори аввал | танҳо агар чунин корбар нест, сохта мешавад |
| `Cors:AllowedOrigins` | рӯйхати адресҳои иҷозатдодашудаи frontend | пешфарз `http://localhost:5173`; тамғаи `*` истифода намешавад |
| `Https:Redirect` | гузариши автоматӣ ба HTTPS | `false`, агар TLS дар Nginx қатъ мешавад |
| `RateLimiting:AuthPermitPerMinute` | маҳдудият барои login/refresh | пешфарз 20/дақиқа барои як IP |
| `RateLimiting:GlobalPermitPerMinute` | маҳдудияти умумӣ | пешфарз 600/дақиқа барои як IP |

Мисол (маҳаллӣ), аз `backend\ToolShop.API`:

```powershell
dotnet user-secrets set "Jwt:Key" "<сатри тасодуфӣ, камаш 32 байт>"
dotnet user-secrets list      # танҳо барои худатон; натиҷаро ҷое нафиристед
```

Барои тавлиди калид метавонед ин фармонро истифода баред:

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```
(Барои истеҳсолот генератори криптографӣ беҳтар аст; ин мисол барои кори маҳаллӣ.)

## 3. Ишга андохтан

```powershell
# backend
cd backend\ToolShop.API ; dotnet run                  # http://localhost:5252
# frontend
cd frontend\toolshop-client ; npm install ; npm run dev   # http://localhost:5173
```

Санҷиши ҳолат: `GET /health/live` (барнома зинда аст) ва `GET /health/ready` (пайвастшавӣ ба база кор мекунад).

## 3.1. PWA, HTTPS ва навсозии версия

Frontend PWA аст (насб ба Windows, macOS, Android, iOS). Тафсилот, ҷадвали насб ва маҳдудиятҳо: [PWA.tj.md](PWA.tj.md).

- **HTTPS ҳатмист** барои насб ва Service Worker (истисно: `http://localhost`). Frontend ва API ҳарду бояд HTTPS бошанд.
- Суроғаи API-и frontend: `VITE_API_URL` ҳангоми `npm run build`. Домени frontend бояд дар `Cors:AllowedOrigins` бошад (агар як домен набошанд).
- `sw.js`, `index.html`, `manifest.webmanifest` бояд бо `Cache-Control: no-cache` дода шаванд; `assets/*` бо `immutable`.
- Версияи нав ба корбарон бо паёми «Версияи нав дастрас аст» мерасад ва худкор ҷорӣ намешавад. Тартиби нашр: аввал backend (мутобиқ ба қафо), баъд frontend.
- Service Worker ягон ҷавоби API, токен ё маълумоти молиявиро кэш намекунад; амалҳои навиштан (фурӯш, пардохт, анбор) бе пайвастшавӣ рад мешаванд.

## 4. Амният

- **Қуфли ҳисоб:** 5 кӯшиши нодурусти воридшавӣ -> қуфл барои 5 дақиқа.
- **Маҳдудияти басомад:** login/refresh ба 20/дақиқа барои як IP. Агар корбарон бо як IP кор кунанд (як шабака), ин ҳадро аз `RateLimiting:AuthPermitPerMinute` боло кунед.
- **Токенҳо:** access token кӯтоҳумр; refresh token якдафъаина ва дар база ҳеш карда нигоҳ дошта мешавад.
- Сарлавҳаҳои амниятӣ (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`), `Cache-Control: no-store` барои `/api`, ҳудуди ҳаҷми дархост 1 МБ, сарлавҳаи `Server` пинҳон.
- Логи дархостҳо роҳро бе query string навишта, ҳеҷ гоҳ тана, сарлавҳа ва токенро сабт намекунад.
- **Аудит:** амалҳои муҳим (масалан бекор кардани фармоиш, ғайрифаъол кардани маҳсулот) дар ҷадвали `AuditLogs` сабт мешаванд. Ҳоло экрани дидани аудит нест; ба база муроҷиат кунед (`Order.Return` ҳам дар он сабт мешавад).
- **Таърих нест намешавад:** маҳсулот/муштарӣ/таъминкунанда ғайрифаъол мешаванд, пардохт ва хароҷот Void мешаванд. Маълумоти таърихиро аз база дастӣ нест накунед.

### Пеш аз staging/production ҳатмист

1. **Амнияти PostgreSQL (иҷро шуд дар базаи маҳаллӣ).** CRM бо корбари маҳдуди `toolshop_app` кор мекунад (на `postgres`), пароли `postgres` иваз шудааст. Барои сервер скрипти `Rotate-DbCredentials.ps1`-ро дар он ҷо иҷро кунед ё ҳамин ҳуқуқҳоро дастӣ диҳед (бахши 4.1).
2. `Jwt:Key`-и нав ва дароз (камаш 32 байт), ҷудо барои ҳар муҳит.
3. Паролҳои корбарони тест ва маълумоти санҷишӣ (`toolshop_crm_test`, корбарони `e2e_*`, `t_*`) дар базаи истеҳсолӣ набояд бошанд.
4. Файлҳои `_dump1.txt`, `_dump2.txt`, `_dump3.txt` дар реша: мундариҷаи онҳо аз тарафи ман хонда шуд — танҳо коди сарчашма аст, парол ё калид надорад. Онҳо ҳоло дар `.gitignore`-анд; агар пештар commit шуда бошанд, бо `git rm --cached _dump*.txt` аз индекс гиред. Таърихи git бо `backend\scripts\Scan-Secrets.ps1` санҷида шуд: сири ҳақиқӣ ёфт нашуд (натиҷаҳои нодуруст дар тест ва файлҳои намунавӣ). Пеш аз ҳар release онро дубора иҷро кунед.

### 4.1. Корбари маҳдуди PostgreSQL

Скрипт: `backend\scripts\Rotate-DbCredentials.ps1` (барнома хомӯш бошад; `-RotateAdminPassword` пароли `postgres`-ро низ иваз мекунад).

- Пеш аз тағйирот backup мегирад ва барқароркуниро месанҷад; агар хато шавад, қатъ мешавад.
- Корбари `toolshop_app` месозад: бе superuser/CREATEDB/CREATEROLE; танҳо SELECT, INSERT, UPDATE, DELETE ва sequence; `__EFMigrationsHistory` танҳо хондан. CREATE/DROP/TRUNCATE рад мешаванд.
- Connection String-и нав танҳо дар User Secrets навишта мешавад; паролҳо чоп ё сабт намешаванд.
- Баъд Backend-ро бо `/health/ready` ва шумораи сатрҳои ҳамаи ҷадвалҳо месанҷад.
- **Миграсияҳо** (`dotnet ef database update`) ва скриптҳои backup/test бо ҳисоби АДМИН иҷро мешаванд, на бо `toolshop_app`; онҳо пароли админро мепурсанд. Пароли админро дар password manager нигоҳ доред.

## 5. Барқарорсозии дастрасӣ

Агар рамзи администратор фаромӯш шуд (**танҳо барои кори маҳаллӣ/dev**):

```powershell
cd backend\scripts
powershell -ExecutionPolicy Bypass -File .\Reset-AdminPassword.ps1
```
Скрипт рамзи навро пинҳонӣ мепурсад, онро ҳеҷ ҷо чоп ё нигоҳ намедорад ва қуфли ҳисобро мебардорад. Барои сервер аз ин скрипт истифода набаред; барои он раванди ҷудо лозим.

## 6. Санҷишҳо

| Санҷиш | Фармон |
|---|---|
| Backend | `cd backend ; .\scripts\Run-Tests.ps1` (`-Filter WalkIn` барои як гурӯҳ) |
| Frontend | `cd frontend\toolshop-client ; npm test` |
| E2E (Playwright) | `cd backend ; .\scripts\Run-E2E.ps1` (Chrome/Edge-ро худаш меёбад; `-BrowserPath` барои роҳи дастӣ) |
| Линт / build | `npm run lint` ; `npm run build` |

Таъмин аст, ки ҳамаи санҷишҳои базадор танҳо бо базае, ки номаш `_test` аст, кор мекунанд. Онҳо маълумоти кории базаи санҷиширо **нест** мекунанд (backend) ё ба он маълумоти нав илова мекунанд (E2E). Онҳоро ҳеҷ гоҳ ба `toolshop_crm` равона накунед.

## 7. Нусхабардорӣ (backup) ва барқарорсозӣ

Скриптҳо дар `backend\scripts` (PostgreSQL клиент: `pg_dump`, `pg_restore`, `psql` бояд дар PATH бошад ё `-PgBinPath` диҳед; парол пинҳонӣ пурсида мешавад):

| Скрипт | Вазифа |
|---|---|
| `Backup-Database.ps1` | нусхаи пурра (`.dump`, фишурда) ба `<реша>\backups`, санҷиши хондашавӣ ва SHA-256 |
| `Restore-Database.ps1 -BackupFile … -TargetDatabase …` | барқарор кардан ба базаи НАВ; базаи `*_test`/`*_restore_test` озод аст, дигарон (аз ҷумла `toolshop_crm`) танҳо бо `-AllowProduction` ва навиштани `RESTORE <ном>` |
| `Test-BackupRestore.ps1` | нусха мегирад, ба `toolshop_crm_restore_test` барқарор мекунад, шумораи сатрҳои ҲАМАИ ҷадвалҳоро муқоиса мекунад, PASS/FAIL медиҳад ва базаи озмоиширо нест мекунад |

Тавсияҳо: `Test-BackupRestore.ps1`-ро вақте иҷро кунед, ки барнома навишт намекунад. Нусхаро **берун аз ин компютер** нигоҳ доред ва махфӣ гиред (дар он хешҳои рамзи корбарон ҳастанд). Скриптҳо нусхаҳои кӯҳнаро худкор нест намекунанд. **Ҳолати санҷиш: `Test-BackupRestore.ps1` дар базаи маҳаллӣ PASS (23 ҷадвал, 931 сатр, 2026-10-11). Нусхаро ҳамеша пеш аз такя кардан санҷед.**

## 8. Ҳалли мушкилот

| Аломат | Сабаб ва ҳал |
|---|---|
| Backend бо хатои `Jwt:Key is not configured` бархест | `Jwt:Key/Issuer/Audience` танзим нашудааст (бахши 2). |
| `Jwt:Key must be at least 32 bytes` | Калид кӯтоҳ аст (берун аз Development). |
| Backend дар оғоз меафтад ва ҷадвалҳоро намеёбад | Миграсияҳо татбиқ нашудаанд. |
| Frontend «Failed to load data» / хатои шабака | Backend кор намекунад ё `Cors:AllowedOrigins` адреси frontend-ро надорад. |
| `login` -> 429 | Маҳдудияти басомад; интизор шавед ё `RateLimiting:AuthPermitPerMinute`-ро баланд кунед. |
| `login` -> хато, ҳарчанд рамз дуруст аст | Ҳисоб қуфл шудааст (5 дақиқа) ё ғайрифаъол аст. |
| `Run-E2E.ps1`: «Port … is already in use» | `dotnet run`/`npm run dev`-ро хомӯш кунед; ин ҳимоя аз кор бар зидди базаи асосист. |
| Playwright Chromium-ро боргирӣ карда наметавонад | Шабака; скрипт Chrome/Edge-и насбшударо истифода мебарад ё `-BrowserPath` диҳед. |
| Санҷиши backend: «Refusing to run… _test» | Номи база бояд бо `_test` ба охир расад. |

## 9. Маҳдудиятҳои маълум (хулоса)

- Баргардонидани қисмии фармоиш нест (танҳо пурра).
- Экрани аудит нест.
- Санҷиши барқарорсозии backup ва скани сирҳо ҳанӯз иҷро нашудаанд.
- Docker, Nginx ва deployment ҳанӯз нестанд (мунтазири тасдиқ).
