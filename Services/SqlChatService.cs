using Microsoft.Data.Sqlite;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace SqlChatbot.Services;

public class SqlChatService
{
    private readonly IChatCompletionService _chat;
    private readonly string _connectionString;

    public SqlChatService(IChatCompletionService chat, IConfiguration config)
    {
        _chat = chat;
        _connectionString = config["Database:ConnectionString"]!;
    }

    public async Task<string> AskAsync(string userPrompt)
    {
        var schema = GetSchema();
        var sql    = await GenerateSqlAsync(userPrompt, schema);
        var result = ExecuteSql(sql);
        return await SummarizeAsync(userPrompt, sql, result);
    }

    // ── Step 1: introspect SQLite schema ────────────────────────────────────
    private string GetSchema()
    {
        var sb = new StringBuilder();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Get all user tables
        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        // Get columns for each table
        foreach (var table in tables)
        {
            sb.AppendLine($"Table: {table}");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var colName = reader["name"];
                var colType = reader["type"];
                var notNull = reader["notnull"].ToString() == "1" ? " NOT NULL" : "";
                var pk      = reader["pk"].ToString()     == "1" ? " PRIMARY KEY" : "";
                sb.AppendLine($"  - {colName} ({colType}{notNull}{pk})");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Step 2: ask SK to generate SQL ──────────────────────────────────────
    private async Task<string> GenerateSqlAsync(string userPrompt, string schema)
    {
        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage($"""
                You are a SQL expert. Given the schema below, generate a single valid SQLite SELECT query
                that answers the user's question. Return ONLY the raw SQL — no explanation, no markdown.

                Schema:
                {schema}
                """);
            history.AddUserMessage(userPrompt);

            var response = await _chat.GetChatMessageContentAsync(history);
            return response.Content?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"Error generating SQL: {ex.Message}";
        }
    }

    // ── Step 3: execute the generated SQL ───────────────────────────────────
    private string ExecuteSql(string sql)
    {
        // Safety guard — allow only SELECT statements
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "Error: Only SELECT queries are permitted.";

        try
        {
            var sb = new StringBuilder();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = cmd.ExecuteReader();

            // Header row
            var columns = Enumerable.Range(0, reader.FieldCount)
                                    .Select(i => reader.GetName(i));
            sb.AppendLine(string.Join(" | ", columns));

            // Data rows (cap at 100 to avoid huge payloads)
            int rows = 0;
            while (reader.Read() && rows++ < 100)
            {
                var values = Enumerable.Range(0, reader.FieldCount)
                                       .Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString());
                sb.AppendLine(string.Join(" | ", values));
            }

            return sb.Length > 0 ? sb.ToString() : "No results found.";
        }
        catch (Exception ex)
        {
            return $"SQL Error: {ex.Message}";
        }
    }

    // ── Step 4: summarize raw results in plain English ───────────────────────
    private async Task<string> SummarizeAsync(string userPrompt, string sql, string rawResult)
    {
        var history = new ChatHistory();
        history.AddSystemMessage("You are a helpful data assistant. Summarize query results in plain English.");
        history.AddUserMessage($"""
            User asked: {userPrompt}

            SQL executed:
            {sql}

            Raw result:
            {rawResult}

            Provide a clear, concise natural language answer.
            """);

        var response = await _chat.GetChatMessageContentAsync(history);
        return response.Content ?? "Could not generate a response.";
    }
}