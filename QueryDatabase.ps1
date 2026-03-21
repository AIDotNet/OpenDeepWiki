# KoalaWiki 数据库查询工具
Write-Host "KoalaWiki 数据库查询工具" -ForegroundColor Green
Write-Host "=========================" -ForegroundColor Green

# 数据库路径
$dbPath = Join-Path $PSScriptRoot "src\KoalaWiki\KoalaWiki.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "错误: 数据库文件不存在: $dbPath" -ForegroundColor Red
    Write-Host "正在搜索数据库文件..." -ForegroundColor Yellow
    
    # 搜索可能的数据库文件
    $possiblePaths = @(
        ".\KoalaWiki.db",
        ".\src\KoalaWiki\KoalaWiki.db", 
        ".\data\KoalaWiki.db",
        ".\bin\Debug\net9.0\KoalaWiki.db"
    )
    
    foreach ($path in $possiblePaths) {
        $fullPath = Join-Path $PSScriptRoot $path
        if (Test-Path $fullPath) {
            $dbPath = $fullPath
            Write-Host "找到数据库文件: $dbPath" -ForegroundColor Green
            break
        }
    }
    
    if (-not (Test-Path $dbPath)) {
        Write-Host "未找到数据库文件，请手动指定路径" -ForegroundColor Red
        return
    }
}

Write-Host "数据库路径: $dbPath" -ForegroundColor Cyan
Write-Host ""

# 检查SQLite工具是否可用
try {
    $sqliteVersion = sqlite3 --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "SQLite未安装"
    }
    Write-Host "SQLite版本: $sqliteVersion" -ForegroundColor Green
} catch {
    Write-Host "正在下载SQLite工具..." -ForegroundColor Yellow
    
    # 下载SQLite工具
    $sqliteUrl = "https://www.sqlite.org/2024/sqlite-tools-win32-x64-3450000.zip"
    $sqliteZip = "$env:TEMP\sqlite-tools.zip"
    $sqliteDir = "$env:TEMP\sqlite-tools"
    
    try {
        Invoke-WebRequest -Uri $sqliteUrl -OutFile $sqliteZip -UseBasicParsing
        Expand-Archive -Path $sqliteZip -DestinationPath $sqliteDir -Force
        $sqlitePath = Get-ChildItem -Path $sqliteDir -Name "sqlite3.exe" -Recurse | Select-Object -First 1
        $sqliteExe = Join-Path $sqliteDir $sqlitePath
        
        Write-Host "SQLite工具下载完成: $sqliteExe" -ForegroundColor Green
    } catch {
        Write-Host "无法下载SQLite工具，请手动安装SQLite" -ForegroundColor Red
        return
    }
}

# 数据库查询函数
function Invoke-SqlQuery {
    param(
        [string]$Query,
        [string]$DatabasePath = $dbPath
    )
    
    if ($sqliteExe -and (Test-Path $sqliteExe)) {
        & $sqliteExe $DatabasePath $Query 2>$null
    } else {
        sqlite3 $DatabasePath $Query 2>$null
    }
}

# 获取所有表
Write-Host "=== 数据库表列表 ===" -ForegroundColor Yellow
$tables = Invoke-SqlQuery ".tables"
if ($tables) {
    $tableList = $tables -split "\s+" | Where-Object { $_ -ne "" }
    $tableCount = 0
    foreach ($table in $tableList) {
        Write-Host "- $table" -ForegroundColor White
        $tableCount++
    }
    Write-Host "总计: $tableCount 个表" -ForegroundColor Cyan
} else {
    Write-Host "没有找到表或数据库为空" -ForegroundColor Red
}
Write-Host ""

# 查询每个表的统计信息
Write-Host "=== 表统计信息 ===" -ForegroundColor Yellow

# 定义主要业务表
$businessTables = @("Warehouses", "Users", "Documents", "DocumentCatalogs", "TrainingDatasets", "FineTuningTasks", "Roles", "AccessRecords", "UserInRoles", "WarehouseInRoles")

