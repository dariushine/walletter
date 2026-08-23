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
