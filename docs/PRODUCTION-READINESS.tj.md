# Дастури омодагӣ ба Production — ToolShop CRM (Ubuntu Server)

> **Ин дастур танҳо омодагӣ аст, на деплой.** Ба маълумоти воқеӣ ва сервери воқеӣ то охири чеклист даст нарасонед.
> Фармонҳои поён стандартӣ мебошанд, вале ман онҳоро дар сервери воқеии Ubuntu иҷро накардаам (дар ин ҷо танҳо staging дар CI санҷида шудааст). Аввал онҳоро дар **сервери санҷишӣ** (арзон, бе маълумоти воқеӣ) такрор кунед.

**Аломатҳо:** ⚠️ **ХАТАРНОК** = қадаме, ки метавонад шуморо аз сервер бимонад ё маълумотро гум кунад. Пеш аз он бодиққат хонед.
Дар фармонҳо `crm.example.com` — домени шумо, `crmadmin` — номи корбар дар сервер. Онҳоро бо номҳои худатон иваз кунед.

## Мундариҷа
1. Интихоби сервер
2. Омода кардани сервер (амният ва Docker)
3. Домен ва HTTPS-и воқеӣ
4. Гирифтани код, `deploy/.env` ва рамзҳо
5. Кӯчонидани маълумоти воқеӣ ба сервер (бо эҳтиёт)
6. Backup ва Restore: санҷиш
7. Санҷиши CSP (POS, Dashboard, Reports)
8. Санҷиш дар Android ва iPhone
9. Шартҳои «Production омода аст»

---

## 1. Интихоби сервер

| Чиз | Ҳадди аққал | Тавсия |
|---|---|---|
| Система | Ubuntu Server **24.04 LTS** | ҳамон |
| CPU / RAM | 2 vCPU / 2 GB (+ swap) | **2 vCPU / 4 GB** (build-и .NET хотираро талаб мекунад) |
| Диск | 30 GB | 40–80 GB SSD (база + backup) |
| IP | IPv4-и доимӣ (static) | ҳамон |
| Макон | ба мизоҷон наздиктарин минтақа | — |

Чӣ лозим аст:
- **VPS** аз провайдери боэътимод (Hetzner, DigitalOcean, Vultr, Contabo ва ғайра) ё сервери худатон дар мағоза бо IP-и сафед. Сервери худӣ дар мағоза = нур, интернет ва нусхаи backup-и худатон масъул аст.
- **Домен** (мас. `crm.example.com`) ва дастрасӣ ба DNS-и он.
- Калиди **SSH** (на рамз). Дар Windows PowerShell:
  ```powershell
  ssh-keygen -t ed25519 -C "crm-server"
  Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
  ```
  Танҳо файли `.pub`-ро (ҷамъиятӣ) ба провайдер диҳед. Файли `id_ed25519` (бе `.pub`) **махфӣ** аст: ба ҳеҷ кас ва ҳеҷ ҷо нафиристед.

> Барои санҷиши аввал сервери ба қадри 1–2 рӯз кофӣ мегиред ва баъд онро нест мекунед, чун маълумоти воқеӣ нест.

## 2. Омода кардани сервер

Ба сервер бо IP-и он ворид шавед (аввалин бор бо `root` ё корбари провайдер):
```powershell
ssh root@IP_СЕРВЕР
```

### 2.1 Навсозӣ ва корбари оддӣ
```bash
apt update && apt -y upgrade
adduser crmadmin                 # рамзи қавӣ гузоред ва дар менеҷери рамзҳо нигоҳ доред
usermod -aG sudo crmadmin
mkdir -p /home/crmadmin/.ssh
cp ~/.ssh/authorized_keys /home/crmadmin/.ssh/ && chown -R crmadmin:crmadmin /home/crmadmin/.ssh
chmod 700 /home/crmadmin/.ssh && chmod 600 /home/crmadmin/.ssh/authorized_keys
```
**Равзанаи нави** терминалро кушоед ва санҷед: `ssh crmadmin@IP_СЕРВЕР`, баъд `sudo whoami` (бояд `root` нависад). Равзанаи кӯҳнаро **набандед**, то санҷиш гузарад.

### 2.2 Деворӣ оташ (firewall)
> ⚠️ **ХАТАРНОК:** `ufw enable`-ро бе иҷозати портии 22 иҷро накунед, вагарна аз сервер мемонед.
```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status
```
Танҳо 22, 80, 443 кушодаанд. PostgreSQL берун кушода **нест** ва набояд кушода шавад.

