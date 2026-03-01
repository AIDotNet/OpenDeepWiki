# 检测是否支持 docker compose
DOCKER_COMPOSE := $(shell if docker compose version >/dev/null 2>&1; then echo "docker compose"; else echo "docker-compose"; fi)

# 检测 Node.js 和 npm
NODE_EXISTS := $(shell if node --version >/dev/null 2>&1; then echo "true"; else echo "false"; fi)
NPM_EXISTS := $(shell if npm --version >/dev/null 2>&1; then echo "true"; else echo "false"; fi)

# 检测 .NET CLI
DOTNET_EXISTS := $(shell if dotnet --version >/dev/null 2>&1; then echo "true"; else echo "false"; fi)

.PHONY: all build build-backend build-frontend build-docs build-arm build-amd build-backend-arm build-backend-amd up down restart dev dev-backend dev-web logs clean help test test-backend test-frontend lint lint-frontend format format-backend install install-frontend install-backend check-deps

all: build up

# 构建所有Docker镜像
build: build-frontend
	$(DOCKER_COMPOSE) build

# 只构建后端服务
build-backend:
	$(DOCKER_COMPOSE) build opendeepwiki

# 构建前端项目
build-frontend:
	@echo "Building frontend..."
	@if [ "$(NODE_EXISTS)" = "false" ] || [ "$(NPM_EXISTS)" = "false" ]; then \
		echo "错误: Node.js 或 npm 未安装，请先安装 Node.js"; \
		exit 1; \
	fi
	cd web && npm install && npm run build
	@echo "Frontend build completed!"

# 构建文档站
build-docs:
	@echo "Building docs..."
	@if [ "$(NODE_EXISTS)" = "false" ] || [ "$(NPM_EXISTS)" = "false" ]; then \
		echo "错误: Node.js 或 npm 未安装，请先安装 Node.js"; \
		exit 1; \
	fi
	cd docs && npm install && npm run build
	@echo "Docs build completed!"

# 构建ARM架构的所有Docker镜像
build-arm:
	$(DOCKER_COMPOSE) build --build-arg ARCH=arm64

# 构建AMD架构的所有Docker镜像
build-amd:
	$(DOCKER_COMPOSE) build --build-arg ARCH=amd64

# 构建ARM架构的后端服务
build-backend-arm:
	$(DOCKER_COMPOSE) build --build-arg ARCH=arm64 opendeepwiki

# 构建AMD架构的后端服务
build-backend-amd:
	$(DOCKER_COMPOSE) build --build-arg ARCH=amd64 opendeepwiki

# 启动所有服务
up:
	$(DOCKER_COMPOSE) up -d

# 停止所有服务
down:
	$(DOCKER_COMPOSE) down

# 重启所有服务
restart: down up

# 启动开发环境（非后台模式，可以看到日志输出）
dev:
	$(DOCKER_COMPOSE) up

# 只启动后端开发环境
dev-backend:
	$(DOCKER_COMPOSE) up opendeepwiki

# 只启动前端开发环境
dev-web:
	$(DOCKER_COMPOSE) up web

# 查看服务日志
logs:
	$(DOCKER_COMPOSE) logs -f

# 清理所有Docker资源（慎用）
clean:
	$(DOCKER_COMPOSE) down --rmi all --volumes --remove-orphans
	docker system prune -f

# 安装依赖
install: install-frontend install-backend

# 安装前端依赖
install-frontend:
	@echo "Installing frontend dependencies..."
	@if [ "$(NODE_EXISTS)" = "false" ] || [ "$(NPM_EXISTS)" = "false" ]; then \
		echo "错误: Node.js 或 npm 未安装，请先安装 Node.js"; \
		exit 1; \
	fi
	cd web && npm install
	cd docs && npm install
	@echo "Frontend dependencies installed!"

# 安装后端依赖
install-backend:
	@echo "Installing backend dependencies..."
	@if [ "$(DOTNET_EXISTS)" = "false" ]; then \
		echo "错误: .NET CLI 未安装，请先安装 .NET SDK"; \
		exit 1; \
	fi
	dotnet restore OpenDeepWiki.sln
	@echo "Backend dependencies restored!"

