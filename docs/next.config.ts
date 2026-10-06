import { createMDX } from 'fumadocs-mdx/next';

const config = {
  reactStrictMode: true,
  output: 'standalone' as const,
};

const withMDX = createMDX({
  // customise the config file path
  // configPath: "source.config.ts"
});

export default withMDX(config);