### 2.3 Манъ кардани воридшавӣ бо рамз (танҳо калид)
> ⚠️ **ХАТАРНОК:** Танҳо баъди он иҷро кунед, ки воридшавии `crmadmin` бо калид кор мекунад (қадами 2.1) ва равзанаи дуюм кушода аст.
```bash
sudo sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo sed -i 's/^#\?PermitRootLogin.*/PermitRootLogin no/' /etc/ssh/sshd_config
sudo sshd -t && sudo systemctl restart ssh
```
Дар равзанаи сеюм аз нав `ssh crmadmin@IP_СЕРВЕР`-ро санҷед.

### 2.4 Навсозии худкори амнияти система
```bash
sudo apt install -y unattended-upgrades fail2ban
sudo dpkg-reconfigure -plow unattended-upgrades    # «Yes»
```

### 2.5 Насб кардани Docker (аз сайти расмии Docker)
```bash
sudo apt update && sudo apt install -y ca-certificates curl git
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker crmadmin        # баъд аз сессия баромада, дубора ворид шавед
docker --version && docker compose version
```
Огоҳӣ: гурӯҳи `docker` ба қариб-`root` баробар аст. Ба он танҳо шахсони боэътимодро илова кунед.

### 2.6 Лимити ҳаҷми лог-ҳо (то диск пур нашавад)
```bash
echo '{ "log-driver": "json-file", "log-opts": { "max-size": "10m", "max-file": "3" } }' | sudo tee /etc/docker/daemon.json
sudo systemctl restart docker
```
Ин танҳо ба контейнерҳои **нав** татбиқ мешавад (баъдтар `up -d --force-recreate`).

## 3. Домен ва HTTPS-и воқеӣ

### 3.1 DNS
Дар панели домен сабти **A** созед: `crm.example.com` → IP-и сервер. Санҷед (баъди 5–30 дақиқа):
```bash
getent hosts crm.example.com        # бояд IP-и серверро нишон диҳад
```

### 3.2 Гирифтани сертификат (Let's Encrypt, ройгон)
**Пеш аз ин қадамҳои 4.1 (clone) ва 4.2 (`.env`) -ро иҷро кунед**, чун hook-ҳои certbot аз `/opt/toolshop/deploy` истифода мебаранд. Порти 80 бояд озод бошад (ҳанӯз `docker compose up` накардаед).
```bash
sudo apt install -y certbot
sudo certbot certonly --standalone -d crm.example.com --agree-tos -m ЭМАИЛ_ШУМО --no-eff-email \
  --pre-hook  "docker compose -f /opt/toolshop/deploy/docker-compose.yml stop web" \
  --deploy-hook "install -m 644 \$RENEWED_LINEAGE/fullchain.pem /opt/toolshop/deploy/certs/fullchain.pem && install -m 640 -g 101 \$RENEWED_LINEAGE/privkey.pem /opt/toolshop/deploy/certs/privkey.pem" \
  --post-hook "docker compose -f /opt/toolshop/deploy/docker-compose.yml start web"
```
Чӣ мешавад: certbot порти 80-ро муваққатан истифода мебарад, сертификатро мегирад ва ба `deploy/certs/` нусха мекунад. Калиди хусусӣ ба гурӯҳи 101 (корбари Nginx дар контейнер) хондан иҷозат дорад ва ба дигарон не. Азнавсозии худкор дар ҳар ~60 рӯз кор мекунад (бо хомӯш/равшан шудани `web` ба якчанд сония).

Санҷиши азнавсозӣ (бе тағйири воқеӣ):
```bash
sudo certbot renew --dry-run
```

### 3.3 HSTS: оҳиста
HSTS браузерро «ёд медиҳад», ки ин сайт ҳамеша HTTPS бошад. Хатогӣ = сайт то мӯҳлат дастнорас.
> ⚠️ **ХАТАРНОК:** `HSTS_MAX_AGE`-ро аввал **300** (5 дақиқа) гузоред. Танҳо баъди он ки ҳама чиз 1–2 ҳафта хуб кор кард, онро ба `31536000` (1 сол) баландтар кунед.

## 4. Гирифтани код, `deploy/.env` ва рамзҳо

