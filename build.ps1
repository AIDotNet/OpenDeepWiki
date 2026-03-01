param(
    [Parameter(Position=0)]
    [string]$Command = "help"
)

# Check if docker compose is available
$dockerCompose = "docker compose"
try {
    docker-compose version | Out-Null
    $dockerCompose = "docker-compose"
} catch {
    # Try docker compose
}

function Test-Command {
    param($CommandName)
    try { Get-Command $CommandName -ErrorAction Stop | Out-Null; return $true } catch { return $false }
}

function Check-Dependencies {
    Write-Host "Checking dependencies..." -ForegroundColor Green
    
    Write-Host "Node.js: " -NoNewline
    if (Test-Command "node") { node --version } else { Write-Host "Not installed" -ForegroundColor Red }
    
    Write-Host "npm: " -NoNewline
    if (Test-Command "npm") { npm --version } else { Write-Host "Not installed" -ForegroundColor Red }
    
    Write-Host ".NET: " -NoNewline
    if (Test-Command "dotnet") { dotnet --version } else { Write-Host "Not installed" -ForegroundColor Red }
    
    Write-Host "Docker: " -NoNewline
    if (Test-Command "docker") { docker --version } else { Write-Host "Not installed" -ForegroundColor Red }
    
    Write-Host "Docker Compose: " -NoNewline
    try { Invoke-Expression "$dockerCompose version" } catch { Write-Host "Not available" -ForegroundColor Red }
}

function Build-Frontend {
    Write-Host "Building frontend..." -ForegroundColor Green
    
    if (-not (Test-Command "node")) { 
        Write-Host "Error: Node.js not installed" -ForegroundColor Red
        exit 1
    }
    
    if (-not (Test-Command "npm")) { 
        Write-Host "Error: npm not installed" -ForegroundColor Red
        exit 1
    }
    
    Set-Location web
    npm install
    npm run build
    Set-Location ..
    Write-Host "Frontend build completed!" -ForegroundColor Green
}

function Build-Docs {
    Write-Host "Building docs..." -ForegroundColor Green
    
    if (-not (Test-Command "node")) { 
        Write-Host "Error: Node.js not installed" -ForegroundColor Red
        exit 1
    }
    
    if (-not (Test-Command "npm")) { 
        Write-Host "Error: npm not installed" -ForegroundColor Red
        exit 1
    }
    
    Set-Location docs
    npm install
    npm run build
    Set-Location ..
    Write-Host "Docs build completed!" -ForegroundColor Green
}

function Install-Frontend {
    Write-Host "Installing frontend dependencies..." -ForegroundColor Green
    
    if (-not (Test-Command "node")) { 
        Write-Host "Error: Node.js not installed" -ForegroundColor Red
        exit 1
    }
    
    if (-not (Test-Command "npm")) { 
        Write-Host "Error: npm not installed" -ForegroundColor Red
        exit 1
    }
    
    Set-Location web
    npm install
    Set-Location ..
    Set-Location docs
    npm install
    Set-Location ..
    Write-Host "Frontend dependencies installed!" -ForegroundColor Green
}

function Install-Backend {
    Write-Host "Installing backend dependencies..." -ForegroundColor Green
    
    if (-not (Test-Command "dotnet")) { 
        Write-Host "Error: .NET CLI not installed" -ForegroundColor Red
        exit 1
    }
    
    dotnet restore OpenDeepWiki.sln
    Write-Host "Backend dependencies restored!" -ForegroundColor Green
}

function Install-All {
    Install-Frontend
    Install-Backend
}

function Test-Backend {
    Write-Host "Running backend tests..." -ForegroundColor Green
    
    if (-not (Test-Command "dotnet")) { 
        Write-Host "Error: .NET CLI not installed" -ForegroundColor Red
        exit 1
    }
    
    dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --logger "console;verbosity=detailed"
}

function Test-Frontend {
    Write-Host "Running frontend tests..." -ForegroundColor Green
    
    if (-not (Test-Command "node")) { 
        Write-Host "Error: Node.js not installed" -ForegroundColor Red
        exit 1
    }
    
    if (-not (Test-Command "npm")) { 
        Write-Host "Error: npm not installed" -ForegroundColor Red
        exit 1
    }
    
    Set-Location web
    npm test
    Set-Location ..
}

function Test-All {
    Test-Backend
    Test-Frontend
}

