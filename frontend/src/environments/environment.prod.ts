// Configuración del entorno de producción.
// Se sirve estático desde el mismo origen (reverse proxy) o se configura con
// apiUrl si el backend está en otro host.
export const environment = {
  production: true,
  apiUrl: '/api',
};
