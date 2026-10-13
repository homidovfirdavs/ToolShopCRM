# ToolShopCRM

CRM барои мағозаи асбобҳо ва таҷҳизоти сохтмонӣ: касса (POS), фармоишҳо, муштариён, пардохтҳо ва қарзҳо, анбор, харидҳо, хароҷот, молия ва ҳисоботҳо.

> Ҳолат: барои кори маҳаллӣ (development) омода аст. Frontend ҳоло PWA аст (Windows, macOS, Android, iOS), ниг. `docs/PWA.tj.md`. **Docker/Nginx барои staging санҷида шудааст (ниг. `docs/DEPLOYMENT.tj.md`); production-деплой анҷом дода нашудааст.**

| Ҳуҷҷат | Барои кӣ |
|---|---|
| [Дастури корбар](docs/USER-MANUAL.tj.md) | кассир, анбордор, менеҷер |
| [Дастури администратор](docs/ADMIN-MANUAL.tj.md) | администратор / барномасоз |

## Технологияҳо

- **Backend:** C#, ASP.NET Core 10, Entity Framework Core 10, PostgreSQL, ASP.NET Identity, JWT + refresh token.
- **Frontend:** React 19, TypeScript, Vite, Tailwind CSS v4, Recharts. Забонҳо: тоҷикӣ (пешфарз), русӣ, англисӣ.
- **Санҷишҳо:** xUnit (backend), Vitest + Testing Library (frontend), Playwright (E2E).

## Сохтор

```
ToolShopCRM/
├── backend/
│   ├── ToolShop.API/            # Web API, контроллерҳо, Program.cs
│   ├── ToolShop.Domain/         # entity-ҳо, иҷозатҳо (Permissions)
│   ├── ToolShop.Infrastructure/ # EF Core, сервисҳо (фурӯш, пардохт, анбор, молия)
│   ├── ToolShop.Tests/          # санҷишҳои интегратсионии backend
│   ├── scripts/                 # Run-Tests, Run-E2E, Backup/Restore/Test-BackupRestore, Scan-Secrets, Reset-AdminPassword
│   └── ToolShopCRM.slnx
├── frontend/toolshop-client/    # React + TypeScript (src/test = Vitest, e2e = Playwright)
└── docs/                        # дастурҳо
```

## Талабот

- .NET SDK 10
- Node.js 20+ ва npm
- PostgreSQL 16+ (локалӣ)
- Google Chrome ё Microsoft Edge (барои E2E)

## Оғози кор

### 1. База ва танзимот (як маротиба)

Маълумоти махфӣ **дар git нигоҳ дошта намешавад**; ба .NET User Secrets навишта мешавад:

```powershell
cd backend\ToolShop.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=toolshop_crm;Username=<корбар>;Password=<парол>"
dotnet user-secrets set "Jwt:Key" "<сатри тасодуфӣ, камаш 32 байт>"
dotnet user-secrets set "Jwt:Issuer" "toolshop"
dotnet user-secrets set "Jwt:Audience" "toolshop"
# администратори аввал (дар оғози backend сохта мешавад, агар набошад):
dotnet user-secrets set "AdminUser:Username" "admin"
dotnet user-secrets set "AdminUser:Email" "admin@example.local"
dotnet user-secrets set "AdminUser:Password" "<парол, камаш 8 аломат>"
```

Схемаи базаро бо миграсияҳои EF Core созед. Агар `dotnet-ef` насб нест: `dotnet tool install --global dotnet-ef`. Баъд аз `backend\ToolShop.API`:

```powershell
dotnet ef database update --project ..\ToolShop.Infrastructure --startup-project .
```

> Эзоҳ: ин фармон дар ин ҳуҷҷат санҷида нашудааст; базаи `toolshop_crm_test`-ро санҷишҳои backend худашон migrate мекунанд.

### 2. Backend

```powershell
cd backend\ToolShop.API
dotnet run
```

- API: `http://localhost:5252`
- Ҳолати хидмат: `/health/live`, `/health/ready`
- Ҳуҷҷатҳои API (Scalar) танҳо дар муҳити Development: `http://localhost:5252/scalar/v1`

### 3. Frontend

```powershell
cd frontend\toolshop-client
npm install
npm run dev
```

PWA (Service Worker) танҳо дар build-и истеҳсолӣ фаъол аст: `npm run build` ва `npm run preview`. Сервери `npm run dev` онро насб намекунад. Барои API-и дигар: `VITE_API_URL` ҳангоми build.

Барнома: `http://localhost:5173`. Суроғаи API: `VITE_API_URL` ё пешфарз `http://localhost:5252/api` (`src/api/client.ts`).

## Санҷишҳо

