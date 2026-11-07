// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public sealed class Candidate
    {
        public string Label
        {
            get; set;
        }
        public string Insert
        {
            get; set;
        }
        public string Detail
        {
            get; set;
        }
        public string Category { get; set; } = "Object";
        public int Priority { get; set; } = 1;
        public int[] MatchIndices { get; set; } = Array.Empty<int>();
        public List<CompletionPlaceholder> Placeholders { get; set; } = new List<CompletionPlaceholder>();
        public override string ToString() => Label + "    " + Detail;
    }
    public sealed class DbObject
    {
        public string Schema
        {
            get; set;
        }
        public string Name
        {
            get; set;
        }
        public string Kind
        {
            get; set;
        }
        public List<string> Columns { get; set; } = new List<string>();
        public List<InsertColumn> ColumnDetails { get; set; } = new List<InsertColumn>();
        // Null means writable-column metadata is unavailable; never guess INSERT columns.
        public List<InsertColumn> InsertColumns
        {
            get; set;
        }
        public List<ForeignKey> ForeignKeys { get; set; } = new List<ForeignKey>();
        public List<ProcedureArgument> Parameters { get; set; } = new List<ProcedureArgument>();
        public string Qualified => Quote(Schema) + "." + Quote(Name);
        public static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";
    }
    public sealed class Issue
    {
        public string Code
        {
            get; set;
        }
        public string Message
        {
            get; set;
        }
        public int Offset
        {
            get; set;
        }
        public int Length
        {
            get; set;
        }
        public string Replacement
        {
            get; set;
        }
        public int? AnchorOffset
        {
            get; set;
        }
        public int SectionStart
        {
            get; set;
        }
        public int SectionEnd
        {
            get; set;
        }
        public override string ToString() => Code + " — " + Message;
    }
    public sealed class CompletionOptions
    {
        public bool FuzzyMatching
        {
            get; set;
        }
        public bool TableAliases
        {
            get; set;
        }
        public bool QualifyColumns
        {
            get; set;
        }
        public bool UseBrackets { get; set; } = true;
        public int MaxResults { get; set; } = 200;
        public IDictionary<string, string> Snippets
        {
            get; set;
        }
    }
    public static partial class Engine
    {
        public static readonly Dictionary<string, string> Snippets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ssf"] = "SELECT * FROM ",
            ["stf"] = "SELECT TOP (100) * FROM ",
            ["sct"] = "SELECT COUNT(*) FROM ",
            ["whr"] = "WHERE ",
            ["joi"] = "INNER JOIN ",
            ["lj"] = "LEFT JOIN ",
            ["gb"] = "GROUP BY ",
            ["ob"] = "ORDER BY ",
            ["cte"] = "WITH cte AS (\n    SELECT \n)\nSELECT * FROM cte;",
            ["tran"] = "SET XACT_ABORT ON;\nBEGIN TRY\n    BEGIN TRANSACTION;\n    \n    COMMIT TRANSACTION;\nEND TRY\nBEGIN CATCH\n    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;\n    THROW;\nEND CATCH;"
        };
        public static readonly string[] Keywords = ("SELECT|SET|SELECT DISTINCT|SELECT TOP|FROM|WHERE|JOIN|INNER JOIN|LEFT JOIN|RIGHT JOIN|FULL JOIN|CROSS JOIN|ON|AND|OR|NOT|IN|EXISTS|BETWEEN|LIKE|IS NULL|IS NOT NULL|GROUP BY|ORDER BY|HAVING|UNION|UNION ALL|INTERSECT|EXCEPT|INSERT INTO|UPDATE|DELETE FROM|MERGE|CREATE TABLE|CREATE VIEW|CREATE PROCEDURE|ALTER TABLE|DROP TABLE|TRUNCATE TABLE|EXEC|EXECUTE|DECLARE|BEGIN|END|IF|ELSE|WHILE|RETURN|PRINT|THROW|TRY|CATCH|COMMIT|ROLLBACK|TRANSACTION|WITH|CASE|WHEN|THEN|CAST|CONVERT|COUNT|SUM|AVG|MIN|MAX|STRING_AGG|ROW_NUMBER|PARTITION BY|OFFSET|FETCH|USE|GO").Split('|');
        public static bool IsCode(string sql, int position)
        {
            position = Math.Max(0, Math.Min(sql.Length, position));
            int state = 0, depth = 0;
            for (int i = 0; i < position; i++)
            {
                char c = sql[i], n = i + 1 < position ? sql[i + 1] : '\0';
                if (state == 1)
                {
                    if (c == '\'')
                    {
                        if (n == '\'')
                            i++;
                        else
                            state = 0;
                    }
                }
                else if (state == 2)
                {
                    if (c == '\n')
                        state = 0;
                }
                else if (state == 3)
                {
                    if (c == '/' && n == '*')
                    {
                        depth++;
                        i++;
                    }
                    else if (c == '*' && n == '/')
                    {
                        if (--depth == 0)
                            state = 0;
                        i++;
                    }
                }
                else if (c == '\'')
                    state = 1;
                else if (c == '-' && n == '-')
                {
                    state = 2;
                    i++;
                }
                else if (c == '/' && n == '*')
                {
                    state = 3;
                    depth = 1;
                    i++;
                }
            }
            return state == 0;
        }
        public static int WordStart(string sql, int caret)
        {
            int start = caret;
            while (start > 0 && (char.IsLetterOrDigit(sql[start - 1]) || sql[start - 1] == '_' || sql[start - 1] == '@' || sql[start - 1] == '#'))
                start--;
            return start;
        }
        static List<TSqlParserToken> ContextTokens(string sql)
        {
            var parser = new TSql180Parser(true);
            return parser.GetTokenStream(new StringReader(sql), out var errors)
                .Where(t => t.TokenType != TSqlTokenType.WhiteSpace && t.TokenType != TSqlTokenType.SingleLineComment
                    && t.TokenType != TSqlTokenType.MultilineComment && t.TokenType != TSqlTokenType.EndOfFile).ToList();
        }
        static bool Relation(DbObject obj) => new[] { "table", "view", "synonym", "USER_TABLE", "VIEW", "SYNONYM" }.Contains(obj.Kind, StringComparer.OrdinalIgnoreCase);
        static bool Procedure(DbObject obj) => (obj.Kind ?? "").IndexOf("PROCEDURE", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(obj.Kind, "procedure", StringComparison.OrdinalIgnoreCase);
        static IEnumerable<DbObject> ReferencedTables(List<TSqlParserToken> tokens, List<DbObject> objects)
        {
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (!new[] { "FROM", "JOIN", "UPDATE", "INTO" }.Contains(tokens[i].Text, StringComparer.OrdinalIgnoreCase))
                    continue;
                string schema = null, table = tokens[i + 1].Text.Trim('[', ']', '"');
                if (i + 3 < tokens.Count && tokens[i + 2].Text == ".")
                {
                    schema = table;
                    table = tokens[i + 3].Text.Trim('[', ']', '"');
                }
                foreach (var obj in objects.Where(o => o.Name.Equals(table, StringComparison.OrdinalIgnoreCase) && (schema == null || o.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase))))
                    yield return obj;
            }
        }
        static void Words(List<Candidate> items, params string[] words) => items.AddRange(words.Select((w, index) => new Candidate { Label = w, Insert = w, Detail = "T-SQL", Priority = 20 + index }));
        public static string AliasName(string name)
        {
            var words = Regex.Matches(name, @"[A-Z]+(?=[A-Z][a-z]|[^a-zA-Z]|$)|[A-Z]?[a-z]+|\d+").Cast<Match>().Select(m => m.Value).ToList();
            string result = string.Concat(words.Select(w => w.Substring(0, 1))).ToLowerInvariant();
            if (string.IsNullOrEmpty(result) || !char.IsLetter(result[0]))
                result = "t" + result;
            if (Keywords.Contains(result, StringComparer.OrdinalIgnoreCase))
                result += "1";
            return result;
        }
        public static int MatchScore(string label, string query, bool fuzzy)
        {
            if (string.IsNullOrEmpty(query))
                return 0;
            if (label.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (label.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 1;
            if (label.Contains("_") && label.Replace("_", "").StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 1;
            if (!fuzzy)
                return -1;
            if (AliasName(label).StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 2;
            if (label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return 3;
            int p = 0;
            foreach (char c in label)
                if (char.ToUpperInvariant(c) == char.ToUpperInvariant(query[p]) && ++p == query.Length)
                    return 4;
            return -1;
        }
        public static int[] MatchedCharacters(string label, string query, bool fuzzy)
        {
            int score = MatchScore(label, query, fuzzy);
            if (score < 0 || string.IsNullOrEmpty(query))
                return Array.Empty<int>();
            if (score <= 1 && !label.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return label.Select((c, i) => new { c, i }).Where(p => p.c != '_').Take(query.Length).Select(p => p.i).ToArray();
            if (score <= 1)
                return Enumerable.Range(0, query.Length).ToArray();
            if (score == 2)
                return Regex.Matches(label, @"[A-Z]+(?=[A-Z][a-z]|[^a-zA-Z]|$)|[A-Z]?[a-z]+|\d+")
                    .Cast<Match>().Take(query.Length).Select(m => m.Index).ToArray();
            if (score == 3)
                return Enumerable.Range(label.IndexOf(query, StringComparison.OrdinalIgnoreCase), query.Length).ToArray();
            var positions = new List<int>();
            int p = 0;
            for (int i = 0; i < label.Length && p < query.Length; i++)
                if (char.ToUpperInvariant(label[i]) == char.ToUpperInvariant(query[p]))
                {
                    positions.Add(i);
                    p++;
                }
            return positions.ToArray();
        }
        static bool DirectDeleteFrom(List<TSqlParserToken> tokens, int index)
        {
            int delete = tokens.FindIndex(t => t.Text.Equals("DELETE", StringComparison.OrdinalIgnoreCase));
            return delete >= 0 && index > delete && tokens[index].Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                (tokens[delete + 1].Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) || tokens[delete + 1].Text.Equals("TOP", StringComparison.OrdinalIgnoreCase)) &&
                !tokens.Skip(delete + 1).Take(index - delete - 1).Any(t => t.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase));
        }
        static List<Tuple<DbObject, string>> Bindings(string statement, List<DbObject> objects)
        {
            var tokens = ScopeTokens(statement);
            var result = new List<Tuple<DbObject, string>>();
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (!new[] { "FROM", "JOIN", "APPLY", "UPDATE", "INTO" }.Contains(tokens[i].Text, StringComparer.OrdinalIgnoreCase))
                    continue;
                int end = i + 1;
                string schema = null, name = tokens[end].Text.Trim('[', ']', '"');
                if (end + 2 < tokens.Count && tokens[end + 1].Text == ".")
                {
                    schema = name;
                    end += 2;
                    name = tokens[end].Text.Trim('[', ']', '"');
                }
                int a = end + 1;
                if (a < tokens.Count && tokens[a].Text.Equals("AS", StringComparison.OrdinalIgnoreCase))
                    a++;
                string alias = a < tokens.Count && (tokens[a].TokenType == TSqlTokenType.Identifier || tokens[a].TokenType == TSqlTokenType.QuotedIdentifier) ? tokens[a].Text.Trim('[', ']', '"') : null;
                if (new[] { "UPDATE", "INTO" }.Contains(tokens[i].Text.ToUpperInvariant()) || DirectDeleteFrom(tokens, i))
                    alias = null;
                foreach (var obj in objects.Where(o => Relation(o) && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (schema == null || o.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase))))
                    result.Add(Tuple.Create(obj, alias));
            }
            return result;
        }
        public static List<Candidate> Complete(string sql, int caret, IEnumerable<DbObject> catalog, bool contextualOnly = false, CompletionOptions options = null)
        {
            options = options ?? new CompletionOptions();
            if (!IsCode(sql, caret))
                return new List<Candidate>();
            int start = WordStart(sql, caret);
            string prefix = sql.Substring(start, caret - start);
            var objects = catalog.ToList();
            var items = new List<Candidate>();
            string before = sql.Substring(0, start);
            var tokens = ContextTokens(before);
            int boundary = tokens.FindLastIndex(t => t.Text == ";" || t.TokenType == TSqlTokenType.Go);
            int statementStart = boundary >= 0 ? tokens[boundary].Offset + tokens[boundary].Text.Length : 0;
            if (boundary >= 0)
                tokens = tokens.Skip(boundary + 1).ToList();
            statementStart = Math.Max(statementStart, SelectScopeStart(sql, start));
            tokens = ScopeTokens(before.Substring(statementStart));
            string last = tokens.LastOrDefault()?.Text.ToUpperInvariant() ?? "";
            string clause = tokens.LastOrDefault(t => new[] { "SELECT", "FROM", "JOIN", "APPLY", "UPDATE", "INTO", "EXEC", "EXECUTE", "WHERE", "ON", "AND", "OR", "HAVING", "SET", "BY" }.Contains(t.Text, StringComparer.OrdinalIgnoreCase))?.Text.ToUpperInvariant() ?? "";
            if (tokens.LastOrDefault()?.TokenType == TSqlTokenType.Variable && tokens.Any(t => t.TokenType == TSqlTokenType.Declare))
            {
                foreach (var type in new[] { "int", "bigint", "bit", "decimal(18, 2)", "nvarchar(100)", "varchar(100)", "datetime2", "date", "uniqueidentifier", "varbinary(max)", "sql_variant" })
                    if (MatchScore(type, prefix, options.FuzzyMatching) >= 0)
                        items.Add(new Candidate { Label = type, Insert = type, Detail = "SQL data type", Category = "Type" });
                return items;
            }
            if (last == "AS")
            {
                var bound = Bindings(before.Substring(statementStart), objects).LastOrDefault();
                if (bound == null || !new[] { "FROM", "JOIN", "APPLY" }.Contains(clause))
                    return items;
                string aliasName = AliasName(bound.Item1.Name);
                var used = Bindings(before.Substring(statementStart), objects).Where(b => b.Item2 != null).Select(b => b.Item2).ToList();
                string root = aliasName;
                int suffix = 2;
                while (used.Contains(aliasName, StringComparer.OrdinalIgnoreCase))
                    aliasName = root + suffix++;
                return MatchScore(aliasName, prefix, options.FuzzyMatching) < 0 ? items : new List<Candidate> { new Candidate { Label = aliasName, Insert = OptionalBrackets(DbObject.Quote(aliasName), options.UseBrackets), Detail = "Table alias", Category = "Alias", MatchIndices = MatchedCharacters(aliasName, prefix, options.FuzzyMatching) } };
            }
            bool relations = new[] { "FROM", "JOIN", "APPLY", "UPDATE", "INTO" }.Contains(last) || (last == "," && clause == "FROM");
            bool topColumns = clause == "SELECT" && Regex.IsMatch(string.Join(" ", tokens.Select(t => t.Text)), @"\bSELECT\s+(?:DISTINCT\s+)?TOP\s*(?:\(\s*\d+\s*\)|\d+)(?:\s+PERCENT)?(?:\s+WITH\s+TIES)?$", RegexOptions.IgnoreCase);
            // A qualifier in a relation position is a schema, not a table/alias column prefix.
            if (tokens.Count >= 3 && tokens[tokens.Count - 1].Text == ".")
                relations = new[] { "FROM", "JOIN", "APPLY", "UPDATE", "INTO" }.Contains(tokens[tokens.Count - 3].Text.ToUpperInvariant()) || (tokens[tokens.Count - 3].Text == "," && clause == "FROM");
            var qualifier = Regex.Match(before, @"(?<q>\[[^\]]+\]|[\w]+)\.$");
            var context = DecideContext(before.Substring(statementStart));
            if (context.Role == CompletionRole.None)
                return items;
            if (qualifier.Success)
            {
                string q = qualifier.Groups["q"].Value.Trim('[', ']');
                // Resolve a simple FROM/JOIN alias in the current statement. Complex scopes remain a documented limitation.
                var scoped = Bindings(sql.Substring(statementStart), objects);
                var matching = scoped.Where(b => string.Equals(b.Item2 ?? b.Item1.Name, q, StringComparison.OrdinalIgnoreCase)).ToList();
                var table = relations ? null : matching.Count == 1 ? matching[0].Item1 : null;
                if (table != null)
                    items.AddRange(table.Columns.Select(c => new Candidate { Label = c, Insert = DbObject.Quote(c), Detail = table.Qualified }));
                else
                    items.AddRange(objects.Where(o => o.Schema.Equals(q, StringComparison.OrdinalIgnoreCase) && (!relations || Relation(o))).Select(o => new Candidate { Label = o.Name, Insert = DbObject.Quote(o.Name), Detail = o.Schema + " · " + o.Kind }));
            }
            else if (relations)
                items.AddRange(objects.Where(Relation).Select(o => new Candidate { Label = o.Name, Insert = o.Qualified, Detail = o.Schema + " · " + o.Kind }));
            else if (context.Role == CompletionRole.Keywords)
                Words(items, context.Words);
            else if (last == "EXEC" || last == "EXECUTE")
                items.AddRange(objects.Where(Procedure).Select(o => new Candidate { Label = o.Name, Insert = o.Qualified, Detail = o.Kind }));
            else if (last == "*" && clause == "SELECT")
                Words(items, "FROM");
            else if (context.Role == CompletionRole.Columns || topColumns || new[] { "SELECT", "DISTINCT", "WHERE", "ON", "AND", "OR", "HAVING", "SET", "BY" }.Contains(last) || (last == "," && new[] { "SELECT", "SET", "BY" }.Contains(clause)) ||
                (new[] { "WHERE", "ON", "AND", "OR", "HAVING", "SET" }.Contains(clause) && new[] { "=", "<>", "!=", ">", "<", ">=", "<=", "(" }.Contains(last)))
            {
                var referenced = ReferencedTables(tokens, objects).ToList();
                // SELECT columns can refer to a FROM clause already written to the right of the cursor.
                if (referenced.Count == 0)
                    referenced = ReferencedTables(ScopeTokens(sql.Substring(statementStart)), objects).ToList();
                bool hasSource = ScopeTokens(sql.Substring(statementStart)).Any(t => new[] { "FROM", "JOIN", "APPLY", "UPDATE", "INTO" }.Contains(t.Text, StringComparer.OrdinalIgnoreCase));
                var columnTables = referenced.Count > 0 ? referenced : hasSource ? Enumerable.Empty<DbObject>() : objects.Where(Relation);
                items.AddRange(columnTables.Distinct().SelectMany(o => o.Columns.Select(c => new Candidate { Label = c, Insert = DbObject.Quote(c), Detail = o.Qualified, Category = "Column" })));
                if (context.Role == CompletionRole.Columns)
                    Words(items, context.Words);
                if (topColumns || last == "SELECT" || last == "DISTINCT")
                    Words(items, "*", "COUNT(*)", "SUM", "AVG", "MIN", "MAX", "CASE");
                if (!topColumns && (last == "SELECT" || last == "DISTINCT"))
                    Words(items, "TOP");
                if (last == "SELECT")
                    Words(items, "DISTINCT");
                if (context.Role == CompletionRole.Legacy && (last == "WHERE" || last == "ON" || last == "AND" || last == "OR"))
                    Words(items, "EXISTS", "NOT EXISTS");
            }
            else if (ReferencedTables(tokens, objects).Any() && new[] { "FROM", "JOIN", "INTO", "UPDATE" }.Contains(clause))
            {
                if (clause == "UPDATE")
                    Words(items, "SET");
                else if (clause == "JOIN")
                    Words(items, "ON");
                else if (clause == "INTO")
                    Words(items, "VALUES", "SELECT");
                else
                    Words(items, "WHERE", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "CROSS JOIN", "GROUP BY", "ORDER BY");
            }
            else if (contextualOnly && new[] { "WHERE", "ON", "AND", "OR", "HAVING" }.Contains(clause))
                Words(items, "=", "<>", ">", "<", ">=", "<=", "IS NULL", "IS NOT NULL", "LIKE", "IN", "BETWEEN");
            else if (contextualOnly && clause == "SELECT")
                Words(items, "FROM", "AS");
            else if (contextualOnly)
                return items;
            else
            {
                items.AddRange((options.Snippets ?? Snippets).Select(s => new Candidate { Label = s.Key, Insert = s.Value, Detail = "snippet: " + s.Value.Split('\n')[0] }));
                items.AddRange(Keywords.Select(k => new Candidate { Label = k, Insert = k, Detail = "T-SQL" }));
                items.AddRange(objects.Select(o => new Candidate { Label = o.Name, Insert = o.Qualified, Detail = o.Schema + " · " + o.Kind }));
                items.AddRange(objects.SelectMany(o => o.Columns.Select(c => new Candidate { Label = c, Insert = DbObject.Quote(c), Detail = o.Qualified, Category = "Column" })));
            }
            if (relations && !qualifier.Success && prefix.Length >= 2 && new[] { "FROM", "JOIN", "APPLY" }.Contains(clause))
                AddFunctions(items, true);
            else if (!relations && !qualifier.Success && (context.Role == CompletionRole.Columns || context.Role == CompletionRole.Legacy && new[] { "", "SELECT", "WHERE", "ON", "AND", "OR", "HAVING", "SET", "BY" }.Contains(clause)))
                AddFunctions(items, false, clause == "" || clause == "SELECT" || clause == "BY" && tokens.Any(t => t.Text.Equals("ORDER", StringComparison.OrdinalIgnoreCase)));
            var statementTokens = ScopeTokens(sql.Substring(statementStart));
            string currentStatement = string.Join(" ", statementTokens.Select(t => t.Text));
            var bindings = Bindings(currentStatement, objects);
            var priorBindings = Bindings(before.Substring(statementStart), objects);
            foreach (var item in items)
            {
                if (!item.Detail.StartsWith("Built-in", StringComparison.Ordinal))
                    item.Category = item.Detail == "T-SQL" ? "Keyword" : item.Detail.StartsWith("snippet:", StringComparison.Ordinal) ? "Snippet" : item.Detail == "column" || item.Detail.StartsWith("[") ? "Column" : "Object";
                if (item.Category == "Object")
                {
                    var obj = objects.FirstOrDefault(o => o.Name == item.Label && (o.Qualified == item.Insert || DbObject.Quote(o.Name) == item.Insert && item.Detail.StartsWith(o.Schema + " · ", StringComparison.Ordinal)));
                    if (obj != null)
                    {
                        item.Category = Procedure(obj) ? "Procedure" : obj.Kind.IndexOf("VIEW", StringComparison.OrdinalIgnoreCase) >= 0 ? "View" : Relation(obj) ? "Table" : "Function";
                        item.Detail = obj.Schema + " · " + item.Category;
                        if (Procedure(obj) && (last == "EXEC" || last == "EXECUTE" || tokens.Count == 0 || qualifier.Success && clause == "EXEC" || qualifier.Success && clause == "EXECUTE"))
                            ProcedureCall(item, obj, last == "EXEC" || last == "EXECUTE" || clause == "EXEC" || clause == "EXECUTE", qualifier.Success);
                        if (relations && clause == "INTO" && tokens.Any(t => t.Text.Equals("INSERT", StringComparison.OrdinalIgnoreCase)))
                            InsertBody(item, obj, sql.Substring(caret));
                        // SQL Server does not accept an AS alias after INSERT INTO / UPDATE targets.
                        if (options.TableAliases && relations && new[] { "FROM", "JOIN", "APPLY" }.Contains(clause) &&
                            (clause != "FROM" || !DirectDeleteFrom(tokens, tokens.FindLastIndex(t => t.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase)))) &&
                            !(ContextTokens(sql.Substring(caret)).FirstOrDefault()?.TokenType == TSqlTokenType.Identifier) &&
                            !(ContextTokens(sql.Substring(caret)).FirstOrDefault()?.Text.Equals("AS", StringComparison.OrdinalIgnoreCase) ?? false))
                        {
                            string root = AliasName(obj.Name), alias = root;
                            int suffix = 2;
                            while (bindings.Any(b => string.Equals(b.Item2, alias, StringComparison.OrdinalIgnoreCase)))
                                alias = root + suffix++;
                            item.Insert += " AS " + DbObject.Quote(alias);
                        }
                    }
                }
            }
            if (options.QualifyColumns && !qualifier.Success && items.Any(c => c.Category == "Column") && bindings.Any(b => b.Item2 != null))
            {
                items.RemoveAll(c => c.Category == "Column");
                foreach (var binding in bindings)
                    items.AddRange(binding.Item1.Columns.Select(c => new Candidate
                    {
                        Label = c,
                        Insert = (binding.Item2 == null ? binding.Item1.Qualified : DbObject.Quote(binding.Item2)) + "." + DbObject.Quote(c),
                        Detail = (binding.Item2 ?? binding.Item1.Name) + " · " + binding.Item1.Schema + "." + binding.Item1.Name,
                        Category = "Column"
                    }));
            }
            if (relations && clause == "JOIN" && context.JoinPredicate)
                foreach (var item in items)
                {
                    var target = objects.FirstOrDefault(o => o.Name == item.Label && (item.Insert.StartsWith(o.Qualified, StringComparison.Ordinal) || item.Insert.StartsWith(DbObject.Quote(o.Name), StringComparison.Ordinal) && item.Detail.StartsWith(o.Schema + " · ", StringComparison.Ordinal)));
                    if (target != null && priorBindings.Any(b => Linked(b.Item1, target, objects)))
                    {
                        item.Priority = 0;
                        item.Category = "FK Table";
                        item.Detail = target.Schema + " · Related";
                    }
                }
            if (context.JoinPredicate && (last == "ON" || clause == "ON" && context.Role == CompletionRole.Columns && !qualifier.Success))
                items.AddRange(JoinConditions(priorBindings, objects));
            if (context.JoinPredicate && context.Role == CompletionRole.Keywords && context.Words.Contains("ON"))
                foreach (var condition in JoinConditions(priorBindings, objects))
                {
                    condition.Insert = "ON " + condition.Insert;
                    items.Add(condition);
                }
            return items.Select(c => new { Item = c, Score = MatchScore(c.Label, prefix, options.FuzzyMatching) }).Where(c => c.Score >= 0)
                .OrderBy(c => c.Item.Priority).ThenBy(c => c.Score).ThenBy(c => c.Item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Item.Label, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Item.Detail, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(20, Math.Min(500, options.MaxResults))).Select(c => { c.Item.MatchIndices = MatchedCharacters(c.Item.Label, prefix, options.FuzzyMatching); return c.Item; }).Select(c => IdentifierStyle(c, options.UseBrackets)).ToList();
        }
        public static List<Issue> Analyze(string sql)
        {
            var parser = new TSql180Parser(true);
            var tree = parser.Parse(new StringReader(sql), out var errors);
            var result = errors.Select(e => new Issue { Code = "SYNTAX", Message = $"Line {e.Line}, column {e.Column}: {e.Message}", Offset = e.Offset, Length = 0 }).ToList();
            CrossJoinIssues(sql, result);
            var visitor = new ReviewVisitor(result);
            tree?.Accept(visitor);
            var variables = new VariableInventory();
            tree?.Accept(variables);
            if (errors.Count > 0)
            {
                RecoverFindings(sql, result, variables);
                OperatorIssues(sql, result);
            }
            variables.AddIssues(sql, result);
            return result.GroupBy(i => i.Code + ":" + i.Offset + ":" + i.Message).Select(g => g.First()).ToList();
        }
        public static string Format(string sql)
        {
            var parser = new TSql180Parser(true);
            var tree = parser.Parse(new StringReader(sql), out var errors);
            if (errors.Count > 0)
                throw new InvalidOperationException("Fix syntax errors before formatting.");
            var generator = new Sql180ScriptGenerator(new SqlScriptGeneratorOptions { KeywordCasing = KeywordCasing.Uppercase, IndentationSize = 4 });
            generator.GenerateScript(tree, out string text);
            return PreserveComments(sql, text);
        }
        sealed class ReviewVisitor : TSqlFragmentVisitor
        {
            readonly List<Issue> issues; public ReviewVisitor(List<Issue> issues)
            {
                this.issues = issues;
            }
            void Add(TSqlFragment node, string code, string message) => issues.Add(new Issue { Code = code, Message = message, Offset = node.StartOffset, Length = node.FragmentLength });
            public override void ExplicitVisit(UpdateSpecification node)
            {
                if (node.WhereClause == null)
                    Add(node, "UPDATE-WHERE", "UPDATE has no WHERE. Confirm the intended rows before execution.");
                base.ExplicitVisit(node);
            }
            public override void ExplicitVisit(DeleteSpecification node)
            {
                if (node.WhereClause == null)
                    Add(node, "DELETE-WHERE", "DELETE has no WHERE. Confirm the intended rows before execution.");
                base.ExplicitVisit(node);
            }
            public override void ExplicitVisit(SelectStarExpression node)
            {
                Add(node, "SELECT-STAR", "Prefer explicit columns for a stable result shape and less transferred data.");
                base.ExplicitVisit(node);
            }
            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (node.FirstExpression is NullLiteral || node.SecondExpression is NullLiteral)
                {
                    if (node.ComparisonType == BooleanComparisonType.Equals || node.ComparisonType == BooleanComparisonType.NotEqualToBrackets || node.ComparisonType == BooleanComparisonType.NotEqualToExclamation)
                    {
                        var other = node.FirstExpression is NullLiteral ? node.SecondExpression : node.FirstExpression;
                        var generator = new Sql180ScriptGenerator();
                        generator.GenerateScript(other, out string expr);
                        issues.Add(new Issue { Code = "NULL-COMPARE", Message = "Use IS NULL / IS NOT NULL instead of comparing with NULL.", Offset = node.StartOffset, Length = node.FragmentLength, Replacement = expr + (node.ComparisonType == BooleanComparisonType.Equals ? " IS NULL" : " IS NOT NULL") });
                    }
                }
                base.ExplicitVisit(node);
            }
            public override void ExplicitVisit(QuerySpecification node)
            {
                if (node.TopRowFilter != null && node.OrderByClause == null)
                {
                    Add(node, "TOP-ORDER", "TOP without ORDER BY can return unpredictable rows. Choose an ORDER BY expression to make the selected rows predictable.");
                    issues[issues.Count - 1].AnchorOffset = node.TopRowFilter.StartOffset;
                }
                base.ExplicitVisit(node);
            }
        }
    }
}
