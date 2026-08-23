// Proxy de desarrollo: /api → backend .NET (http://localhost:3002).
// Evita CORS y permite que las cookies httpOnly viajen en el mismo origen.
module.exports = [
  {
    context: ['/api'],
    target: 'http://localhost:3002',
    secure: false,
    changeOrigin: true,
  },
];
