// Configuración del entorno de desarrollo.
// La API se expone a través del reverse proxy del servidor Angular (proxy.conf.json)
// para evitar problemas de CORS y cookies en desarrollo.
export const environment = {
  production: false,
  apiUrl: '/api',
};
