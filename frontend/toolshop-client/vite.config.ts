import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

// PWA: the service worker precaches ONLY the static app shell (built JS/CSS/HTML/icons).
// It has no runtime caching rules at all, so API responses (orders, payments, finance,
// reports, tokens) are never stored by the service worker and always come from the network.
// Build label shown in the sidebar and used to tell versions apart when checking updates.
// CI / release scripts can set APP_BUILD_ID; otherwise it is the build time (UTC, minutes).
const APP_BUILD = process.env.APP_BUILD_ID ?? new Date().toISOString().slice(0, 16).replace('T', ' ')

export default defineConfig({
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
    proxy: {
      '/api': { target: 'http://localhost:5252', changeOrigin: false },
      '/health': { target: 'http://localhost:5252', changeOrigin: false },
    },
  },
})
