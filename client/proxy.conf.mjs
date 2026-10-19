const target = process.env['API_PROXY_TARGET'] ?? 'http://localhost:5080';

export default {
  '/api': {
    target,
    secure: false,
    changeOrigin: true,
  },
};