foreach ($table in $businessTables) {
    try {
        # 检查表是否存在
        $exists = Invoke-SqlQuery "SELECT name FROM sqlite_master WHERE type='table' AND name='$table';"
        if (-not $exists) {
            continue
        }
        
        # 获取行数
        $count = Invoke-SqlQuery "SELECT COUNT(*) FROM $table;"
        Write-Host "$table`: $count 行" -ForegroundColor Green
        
        # 获取列信息
        $schema = Invoke-SqlQuery "PRAGMA table_info($table);"
        if ($schema) {
            $columns = $schema -split "\n" | ForEach-Object {
                $parts = $_ -split "\|"
                if ($parts.Count -ge 2) { $parts[1] }
            } | Where-Object { $_ -ne "" }
            Write-Host "  列: $($columns -join ', ')" -ForegroundColor Gray
        }
        
        # 显示前2行数据
        if ([int]$count -gt 0) {
            Write-Host "  示例数据:" -ForegroundColor Gray
            $data = Invoke-SqlQuery "SELECT * FROM $table LIMIT 2;"
            $rows = $data -split "\n" | Where-Object { $_ -ne "" }
            $rowNum = 0
            foreach ($row in $rows) {
                $rowNum++
                Write-Host "    行$rowNum`: $row" -ForegroundColor DarkGray
            }
        }
        Write-Host ""
    } catch {
        Write-Host "$table`: 查询失败 - $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
    }
}

# 执行一些特定的查询
Write-Host "=== 业务数据查询 ===" -ForegroundColor Yellow

# 最近创建的仓库
Write-Host "\n最近创建的5个仓库:" -ForegroundColor Cyan
try {
    $recentWarehouses = Invoke-SqlQuery @"
SELECT Name, OrganizationName, Status, CreatedAt 
FROM Warehouses 
ORDER BY CreatedAt DESC 
LIMIT 5
"@
    if ($recentWarehouses) {
        $rows = $recentWarehouses -split "\n" | Where-Object { $_ -ne "" }
        foreach ($row in $rows) {
            Write-Host "  $row" -ForegroundColor White
        }
    } else {
        Write-Host "  没有找到仓库数据" -ForegroundColor Gray
    }
} catch {
    Write-Host "  查询失败: $($_.Exception.Message)" -ForegroundColor Red
}

# 用户活跃度统计
Write-Host "\n用户活跃度统计 (前5名):" -ForegroundColor Cyan
try {
    $activeUsers = Invoke-SqlQuery @"
SELECT u.Name, u.Email, COUNT(ar.Id) as AccessCount
FROM Users u
LEFT JOIN AccessRecords ar ON u.Id = ar.UserId
GROUP BY u.Id, u.Name, u.Email
ORDER BY AccessCount DESC
LIMIT 5
"@
    if ($activeUsers) {
        $rows = $activeUsers -split "\n" | Where-Object { $_ -ne "" }
        foreach ($row in $rows) {
            Write-Host "  $row" -ForegroundColor White
        }
    } else {
        Write-Host "  没有找到用户数据" -ForegroundColor Gray
    }
} catch {
    Write-Host "  查询失败: $($_.Exception.Message)" -ForegroundColor Red
}

# 文档分类统计
Write-Host "\n文档分类统计:" -ForegroundColor Cyan
try {
    $docStats = Invoke-SqlQuery @"
SELECT dc.Name, COUNT(d.Id) as DocumentCount
FROM DocumentCatalogs dc
LEFT JOIN Documents d ON dc.Id = d.CatalogId
GROUP BY dc.Id, dc.Name
ORDER BY DocumentCount DESC
LIMIT 10
"@
    if ($docStats) {
        $rows = $docStats -split "\n" | Where-Object { $_ -ne "" }
        foreach ($row in $rows) {
            Write-Host "  $row" -ForegroundColor White
        }
    } else {
        Write-Host "  没有找到文档数据" -ForegroundColor Gray
    }
} catch {
    Write-Host "  查询失败: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "\n数据库查询完成!" -ForegroundColor Green