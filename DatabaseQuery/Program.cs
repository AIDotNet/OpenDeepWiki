using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace DatabaseQuery
{
    /// <summary>
    /// SQLite数据库查询工具
    /// </summary>
    public class DatabaseQueryTool
    {
        private readonly string _connectionString;

        public DatabaseQueryTool(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// 执行SQL查询并返回结果
        /// </summary>
        /// <param name="sql">SQL查询语句</param>
        /// <returns>查询结果</returns>
        public DataTable ExecuteQuery(string sql)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = new SqliteCommand(sql, connection);
            using var reader = command.ExecuteReader();

            var dataTable = new DataTable();
            dataTable.Load(reader);

            return dataTable;
        }

        /// <summary>
        /// 获取数据库中的所有表
        /// </summary>
        /// <returns>表名列表</returns>
        public DataTable GetTables()
        {
            const string sql = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            return ExecuteQuery(sql);
        }

        /// <summary>
        /// 获取表的列信息
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>列信息</returns>
        public DataTable GetTableSchema(string tableName)
        {
            var sql = $"PRAGMA table_info({tableName});";
            return ExecuteQuery(sql);
        }

        /// <summary>
        /// 获取表中的数据
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="limit">限制返回的行数</param>
        /// <returns>表数据</returns>
        public DataTable GetTableData(string tableName, int limit = 10)
        {
            var sql = $"SELECT * FROM {tableName} LIMIT {limit};";
            return ExecuteQuery(sql);
        }

        /// <summary>
        /// 获取表的行数
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>行数</returns>
        public int GetTableRowCount(string tableName)
        {
            var sql = $"SELECT COUNT(*) as Count FROM {tableName};";
            var result = ExecuteQuery(sql);
            return Convert.ToInt32(result.Rows[0]["Count"]);
        }

        /// <summary>
        /// 打印数据表
        /// </summary>
        /// <param name="dataTable">数据表</param>
        /// <param name="title">标题</param>
        public static void PrintDataTable(DataTable dataTable, string title = "")
        {
            if (!string.IsNullOrEmpty(title))
            {
                Console.WriteLine($"\n=== {title} ===");
            }

            if (dataTable.Rows.Count == 0)
            {
                Console.WriteLine("没有数据");
                return;
            }

            // 打印列名
            foreach (DataColumn column in dataTable.Columns)
            {
                Console.Write($"{column.ColumnName,-20}");
            }
            Console.WriteLine();
            Console.WriteLine(new string('-', dataTable.Columns.Count * 20));

            // 打印数据行
            foreach (DataRow row in dataTable.Rows)
            {
                foreach (DataColumn column in dataTable.Columns)
                {
                    var value = row[column]?.ToString() ?? "NULL";
                    if (value.Length > 18)
                    {
                        value = value.Substring(0, 15) + "...";
                    }
                    Console.Write($"{value,-20}");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"总计: {dataTable.Rows.Count} 行");
        }
    }

    /// <summary>
    /// 主程序
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            // 从配置文件读取连接字符串
            var connectionString = "Data Source=/data/KoalaWiki.db";
            
            // 如果Windows系统，使用相对路径
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                connectionString = "Data Source=..\\..\\..\\..\\data\\KoalaWiki.db";
            }

            var queryTool = new DatabaseQueryTool(connectionString);

            try
            {
                Console.WriteLine("KoalaWiki 数据库查询工具");
                Console.WriteLine("=========================");

                // 显示所有表
                var tables = queryTool.GetTables();
                DatabaseQueryTool.PrintDataTable(tables, "数据库表列表");

                // 查询每个表的前几行数据
                foreach (DataRow row in tables.Rows)
                {
                    var tableName = row["name"].ToString();
                    if (tableName.StartsWith("sqlite_")) continue; // 跳过SQLite系统表

                    Console.WriteLine($"\n表: {tableName}");
                    
                    // 显示表结构
                    var schema = queryTool.GetTableSchema(tableName);
                    Console.WriteLine($"列数: {schema.Rows.Count}");
                    
                    // 显示行数
                    var rowCount = queryTool.GetTableRowCount(tableName);
                    Console.WriteLine($"行数: {rowCount}");
                    
                    // 显示前几行数据
                    var data = queryTool.GetTableData(tableName, 3);
                    if (data.Rows.Count > 0)
                    {
                        DatabaseQueryTool.PrintDataTable(data, $"{tableName} 表数据 (前3行)");
                    }
                    
                    Console.WriteLine(new string('=', 50));
                }

                // 执行一些特定的查询
                Console.WriteLine("\n=== 统计信息 ===");
                
                // 仓库统计
                var warehouseCount = queryTool.ExecuteQuery("SELECT COUNT(*) as Count FROM Warehouses");
                Console.WriteLine($"仓库总数: {warehouseCount.Rows[0]["Count"]}");

                // 用户统计
                var userCount = queryTool.ExecuteQuery("SELECT COUNT(*) as Count FROM Users");
                Console.WriteLine($"用户总数: {userCount.Rows[0]["Count"]}");

                // 文档统计
                var documentCount = queryTool.ExecuteQuery("SELECT COUNT(*) as Count FROM Documents");
                Console.WriteLine($"文档总数: {documentCount.Rows[0]["Count"]}");

                // 最近创建的仓库
                var recentWarehouses = queryTool.ExecuteQuery(@"
                    SELECT Name, OrganizationName, Status, CreatedAt 
                    FROM Warehouses 
                    ORDER BY CreatedAt DESC 
                    LIMIT 5");
                DatabaseQueryTool.PrintDataTable(recentWarehouses, "最近创建的仓库");

                // 用户活跃度统计
                var activeUsers = queryTool.ExecuteQuery(@"
                    SELECT u.Name, u.Email, COUNT(ar.Id) as AccessCount
                    FROM Users u
                    LEFT JOIN AccessRecords ar ON u.Id = ar.UserId
                    GROUP BY u.Id, u.Name, u.Email
                    ORDER BY AccessCount DESC
                    LIMIT 10");
                if (activeUsers.Rows.Count > 0)
                {
                    DatabaseQueryTool.PrintDataTable(activeUsers, "最活跃的用户 (前10名)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
    }
}