### 4.1 Калиди «танҳо-хониш» барои repo-и Private
Ба сервер аз GitHub танҳо хондан иҷозат диҳед (на нависед):
```bash
ssh-keygen -t ed25519 -f ~/.ssh/toolshop_deploy -N "" -C "toolshop-server"
cat ~/.ssh/toolshop_deploy.pub
```
Матни `.pub`-ро дар GitHub: repo → **Settings → Deploy keys → Add deploy key** (**Allow write access** -ро тик **нагузоред**). Баъд:
```bash
printf 'Host github.com\n  IdentityFile ~/.ssh/toolshop_deploy\n  IdentitiesOnly yes\n' >> ~/.ssh/config
chmod 600 ~/.ssh/config
sudo mkdir -p /opt/toolshop && sudo chown crmadmin:crmadmin /opt/toolshop
git clone git@github.com:homidovfirdavs/ToolShopCRM.git /opt/toolshop
```

### 4.2 Сохтани `.env` бо рамзҳои тасодуфӣ
```bash
cd /opt/toolshop/deploy
sh ./init-env.sh          # .env бо рамзҳои тасодуфӣ месозад (рамзҳоро чоп намекунад, mode 600)
nano .env
```
Дар `.env` ин сатрҳоро тағйир диҳед:
```
PUBLIC_ORIGIN=https://crm.example.com
HTTP_PORT=80
HTTPS_PORT=443
HSTS_MAX_AGE=300
ADMIN_EMAIL=EMAILИ_ВОҚЕИИ_ШУМО
```
Боқимонда (рамзҳо) ҳамонро монед.

**Қоидаҳои оҳанин:**
- `deploy/.env` **ҳеҷ гоҳ** ба Git, чат, скриншот ё почта намеравад. Дар Git танҳо `.env.example` (бе рамз) аст.
- Нусхаи `.env`-ро дар **менеҷери рамзҳо** (Bitwarden, 1Password) нигоҳ доред. Агар сервер гум шавад, бе он барқарорсозӣ душвор аст.
- Дастрасӣ: `ls -l .env` бояд `-rw-------` бошад (танҳо соҳиб мехонад).
- ADMIN_PASSWORD-ро аз `.env` хонед, бо он бори аввал ворид шавед ва **фавран** онро дар CRM иваз кунед. Баъд дар `.env` ADMIN_PASSWORD-ро холӣ кунед (`ADMIN_PASSWORD=`) ва `docker compose up -d` кунед.

> ⚠️ **ХАТАРНОК:** `DB_OWNER_PASSWORD`-ро дар `.env` **баъди** аввалин оғози `db` тағйир надиҳед. Docker рамзи PostgreSQL-ро танҳо дар аввалин сохтани базаи нав мегирад. Тағйири `.env` рамзи воқеиро иваз намекунад ва CRM шикаста метавонад. `APP_DB_PASSWORD`-ро иваз кардан бехатар аст: `docker compose up -d` онро худкор нав мекунад.

### 4.3 Аввалин оғоз (дар базаи холӣ)
```bash
cd /opt/toolshop/deploy
docker compose up -d --build
docker compose ps                     # ҳама бояд «healthy» ё «running» бошанд
docker compose logs --tail=50 api
curl -I https://crm.example.com/health/live        # HTTP/2 200
```
Агар `docker compose ps` хато нишон диҳад, `docker compose logs --tail=100 ИМИ_ХИДМАТ`-ро нусха кунед.

Санҷиши барқароршавӣ баъди хомӯш шудани сервер:
```bash
sudo reboot
# 1–2 дақиқа баъд:
ssh crmadmin@IP_СЕРВЕР "cd /opt/toolshop/deploy && docker compose ps"
```

## 5. Кӯчонидани маълумоти воқеӣ (бо эҳтиёт)

Маълумоти воқеии шумо ҳоло дар PostgreSQL-и компютери Windows аст. Ин қадамҳо онро **танҳо мехонанд**.

> ⚠️ **ХАТАРНОК:** ин қадам маълумоти воқеиро ба сервер мебарад. Танҳо баъди қадамҳои 1–4 ва аввал дар сервери **санҷишӣ** иҷро кунед. Базаи Windows-ро нест накунед ва тағйир надиҳед.

1. **Версияи PostgreSQL:** дар Windows `psql --version`. Агар версия **аз 17 болотар** бошад (мас. 18), `pg_restore`-и сервер файлро намехонад. Ба ман нависед, ман `POSTGRES_IMAGE`-ро мувофиқ мекунам.
2. **Backup дар Windows** (скрипти худатон, танҳо мехонад):
   ```powershell
   Set-Location C:\Users\Asus\ToolShopCRM\backend
   .\scripts\Backup-Database.ps1
   ```
   Он файли `.dump` ва `.dump.sha256` месозад (дар `C:\Users\Asus\ToolShopCRM\backups`).
