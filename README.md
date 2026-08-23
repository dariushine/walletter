# Walletter

Sistema de finanzas personales. Monorepo:

- **`backend/`** — API REST en **.NET 10 + ASP.NET Core + Entity Framework Core** (Clean Architecture).
- **`frontend/`** — frontend **Angular** (pendiente, se construye en un sprint posterior).

> El backend preserva la **lógica de negocio** del sistema financiero previo
> (rework en NestJS/Prisma), pero implementado desde cero con entidades,
> arquitectura, rutas, servicios y base de datos propios del ecosistema .NET.

---

## Stack

| Capa | Tecnología |
|------|-----------|
| API | ASP.NET Core (Minimal hosting + Controllers) |
| Lógica de negocio | Services en `Walletter.Application` |
| Dominio | Entidades puras en `Walletter.Domain` |
| Datos | Entity Framework Core en `Walletter.Infrastructure` (provider SQLite por defecto, DB-agnóstico) |
| Auth | JWT (access token) + cookies httpOnly + API tokens |
| Base de datos | SQLite (archivo), intercambiable a PostgreSQL/MySQL |

## Arquitectura (Clean Architecture)

```
backend/
├── Walletter.slnx
└── src/
    ├── Walletter.Domain/          # Entidades + reglas puras (Money, TimeZone, tipos)
    ├── Walletter.Application/     # Commands/DTOs + Servicios de negocio
    ├── Walletter.Infrastructure/  # EF Core (DbContext, migraciones), token service, seed
    └── Walletter.Api/             # Controladores REST, auth, middleware, Program.cs
```

**Dependencias:** `Api → Infrastructure → Application → Domain`. El dominio y la
aplicación **no conocen la base de datos**; solo la capa de Infraestructura sabe
de EF Core. Cambiar de SQLite a otro motor = cambiar el provider en
`Walletter.Infrastructure/DependencyInjection.cs` y el connection string.

## Reglas de negocio

- **Montos** como enteros de **centavos (×100)** para evitar errores de float.
- **Tasas** como enteros **×10000**.
- **Fechas**: un único instante **UTC** (`datetime_utc`); el front proyecta a la
  zona horaria del usuario.
- **Soft-delete** en transacciones, billeteras, categorías y exchanges.
- **Categorías de sistema** (`fee`, `exchange_out`, `exchange_in`) protegidas.
- **Transacciones con comisión (fee)** atómicas: padre + fee hijo + balance en un
  solo guardado; el `fee` del padre se denormaliza desde sus hijos.
- **Exchanges** atómicos: débito + crédito (+ comisiones) + registro + balances.

## Requisitos

- .NET 10 SDK (https://dotnet.microsoft.com/download)

## Ejecutar en desarrollo

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project src/Walletter.Api
# API en http://localhost:3002  (Swagger en /swagger)
```

Al arrancar, aplica migraciones y siembra categorías de sistema + zona horaria.

## Configuración (variables de entorno)

| Variable | Descripción |
|----------|-------------|
| `PORT` | Puerto de la API (default `3002`) |
| `CONNECTIONSTRINGS__WALLETTERDB` | Connection string (default `Data Source=data/walletter.db`) |
| `AUTH_USERNAME` / `AUTH_PASSWORD` | Si se definen ambas, activan auth JWT; si no, API abierta |
| `JWT_SECRET` | **Requerido** si la auth está activa (mínimo 32 chars) |

Ver `.env.example`.

## Docker

```bash
docker compose up -d
# API en http://localhost:3002
```

## Endpoints (todos bajo `/api`)

### Auth
| Método | Ruta |
|--------|------|
| GET | `/api/auth/status` |
| GET | `/api/auth/session` |
| POST | `/api/auth/login` |
| POST | `/api/auth/logout` |
| POST | `/api/auth/refresh` |
| GET / DELETE | `/api/auth/sessions`, `/api/auth/sessions/{jti}` |
| GET / POST / DELETE | `/api/auth/tokens`, `/api/auth/tokens`, `/api/auth/tokens/{id}` |

### Recursos
| Método | Ruta |
|--------|------|
| GET/POST | `/api/wallets` |
| GET/PUT/DELETE | `/api/wallets/{id}` |
| PUT | `/api/wallets/{id}/reactivate` |
| GET | `/api/wallets/{id}/report` |
| GET/POST | `/api/transactions` |
| GET/PUT/DELETE | `/api/transactions/{id}` |
| POST | `/api/transactions/{id}/fee`, `/api/transactions/{id}/associate` |
| GET/POST | `/api/exchanges` |
| GET/PUT/DELETE | `/api/exchanges/{id}` |
| GET/POST | `/api/categories` |
| PUT/DELETE | `/api/categories/{id}`, `/api/categories/{id}/reactivate` |
| GET/POST | `/api/recurring-payments` |
| POST | `/api/recurring-payments/{id}/execute` |
| GET/POST/PUT/DELETE | `/api/daily-rates` |
| GET | `/api/rates/effective` |
| GET | `/api/settings`, PUT `/api/settings/user_timezone` |
| GET | `/api/stats`, `/api/stats/by-category` |
| GET | `/api/health` |

## Autenticación

- **Access token JWT** vía `Authorization: Bearer <jwt>` o cookie `access_token`.
- **API token** (para el plugin/agente) vía `X-Api-Key` o `Authorization: Bearer <token>`.
- Con auth deshabilitada, todos los endpoints son accesibles.
- Para crear un API token necesitas login previo: `POST /api/auth/tokens`.

## Pruebas

El backend se probó manualmente cubriendo: wallets (CRUD + soft-delete + reactivar +
report), transacciones (CRUD, fee inline, addFee, associate, fondos insuficientes),
exchanges (crear, actualizar con deltas, borrar), categorías (sistema protegidas),
recurrentes (crear, ejecutar, actualizar, borrar), tasas (upsert, effective, today),
stats, settings y el flujo completo de auth (login, refresh, API token, bad credentials).

> Nota: las pruebas unitarias/xUnit aún no están. Se añaden en un sprint posterior.
