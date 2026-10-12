# Деплой бо Docker (HTTPS + PostgreSQL) — ToolShop CRM

> Ин ҳуҷҷат танҳо омодасозӣ аст. Production-деплой нест. Ҳамаи фармонҳо дар папкаи `deploy/` иҷро мешаванд.

## Таркиб
| Хидмат | Вазифа |
|---|---|
| `web` | Nginx (unprivileged) + React build; HTTPS 8443→`HTTPS_PORT`, HTTP 8080→301 ба HTTPS; `/api/` → `api` |
| `api` | .NET 10 API (корбари оддӣ, rootfs read-only, `/health/live` ва `/health/ready`) |
| `db` | PostgreSQL 17, volume `pgdata`, ба host нашр **намешавад** |
| `migrate` | як бор: нақши `toolshop_app` (бе DDL) → EF-миграцияҳо (idempotent) → grants |
| `backup` | ихтиёрӣ (`--profile backup`): backup-и мунтазам + нигоҳдории `BACKUP_KEEP_DAYS` |

Танҳо Nginx порт мекушояд. Шабакаи `backend` `internal` аст (db/api бе дастрасии беруна).
Restart policy: `unless-stopped`. Volume-ҳо: `pgdata` (маълумот), `dpkeys` (калидҳои DataProtection).

## Танзими аввал
```sh
cd deploy
sh ./init-env.sh                      # .env бо рамзҳои тасодуфӣ (чоп намешавад, mode 600)
# .env-ро кушо: PUBLIC_ORIGIN, HTTP_PORT/HTTPS_PORT; ADMIN_PASSWORD-ро аз ҳамин файл хон
# сертификат: сертификати воқеӣ (Let's Encrypt/CA) → certs/fullchain.pem + certs/privkey.pem
#   барои санҷиш:  sh ./make-selfsigned-cert.sh localhost
docker compose --env-file .env up -d --build
```
`deploy/.env`, `deploy/certs/*`, `deploy/backups/` дар `.gitignore` ҳастанд. Дар Git танҳо `.env.example` (бе рамз).
Рамзҳо танҳо ҳарфу рақам бошанд (онҳо ба connection string меафтанд).
Linux: папкаи `backups` бояд барои корбари postgres (uid 70 дар alpine) навишаниро иҷозат диҳад: `sudo chown 70:70 backups`.

## Амният
- TLS 1.2/1.3, HSTS (`HSTS_MAX_AGE`; дар staging 0), CSP (`default-src 'self'`, `frame-ancestors 'none'`), nosniff, X-Frame-Options, Referrer-Policy, Permissions-Policy.
- `server_tokens off`, `limit_req` (API 20 дар сония; `/api/auth/` 10 дар дақиқа), `client_max_body_size 1m`, файлҳои `.`-ро манъ мекунад.
- `/health/ready` берун нест; танҳо `/health/live`.
- API бо `toolshop_app` кор мекунад (на superuser, бе CREATE/DROP/TRUNCATE; `__EFMigrationsHistory` танҳо-хониш). Миграцияро `toolshop_owner` дар `migrate` иҷро мекунад.
- Контейнерҳо: `cap_drop: ALL`, `no-new-privileges`, rootfs read-only (web/api).
- JWT_KEY ≥ 32 байт (API дар Production бе ин оғоз намешавад).

## Health check
`db`: `pg_isready` · `api`: `curl /health/ready` (санҷиши DB) · `web`: `/nginx-health`. Тартиб: db → migrate → api → web.

## Backup ва Restore
```sh
docker compose exec -T db sh /db/backup.sh                     # → backups/<db>-<сана>.dump + .sha256
docker compose exec -T db sh /db/test-backup-restore.sh        # backup → restore ба <db>_restore_test → ҳисоби сатрҳои ҲАР ҷадвал → танҳо ҳамон DB-и санҷишӣ нест мешавад
docker compose exec -T db sh /db/restore.sh /backups/<файл>.dump <номи_нав>
```
`restore.sh` ҳеҷ гоҳ базаи мавҷударо бознависӣ намекунад (танҳо `*_test` бо `RECREATE=yes`). Барқарорсозии воқеӣ: ба номи нав restore кун, санҷ, баъд `DB_NAME`-ро иваз кун ва `docker compose up -d`.
Backup-ро ба ҷойи дигар (на ҳамин мошин) нусха кун; он ҳамаи маълумот ва hash-и рамзҳоро дорад.

## Санҷиши staging (на production)
```sh
sh ./verify-staging.sh
```
Лоиҳаи ҷудогонаи `toolshop-stg` (volume-ҳои худ, порти 8080/8443, рамзҳои муваққатӣ), build + up + санҷишҳо + backup/restore; дар охир танҳо ҳамон лоиҳаро пок мекунад.

## Навсозӣ
`docker compose --env-file .env up -d --build` — миграцияҳо худкор ва бетағйир (additive) мегузаранд. Пеш аз навсозӣ backup гир.