3. **Нусха ба сервер** (ҳарду файл!):
   ```powershell
   scp C:\Users\Asus\ToolShopCRM\backups\toolshop_crm-*.dump* crmadmin@IP_СЕРВЕР:/opt/toolshop/deploy/backups/
   ```
4. **Restore ба базаи НАВ** дар сервер (ҳеҷ чиз бознавис намешавад):
   ```bash
   cd /opt/toolshop/deploy
   docker compose exec -T db sh /db/restore.sh /backups/toolshop_crm-САНА.dump toolshop_crm_v1
   ```
   Скрипт аввал SHA-256-ро месанҷад. Агар «mismatch» гӯяд, нусха вайрон шудааст, дубора нусха кунед.
5. **Гузариш ба базаи нав:**
   ```bash
   docker compose stop api
   sed -i 's/^DB_NAME=.*/DB_NAME=toolshop_crm_v1/' .env
   docker compose up -d        # db як лаҳза аз нав оғоз мешавад (маълумот дар volume мемонад)
   ```
6. **Санҷиш:** ба CRM ворид шавед, шумораи фармоишҳо, қарзҳо ва қолиби маҳсулотро бо Windows муқоиса кунед.
7. **Муҳим:** корбарон ва рамзҳо ҳамроҳ кӯчиданд. Рамзи admin = рамзи кӯҳнаи компютери шумо. **Онро фавран иваз кунед**. Корбарони санҷиширо (аз E2E ва ғайра) ғайрифаъол кунед, **нест накунед**.

## 6. Backup ва Restore: санҷиш

Ҳамаи фармонҳо дар `/opt/toolshop/deploy`.

### 6.1 Санҷиши автоматӣ (бехатар, ба базаи асосӣ тағйир намедиҳад)
```bash
docker compose exec -T db sh /db/test-backup-restore.sh
```
Он: backup мегирад → ба базаи муваққатии `…_restore_test` барқарор мекунад → шумораи сатрҳои **ҳар** ҷадвалро муқоиса мекунад → танҳо ҳамон базаи муваққатиро нест мекунад. Бояд `PASS: N tables - restored copy matches the source` нависад. Ин санҷиш дар CI гузашт; ҳоло онро бо маълумоти воқеӣ такрор кунед.

### 6.2 Backup-и худкор
```bash
docker compose --profile backup up -d backup      # ҳар 24 соат, нигоҳдории 14 рӯз
ls -lh backups/
```
Файлҳо аз тарафи `root`-и контейнер сохта мешаванд, барои хондани онҳо `sudo` лозим аст.

### 6.3 Нусха ба ҷойи ДИГАР (мутлақо лозим)
Backup дар ҳамон сервер = ҳеҷ backup, агар сервер гум шавад. Аз компютери дигар (мас. Windows) нусха гиред:
```powershell
scp crmadmin@IP_СЕРВЕР:/opt/toolshop/deploy/backups/*.dump* D:\crm-backups\
```
(агар «Permission denied» гӯяд, дар сервер `sudo chown -R crmadmin /opt/toolshop/deploy/backups` иҷро кунед). Ҳар ҳафта, ё худкор бо `rclone`/`rsync` ба захираи абрӣ.

### 6.4 Омӯзиш: барқарорсозии воқеӣ (як бор дар 1–3 моҳ)
Ба номи НАВ барқарор кунед, санҷед, баъд (дар вақти вайроншавӣ) `DB_NAME`-ро иваз кунед:
```bash
docker compose exec -T db sh /db/restore.sh /backups/ФАЙЛ.dump toolshop_crm_drill
docker compose exec db psql -U toolshop_owner -d toolshop_crm_drill -c '\dt'
```
> ⚠️ **ХАТАРНОК:** ҳеҷ гоҳ `DROP DATABASE` ё `docker compose down -v` -ро дар сервери воқеӣ иҷро накунед. `-v` **volume-и базаро нест мекунад**.

## 7. Санҷиши CSP (POS, Dashboard, Reports)

CSP ҳимоя аст, ки браузер намегузорад скрипти бегона кор кунад. Агар чизе масдуд шавад, саҳифа метавонад ба таври нодуруст кор кунад.

> ⚠️ **ХАТАРНОК барои маълумот:** POS фармоиши воқеӣ месозад (нест кардан манъ аст). Ин санҷишро дар сервери **санҷишӣ ё базаи холӣ** гузаронед (қадами 4.3), на дар маълумоти воқеӣ.

