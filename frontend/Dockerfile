# Frontend Angular — Walletter
# Multi-stage: compila la app y la sirve con Nginx (reverse proxy /api → backend).

# --- Stage 1: build ---
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
# Instala dependencias incluyendo dev (Angular CLI / build tooling).
RUN npm install --include=dev --no-audit --no-fund

COPY . .
RUN npm run build -- --configuration production

# --- Stage 2: runtime (Nginx) ---
FROM nginx:1.27-alpine
COPY --from=build /app/dist/walletter-app/browser /usr/share/nginx/html

# Proxy /api hacia el backend (mismo origen, evita CORS y cookies cross-site).
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]

# --- Stage 3: dev (hot reload con bind mounts) ---
# Requiere node (Angular dev server); el compose dev monta ./frontend:/app/frontend.
FROM node:22-alpine AS dev
WORKDIR /app/frontend

# Instala dependencias en la imagen para que el bind conserve el node_modules
# del contenedor (evita dependencias nativas del host).
COPY package.json package-lock.json ./
RUN npm install --include=dev --no-audit --no-fund

EXPOSE 4200
ENV NODE_ENV=development

CMD ["npx", "ng", "serve", "--host", "0.0.0.0", "--port", "4200"]
