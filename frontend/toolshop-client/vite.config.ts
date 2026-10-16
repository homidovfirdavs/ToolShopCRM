import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'
import fs from 'node:fs'
import { fileURLToPath } from 'node:url'

// LAN HTTPS for testing the camera barcode scanner on a real phone (npm run dev:lan).
// Browsers open the camera only on HTTPS or localhost, so http://192.168.x.x:5173 does NOT work from a phone.
// The certificate is made once on the developer PC with mkcert (see docs/PHONE-SCANNER-TEST.tj.md); the files live in
// ".certs/" which is git-ignored, so no key is ever committed. Without those files nothing here changes.
const certDir = fileURLToPath(new URL('./.certs/', import.meta.url))
const lanCert = certDir + 'lan.pem'
const lanKey = certDir + 'lan-key.pem'
const lanHttps = fs.existsSync(lanCert) && fs.existsSync(lanKey)
  ? { cert: fs.readFileSync(lanCert), key: fs.readFileSync(lanKey) }
  : undefined

// One origin for the app AND the API: /api is forwarded to the local backend (no CORS, no mixed content).
const localApiProxy = {
  '/api': { target: 'http://localhost:5252', changeOrigin: false },
  '/health': { target: 'http://localhost:5252', changeOrigin: false },
}

// PWA: the service worker precaches ONLY the static app shell (built JS/CSS/HTML/icons).
// It has no runtime caching rules at all, so API responses (orders, payments, finance,
// reports, tokens) are never stored by the service worker and always come from the network.
// Build label shown in the sidebar and used to tell versions apart when checking updates.
// CI / release scripts can set APP_BUILD_ID; otherwise it is the build time (UTC, minutes).
const APP_BUILD = process.env.APP_BUILD_ID ?? new Date().toISOString().slice(0, 16).replace('T', ' ')

export default defineConfig(({ mode }) => {
  if (mode === 'lan' && !lanHttps) {
    throw new Error('npm run dev:lan needs .certs/lan.pem and .certs/lan-key.pem (create them with mkcert, see docs/PHONE-SCANNER-TEST.tj.md).')
  }
  // The phone reaches the API through the SAME https origin (/api is proxied below), never through http://localhost.
  if (mode === 'lan') process.env.VITE_API_URL ??= '/api'
  // "lanhttp": plain HTTP on the LAN, only for a phone whose Chrome flag "unsafely-treat-insecure-origin-as-secure"
  // lists this PC's address (fallback when the mkcert CA can not be installed on the phone). Same /api proxy, no certificate.
  // It also accepts a temporary Cloudflare quick tunnel (*.trycloudflare.com) that gives the phone real HTTPS.
  if (mode === 'lanhttp') process.env.VITE_API_URL ??= '/api'
  return {
  define: { __APP_BUILD__: JSON.stringify(APP_BUILD) },
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      // "prompt": a new version waits until the user taps "Reload", so an update can never
      // reload the page in the middle of a sale.
      registerType: 'prompt',
      injectRegister: false, // registered explicitly in src/pwa/register.ts (production only)
      includeAssets: ['favicon.svg', 'apple-touch-icon.png'],
      manifest: {
        name: 'ToolShop CRM',
        short_name: 'ToolShop',
        description: 'CRM for a tool and construction store: POS, inventory, orders and finance.',
        lang: 'tg',
        start_url: '/',
        scope: '/',
        id: '/',
        display: 'standalone',
        orientation: 'any',
        theme_color: '#0f172a',
        background_color: '#f8fafc',
        icons: [
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: 'pwa-maskable-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
        shortcuts: [
          { name: 'POS', url: '/pos', icons: [{ src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' }] },
          { name: 'Orders', url: '/orders', icons: [{ src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' }] },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
        cleanupOutdatedCaches: true,
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/health\//],
        runtimeCaching: [], // deliberately empty: nothing from the API is ever cached
        // A new worker is only activated when the user taps "Reload" (skipWaiting is message-driven in
        // "prompt" mode). clientsClaim lets that activation take control of a page that was opened
        // before the worker existed (first install), so the reload that follows really loads the new version.
        clientsClaim: true,
        skipWaiting: false,
      },
      devOptions: { enabled: false },
    }),
  ],
  // Only used by "vite preview" (local testing, e.g. from a phone through an HTTPS tunnel). It does not change the build.
  // One origin for the app AND the API: build with VITE_API_URL=/api and /api is forwarded to the local backend,
  // so a single tunnel is enough and no CORS setting is needed. Real deployments use a reverse proxy instead.
  preview: {
    allowedHosts: ['localhost', '.trycloudflare.com', '.ngrok-free.app', '.ngrok-free.dev', '.loca.lt'],
    proxy: localApiProxy,
  },
  // Dev server: listens on the network ONLY in "lan" mode (and only when the certificate exists).
  server: mode === 'lan'
    ? { host: true, port: 5173, strictPort: true, https: lanHttps, proxy: localApiProxy }
    : mode === 'lanhttp'
      ? { host: true, port: 5173, strictPort: true, proxy: localApiProxy, allowedHosts: ['.trycloudflare.com'] }
      : {},
  }
})
