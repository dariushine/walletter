# 🧾 Walletter — Tu sistema de finanzas personales

Walletter es una aplicación web para llevar el control de tu **dinero** de forma simple:

- 💰 **Billeteras**: guarda tu dinero en distintas "carpetas" (efectivo, banco, dólares, bolívares…).
- 🧾 **Transacciones**: anota cada ingreso y gasto.
- 🔁 **Exchanges**: cambia de una moneda a otra (ej. VES → USD) con su comisión.
- 📈 **Tasas**: consulta la tasa del día (BCV y paralelo).
- ⏰ **Pagos recurrentes**: programa gastos que se repiten cada mes.
- 📊 **Reportes**: mira cómo se mueve tu dinero por períodos y categorías.

Tiene dos partes que trabajan juntas:

| Parte | ¿Qué es? | Tecnología |
|-------|----------|------------|
| **Backend** (`backend/`) | El "cerebro": guarda tus datos y expone la API | ASP.NET Core (.NET 10) + EF Core |
| **Frontend** (`frontend/`) | La pantalla bonita que ves en el navegador | Angular 22 + Material |

> 🧑‍🦱 **¿No eres programador?** No pasa nada: la forma más fácil de usarlo es con **Docker** (paso 1 abajo). Solo necesitas instalar Docker y copiar/pegar unos comandos. El resto lo hace solo.

---

## 🚀 Cómo correrlo desde cero

> ⚠️ **Requisito:** necesitas **Docker** instalado.
> - Windows/macOS: instala [Docker Desktop](https://www.docker.com/products/docker-desktop/).
> - Linux: instala `docker` y `docker compose` (docker compose plugin).
>
> Verifica con: `docker --version` y `docker compose version` (debe responder sin error).

### Paso 1 — Clona el proyecto

Abre una terminal y escribe:

```bash
git clone https://github.com/dariushine/walletter.git
cd walletter
```

### Paso 2 — Configura (opcional, pero recomendado)

Copia el archivo de ejemplo y ajústalo si quieres poner contraseña:

```bash
cp .env.example .env
```

- Si dejas `AUTH_USERNAME` y `AUTH_PASSWORD` **vacías**, la app queda **abierta** (cualquiera en tu red puede entrar). Para uso real, ponle un usuario y una contraseña.
- Si activas la autenticación, debes poner un `JWT_SECRET` largo (más de 32 caracteres), por ejemplo una frase aleatoria.

### Paso 3 — Levántalo

```bash
docker compose up -d --build
```

Este primer arranque tarda unos minutos (descarga y compila todo). Cuando termine:

- 🌐 **Frontend (la app):** abre tu navegador en **http://localhost:19443**
- 🔌 **API (para técnicos):** **http://localhost:3002/swagger**

### Paso 4 — Úsalo

Abre el navegador en `http://localhost:19443`, crea tus billeteras y empieza a anotar tus gastos. Al primer arranque, Walletter crea la base de datos (archivo `data/walletter.db`) y deja listas las categorías básicas.

### Paso 5 — Apagarlo / verlo

```bash
docker compose logs -f     # ver lo que pasa en vivo (Ctrl+C para salir)
docker compose down        # detener (sin borrar tus datos)
docker compose up -d       # volver a encender
```

Tus datos se guardan en un volumen de Docker, así que **no se pierden** al apagar.

---

## 🧑‍💻 Para desarrolladores (sin Docker)

### Requisitos

- **.NET 10 SDK** → https://dotnet.microsoft.com/download
- **Node.js 22** (para el frontend) → https://nodejs.org

### Levantar el backend

```bash
cd backend
dotnet restore
dotnet run --project src/Walletter.Api
# API en http://localhost:3002  (Swagger en /swagger)
```

### Levantar el frontend (en otra terminal)

```bash
cd frontend
npm install
npm start
# App en http://localhost:4200  (proxy /api → http://localhost:3002)
```

---

## 🐳 Desarrollo con hot reload (Docker + bind mounts)

Si vas a programar y quieres que los cambios se reflejen al instante (recarga automática):

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up
```

- Backend con `dotnet watch`, frontend con `ng serve` (hot reload en ambos).
- Frontend en **http://localhost:3000**, API en **http://localhost:3002**.
- No uses este modo en producción.

---

## ⚙️ Configuración (variables de entorno)

| Variable | ¿Qué hace? | Default |
|----------|-----------|---------|
| `PORT` | Puerto de la API | `3002` |
| `AUTH_USERNAME` / `AUTH_PASSWORD` | Usuario y clave de acceso. Si van vacíos, la app queda abierta | vacío |
| `JWT_SECRET` | Clave secreta (obligatoria si hay auth, mínimo 32 caracteres) | vacío |
| `CONNECTIONSTRINGS__WALLETTERDB` | Dónde se guardan los datos (SQLite por defecto) | `Data Source=data/walletter.db` |

Todas se pueden poner en el archivo `.env`.

---

## 🧰 Funciones paso a paso (para el usuario)

1. **Crear una billetera** → En la app, "Nueva billetera": ponle nombre, moneda (USD, VES, etc.) y un monto inicial.
2. **Anotar un gasto o ingreso** → "Nueva transacción": elige la billetera, el tipo (ingreso/gasto), el monto y la categoría.
3. **Cambiar de moneda** → "Exchange": de qué billetera a cuál, montos y comisión. Se hace en un solo paso (es atómico).
4. **Pago recurrente** → Configura un gasto mensual (ej. internet) y ejecútalo cuando toque.
5. **Ver tu balance y reportes** → La pantalla principal y las secciones de stats/reportes muestran el resumen.

---

## ❓ Preguntas frecuentes

**¿Mis datos se guardan en la nube?**
No. Todo vive en el archivo/volumen de tu propia máquina. No dependes de ningún servicio externo.

**¿Puedo usar PostgreSQL o MySQL?**
Sí, la base de datos es intercambiable (SQLite por defecto). Requiere cambiar el proveedor en `backend/src/Walletter.Infrastructure/DependencyInjection.cs` y el connection string.

**¿Cómo conecto a mi agente de OpenClaw?**
Instala el plugin desde [dariushine/walletter-openclaw](https://github.com/dariushine/walletter-openclaw). En su `baseUrl` usa tu URL terminada en `/api` (ej. `http://localhost:19443/api`) y un API token generado en Walletter.

---

## 🧑‍💼 Sobre el proyecto

- **Backend:** API REST en ASP.NET Core (.NET 10) + EF Core, con Clean Architecture (`Domain` → `Application` → `Infrastructure` → `Api`).
- **Reglas de negocio importantes:**
  - Los montos se guardan como **centavos (×100)** y las tasas **×10000** para evitar errores de decimales.
  - Fechas en **UTC**; la interfaz muestra la zona horaria del usuario.
  - Borrado "suave" (soft delete): nada se pierde de verdad.
  - Transacciones con comisión y exchanges se procesan **de forma atómica** (si algo falla, no queda a medias).

### Estructura del monorepo

```
walletter/
├── backend/                     # API .NET
│   └── src/
│       ├── Walletter.Domain/        # Entidades y reglas puras
│       ├── Walletter.Application/   # Lógica de negocio
│       ├── Walletter.Infrastructure/# EF Core, base de datos, tokens
│       └── Walletter.Api/           # Controladores REST, auth, Program.cs
├── frontend/                    # App Angular
├── docker-compose.yml           # Orquestación (front + back)
├── docker-compose.dev.yml       # Modo desarrollo hot reload
├── Dockerfile                   # Build del backend
└── .env.example                 # Plantilla de configuración
```

---

¿Problemas o dudas? Abre un *issue* en este repositorio.