Ҳамаи санҷишҳои базадор танҳо бо базаи ҷудогона, ки номаш бо `_test` ба охир мерасад (масалан `toolshop_crm_test`), кор мекунанд ва базаи асосии `toolshop_crm`-ро ҳеҷ гоҳ намебинанд. Парол дар терминал пинҳонӣ пурсида мешавад.

| Санҷиш | Фармон | Ҳолат |
|---|---|---|
| Backend (пардохт, қарз, анбор, walk-in) | `backend> .\scripts\Run-Tests.ps1` | 62/62 PASS (иҷрои 2026-10-12, дар мошини корбар; аз ҷумла корбарон (10) ва баргардонидан (9)) |
| Frontend (Vitest) | `frontend\toolshop-client> npm test` | 70/70 PASS (иҷрои 2026-10-12, дар мошини корбар; аз ҷумла 15 санҷиши нави PWA/offline/POS) |
| Линт ва build | `npm run lint` · `npm run build` | `lint` бе хато дар мошини корбар (2026-10-12); `build` PASS дар cloud, дар мошини корбар алоҳида нишон дода нашудааст |
| E2E (Login, POS, Dashboard, Debts) | `backend> .\scripts\Run-E2E.ps1` | 19/19 PASS (Chrome, 2026-10-11; аз ҷумла баргардонидан (2) ва корбарон (2)) |
| PWA / responsive / offline (эмулятсия, mock API) | `frontend\toolshop-client> npm run e2e:pwa` | 188 PASS, 4 skip (санҷишҳои танҳо барои touch), 0 FAIL дар 6 профили эмулятсия (cloud ва мошини корбар бо Chrome-и воқеӣ, 2026-10-12); дастгоҳи воқеӣ нест |
| Ҷараёни навсозии версия | `npm run verify:update` | 4/4 PASS дар cloud ва дар мошини корбар (2026-10-12; v1 -> паём -> бе reload-и худкор -> v2). Дар Windows бо `E2E_CHROMIUM_PATH` ба Chrome |
| Скани сирҳо | `backend> .\scripts\Scan-Secrets.ps1` | сири ҳақиқӣ ёфт нашуд (18 натиҷаи нодуруст дар тест/намунаҳо; таърихи git тоза) |
| Backup + санҷиши барқарорсозӣ | `backend> .\scripts\Test-BackupRestore.ps1` | PASS (23 ҷадвал, 931 сатр, 2026-10-11) |
| Корбари маҳдуди PostgreSQL | `backend> .\scripts\Rotate-DbCredentials.ps1 -RotateAdminPassword` | иҷро шуд: `toolshop_app` сохта шуд, `/health/ready` = 200, маълумот нигоҳ дошта шуд, пароли админ иваз шуд |

Пеш аз `Run-E2E.ps1` backend ва frontend-и оддиро хомӯш кунед (портҳои 5252 ва 5173 бояд озод бошанд). Скрипт худаш серверҳоро бо базаи `_test` бардошта, пас аз тест мебандад.

## Деплой бо Docker (staging тайёр, production интизор)

`deploy/` — Docker Compose: Nginx (HTTPS) + API + PostgreSQL, health check, `restart: unless-stopped`, volume-ҳои доимӣ, backup/restore. Рамзҳо танҳо дар `deploy/.env` (дар `.gitignore`). Тафсилот: [`docs/DEPLOYMENT.tj.md`](docs/DEPLOYMENT.tj.md). Дастури қадам ба қадам барои сервери Ubuntu: [`docs/PRODUCTION-READINESS.tj.md`](docs/PRODUCTION-READINESS.tj.md).