function Lint-Frontend {
    Write-Host "Running frontend linting..." -ForegroundColor Green
    
    if (-not (Test-Command "node")) { 
        Write-Host "Error: Node.js not installed" -ForegroundColor Red
        exit 1
    }
    
    if (-not (Test-Command "npm")) { 
        Write-Host "Error: npm not installed" -ForegroundColor Red
        exit 1
    }
    
    Set-Location web
    npm run lint
    Set-Location ..
    Set-Location docs
    npm run lint
    Set-Location ..
}

function Format-Backend {
    Write-Host "Formatting backend code..." -ForegroundColor Green
    
    if (-not (Test-Command "dotnet")) { 
        Write-Host "Error: .NET CLI not installed" -ForegroundColor Red
        exit 1
    }
    
    dotnet format OpenDeepWiki.sln
}

function Docker-Build {
    Write-Host "Building all Docker images..." -ForegroundColor Green
    Build-Frontend
    Invoke-Expression "$dockerCompose build"
}

function Docker-Up {
    Write-Host "Starting all services..." -ForegroundColor Green
    Invoke-Expression "$dockerCompose up -d"
}

function Docker-Down {
    Write-Host "Stopping all services..." -ForegroundColor Green
    Invoke-Expression "$dockerCompose down"
}

function Docker-Restart {
    Docker-Down
    Docker-Up
}

function Docker-Dev {
    Write-Host "Starting development environment..." -ForegroundColor Green
    Invoke-Expression "$dockerCompose up"
}

function Docker-Logs {
    Write-Host "Showing service logs..." -ForegroundColor Green
    Invoke-Expression "$dockerCompose logs -f"
}

function Docker-Clean {
    Write-Host "Cleaning Docker resources..." -ForegroundColor Green
    Invoke-Expression "$dockerCompose down --rmi all --volumes --remove-orphans"
    docker system prune -f
}

function Show-Help {
    Write-Host "Usage:" -ForegroundColor Cyan
    Write-Host "Docker Commands:"
    Write-Host "  .\build.ps1 build              - Build all Docker images"
    Write-Host "  .\build.ps1 up                 - Start all services (detached)"
    Write-Host "  .\build.ps1 down               - Stop all services"
    Write-Host "  .\build.ps1 restart            - Restart all services"
    Write-Host "  .\build.ps1 dev                - Start development environment"
    Write-Host "  .\build.ps1 logs               - Show service logs"
    Write-Host "  .\build.ps1 clean              - Clean Docker resources"
    Write-Host ""
    Write-Host "Build Commands:"
    Write-Host "  .\build.ps1 build-frontend     - Build frontend project"
    Write-Host "  .\build.ps1 build-docs         - Build documentation"
    Write-Host ""
    Write-Host "Install Commands:"
    Write-Host "  .\build.ps1 install            - Install all dependencies"
    Write-Host "  .\build.ps1 install-frontend   - Install frontend dependencies"
    Write-Host "  .\build.ps1 install-backend    - Install backend dependencies"
    Write-Host ""
    Write-Host "Test Commands:"
    Write-Host "  .\build.ps1 test               - Run all tests"
    Write-Host "  .\build.ps1 test-backend       - Run backend tests"
    Write-Host "  .\build.ps1 test-frontend      - Run frontend tests"
    Write-Host ""
    Write-Host "Code Quality:"
    Write-Host "  .\build.ps1 lint               - Run linting"
    Write-Host "  .\build.ps1 format             - Format code"
    Write-Host ""
    Write-Host "Utilities:"
    Write-Host "  .\build.ps1 check-deps         - Check system dependencies"
    Write-Host "  .\build.ps1 help               - Show this help"
}

# Execute command
switch ($Command.ToLower()) {
    "check-deps" { Check-Dependencies }
    "build" { Docker-Build }
    "build-frontend" { Build-Frontend }
    "build-docs" { Build-Docs }
    "up" { Docker-Up }
    "down" { Docker-Down }
    "restart" { Docker-Restart }
    "dev" { Docker-Dev }
    "logs" { Docker-Logs }
    "clean" { Docker-Clean }
    "install" { Install-All }
    "install-frontend" { Install-Frontend }
    "install-backend" { Install-Backend }
    "test" { Test-All }
    "test-backend" { Test-Backend }
    "test-frontend" { Test-Frontend }
    "lint" { Lint-Frontend }
    "format" { Format-Backend }
    "help" { Show-Help }
    default { 
        Write-Host "Unknown command: $Command" -ForegroundColor Red
        Write-Host ""
        Show-Help
        exit 1
    }
}
