@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo.
echo   ___                   ____                 __        ___ _    _ 
echo  / _ \ _ __   ___ _ __ ^|  _ \  ___  ___ _ __ \ \      / (_) ^| _(_)
echo ^| ^| ^| ^| '_ \ / _ \ '_ \^| ^| ^| ^|/ _ \/ _ \ '_ \ \ \ /\ / /^| ^| ^|/ / ^|
echo ^| ^|_^| ^| ^|_) ^|  __/ ^| ^| ^| ^|_^| ^|  __/  __/ ^|_) ^| \ V  V / ^| ^|   ^<^| ^|
echo  \___/^| .__/ \___^|_^| ^|_^|____/ \___^|\___^| .__/   \_/\_/  ^|_^|_^|\_\_^|
echo       ^|_^|                              ^|_^|                        
echo.
echo ============================================
echo            Windows Publish Script
echo ============================================
echo.

set "PUBLISH_DIR=publish"
set "BACKEND_DIR=%PUBLISH_DIR%\backend"
set "FRONTEND_DIR=%PUBLISH_DIR%\frontend"

:: 清理旧的发布目录
if exist "%PUBLISH_DIR%" (
    echo 清理旧的发布目录...
    rmdir /s /q "%PUBLISH_DIR%"
)

:: 创建发布目录
echo 创建发布目录...
mkdir "%BACKEND_DIR%"
mkdir "%FRONTEND_DIR%"

:: ============================================
:: 构建后端
:: ============================================
echo.
echo [1/2] 构建后端项目...
echo --------------------------------------------

dotnet publish src/OpenDeepWiki/OpenDeepWiki.csproj -c Release -o "%BACKEND_DIR%" -p:PublishSingleFile=true --self-contained true -r win-x64

if %ERRORLEVEL% neq 0 (
    echo 后端构建失败！
    exit /b 1
)

:: 复制 Prompts 目录
if exist "src\OpenDeepWiki\Prompts" (
    echo 复制 Prompts 目录...
    xcopy /E /I /Y "src\OpenDeepWiki\Prompts" "%BACKEND_DIR%\Prompts"
)

:: 创建后端 .env 示例文件
echo 创建后端 .env 示例文件...
(
echo # OpenDeepWiki 后端环境变量配置
echo # 复制此文件为 .env 并修改配置
echo.
echo # 服务监听地址
echo URLS=http://localhost:8080
echo.
echo # 数据库配置 ^(支持: sqlite, postgresql^)
echo DB_TYPE=sqlite
echo CONNECTION_STRING=Data Source=./opendeepwiki.db
echo.
echo # 仓库存储目录
echo REPOSITORIES_DIRECTORY=./repositories
echo.
echo # AI 服务配置 ^(全局默认，用于 Chat 等功能^)
echo CHAT_API_KEY=your-api-key-here
echo ENDPOINT=https://api.openai.com/v1
echo CHAT_REQUEST_TYPE=OpenAI
echo.
echo # Wiki 生成器配置 - 目录生成
echo WIKI_CATALOG_MODEL=gpt-4o
echo WIKI_CATALOG_ENDPOINT=https://api.openai.com/v1
echo WIKI_CATALOG_API_KEY=your-catalog-api-key
echo WIKI_CATALOG_REQUEST_TYPE=OpenAI
echo.
echo # Wiki 生成器配置 - 内容生成
echo WIKI_CONTENT_MODEL=gpt-4o
echo WIKI_CONTENT_ENDPOINT=https://api.openai.com/v1
echo WIKI_CONTENT_API_KEY=your-content-api-key
echo WIKI_CONTENT_REQUEST_TYPE=OpenAI
echo.
echo # Wiki 生成器配置 - 翻译 ^(可选，不配置则使用内容生成配置^)
echo # WIKI_TRANSLATION_MODEL=gpt-4o
echo # WIKI_TRANSLATION_ENDPOINT=https://api.openai.com/v1
echo # WIKI_TRANSLATION_API_KEY=your-translation-api-key
echo # WIKI_TRANSLATION_REQUEST_TYPE=OpenAI
echo.
echo # Wiki 生成并行数 ^(默认: 5^)
echo WIKI_PARALLEL_COUNT=5
echo.
echo # 多语言支持 ^(逗号分隔，如: en,zh,ja,ko,es,fr,de,pt-br^)
echo WIKI_LANGUAGES=en,zh,zh-tw,ja,ko,es,fr,de,pt-br,pl,ru,ar
echo.
echo # JWT 配置（生产环境请修改）
echo JWT_SECRET_KEY=REPLACE_WITH_RANDOM_SECRET_AT_LEAST_32_CHARS
) > "%BACKEND_DIR%\.env.example"

echo 后端构建完成！

:: ============================================
:: 构建前端
:: ============================================
echo.
echo [2/2] 构建前端项目...
echo --------------------------------------------

cd web

:: 安装依赖
if not exist "node_modules" (
    echo 安装前端依赖...
    call npm install
    if !ERRORLEVEL! neq 0 (
        echo 前端依赖安装失败！
        cd ..
        exit /b 1
    )
)

:: 构建前端（不需要 API_PROXY_URL，运行时动态获取）
echo 构建前端项目...
call npm run build

