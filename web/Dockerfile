# 依赖安装阶段
FROM node:22-bookworm AS deps
WORKDIR /app

# 安装依赖
COPY package.json package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm install --prefer-offline; else npm install; fi

# 构建阶段
FROM node:22-bookworm AS builder
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY . .

# 构建时不需要 API_PROXY_URL，运行时通过环境变量动态获取
RUN npm run build

# 生产运行阶段
FROM node:22-bookworm AS runner
WORKDIR /app

ENV NODE_ENV=production

# 创建非 root 用户
RUN groupadd --system --gid 1001 nodejs && \
    useradd --system --uid 1001 -g nodejs nextjs

# 复制构建产物
COPY --from=builder /app/public ./public
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static

# 不复制 .env 文件，API_PROXY_URL 通过运行时环境变量传入

USER nextjs

EXPOSE 3000

ENV PORT=3000
ENV HOSTNAME="0.0.0.0"

CMD ["node", "server.js"]
