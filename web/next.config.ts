import type { NextConfig } from "next";

const apiOrigin = process.env.SMARTINBOX_API_ORIGIN ?? "http://127.0.0.1:5168";

const nextConfig: NextConfig = {
  turbopack: { root: process.cwd() },
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiOrigin}/api/:path*` }];
  },
};

export default nextConfig;