**Ҳолат:** `COMPLETED FOR STAGING — PRODUCTION PENDING`. Санҷиши staging дар Docker-и воқеӣ (GitHub Actions, workflow «Docker staging check», `deploy/verify-staging.sh`): **18 аз 18 PASS** (иҷрои #3, 2026-10-12; PostgreSQL 17.11, Nginx 1.27.5, .NET 10.0.12). Дар компютери маҳаллӣ (Windows кӯҳна) Docker кор намекунад.

### Чеклисти production (то иҷро нашавад, production тасдиқ намешавад)
- [ ] 1. Сертификати воқеӣ (Let's Encrypt/CA) ва домен; `HSTS_MAX_AGE` -ро баъди санҷиш ба 31536000 гузоред (дар staging self-signed ва HSTS=0 буд).
- [ ] 2. Рамзҳои воқеӣ танҳо дар `deploy/.env`-и сервер (`sh ./init-env.sh`); ҳеҷ рамз дар Git нест. ADMIN_PASSWORD-ро баъди login иваз кунед.
- [ ] 3. Санҷиши backup/restore бо маълумоти воқеӣ (`test-backup-restore.sh`) ва нусхаи backup дар ҷойи дигар (на ҳамон сервер).
- [ ] 4. Санҷиш бо Docker дар муҳити ниҳоӣ (сервери production/VPS), на танҳо дар GitHub.
- [x] 5. Образҳо бо `тег@sha256` pin шудаанд (PostgreSQL 17.11, Nginx 1.27.5, .NET 10.0.12, Node 22) ва дар CI бо pin санҷида шудаанд; навсозӣ: натиҷаи `digest …`-и workflow-ро нусха кунед ва CI-ро дубора иҷро кунед.
- [ ] 6. CSP дар саҳифаҳои дохилӣ (POS, графикҳои Dashboard, Reports) дар Nginx-и воқеӣ бо корбар санҷида шавад (console бе хатои CSP).
- [ ] 7. Санҷиши телефон (Android/iPhone, PWA) бо HTTPS-и воқеӣ: `docs/PWA-PHONE-TEST.tj.md`.

## Қоидаҳои асосии тиҷоратӣ

Ин қоидаҳо дар backend иҷро мешаванд ва санҷишҳо онҳоро месанҷанд:

- **Захираи анбор** ҳеҷ гоҳ ҷудо нигоҳ дошта намешавад: он ҷамъи `InventoryMovement` аст (фурӯш, харид, ислоҳ ва ғ.). Фурӯш бе захираи кофӣ рад мешавад.
- **Қарз** захира намешавад: `қарз = маблағи фармоиш − пардохтҳои дурусти (Completed)`. Дар Dashboard, Молия ва «Қарзҳо» як рақам аст.
- **Фурӯши walk-in** (бе муштарӣ) бояд пурра пардохт шавад. Пардохти қисмӣ ва қарз танҳо барои муштарии сабтшуда.
- **Фоида:** таннарх (COGS) аз нархи харидии таърихии лаҳзаи фурӯш (`CostAtSale`) гирифта мешавад. Агар таннарх номаълум бошад, фоида **N/A** нишон дода мешавад, на 0.
- **Нест намекунем, ғайрифаъол мекунем:** маҳсулот, муштарӣ, таъминкунанда ва категорияҳо ғайрифаъол мешаванд; таърих нигоҳ дошта мешавад. Пардохт ва хароҷот «бекор» (Void) мешаванд, на нест.
- Фармоиши анҷомёфтаро бекор кардан мумкин нест, вале онро **пурра баргардонидан** мумкин аст (Администратор ва Менеҷер, сабаб ҳатмист): мол ба анбор бармегардад (`SaleReturn`), ҳамаи пардохтҳои дурусти фармоиш «Баргардонида шуд» (Refunded) мешаванд, фармоиш «Returned» мешавад ва аз фурӯш, таннарх, қарз ва ҳисоботҳо хориҷ мешавад. Баргардонидани қисмӣ нест. Таннарх (`CostAtSale`), нарх ва тахфифҳо тағйир намеёбанд.
- Корбаронро танҳо Администратор идора мекунад (саҳифаи «Корбарон»): эҷод, нақш, ғайрифаъол/фаъол, рамзи нав. Корбар нест намешавад; худро ғайрифаъол кардан ё нақши худро иваз кардан мумкин нест.
- Ҳамаи амалҳои муҳим дар логи аудит сабт мешаванд.

## Амният (хулоса)

- JWT (access) + refresh token-и якдафъаина (дар база ҳеш карда нигоҳ дошта мешавад).
- Қуфл баъд аз 5 кӯшиши нодуруст барои 5 дақиқа; маҳдудияти басомад барои `login`/`refresh`.
- Сарлавҳаҳои амниятӣ, ҳудуди ҳаҷми дархост, CORS бо рӯйхати ошкоро (`Cors:AllowedOrigins`).
- Паролҳо ва калидҳо ҳеҷ гоҳ дар код ё git нестанд. Тафсилот: [Дастури администратор](docs/ADMIN-MANUAL.tj.md).

## Маҳдудиятҳои маълум

- Баргардонидани қисмии фармоиш нест (танҳо пурра).
- Экрани дидани аудит нест (сабтҳо дар ҷадвали `AuditLogs` ҳастанд).
- Backup/restore дар базаи маҳаллӣ санҷида шудааст (PASS); дар муҳити staging/production бояд дубора санҷида шавад.
- Docker: staging дар CI санҷида шудааст (18/18); production-деплой анҷом дода нашудааст, чеклисти боло интизор аст.
- PWA: санҷиш дар Windows/macOS/Android/iOS-и воқеӣ анҷом дода нашудааст, танҳо эмулятсия дар Chromium; насб ва Service Worker HTTPS талаб мекунанд. Тафсилот: [PWA](docs/PWA.tj.md).