if %ERRORLEVEL% neq 0 (
    echo 前端构建失败！
    cd ..
    exit /b 1
)

cd ..

:: 复制前端 standalone 构建产物
echo 复制前端构建产物...
xcopy /E /I /Y "web\.next\standalone\*" "%FRONTEND_DIR%\"
xcopy /E /I /Y "web\.next\static" "%FRONTEND_DIR%\.next\static"
xcopy /E /I /Y "web\public" "%FRONTEND_DIR%\public"

:: 创建前端 .env 示例文件
echo 创建前端 .env 示例文件...
(
echo # OpenDeepWiki 前端环境变量配置
echo # 启动前端服务时设置此环境变量
echo.
echo # 后端 API 代理地址（运行时动态读取）
echo API_PROXY_URL=http://localhost:8080
echo.
echo # 公开站点地址（用于 sitemap、robots.txt 和 SEO 元数据）
echo SITE_URL=http://localhost:3000
) > "%FRONTEND_DIR%\.env.example"

echo 前端构建完成！

:: ============================================
:: 创建启动脚本
:: ============================================
echo.
echo 创建启动脚本...

:: 后端启动脚本
(
echo @echo off
echo chcp 65001 ^>nul
echo echo 启动 OpenDeepWiki 后端服务...
echo.
echo :: 加载 .env 文件中的环境变量（如果存在）
echo if exist ".env" ^(
echo     echo 加载 .env 配置文件...
echo     for /f "usebackq tokens=1,* delims==" %%%%a in ^(".env"^) do ^(
echo         if not "%%%%a"=="" if not "%%%%a:~0,1%%"=="#" ^(
echo             set "%%%%a=%%%%b"
echo         ^)
echo     ^)
echo ^)
echo.
echo :: 设置默认值（如果 .env 中未设置）
echo if not defined URLS set URLS=http://localhost:8080
echo.
echo echo 后端服务地址: %%URLS%%
echo echo.
echo.
echo OpenDeepWiki.exe
echo.
echo pause
) > "%BACKEND_DIR%\start-backend.bat"

:: 前端启动脚本
(
echo @echo off
echo chcp 65001 ^>nul
echo echo 启动 OpenDeepWiki 前端服务...
echo.
echo :: 设置端口
echo set PORT=3000
echo set HOSTNAME=0.0.0.0
echo.
echo if exist ".env" ^(
echo     for /f "usebackq tokens=1,* delims==" %%%%a in ^(".env"^) do ^(
echo         if not "%%%%a"=="" if not "%%%%a:~0,1%%"=="#" ^(
echo             set "%%%%a=%%%%b"
echo         ^)
echo     ^)
echo ^)
echo if exist ".env.local" ^(
echo     for /f "usebackq tokens=1,* delims==" %%%%a in ^(".env.local"^) do ^(
echo         if not "%%%%a"=="" if not "%%%%a:~0,1%%"=="#" ^(
echo             set "%%%%a=%%%%b"
echo         ^)
echo     ^)
echo ^)
echo.
echo echo 前端服务地址: http://localhost:%%PORT%%
echo echo.
echo.
echo node server.js
echo.
echo pause
) > "%FRONTEND_DIR%\start-frontend.bat"

:: 总体启动脚本
(
echo @echo off
echo chcp 65001 ^>nul
echo setlocal
echo.
echo echo ============================================
echo echo   OpenDeepWiki 服务启动
echo echo ============================================
echo echo.
echo.
echo :: 获取脚本所在目录
echo set "SCRIPT_DIR=%%~dp0"
echo.
echo :: 启动后端服务（新窗口）
echo echo 启动后端服务...
echo start "OpenDeepWiki Backend" cmd /c "cd /d \"%%SCRIPT_DIR%%backend\" ^&^& start-backend.bat"
echo.
echo :: 等待后端启动
echo echo 等待后端服务启动...
echo timeout /t 5 /nobreak ^>nul
echo.
echo :: 启动前端服务（新窗口）
echo echo 启动前端服务...
echo start "OpenDeepWiki Frontend" cmd /c "cd /d \"%%SCRIPT_DIR%%frontend\" ^&^& start-frontend.bat"
echo.
echo echo.
echo echo ============================================
echo echo   服务已启动
echo echo   后端: http://localhost:8080
echo echo   前端: http://localhost:3000
echo echo ============================================
echo echo.
echo echo 按任意键关闭此窗口（服务将继续运行）...
echo pause ^>nul
) > "%PUBLISH_DIR%\start-all.bat"

:: ============================================
:: 完成
:: ============================================
echo.
echo ============================================
echo   发布完成！
echo ============================================
echo.
echo 发布目录结构:
echo   %PUBLISH_DIR%\
echo   ├── backend\          后端服务
echo   │   ├── .env.example  环境变量示例
echo   │   └── start-backend.bat
echo   ├── frontend\         前端服务
echo   │   ├── .env.example  环境变量示例
echo   │   └── start-frontend.bat
echo   └── start-all.bat     一键启动脚本
echo.
echo 使用说明:
echo   1. 复制 backend\.env.example 为 backend\.env 并配置
echo   2. 复制 frontend\.env.example 为 frontend\.env.local 并配置
echo   3. 运行 start-all.bat 启动所有服务
echo.

pause
