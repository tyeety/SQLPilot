// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public sealed class MetadataDescription
    {
        public int Start
        {
            get; set;
        }
        public int Length
        {
            get; set;
        }
        public string Title
        {
            get; set;
        }
        public string Body
        {
            get; set;
        }
    }
    public static partial class Engine
    {
        static string ColumnDescription(DbObject table, string column)
        {
            var detail = table.ColumnDetails.FirstOrDefault(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase))
                ?? table.InsertColumns?.FirstOrDefault(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
            string result = DbObject.Quote(column);
            if (detail == null)
                return result + " · Type metadata unavailable";
            result += "  " + detail.Type + (detail.Nullable ? " NULL" : " NOT NULL");
            if (detail.Identity)
                result += " IDENTITY";
            if (detail.Computed)
                result += " COMPUTED";
            if (detail.HasDefault || !string.IsNullOrWhiteSpace(detail.DefaultExpression))
            {
                string expression = detail.DefaultExpression ?? "(definition unavailable)";
                result += " · DEFAULT " + (expression.Length > 240 ? expression.Substring(0, 240) + "…" : expression);
            }
            return result;
        }
        static MetadataDescription DescribeTable(DbObject table, List<DbObject> objects)
        {
            var columns = table.Columns.Take(16).Select(c => ColumnDescription(table, c));
            string body = table.Kind + " · " + table.Columns.Count + " columns\n" + string.Join("\n", columns);
            if (table.Columns.Count > 16)
                body += "\n… " + (table.Columns.Count - 16) + " more columns";
            var keys = Keys(objects).Where(k => Same(k.Source, table) || Same(k.Target, table)).ToList();
            foreach (var key in keys.Take(4))
                body += "\nFK " + key.Name + ": " + key.Source.Qualified + " → " + key.Target.Qualified;
            return new MetadataDescription { Title = table.Qualified, Body = body };
        }
        public static MetadataDescription DescribeAt(string sql, int position, IEnumerable<DbObject> catalog)
        {
            if (sql == null || position < 0 || position >= sql.Length)
                return null;
            var tokens = ContextTokens(sql);
            int index = tokens.FindIndex(t => position >= t.Offset && position < t.Offset + t.Text.Length);
            if (index < 0 || !(tokens[index].TokenType == TSqlTokenType.Identifier || tokens[index].TokenType == TSqlTokenType.QuotedIdentifier))
                return null;
            MetadataDescription AtToken(MetadataDescription result)
            {
                result.Start = tokens[index].Offset;
                result.Length = tokens[index].Text.Length;
                return result;
            }
            int first = index, last = index;
            while (first >= 2 && tokens[first - 1].Text == ".")
                first -= 2;
            while (last + 2 < tokens.Count && tokens[last + 1].Text == ".")
                last += 2;
            var parts = tokens.Skip(first).Take(last - first + 1).Where(t => t.Text != ".").Select(t => Unquote(t.Text)).ToList();
            var objects = catalog.ToList();
            string prior = first > 0 ? tokens[first - 1].Text.ToUpperInvariant() : "";
            if (new[] { "FROM", "JOIN", "APPLY", "UPDATE", "INTO", "EXEC", "EXECUTE" }.Contains(prior))
            {
                // This catalog belongs to one connection/database. Never label an
                // explicitly different database/server object with local metadata.
                if (parts.Count > 2)
                    return null;
                var matches = objects.Where(o => o.Name.Equals(parts.Last(), StringComparison.OrdinalIgnoreCase) &&
                    (parts.Count == 1 || o.Schema.Equals(parts[parts.Count - 2], StringComparison.OrdinalIgnoreCase))).ToList();
                if (matches.Count != 1)
                    return null;
                var table = matches[0];
                return AtToken(DescribeTable(table, objects));
            }
            int scope = SelectScopeStart(sql, position);
            if (parts.Count > 3)
                return null;
            var bindings = Bindings(sql.Substring(scope), objects);
            if (parts.Count == 1)
            {
                var aliasOwners = bindings.Where(b => string.Equals(b.Item2, parts[0], StringComparison.OrdinalIgnoreCase)).Select(b => b.Item1).Distinct().ToList();
                if (aliasOwners.Count == 1)
                    return AtToken(DescribeTable(aliasOwners[0], objects));
            }
            string name = parts.Last();
            var candidates = bindings.Where(b => b.Item1.Columns.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (parts.Count > 1)
                candidates = candidates.Where(b => string.Equals(b.Item2 ?? b.Item1.Name, parts[parts.Count - 2], StringComparison.OrdinalIgnoreCase));
            if (parts.Count > 2)
                candidates = candidates.Where(b => b.Item1.Schema.Equals(parts[parts.Count - 3], StringComparison.OrdinalIgnoreCase));
            var owners = candidates.Select(b => b.Item1).Distinct().ToList();
            if (owners.Count != 1)
                return null;
            var owner = owners[0];
            return AtToken(new MetadataDescription { Title = owner.Qualified + "." + DbObject.Quote(name), Body = "Column\n" + ColumnDescription(owner, name) });
        }
    }
}
