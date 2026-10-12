# syntax=docker/dockerfile:1
# ToolShop CRM web (React build served by Nginx, HTTPS). Build context = frontend/toolshop-client/.
# Base images pinned by digest (verified in CI 2026-10-12: nginx 1.27.5). Update via the "Docker staging check" workflow output.
FROM node:22-alpine@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402 AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Same-origin API: the browser calls /api and Nginx forwards it to the API container.
ARG VITE_API_URL=/api
ARG APP_BUILD_ID
ENV VITE_API_URL=$VITE_API_URL APP_BUILD_ID=$APP_BUILD_ID
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine@sha256:65e3e85dbaed8ba248841d9d58a899b6197106c23cb0ff1a132b7bfe0547e4c0 AS runtime
# envsubst renders *.template into /etc/nginx/conf.d at start; only these variables are substituted
ENV NGINX_ENVSUBST_FILTER="PUBLIC_HTTPS_PORT|HSTS_MAX_AGE" \
    PUBLIC_HTTPS_PORT=443 HSTS_MAX_AGE=31536000
COPY nginx/templates/ /etc/nginx/templates/
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080 8443
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
  CMD wget -q --spider http://127.0.0.1:8080/nginx-health || exit 1