# 运行测试
test: test-backend test-frontend

# 运行后端测试
test-backend:
	@echo "Running backend tests..."
	@if [ "$(DOTNET_EXISTS)" = "false" ]; then \
		echo "错误: .NET CLI 未安装，请先安装 .NET SDK"; \
		exit 1; \
	fi
	dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --logger "console;verbosity=detailed"

# 运行前端测试
test-frontend:
	@echo "Running frontend tests..."
	@if [ "$(NODE_EXISTS)" = "false" ] || [ "$(NPM_EXISTS)" = "false" ]; then \
		echo "错误: Node.js 或 npm 未安装，请先安装 Node.js"; \
		exit 1; \
	fi
	cd web && npm test

# 代码检查
lint: lint-frontend

# 前端代码检查
lint-frontend:
	@echo "Running frontend linting..."
	@if [ "$(NODE_EXISTS)" = "false" ] || [ "$(NPM_EXISTS)" = "false" ]; then \
		echo "错误: Node.js 或 npm 未安装，请先安装 Node.js"; \
		exit 1; \
	fi
	cd web && npm run lint
	cd docs && npm run lint

# 代码格式化
format: format-backend

# 后端代码格式化
format-backend:
	@echo "Formatting backend code..."
	@if [ "$(DOTNET_EXISTS)" = "false" ]; then \
		echo "错误: .NET CLI 未安装，请先安装 .NET SDK"; \
		exit 1; \
	fi
	dotnet format OpenDeepWiki.sln

# 检查依赖
check-deps:
	@echo "Checking dependencies..."
	@echo "Node.js: $$(node --version 2>/dev/null || echo 'Not installed')"
	@echo "npm: $$(npm --version 2>/dev/null || echo 'Not installed')"
	@echo ".NET: $$(dotnet --version 2>/dev/null || echo 'Not installed')"
	@echo "Docker: $$(docker --version 2>/dev/null || echo 'Not installed')"
	@echo "Docker Compose: $$($(DOCKER_COMPOSE) version 2>/dev/null || echo 'Not available')"

# 显示帮助信息
help:
	@echo "使用方法:"
	@echo "  make build              - 构建所有Docker镜像"
	@echo "  make build-backend      - 只构建后端服务"
	@echo "  make build-frontend     - 构建前端项目"
	@echo "  make build-docs         - 构建文档站"
	@echo "  make build-arm          - 构建ARM架构的所有镜像"
	@echo "  make build-amd          - 构建AMD架构的所有镜像"
	@echo "  make build-backend-arm  - 构建ARM架构的后端服务"
	@echo "  make build-backend-amd  - 构建AMD架构的后端服务"
	@echo "  make up                 - 启动所有服务（后台模式）"
	@echo "  make down               - 停止所有服务"
	@echo "  make restart            - 重启所有服务"
	@echo "  make dev                - 启动开发环境（非后台模式，可查看日志）"
	@echo "  make dev-backend        - 只启动后端开发环境"
	@echo "  make dev-web            - 只启动前端开发环境"
	@echo "  make logs               - 查看服务日志"
	@echo "  make clean              - 清理所有Docker资源（慎用）"
	@echo "  make install            - 安装所有依赖"
	@echo "  make install-frontend   - 安装前端依赖"
	@echo "  make install-backend    - 安装后端依赖"
	@echo "  make test               - 运行所有测试"
	@echo "  make test-backend       - 运行后端测试"
	@echo "  make test-frontend      - 运行前端测试"
	@echo "  make lint               - 运行代码检查"
	@echo "  make lint-frontend      - 运行前端代码检查"
	@echo "  make format             - 格式化代码"
	@echo "  make format-backend     - 格式化后端代码"
	@echo "  make check-deps         - 检查系统依赖"
	@echo "  make help               - 显示此帮助信息"

# 默认目标
default: help
