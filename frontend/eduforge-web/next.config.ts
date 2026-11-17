import type { NextConfig } from "next";
const config: NextConfig = {
  experimental: { proxyTimeout: 240000, proxyClientMaxBodySize: "16mb" },
  async rewrites() {
    const backend = process.env.API_INTERNAL_URL || "http://localhost:5080";
    return [{ source: "/api/:path*", destination: backend + "/api/:path*" }];
  },
};
export default config;
