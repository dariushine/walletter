// Proxy de desarrollo: /api → backend .NET.
// En dev local (ng serve) apunta a http://localhost:3002.
// En dev docker (docker-compose.dev.yml) se sobreescribe con API_UPSTREAM=http://backend:3002.
const upstream = process.env.API_UPSTREAM || 'http://localhost:3002';

module.exports = [
  {
    context: ['/api'],
    target: upstream,
    secure: false,
    changeOrigin: true,
  },
];
