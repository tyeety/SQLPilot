// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public sealed class ForeignKey
    {
        public string Name
        {
            get; set;
        }
        public bool IsDisabled
        {
            get; set;
        }
        public DbObject Source
        {
            get; set;
        }
        public DbObject Target
        {
            get; set;
        }
        public List<Tuple<string, string>> Columns { get; set; } = new List<Tuple<string, string>>();
    }
    public sealed class TextExpansion
    {
        public int Start
        {
            get; set;
        }
        public int Length
        {
            get; set;
        }
        public string Text
        {
            get; set;
        }
        public int CaretOffset { get; set; } = -1;
    }
    public static partial class Engine
    {
        static int SelectScopeStart(string sql, int caret)
        {
            int start = 0;
            var parents = new Stack<int>();
            foreach (var t in ContextTokens(sql.Substring(0, caret)))
            {
                if (t.Text == "(")
                    parents.Push(start);
                else if (t.Text == ")" && parents.Count > 0)
                    start = parents.Pop();
                else if (new[] { "SELECT", "UPDATE", "DELETE", "INSERT" }.Contains(t.Text.ToUpperInvariant()))
                    start = t.Offset;
                else if (t.Text == ";" || t.TokenType == TSqlTokenType.Go)
                {
                    start = t.Offset + t.Text.Length;
                    parents.Clear();
                }
            }
            return start;
        }
        static List<TSqlParserToken> ScopeTokens(string sql)
        {
            var tokens = ContextTokens(sql);
            var result = new List<TSqlParserToken>();
            int depth = 0;
            string command = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Text == ")" && depth == 0 || t.Text == ";" || t.TokenType == TSqlTokenType.Go)
                    break;
                string word = t.Text.ToUpperInvariant();
                if (depth == 0 && new[] { "SELECT", "UPDATE", "DELETE", "INSERT" }.Contains(word))
                {
                    bool setSelect = word == "SELECT" && i > 0 && (new[] { "UNION", "INTERSECT", "EXCEPT" }.Contains(tokens[i - 1].Text.ToUpperInvariant()) || tokens[i - 1].Text.Equals("ALL", StringComparison.OrdinalIgnoreCase) && i > 1 && tokens[i - 2].Text.Equals("UNION", StringComparison.OrdinalIgnoreCase));
                    if (command != null && !(command == "INSERT" && word == "SELECT") && !setSelect)
                        break;
                    command = word;
                }
                if (t.Text == "(" && i + 1 < tokens.Count && tokens[i + 1].Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    int nested = 1;
                    while (++i < tokens.Count && nested > 0)
                    {
                        if (tokens[i].Text == "(")
                            nested++;
                        else if (tokens[i].Text == ")")
                            nested--;
                    }
                    i--;
                    continue;
                }
                if (t.Text == "(")
                    depth++;
                else if (t.Text == ")")
                    depth--;
                result.Add(t);
            }
            return result;
        }
        static bool Same(DbObject a, DbObject b) => a != null && b != null &&
            string.Equals(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        static IEnumerable<ForeignKey> Keys(IEnumerable<DbObject> catalog) => catalog.SelectMany(o => o.ForeignKeys).Distinct();
        static bool Linked(DbObject a, DbObject b, IEnumerable<DbObject> catalog) => Keys(catalog).Any(f =>
            Same(f.Source, a) && Same(f.Target, b) || Same(f.Target, a) && Same(f.Source, b));
        static string Qualify(Tuple<DbObject, string> b, string column) =>
            (b.Item2 == null ? b.Item1.Qualified : DbObject.Quote(b.Item2)) + "." + DbObject.Quote(column);
        static IEnumerable<Candidate> JoinConditions(List<Tuple<DbObject, string>> bindings, List<DbObject> catalog)
        {
            if (bindings.Count < 2)
                yield break;
            var joined = bindings.Last();
            foreach (var previous in bindings.Take(bindings.Count - 1))
            {
                // A repeated unaliased table cannot form an unambiguous self join.
                if (Same(previous.Item1, joined.Item1) && (previous.Item2 == null || joined.Item2 == null || previous.Item2.Equals(joined.Item2, StringComparison.OrdinalIgnoreCase)))
                    continue;
                foreach (var key in Keys(catalog))
                {
                    bool forward = Same(previous.Item1, key.Source) && Same(joined.Item1, key.Target);
                    bool reverse = Same(previous.Item1, key.Target) && Same(joined.Item1, key.Source);
                    if (!forward && !reverse || key.Columns.Count == 0)
                        continue;
                    var source = forward ? previous : joined;
                    var target = forward ? joined : previous;
                    string condition = string.Join(" AND ", key.Columns.Select(c => Qualify(source, c.Item1) + " = " + Qualify(target, c.Item2)));
                    yield return new Candidate { Label = condition, Insert = condition, Detail = key.Name + (key.IsDisabled ? " (disabled)" : ""), Category = "FK Join", Priority = key.IsDisabled ? 1 : 0 };
                    if (forward && reverse)
                    {
                        condition = string.Join(" AND ", key.Columns.Select(c => Qualify(joined, c.Item1) + " = " + Qualify(previous, c.Item2)));
                        yield return new Candidate { Label = condition, Insert = condition, Detail = key.Name + (key.IsDisabled ? " (disabled)" : ""), Category = "FK Join", Priority = key.IsDisabled ? 1 : 0 };
                    }
                }
            }
        }
        // Use the parser's SELECT projection nodes: COUNT(*) and arithmetic stars never qualify.
        public static TextExpansion ExpandStar(string sql, int caret, IEnumerable<DbObject> catalog, bool useBrackets = true)
        {
            if (caret < 0 || caret > sql.Length || !IsCode(sql, caret))
                return null;
            if (!(caret > 0 && sql[caret - 1] == '*') && !(caret < sql.Length && sql[caret] == '*'))
                return null;
            var tree = new TSql180Parser(true).Parse(new StringReader(sql), out var errors);
            var finder = new StarFinder(caret);
            tree.Accept(finder);
            if (finder.Star == null || finder.Query?.FromClause == null)
                return null;
            // Conservative handling for malformed statements prevents expansion against guessed tables.
            if (errors.Any(e => e.Offset >= finder.Query.StartOffset && e.Offset < finder.Query.StartOffset + finder.Query.FragmentLength))
                return null;
            var named = new List<NamedTableReference>();
            if (!CollectTables(finder.Query.FromClause.TableReferences, named))
                return null;
            var ctes = new CteFinder();
            tree.Accept(ctes);
            if (named.Any(t => t.SchemaObject.Identifiers.Count == 1 && ctes.Names.Contains(t.SchemaObject.BaseIdentifier.Value, StringComparer.OrdinalIgnoreCase)))
                return null;
            var objects = catalog.ToList();
            var columns = new List<string>();
            var qualifier = finder.Star.Qualifier?.Identifiers;
            foreach (var table in named)
            {
                var ids = table.SchemaObject.Identifiers;
                if (ids.Count > 2 || ids.Count == 0)
                    return null;
                string name = ids.Last().Value, schema = ids.Count == 2 ? ids[0].Value : null;
                string alias = table.Alias?.Value;
                if (qualifier != null && qualifier.Count > 0 && !string.Equals(qualifier.Last().Value, alias ?? name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (qualifier != null && qualifier.Count > 1 && (alias != null || qualifier.Count != 2 || !string.Equals(qualifier[0].Value, schema, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var matches = objects.Where(o => Relation(o) && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase) && (schema == null || string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matches.Count != 1 || matches[0].Columns.Count == 0)
                    return null;
                var binding = Tuple.Create(matches[0], alias);
                columns.AddRange(matches[0].Columns.Select(c => Qualify(binding, c)));
            }
            if (columns.Count == 0)
                return null;
            string newline = sql.Contains("\r\n") ? "\r\n" : "\n";
            return new TextExpansion
            {
                Start = finder.Star.StartOffset,
                Length = finder.Star.FragmentLength,
                Text = OptionalBrackets(string.Join("," + newline + "    ", columns), useBrackets)
            };
        }
        static bool CollectTables(IEnumerable<TableReference> tables, List<NamedTableReference> result)
        {
            foreach (var table in tables)
            {
                if (table is NamedTableReference named)
                    result.Add(named);
                else if (table is JoinTableReference join)
                {
                    if (!CollectTables(new[] { join.FirstTableReference, join.SecondTableReference }, result))
                        return false;
                }
                else if (table is JoinParenthesisTableReference parenthesis)
                {
                    if (!CollectTables(new[] { parenthesis.Join }, result))
                        return false;
                }
                else
                    return false; // Derived tables/functions require their own projection metadata.
            }
            return true;
        }
        sealed class StarFinder : TSqlFragmentVisitor
        {
            readonly int caret;
            public SelectStarExpression Star; public QuerySpecification Query;
            public StarFinder(int caret)
            {
                this.caret = caret;
            }
            public override void ExplicitVisit(QuerySpecification node)
            {
                foreach (var star in node.SelectElements.OfType<SelectStarExpression>())
                    if (caret >= star.StartOffset && caret <= star.StartOffset + star.FragmentLength)
                    {
                        Star = star;
                        Query = node;
                    }
                base.ExplicitVisit(node);
            }
        }
        sealed class CteFinder : TSqlFragmentVisitor
        {
            public readonly List<string> Names = new List<string>();
            public override void ExplicitVisit(CommonTableExpression node)
            {
                Names.Add(node.ExpressionName.Value);
                base.ExplicitVisit(node);
            }
        }
    }
}
