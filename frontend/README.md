# Frontend — Angular (en construcción)

Este directorio alojará el frontend **Angular** de Walletter.

> ⚠️ Aún no está implementado. El enfoque actual está en el backend .NET
> (carpeta `../backend`). El frontend se construirá en una sesión/sprint
> posterior.

## Estructura futura (Angular)

```
frontend/
├── src/
│   ├── app/            # módulos, componentes, servicios
│   ├── environments/   # config por entorno
│   └── ...
├── angular.json
└── package.json
```

## Consumo de la API

El frontend consumirá la API en `http://localhost:3002/api` (ver README raíz para
la lista completa de endpoints y auto‑auth).
