# Frontend — Walletter (Angular)

Frontend **Angular 22** (standalone components + signals) del sistema de finanzas
personales Walletter. Consume la API .NET de `../backend`.

## Stack

- **Angular 22** — componentes standalone, signals, lazy-loading de rutas.
- **Angular Material 22** — UI, tema personalizado, responsive.
- **RxJS + Signals** — estado global de zona horaria y autenticación.
- **HTTP interceptor** — cookies `httpOnly`, manejo de errores unificado.

## Estructura

```
frontend/
├── src/
│   ├── environments/          # environment.ts / environment.prod.ts
│   ├── index.html
│   ├── main.ts
│   ├── styles.scss            # tema Material + estilos globales
│   └── app/
│       ├── core/
│       │   ├── guards/         # auth.guard
│       │   ├── interceptors/   # http-interceptor (httpOnly + errores)
│       │   ├── services/       # walletter-api, auth-store, settings-store, notification
│       │   └── utils/          # money, dates
│       ├── models/             # DTOs de la API (.NET)
│       ├── auth/               # Login
│       ├── layout/             # Shell (sidenav responsive)
│       └── features/           # dashboard, wallets, transactions, exchanges,
│                               # categories, recurring, rates, reports, sessions, settings
├── angular.json
├── proxy.conf.js              # proxy dev /api → backend:3002
├── Dockerfile + nginx.conf    # build + serve estático con reverse proxy
└── package.json
```

## Ejecutar en desarrollo

Requisitos: Node 20+ (el repo usa Node 24 / npm 11).

```bash
npm install --include=dev
npm start
# App en http://localhost:4200  (proxy /api → http://localhost:3002)
```

## Consumo de la API

En desarrollo, `proxy.conf.js` reenvía `/api/*` a `http://localhost:3002`.
En Docker, Nginx hace el mismo proxy al servicio `backend:3002`. Las cookies
`httpOnly` de auth viajan en el mismo origen.

## Build de producción

```bash
npm run build
# Salida en dist/walletter-app/browser
```

## Docker

```bash
# Desde la raíz del monorepo (projects/walletter):
docker compose up -d --build
# Frontend en http://localhost:3000, API en http://localhost:3002
```

## Autenticación

El login usa cookies `httpOnly` (`access_token` / `refresh_token`) que gestiona el
backend. El interceptor adjunta `withCredentials`. Si la API corre con auth
deshabilitada (`AUTH_USERNAME`/`AUTH_PASSWORD` vacíos), la app no pide login.