1. Дар Chrome сайтро кушоед: `https://crm.example.com`, F12 → ҷадвали **Console**, тугмаи 🚫 (Clear).
2. Бо тартиб кушоед: **Login → Dashboard → POS** (маҳсулот илова кунед, сабад, пардохт) **→ Reports** (ҳар 9 ҷадвалча ва CSV export) **→ Finance → Expenses**.
3. Дар Console ҷустуҷӯ кунед: `Content Security Policy`, `Refused to`, `violat`. **Бояд ҳеҷ чиз набошад.**
4. Дар ҷадвали **Application → Service Workers**: ҳолат «activated».
5. Аз сервер: `curl -sI https://crm.example.com/ | grep -i content-security-policy`.

Агар хато бошад: матни пурраи хатои Console-ро нусха кунед ва ба ман фиристед. CSP-ро **худатон суст накунед** (мас. `unsafe-inline` дар скриптҳо).

## 8. Санҷиш дар Android ва iPhone

Бо домени воқеӣ ва HTTPS-и воқеӣ туннел ва `mkcert` лозим нест. (Сертификати self-signed дар телефон PWA насб намекунад.) Дастури мукаммали маъмул: `docs/PWA-PHONE-TEST.tj.md`.

**Android (Chrome):**
1. `https://crm.example.com` кушоед, қулфи 🔒 бояд оддӣ бошад.
2. Меню ⋮ → **Install app** (ё «Add to Home screen»).
3. Аз нишона кушоед, login кунед, POS ва Dashboard-ро санҷед (ҷадвалҳо ҳамчун корт).
4. Режими парвоз (airplane): навор «offline»; кӯшиши фурӯш бояд дуруст рад шавад.

**iPhone (танҳо Safari!):**
1. `https://crm.example.com` дар Safari. Chrome дар iPhone PWA насб намекунад.
2. Тугмаи **Share → Add to Home Screen → Add**.
3. Аз нишона кушоед, login кунед, POS ва Dashboard-ро санҷед.
4. Режими парвоз: ҳамон санҷиши offline.

Барои ҳар ду: санҷед, ки клавиатура майдонҳоро намепӯшонад, тугмаҳо бо ангушт осон пахш мешаванд, амаломи «навсозӣ» (update prompt) пас аз нашри нав пайдо мешавад.

## 9. Шартҳои «Production омода аст»

CRM танҳо вақте омода ҳисоб мешавад, ки **ҳамаи** инҳо иҷро шудаанд:

- [ ] Сервер: Ubuntu 24.04, воридшавӣ танҳо бо калид, ufw (22/80/443), unattended-upgrades, лимити лог-ҳо.
- [ ] Домен ва сертификати воқеӣ; `certbot renew --dry-run` бе хато; `curl -I https://…/health/live` → 200.
- [ ] `HSTS_MAX_AGE` баъди 1–2 ҳафта ба `31536000` баландтар шудааст.
- [ ] Рамзҳо танҳо дар `deploy/.env` (mode 600); нусха дар менеҷери рамзҳо; ҳеҷ рамз дар Git/чат. `ADMIN_PASSWORD` иваз ва аз `.env` пок шудааст.
- [ ] Маълумоти воқеӣ кӯчонида шуд ва бо Windows муқоиса шуд; рамзи кӯҳнаи admin иваз шуд; корбарони санҷишӣ ғайрифаъол шуданд.
- [ ] `test-backup-restore.sh` бо маълумоти воқеӣ → **PASS**.
- [ ] Backup-и худкор кор мекунад, нусха ба ҷойи дигар мерасад, ва барқарорсозии омӯзишӣ (6.4) дар 30 рӯзи охир анҷом шуд.
- [ ] CSP: дар POS, Dashboard, Reports хатои Console нест (қадами 7).
- [ ] Android ва iPhone: насб, login, POS, offline (қадами 8).
- [ ] `sudo reboot` → ҳама контейнерҳо худашон бармегарданд (`docker compose ps`).
- [ ] Мониторинг: санҷиши беруна (мас. UptimeRobot) ба `https://…/health/live`; огоҳӣ ҳангоми пур шудани диск (>80%).
- [ ] Нақшаи бозгашт: пеш аз ҳар навсозӣ backup; фармони навсозӣ: `cd /opt/toolshop && git pull && cd deploy && docker compose up -d --build`; ва CI-и «Docker staging check» сабз.

Пеш аз тасдиқ ба ман натиҷаҳоро нишон диҳед (скриншот/лог). Ман онҳоро месанҷам ва ҳолати ниҳоиро менависам.
