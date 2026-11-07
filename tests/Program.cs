using System;
using System.Collections.Generic;
using System.Linq;
using SqlPilot.Core;
int checks = 0;
void Check(bool ok, string name) { if (!ok) { Console.Error.WriteLine("FAIL " + name); Environment.Exit(1); } checks++; Console.WriteLine("PASS " + name); }
var objects = new[] { new DbObject { Schema = "dbo", Name = "Orders", Kind = "table", Columns = new List<string> { "Id", "CustomerId" } } };
Check(Engine.Snippets["ssf"] == "SELECT * FROM ", "ssf expansion");
Check(Engine.Complete("s", 1, objects).Any(c => c.Label == "SELECT"), "single letter");
Check(Engine.Complete("s", 1, objects).Any(c => c.Label == "SELECT DISTINCT"), "multiword keyword");
Check(Engine.Complete("", 0, objects).Count > 0, "explicit empty completion");
Check(Engine.Complete("dbo.O", 5, objects).Single().Insert == "[Orders]", "schema qualification");
var alias = "SELECT o.C FROM dbo.Orders AS o";
Check(Engine.Complete(alias, 10, objects).Single().Insert == "[CustomerId]", "alias columns");
Check(Engine.Complete("-- ssf", 6, objects).Count == 0, "line comment");
Check(!Engine.IsCode("SELECT 'it''s ssf", 16), "escaped string");
Check(!Engine.IsCode("/* a /* b */ ssf", 15), "nested comment");
Check(Engine.IsCode("-- x\nssf", 8), "after comment");
Check(Engine.Complete("/* done */ s", 12, objects).Any(), "after block comment");
Check(Engine.Analyze("DELETE FROM dbo.Orders;").Any(i => i.Code == "DELETE-WHERE"), "delete guard");
Check(!Engine.Analyze("DELETE FROM dbo.Orders WHERE Id=1;").Any(i => i.Code == "DELETE-WHERE"), "bounded delete");
Check(!Engine.Analyze("SELECT 'DELETE FROM Orders' AS x; -- UPDATE Orders").Any(i => i.Code == "DELETE-WHERE" || i.Code == "UPDATE-WHERE"), "no string false positives");
var nullable = Engine.Analyze("SELECT Id FROM dbo.Orders WHERE CustomerId = NULL;").Single(i => i.Code == "NULL-COMPARE");
Check(nullable.Replacement == "CustomerId IS NULL", "AST null fix");
Check(Engine.Analyze("select from where").Any(i => i.Code == "SYNTAX"), "syntax error");
Check(Engine.Format("select Id from dbo.Orders where Id=1").Contains("SELECT"), "format");
Check(DbObject.Quote("a]b") == "[a]]b]", "identifier escaping");
Check(Engine.Analyze("SELECT Id FROM dbo.Orders WHERE NULL <> Id;").Single(i => i.Code == "NULL-COMPARE").Replacement == "Id IS NOT NULL", "reversed null comparison");
Check(!Engine.Analyze("SELECT TOP (1) Id FROM dbo.Orders ORDER BY Id;").Any(i => i.Code == "TOP-ORDER"), "ordered top");
var contextObjects = objects.Concat(new[] {
    new DbObject { Schema = "sales", Name = "Customers", Kind = "USER_TABLE", Columns = new List<string> { "CustomerKey", "Name" } },
    new DbObject { Schema = "dbo", Name = "OrderSummary", Kind = "VIEW", Columns = new List<string> { "Total" } },
    new DbObject { Schema = "dbo", Name = "LoadOrders", Kind = "SQL_STORED_PROCEDURE" },
    new DbObject { Schema = "dbo", Name = "CalculateTax", Kind = "SQL_SCALAR_FUNCTION" }
}).ToArray();
List<Candidate> AtEnd(string text, bool contextualOnly = true) => Engine.Complete(text, text.Length, contextObjects, contextualOnly);
Check(AtEnd("select * from ").Select(c => c.Label).OrderBy(x => x).SequenceEqual(new[] { "Customers", "Orders", "OrderSummary" }.OrderBy(x => x)), "Space after FROM offers only tables and views");
Check(AtEnd("SELECT * FROM O", false).All(c => c.Label.StartsWith("O")) && AtEnd("SELECT * FROM O", false).Count == 2, "FROM prefix filters relation names");
Check(AtEnd("SELECT * FROM dbo.").All(c => c.Detail != "SQL_STORED_PROCEDURE") && AtEnd("SELECT * FROM dbo.").Any(c => c.Insert == "[Orders]"), "schema in FROM filters relations");
Check(AtEnd("SELECT * FROM dbo.Orders o INNER JOIN ").Count == 3, "Space after JOIN offers relations");
Check(AtEnd("SELECT * FROM dbo.Orders, ").Count == 3, "comma-separated FROM offers next relation");
Check(AtEnd("EXEC ").Single().Label == "LoadOrders", "Space after EXEC offers procedures");
Check(AtEnd("SELECT * ").Single().Label == "FROM", "Space after SELECT star offers FROM");
Check(AtEnd("SELECT * FROM dbo.Orders WHERE ").Any(c => c.Label == "CustomerId") && !AtEnd("SELECT * FROM dbo.Orders WHERE ").Any(c => c.Label == "CustomerKey"), "WHERE offers columns of referenced table");
Check(AtEnd("SELECT * FROM dbo.Orders WHERE CustomerId ").Any(c => c.Label == "IS NULL"), "Space after WHERE operand offers operators");
Check(AtEnd("SELECT * FROM dbo.Orders ").Any(c => c.Label == "WHERE") && !AtEnd("SELECT * FROM dbo.Orders ").Any(c => c.Label == "LoadOrders"), "Space after table offers clauses");
Check(AtEnd("UPDATE dbo.Orders ").Single().Label == "SET", "Space after UPDATE table offers SET");
Check(AtEnd("SELECT 'FROM' ").All(c => c.Detail == "T-SQL") && AtEnd("SELECT 'FROM' ").Any(c => c.Label == "FROM"), "literal FROM does not request relations");
Check(AtEnd("PRINT 1 -- FROM ").Count == 0, "no Space completion in a comment");
Check(AtEnd("SELECT * FROM /* source */ ").Count == 3, "comments do not hide relation context");
Check(AtEnd("SELECT * FROM dbo.Orders; EXEC ").Single().Label == "LoadOrders", "context resets at statement boundary");
Check(AtEnd("PRINT ").Count == 0 && AtEnd("PRINT ", false).Any(), "implicit Space suppresses unrelated global list, explicit request remains available");
var rightFrom = "SELECT  FROM sales.Customers";
Check(Engine.Complete(rightFrom, 7, contextObjects, true).Any(c => c.Label == "CustomerKey") && !Engine.Complete(rightFrom, 7, contextObjects, true).Any(c => c.Label == "CustomerId"), "SELECT resolves table written to right of cursor");
Check(AtEnd("SELECT * FROM dbo.Orders; SELECT ").Any(c => c.Label == "CustomerKey"), "SELECT does not inherit previous statement column scope");
Check(AtEnd("SELECT Id ").Any(c => c.Label == "FROM"), "Space after SELECT column offers FROM");
Check(AtEnd("SELECT TOP (100) ").Any(c => c.Label == "Id") && AtEnd("SELECT TOP (100) ").Any(c => c.Label == "*"), "Space after TOP offers select expressions");
var flexible = new CompletionOptions { FuzzyMatching = true, TableAliases = true, QualifyColumns = true };
var alarms = new[] {
    new DbObject { Schema = "CRM", Name = "Alarm_Recivers", Kind = "USER_TABLE", Columns = new List<string> { "Id", "ReceiverName" } },
    new DbObject { Schema = "CRM", Name = "Alarm_Observed", Kind = "USER_TABLE", Columns = new List<string> { "Id", "ObservedAt" } },
    new DbObject { Schema = "dbo", Name = "Archive", Kind = "VIEW", Columns = new List<string> { "Id" } }
};
List<Candidate> Suggest(string text) => Engine.Complete(text, text.Length, alarms, options: flexible);
Check(Suggest("SELECT * FROM ar").Any(c => c.Label == "Alarm_Recivers"), "ar matches Alarm_Recivers initials");
Check(Engine.MatchScore("Alarm_Recivers", "rcv", true) >= 0, "ordered subsequence matching");
Check(Engine.MatchScore("Alarm_Recivers", "Reciv", true) >= 0, "contains matching");
Check(Engine.MatchScore("Alarm_Recivers", "ar", false) == -1, "prefix-only setting");
Check(Suggest("SELECT * FROM ar").First().Label == "Archive", "prefix ranks before initials");
Check(Suggest("SELECT * FROM ar").Single(c => c.Label == "Alarm_Recivers").Insert == "[CRM].[Alarm_Recivers] AS [ar]", "relation alias insertion");
Check(Suggest("SELECT * FROM CRM.ar").Single(c => c.Label == "Alarm_Recivers").Insert == "[Alarm_Recivers] AS [ar]", "schema-qualified alias insertion");
Check(Suggest("SELECT * FROM CRM.Alarm_Recivers AS [ar] JOIN ar").Single(c => c.Label == "Alarm_Recivers").Insert.EndsWith("AS [ar2]"), "alias collision suffix");
var existingAlias = "SELECT * FROM ar AS a";
Check(!Engine.Complete(existingAlias, 16, alarms, options: flexible).Single(c => c.Label == "Alarm_Recivers").Insert.Contains(" AS "), "preserve an existing alias to right");
Check(!Suggest("INSERT INTO ar").Single(c => c.Label == "Alarm_Recivers").Insert.Contains(" AS "), "INSERT target has no generated alias");
Check(!Suggest("UPDATE ar").Single(c => c.Label == "Alarm_Recivers").Insert.Contains(" AS "), "UPDATE target has no generated alias");
Check(Suggest("SELECT * FROM CRM.Alarm_Recivers AS [ar] WHERE Id").Any(c => c.Insert == "[ar].[Id]"), "columns use quoted existing alias");
Check(Suggest("SELECT * FROM CRM.Alarm_Recivers AS [ar] WHERE ar.Rec").Single().Insert == "[ReceiverName]", "explicit alias dot avoids duplicate qualifier");
Check(Suggest("SELECT * FROM CRM.Alarm_Recivers AS [ar] JOIN CRM.Alarm_Observed AS [ao] ON Id").Count(c => c.Label == "Id") == 2, "ambiguous columns retain both aliases");
Check(Suggest("SELECT * FROM CRM.Alarm_Recivers AS [ar] ").All(c => c.Category == "Keyword"), "aliases do not inject columns in clause suggestions");
Check(Suggest("SELECT * FROM ar").Single(c => c.Label == "Alarm_Recivers").Category == "Table", "semantic candidate category");
Check(Suggest("SELECT * FROM CRM.Alarm_Recivers AS ").All(c => c.Category == "Alias") && Suggest("SELECT * FROM CRM.Alarm_Recivers AS ").Single().Insert == "[ar]", "AS expects alias rather than clauses");
Check(Suggest("SELECT x. FROM CRM.Alarm_Recivers ar").Count == 0, "unknown qualifier has no unrelated columns");
var fkCatalog = contextObjects.ToList();
var orders = fkCatalog.Single(o => o.Name == "Orders");
var customers = fkCatalog.Single(o => o.Name == "Customers");
orders.ForeignKeys.Add(new ForeignKey { Name = "FK_Orders_Customers", Source = orders, Target = customers, Columns = new List<Tuple<string, string>> { Tuple.Create("CustomerId", "CustomerKey") } });
List<Candidate> Fk(string text) => Engine.Complete(text, text.Length, fkCatalog, options: flexible);
Check(Fk("SELECT * FROM dbo.Orders o JOIN ").First().Label == "Customers" && Fk("SELECT * FROM dbo.Orders o JOIN ").First().Category == "FK Table", "related JOIN target first and marked");
Check(Fk("SELECT * FROM sales.Customers c JOIN ").First().Label == "Orders", "reverse relation priority");
Check(Fk("SELECT * FROM dbo.Orders o JOIN sales.Customers c ON ").First().Insert == "[o].[CustomerId] = [c].[CustomerKey]", "ON uses actual FK and aliases");
Check(Fk("SELECT * FROM sales.Customers c JOIN dbo.Orders o ON ").First().Insert == "[o].[CustomerId] = [c].[CustomerKey]", "reverse ON preserves FK direction");
orders.ForeignKeys[0].Columns.Add(Tuple.Create("Id", "Name"));
Check(Fk("SELECT * FROM dbo.Orders o JOIN sales.Customers c ON ").First().Insert == "[o].[CustomerId] = [c].[CustomerKey] AND [o].[Id] = [c].[Name]", "composite foreign key stays one condition");
Check(!Fk("SELECT * FROM dbo.Orders o JOIN dbo.OrderSummary s ON ").Any(c => c.Category == "FK Join"), "no inferred relation for unrelated table");
Check(!Fk("SELECT * FROM dbo.Orders o; SELECT * FROM dbo.OrderSummary s JOIN ").Any(c => c.Category == "FK Table"), "relations do not leak between statements");
Check(!Fk("SELECT * FROM dbo.OrderSummary s WHERE EXISTS (SELECT * FROM dbo.Orders o) JOIN ").Any(c => c.Category == "FK Table"), "closed subquery does not leak relations");
Check(Fk("SELECT * FROM dbo.Orders o JOIN dbo.Orders other ON ").All(c => c.Category != "FK Join"), "same table is not a guessed self relation");
orders.ForeignKeys.Add(new ForeignKey { Name = "FK_Orders_Parent", Source = orders, Target = orders, Columns = new List<Tuple<string, string>> { Tuple.Create("CustomerId", "Id") } });
Check(Fk("SELECT * FROM dbo.Orders o JOIN dbo.Orders p ON ").Count(c => c.Category == "FK Join") == 2, "self foreign key offers both alias directions");
TextExpansion Star(string sql, int caret = -1) => Engine.ExpandStar(sql, caret < 0 ? sql.IndexOf('*') + 1 : caret, fkCatalog);
Check(Star("SELECT * FROM dbo.Orders o").Text == "[o].[Id],\n    [o].[CustomerId]", "star expands vertically in catalog order");
var qualifiedStar = Star("SELECT o.* FROM dbo.Orders o JOIN sales.Customers c ON c.CustomerKey=o.CustomerId");
Check(qualifiedStar.Start == 7 && qualifiedStar.Length == 3 && !qualifiedStar.Text.Contains("[c]"), "qualified star replaces qualifier and only its columns");
Check(Star("SELECT * FROM dbo.Orders o JOIN sales.Customers c ON c.CustomerKey=o.CustomerId").Text.Contains("[c].[Name]"), "unqualified star covers joined tables in order");
Check(Star("SELECT COUNT(*) FROM dbo.Orders") == null, "COUNT star stays untouched");
Check(Star("SELECT 2*3 FROM dbo.Orders") == null, "arithmetic star stays untouched");
Check(Star("SELECT '*' FROM dbo.Orders") == null, "string star stays untouched");
Check(Star("SELECT * FROM Missing") == null, "unknown table cannot expand star");
Check(Star("SELECT * FROM (SELECT Id FROM dbo.Orders) d") == null, "derived table expansion is conservative");
Check(Star("SELECT * FROM dbo.Orders; SELECT * FROM sales.Customers", 8).Text.Contains("CustomerId") && !Star("SELECT * FROM dbo.Orders; SELECT * FROM sales.Customers", 8).Text.Contains("CustomerKey"), "star stays in its SELECT");
Check(Star("SELECT * FROM dbo.OrderSummary WHERE EXISTS (SELECT * FROM dbo.Orders)", 8).Text == "[dbo].[OrderSummary].[Total]", "star does not include nested query tables");
Check(Star("WITH Orders AS (SELECT CustomerKey FROM sales.Customers) SELECT * FROM Orders") == null, "CTE cannot masquerade as catalog table");
Check(!Fk("SELECT * FROM Missing WHERE ").Any(c => c.Category == "Column"), "unknown FROM source has no unrelated catalog columns");
Check(Fk("SELECT * FROM dbo.Orders o WHERE Id = ").Any(c => c.Insert == "[o].[CustomerId]") && !Fk("SELECT * FROM dbo.Orders o WHERE Id = ").Any(c => c.Category == "Table"), "comparison operand stays in column context");
Check(Engine.MatchedCharacters("Alarm_Recivers", "ar", true).SequenceEqual(new[] { 0, 6 }), "highlight initials at actual word boundaries");
Check(Engine.MatchedCharacters("Customers", "sto", true).SequenceEqual(new[] { 2, 3, 4 }), "highlight contiguous contains match");
Check(Engine.MatchedCharacters("Customers", "ctm", true).SequenceEqual(new[] { 0, 3, 5 }), "highlight ordered subsequence");
Check(Engine.MatchedCharacters("SELECT", "se", false).SequenceEqual(new[] { 0, 1 }), "highlight case insensitive prefix");
Check(Engine.MatchedCharacters("Customers", "zz", true).Length == 0 && Engine.MatchedCharacters("Customers", "", true).Length == 0, "no highlight on empty or unmatched input");
var schemaCatalog = new[] { new DbObject { Schema = "dbo", Name = "Orders", Kind = "USER_TABLE", Columns = new List<string> { "Id" } }, new DbObject { Schema = "sales", Name = "Orders", Kind = "VIEW", Columns = new List<string> { "Id" } } };
var schemaRows = Engine.Complete("SELECT * FROM sales.Or", 22, schemaCatalog, options: new CompletionOptions { FuzzyMatching = true });
Check(schemaRows.Count == 1 && schemaRows[0].Detail == "sales · View" && schemaRows[0].MatchIndices.SequenceEqual(new[] { 0, 1 }), "schema qualifier preserves correct duplicate object and highlight");
var schemaColumns = Engine.Complete("SELECT I", 8, schemaCatalog);
Check(schemaColumns.Count(c => c.Category == "Column" && c.Label == "Id") == 2 && schemaColumns.Any(c => c.Detail == "[sales].[Orders]"), "unqualified columns retain their schema and table");
var personalRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SqlPilotPersonalTest_" + Guid.NewGuid().ToString("N"));
try
{
    var snippetStore = new SnippetStore(System.IO.Path.Combine(personalRoot, "snippets.xml"));
    var shortcuts = snippetStore.Load();
    var selectSql = shortcuts["ssf"];
    shortcuts.Remove("ssf");
    shortcuts.Add("sf", selectSql);
    shortcuts.Add("myq", "SELECT 1;\nSELECT 2; ");
    snippetStore.Save(shortcuts);
    var loadedShortcuts = snippetStore.Load();
    Check(!loadedShortcuts.ContainsKey("ssf") && loadedShortcuts["SF"] == selectSql && loadedShortcuts["myq"].EndsWith("; "), "snippet rename persists without reviving old defaults and preserves whitespace");
    var customRows = Engine.Complete("sf", 2, Array.Empty<DbObject>(), options: new CompletionOptions { Snippets = loadedShortcuts });
    Check(customRows.Any(c => c.Category == "Snippet" && c.Label == "sf" && c.Insert == selectSql) && !customRows.Any(c => c.Label == "ssf"), "custom snippets drive autocomplete");
    bool invalidShortcut = false;
    try
    {
        SnippetStore.Validate(new Dictionary<string, string> { { "bad key", "SELECT 1" } });
    }
    catch (ArgumentException) { invalidShortcut = true; }
    Check(invalidShortcut, "invalid shortcut rejected");
    var library = new SqlLibrary(System.IO.Path.Combine(personalRoot, "Library"));
    var entry = library.Save(new LibraryEntry { Title = "Customer report", Category = "Reports", Tags = "crm daily", Sql = "SELECT N'آرش < & >';\nSELECT 2; " });
    Check(System.IO.File.ReadAllText(System.IO.Path.Combine(personalRoot, "Library", "Files", entry.Id + ".sql")) == entry.Sql, "library save writes a real SQL file immediately");
    library.Save(new LibraryEntry { Title = "Cleanup", Category = "Maintenance", Tags = "archive", Sql = "SELECT 9" });
    var restored = library.Load().Single(e => e.Id == entry.Id);
    Check(restored.Sql == entry.Sql && restored.Category == "Reports", "library XML roundtrip preserves Unicode SQL and metadata");
    Check(SqlLibrary.Search(library.Load(), "daily SELECT", "Reports").Single().Id == entry.Id && !SqlLibrary.Search(library.Load(), "daily", "Maintenance").Any(), "library search combines content terms and category filter");
    restored.Title = "Renamed report";
    restored.Category = "CRM";
    library.Save(restored);
    Check(library.Load().Count == 2 && library.Load().Single(e => e.Id == entry.Id).Category == "CRM", "library edit retains identity and changes category");
    Check(System.IO.File.ReadAllText(library.SqlPath(restored)) == restored.Sql, "library opens real SQL file with exact saved text");
    var unused = library.Load().Single(e => e.Id != entry.Id);
    string legacyPath = System.IO.Path.Combine(personalRoot, "Library", unused.Id + ".xml");
    var legacyEntry = System.Xml.Linq.XElement.Load(legacyPath);
    legacyEntry.Element("LastInsertedUtc").Remove();
    legacyEntry.Add(new System.Xml.Linq.XElement("ExtraMetadata", "preserve"));
    PersonalFiles.Write(legacyPath, legacyEntry.ToString());
    Check(library.Load().All(e => e.LastInsertedUtc == DateTime.MinValue), "Legacy library entries load without insertion history");
    string sqlFile = library.SqlPath(restored);
    var sqlWriteTime = System.IO.File.GetLastWriteTimeUtc(sqlFile);
    library.MarkInserted(unused.Id);
    Check((string)System.Xml.Linq.XElement.Load(legacyPath).Element("ExtraMetadata") == "preserve", "Usage recording preserves extra metadata in older library files");
    Check(new SqlLibrary(System.IO.Path.Combine(personalRoot, "Library")).Load().First().Id == unused.Id, "Recent INSERT order persists after reloading the library");
    library.MarkInserted(restored.Id);
    var inserted = library.Load().First();
    Check(inserted.Id == restored.Id && inserted.LastInsertedUtc > library.Load().Single(e => e.Id == unused.Id).LastInsertedUtc, "Latest inserted query is first even for consecutive inserts");
    Check(inserted.UpdatedUtc == restored.UpdatedUtc && inserted.Sql == restored.Sql && System.IO.File.GetLastWriteTimeUtc(sqlFile) == sqlWriteTime, "Insertion usage preserves SQL content modification time and SQL files");
    library.Save(new LibraryEntry { Id = unused.Id, Title = unused.Title, Category = unused.Category, Tags = unused.Tags, Sql = unused.Sql });
    Check(library.Load().First().Id == restored.Id && library.Load().Single(e => e.Id == unused.Id).LastInsertedUtc > DateTime.MinValue, "Editing an entry preserves usage order with a fresh editor copy");
    Check(SqlLibrary.Search(library.Load(), "SELECT", null).First().Id == restored.Id, "Library search preserves recent insertion ordering");
    bool traversal = false;
    try
    {
        library.SqlPath(new LibraryEntry { Id = "../escape", Sql = "x" });
    }
    catch (ArgumentException) { traversal = true; }
    Check(traversal, "library path traversal rejected");
    snippetStore.Save(new Dictionary<string, string>());
    Check(snippetStore.Load().Count == 0, "empty custom snippet collection remains empty");
}
finally { if (System.IO.Directory.Exists(personalRoot)) System.IO.Directory.Delete(personalRoot, true); }
var navObjects = new[] { new DbObject { Schema = "dbo", Name = "GetOrders", Kind = "SQL_STORED_PROCEDURE" }, new DbObject { Schema = "sales", Name = "Orders", Kind = "USER_TABLE", Columns = new List<string> { "Id" } }, new DbObject { Schema = "dbo", Name = "Orders", Kind = "USER_TABLE" } };
List<DbObject> Ref(string text, string word) => Engine.ReferenceAt(text, text.IndexOf(word, StringComparison.Ordinal) + 1, navObjects, "MainDb");
Check(Ref("EXEC dbo.GetOrders @Id=1;", "GetOrders").Single().Kind == "SQL_STORED_PROCEDURE", "F12 resolves procedure under caret");
Check(Ref("SELECT * FROM [sales].[Orders];", "Orders").Single().Schema == "sales", "navigation preserves quoted schema");
Check(Ref("SELECT o.Id FROM sales.Orders AS o;", "o.Id").Single().Schema == "sales", "navigation resolves alias qualified column to table");
Check(Ref("SELECT * FROM Orders;", "Orders").Count == 2, "ambiguous object names remain selectable");
Check(Ref("SELECT 'Orders';", "Orders").Count == 0 && Ref("-- Orders\nSELECT 1;", "Orders").Count == 0, "navigation ignores strings and comments");
bool foreignDatabase = false;
try { Ref("SELECT * FROM OtherDb.sales.Orders;", "Orders"); } catch (InvalidOperationException) { foreignDatabase = true; }
Check(foreignDatabase && Ref("SELECT * FROM MainDb.sales.Orders;", "Orders").Single().Schema == "sales", "navigation rejects wrong database rather than resolving local names");
string modify = Engine.ModifyModule("-- heading\nCREATE OR ALTER PROC dbo.OldName @Id int AS SELECT @Id;", "sales", "New]Name");
Check(modify == "-- heading\nALTER PROC [sales].[New]]Name] @Id int AS SELECT @Id;", "modify script rewrites only header with catalog schema and escaped name");
Check(Engine.ModifyModule("ALTER PROCEDURE [dbo].[GetOrders] AS SELECT 'CREATE PROC';", "dbo", "GetOrders").StartsWith("ALTER PROCEDURE [dbo].[GetOrders]"), "existing ALTER definition remains a modify script");
Check(Engine.ObjectUrn("server'1", "db'1", navObjects[1]) == "Server[@Name='server''1']/Database[@Name='db''1']/Table[@Name='Orders' and @Schema='sales']", "Object Explorer URN includes escaped server database schema and table");
var callProcedure = new DbObject { Schema = "sales", Name = "GetCustomer", Kind = "SQL_STORED_PROCEDURE", Parameters = new List<ProcedureArgument> { new ProcedureArgument { Name = "@Id", Type = "int" }, new ProcedureArgument { Name = "@Note", Type = "nvarchar(50)", Optional = true }, new ProcedureArgument { Name = "@Total", Type = "decimal(10,2)", Output = true }, new ProcedureArgument { Name = "@Rows", Type = "[dbo].[RowList]", ReadOnly = true } } };
Candidate Call(string sql) => Engine.Complete(sql, sql.Length, new[] { callProcedure }).Single(c => c.Category == "Procedure");
var call = Call("EXEC GetC");
Check(call.Insert.StartsWith("[sales].[GetCustomer]\n    @Id = NULL") && !call.Insert.Contains("@Note") && call.Insert.Contains("@Total = @Total OUTPUT"), "procedure completion expands required arguments and output variable");
Check(call.Placeholders.Count == 3 && call.Insert.Substring(call.Placeholders[0].Offset, call.Placeholders[0].Length) == "NULL" && call.Insert.Substring(call.Placeholders[1].Offset, call.Placeholders[1].Length) == "@Total", "procedure placeholders point to editable values in argument order");
Check(call.Insert.Contains("@Rows = @Rows /* [dbo].[RowList]; table variable */"), "table valued procedure argument requests a variable");
Check(Call("EXEC sales.GetC").Insert.StartsWith("[GetCustomer]\n") && !Call("EXEC sales.GetC").Insert.StartsWith("EXEC"), "schema-qualified EXEC preserves existing prefix");
Check(Call("GetC").Insert.StartsWith("EXEC [sales].[GetCustomer]"), "procedure completion without EXEC inserts call keyword");
Check(Engine.ProcedureDefaults("CREATE PROC dbo.p @Id int, @Note nvarchar(10) = N'a,b', @Flag int = NULL AS SELECT 1;").SetEquals(new[] { "@Note", "@Flag" }), "procedure defaults parsed from TSQL rather than unreliable catalog flag");
var noArgs = new DbObject { Schema = "dbo", Name = "NoArgs", Kind = "SQL_STORED_PROCEDURE" };
Check(Engine.Complete("EXEC No", 7, new[] { noArgs }).Single().Placeholders.Count == 0, "parameterless procedure adds no placeholders");
var screenCatalog = new[] { new DbObject { Schema = "dbo", Name = "AiMessages", Kind = "USER_TABLE", Columns = new List<string> { "Id", "UserId", "Body" } }, new DbObject { Schema = "dbo", Name = "Users", Kind = "USER_TABLE", Columns = new List<string> { "Id", "Name" } } };
List<Candidate> Suggestions(string sql, bool explicitRequest = false) => Engine.Complete(sql, sql.Length, screenCatalog, contextualOnly: !explicitRequest, options: new CompletionOptions { QualifyColumns = true });
// Command fragments must be filtered within their grammar position, not the catalog.
foreach (bool explicitRequest in new[] { false, true })
{
    foreach (string sql in new[] { "insert in", "INSERT i", "INSERT ", "INSERT\n/* target */ in", "SELECT 1; INSERT in", "SELECT 1\nGO\nINSERT in", "INSERT TOP (5) in", "DELETE TOP (5) fr" })
    {
        string expected = sql.StartsWith("DELETE", StringComparison.Ordinal) ? "FROM" : "INTO";
        var rows = Engine.RequestCompletion(sql, sql.Length, screenCatalog, explicitRequest).Candidates;
        Check(rows.Count == 1 && rows[0].Label == expected && rows[0].Category == "Keyword", "Command grammar: " + sql.Replace('\n', ' ') + " explicit=" + explicitRequest);
    }
}
foreach (var pair in new[] {
    Tuple.Create("DELETE fr", "FROM"), Tuple.Create("TRUNCATE ta", "TABLE"),
    Tuple.Create("CREATE pr", "PROCEDURE"), Tuple.Create("ALTER pr", "PROCEDURE"), Tuple.Create("DROP ta", "TABLE"),
    Tuple.Create("CREATE OR ALTER pr", "PROCEDURE"), Tuple.Create("CREATE OR al", "ALTER"), Tuple.Create("MERGE in", "INTO"),
    Tuple.Create("SELECT * FROM dbo.Users LEFT j", "JOIN"),
    Tuple.Create("SELECT * FROM dbo.Users LEFT OUTER j", "JOIN"),
    Tuple.Create("SELECT * FROM dbo.Users INNER j", "JOIN"),
    Tuple.Create("SELECT * FROM dbo.Users CROSS ap", "APPLY"),
    Tuple.Create("SELECT * FROM dbo.Users OUTER ap", "APPLY"),
    Tuple.Create("SELECT Id FROM dbo.Users UNION s", "SELECT"),
    Tuple.Create("SELECT Id FROM dbo.Users UNION ALL s", "SELECT"),
    Tuple.Create("SELECT Id FROM dbo.Users INTERSECT s", "SELECT"),
    Tuple.Create("SELECT Id FROM dbo.Users EXCEPT s", "SELECT"),
    Tuple.Create("SELECT * FROM dbo.Users ORDER b", "BY"),
    Tuple.Create("SELECT * FROM dbo.Users GROUP b", "BY") })
{
    var rows = Engine.RequestCompletion(pair.Item1, pair.Item1.Length, screenCatalog, true).Candidates;
    Check(rows.Any(c => c.Label == pair.Item2) && rows.All(c => c.Category == "Keyword"), "Context-only command continuation: " + pair.Item1);
}
foreach (string sql in new[] { "SELECT * FROM dbo.Users WHERE LEFT ", "SELECT LEFT ", "SELECT * FROM dbo.Users WHERE Id = LEFT(", "SELECT [LEFT] " })
    Check(!Suggestions(sql, true).Any(c => c.Label == "JOIN" || c.Label == "OUTER JOIN"), "JOIN modifiers do not leak into scalar expressions: " + sql);
foreach (string sql in new[] { "INSERT in", "INSERT i", "INSERT INTO Us" })
    Check(Engine.Complete(sql, sql.Length, screenCatalog, options: new CompletionOptions { FuzzyMatching = false }).Count == 1, "Command filtering also works with prefix-only matching: " + sql);
foreach (string sql in new[] { "INSERT TOP (5) INTO dbo.Users VALUES (1) ", "DELETE TOP (5) FROM dbo.Users WHERE Id IN (1,2) " })
    Check(!Suggestions(sql, true).Any(c => c.Label == "INTO" || c.Label == "FROM"), "TOP header does not consume the later statement body: " + sql);
Check(Suggestions("INSERT TOP (ABS(@n)) in", true).Single().Label == "INTO", "Nested TOP expression retains target-keyword context");
Check(Suggestions("INSERT INTO Us", true).Single().Category == "Table", "INTO transitions to target tables");
Check(Suggestions("SELECT * FROM dbo.Users WHERE Id in", true).Any(c => c.Label == "IN") && !Suggestions("SELECT * FROM dbo.Users WHERE Id in", true).Any(c => c.Label == "INTO"), "IN remains a predicate operator");
Check(Suggestions("SELECT * FROM dbo.Users WHERE Id IN (", true).Any(c => c.Category == "Column"), "IN list still requests values");
Check(Suggestions("INSERT /* unfinished in", true).Count == 0 && Suggestions("SELECT N'insert in", true).Count == 0, "Command completion preserves comment and literal boundaries");
string crossScreen = "SELECT * FROM [dbo].[AiMessages] AS [am] CROSS JOIN [dbo].[Users] AS [u] ";
Check(Suggestions(crossScreen).First().Label == "WHERE" && !Suggestions(crossScreen).Any(c => c.Label == "ON"), "screenshot CROSS JOIN offers legal continuations with WHERE first");
string invalidScreen = crossScreen + "ON [am].[UserId] = [u].[Id] ";
Check(Suggestions(invalidScreen).Count == 0, "invalid CROSS JOIN ON does not propose further invalid operators");
var correction = Engine.Analyze(invalidScreen).Single(i => i.Code == "JOIN-ON");
string corrected = invalidScreen.Remove(correction.Offset, correction.Length).Insert(correction.Offset, correction.Replacement);
Check(!Engine.Analyze(corrected).Any(i => i.Code == "SYNTAX"), "Analyze offers reviewable CROSS to INNER correction preserving predicate");
string joined = "SELECT * FROM dbo.AiMessages am INNER JOIN dbo.Users u ON ";
Check(Suggestions(joined + "am.UserId = u.Id ").First().Label == "AND" && !Suggestions(joined + "am.UserId = u.Id ").Any(c => c.Label == "="), "complete joined predicate suggests connectors rather than another operator");
Check(Suggestions(joined + "am.UserId = u.Id A", true).Any(c => c.Label == "AND") && !Suggestions(joined + "am.UserId = u.Id A", true).Any(c => c.Category == "Column"), "typed connector prefix and explicit completion retain predicate context");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id ").Any(c => c.Label == "=") && !Suggestions("SELECT * FROM dbo.Users u WHERE u.Id ").Any(c => c.Label == "AND"), "uncompared operand requests an operator");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id = ").Any(c => c.Category == "Column") && !Suggestions("SELECT * FROM dbo.Users u WHERE u.Id = ").Any(c => c.Label == "AND"), "missing comparison RHS requests values");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id = 1 AND u.Id ").Any(c => c.Label == "="), "later Boolean atom requests its own comparison operator");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id = 1 AND ").Any(c => c.Category == "Column"), "AND starts a fresh predicate");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id BETWEEN 1 ").Select(c => c.Label).SequenceEqual(new[] { "AND" }), "BETWEEN lower bound requests range AND");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id BETWEEN 1 AND ").Any(c => c.Category == "Column") && !Suggestions("SELECT * FROM dbo.Users u WHERE u.Id BETWEEN 1 AND ").Any(c => c.Label == "EXISTS"), "BETWEEN upper bound does not become a Boolean EXISTS predicate");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id BETWEEN 1 AND 10 ").First().Label == "AND", "completed BETWEEN requests predicate continuation");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id IS ").Select(c => c.Label).SequenceEqual(new[] { "NULL", "NOT NULL" }), "IS requests only NULL forms");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id IS NOT ").Single().Label == "NULL", "IS NOT requests NULL");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id IS NOT NULL ").First().Label == "AND", "complete NULL predicate does not repeat comparison operators");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Name LIKE N'x%' ").First().Label == "AND", "complete LIKE predicate has logical continuations");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE u.Id IN (1,2) ").First().Label == "AND", "complete IN list has logical continuations");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE EXISTS (SELECT 1 FROM dbo.AiMessages am WHERE am.UserId=u.Id) ").First().Label == "AND", "closed EXISTS subquery retains outer predicate completion");
Check(Suggestions("SELECT * FROM dbo.Users u WHERE (u.Id = 1 ").Any(c => c.Label == ")") && !Suggestions("SELECT * FROM dbo.Users u WHERE (u.Id = 1 ").Any(c => c.Label == "WHERE"), "unclosed Boolean group requests connector or close parenthesis");
Check(Suggestions("SELECT * FROM dbo.AiMessages am JOIN dbo.Users u ").Single().Label == "ON", "ordinary JOIN requires ON");
Check(!Suggestions("SELECT * FROM dbo.AiMessages am CROSS APPLY (SELECT * FROM dbo.Users) u ").Any(c => c.Label == "ON"), "APPLY never requests ON");
Check(Suggestions("SELECT * FROM dbo.Users ORDER BY Id ").Select(c => c.Label).SequenceEqual(new[] { "ASC", "DESC", ",", "OFFSET" }), "ORDER BY completed item has legal directions and continuation");
Check(Suggestions("SELECT * FROM dbo.Users GROUP BY Id ").Select(c => c.Label).SequenceEqual(new[] { ",", "HAVING", "ORDER BY" }), "GROUP BY completed item offers grouping continuation");
Check(Suggestions("SELECT Id + ").Any(c => c.Category == "Column") && !Suggestions("SELECT Id + ").Any(c => c.Label == "FROM"), "SELECT arithmetic operator requests operand");
Check(Suggestions("SELECT COUNT(*) ").Any(c => c.Label == "FROM"), "completed SELECT function can lead to FROM");
Check(Suggestions("UPDATE dbo.Users SET Name = N'x' ").Any(c => c.Label == "WHERE") && !Suggestions("UPDATE dbo.Users SET Name = N'x' ").Any(c => c.Label == "GROUP BY"), "complete UPDATE assignment offers DML continuation");
Check(!Suggestions("DELETE FROM dbo.Users WHERE Id = 1 ").Any(c => c.Label == "GROUP BY" || c.Label == "ORDER BY"), "DELETE predicate does not inherit SELECT-only clauses");
Check(Suggestions("SELECT * FROM dbo.Users WHERE ").Count(c => c.Label == "EXISTS") == 1, "contextual suggestions do not duplicate EXISTS");

Check(Suggestions("SELECT Name ").First().Label == "AS", "AS preferred after completed SELECT expression");
Check(Suggestions("SELECT * FROM dbo.Users ").First().Label == "AS", "AS offered before an unnamed source continuation");
Check(Suggestions("SELECT * FROM dbo.Users u JOIN dbo.AiMessages ").First().Label == "AS", "AS preferred before JOIN ON when alias is missing");
Check(Engine.AutoConversion("SELECT * FROM dbo.Users WHERE Id = 1 && ", 41) == null, "conversion rejects out of range caret");
string convert = "SELECT * FROM dbo.Users WHERE Id = 1 && ";
Check(Engine.AutoConversion(convert, convert.Length)?.Replacement == "AND", "typed Boolean AND conversion");
convert = "SELECT * FROM dbo.Users WHERE Id || ";
Check(Engine.AutoConversion(convert, convert.Length) == null, "incomplete predicate does not convert Boolean operator");
convert = "SELECT * FROM dbo.Users WHERE Id == ";
Check(Engine.AutoConversion(convert, convert.Length)?.Replacement == "=", "typed equality conversion");
convert = "SELECT * FROM dbo.Users WHERE Id != ";
Check(Engine.AutoConversion(convert, convert.Length)?.Replacement == "<>", "typed inequality conversion");
convert = "SELECT N'&& ' ";
Check(Engine.AutoConversion(convert, convert.Length) == null, "conversion leaves string literals untouched");
convert = "-- WHERE Id = 1 && ";
Check(Engine.AutoConversion(convert, convert.Length) == null, "conversion leaves comments untouched");
convert = "SELECT x || ";
Check(Engine.AutoConversion(convert, convert.Length) == null, "conversion leaves concatenation expressions untouched");
string commented = "-- header\nSELECT /* hint */ Id -- identifier\nFROM dbo.Users\nWHERE Id=1; -- tail";
string kept = Engine.Format(commented);
Check(new[] { "-- header", "/* hint */", "-- identifier", "-- tail" }.All(c => kept.Contains(c)) && !Engine.Analyze(kept).Any(c => c.Code == "SYNTAX"), "formatter preserves all comment types and valid SQL");
Check(Engine.Format("SELECT N'-- literal'; /* end */").Contains("/* end */"), "formatter preserves trailing block comment and literal");
var inline = Engine.InlineIssues("SELECT * FROM dbo.Users;");
Check(inline.Single().Offset == 0 && inline.Single().Length == 6 && inline.Single().Severity == IssueSeverity.Suggestion, "SELECT star suggestion anchors first section word");
inline = Engine.InlineIssues("-- header\nUPDATE dbo.Users SET Id = 1;");
Check(inline.Any(m => m.Offset == 10 && m.Length == 6 && m.Severity == IssueSeverity.Warning), "unsafe UPDATE warning ignores leading comment");
inline = Engine.InlineIssues("SELECT TOP (5) * FROM dbo.Users;");
Check(inline.Count == 2 && inline.Any(m => m.Offset == 7 && m.Length == 3 && m.Severity == IssueSeverity.Warning) && inline.Any(m => m.Offset == 0 && m.Severity == IssueSeverity.Suggestion), "SELECT and TOP diagnostics mark their own section words");
string nullSql = "SELECT Id FROM dbo.Users WHERE Id = NULL;";
inline = Engine.InlineIssues(nullSql);
Check(inline.Single().Offset == nullSql.IndexOf("WHERE") && inline.Single().Length == 5, "NULL warning anchors clause while fix span remains expression");
var nullFix = Engine.Analyze(nullSql).Single();
Check(nullSql.Substring(nullFix.Offset, nullFix.Length) == "Id = NULL", "display anchoring preserves exact fix span");
inline = Engine.InlineIssues("SELECT Id FROM ;");
Check(inline.Any(m => m.Severity == IssueSeverity.Error && m.Offset == 10 && m.Length == 4), "syntax error anchors FROM section");
inline = Engine.InlineIssues("SELECT Id FROM dbo.Users; DELETE FROM dbo.Users;");
Check(inline.Single().Offset == 26 && inline.Single().Length == 6, "diagnostics remain in affected statement");
Check(Engine.InlineIssues("-- SELECT * FROM ;\nSELECT Id FROM dbo.Users WHERE Id = 1;").Count == 0, "valid bounded SQL and comment do not get diagnostics");
Check(Engine.InlineIssues("").Count == 0 && Engine.InlineIssues("   -- comment").Count == 0, "empty and comment-only buffers have no marks");
inline = Engine.InlineIssues("SELECT * FROM dbo.Users CROSS JOIN dbo.Teams ON Users.Id = Teams.Id;");
Check(inline.Any(m => m.Severity == IssueSeverity.Error && m.Issues.Any(i => i.Code == "JOIN-ON")), "invalid CROSS JOIN predicate is an error");
Check(Engine.InlineIssues("SELECT (").All(m => m.Offset >= 0 && m.Length > 0 && m.Offset + m.Length <= 8), "incomplete SQL produces bounded display spans");
string fixSql = "SELECT * FROM dbo.Orders AS o; -- keep tail";
var finding = Engine.Analyze(fixSql).Single(i => i.Code == "SELECT-STAR");
var fixResult = Engine.DiagnosticFix(fixSql, finding, objects);
Check(fixResult != null && fixResult.Text.Contains("[o].[Id]") && fixResult.Text.Contains("[o].[CustomerId]"), "suggestion fix expands actual alias-qualified columns");
string fixedSql = fixSql.Remove(fixResult.Start, fixResult.Length).Insert(fixResult.Start, fixResult.Text);
Check(fixedSql.EndsWith("; -- keep tail") && !Engine.Analyze(fixedSql).Any(i => i.Code == "SELECT-STAR" || i.Code == "SYNTAX"), "suggestion fix preserves surrounding SQL and comments");
Check(Engine.DiagnosticFix(fixSql, finding, Array.Empty<DbObject>()) == null, "suggestion fix does not guess unloaded columns");
fixSql = "SELECT o.* FROM dbo.Orders AS o;";
finding = Engine.Analyze(fixSql).Single(i => i.Code == "SELECT-STAR");
fixResult = Engine.DiagnosticFix(fixSql, finding, objects);
Check(fixResult.Start == 7 && fixResult.Length == 3, "qualified star fix replaces entire qualified projection span");
fixSql = "SELECT Id FROM dbo.Orders WHERE NULL <> CustomerId;";
finding = Engine.Analyze(fixSql).Single();
fixResult = Engine.DiagnosticFix(fixSql, finding, objects);
Check(fixResult.Text == "CustomerId IS NOT NULL", "warning fix applies reversed NULL correction");
finding.Replacement = "malicious replacement";
Check(Engine.DiagnosticFix(fixSql, finding, objects).Text == "CustomerId IS NOT NULL", "warning fix recomputes correction rather than trusting stale replacement");
Check(Engine.DiagnosticFix(fixSql.Replace("NULL", "9999"), finding, objects) == null, "resolved stale finding cannot apply");
fixSql = "SELECT Id FROM dbo.Orders WHERE CustomerId /* important */ = NULL;";
finding = Engine.Analyze(fixSql).Single();
Check(Engine.DiagnosticFix(fixSql, finding, objects) == null, "automatic expression fix does not discard comments");
fixSql = "UPDATE dbo.Orders SET CustomerId = 1; -- keep tail";
finding = Engine.Analyze(fixSql).Single();
Check(Engine.DiagnosticFix(fixSql, finding, objects)?.CaretOffset >= 0 && Engine.CanConfigureDiagnosticFix(finding), "missing WHERE fix inserts a clause for user input");
fixResult = Engine.ConfigureDiagnosticFix(fixSql, finding, "Id = @Id");
fixedSql = fixSql.Insert(fixResult.Start, fixResult.Text);
Check(!Engine.Analyze(fixedSql).Any(i => i.Code == "SYNTAX" || i.Code == "UPDATE-WHERE") && fixedSql.EndsWith("; -- keep tail"), "configured WHERE fixes warning and preserves trailing comment");
Check(Engine.ConfigureDiagnosticFix(fixSql, finding, "1=1; DELETE FROM dbo.Orders") == null, "configured WHERE rejects additional statements");
Check(Engine.ConfigureDiagnosticFix(fixSql, finding, "Id = 1 ORDER BY Id") == null, "configured WHERE rejects unrelated clauses");
Check(Engine.ConfigureDiagnosticFix(fixSql, finding, " ") == null && Engine.ConfigureDiagnosticFix(fixSql, finding, "Id =") == null, "configured WHERE rejects empty and malformed conditions");
fixSql = "DELETE FROM dbo.Orders;";
finding = Engine.Analyze(fixSql).Single();
fixResult = Engine.ConfigureDiagnosticFix(fixSql, finding, "Id = @Id");
Check(fixResult != null && !Engine.Analyze(fixSql.Insert(fixResult.Start, fixResult.Text)).Any(i => i.Code == "DELETE-WHERE" || i.Code == "SYNTAX"), "configured DELETE condition is a valid targeted fix");
fixSql = "SELECT TOP (5) Id FROM dbo.Orders;";
finding = Engine.Analyze(fixSql).Single();
fixResult = Engine.ConfigureDiagnosticFix(fixSql, finding, "Id DESC");
Check(fixResult != null && !Engine.Analyze(fixSql.Insert(fixResult.Start, fixResult.Text)).Any(i => i.Code == "TOP-ORDER" || i.Code == "SYNTAX"), "configured ordering fixes TOP warning");
Check(Engine.ConfigureDiagnosticFix(fixSql, finding, "Id; DROP TABLE dbo.Orders;") == null, "configured ORDER BY rejects additional statements");
Check(Engine.ConfigureDiagnosticFix(fixSql.Replace("TOP (5)", "DISTINCT"), finding, "Id DESC") == null, "configured fix rejects stale diagnostics");
string sections = "SELECT * FROM ;\nSELECT * FROM ;";
var sectionMarks = Engine.InlineIssues(sections).Where(m => m.Issues.Any(i => i.Code == "SYNTAX")).ToList();
Check(sectionMarks.Count == 2 && sectionMarks.Select(m => m.Offset).SequenceEqual(new[] { sections.IndexOf("FROM"), sections.LastIndexOf("FROM") }), "two broken SELECT statements have separate FROM error anchors");
sections = "SELECT * FROM\nSELECT * FROM";
Check(Engine.InlineIssues(sections).Count(m => m.Issues.Any(i => i.Code == "SYNTAX")) == 2, "parser recovery finds both errors without semicolons");
sections = "SELECT TOP(2) Id FROM dbo.Orders; SELECT TOP(3) Id FROM dbo.Orders;";
Check(Engine.InlineIssues(sections).Where(m => m.Issues.Any(i => i.Code == "TOP-ORDER")).Select(m => m.Offset).SequenceEqual(new[] { sections.IndexOf("TOP"), sections.LastIndexOf("TOP") }), "TOP warnings anchor each TOP clause independently");
sections = "SELECT Id FROM dbo.Orders WHERE Id = NULL AND CustomerId = NULL;";
Check(Engine.InlineIssues(sections).Select(m => m.Offset).SequenceEqual(new[] { sections.IndexOf("WHERE"), sections.IndexOf("AND") }), "predicate sections have separate WHERE and AND marks");
Check(Engine.Analyze("SELECT @missing;").Any(i => i.Code == "VARIABLE-UNDECLARED" && i.Message.Contains("@missing")), "undeclared variable has explicit cause");
Check(!Engine.Analyze("DECLARE @Id int; SELECT @Id;").Any(i => i.Code == "VARIABLE-UNDECLARED"), "declared scalar variable is recognized");
Check(!Engine.Analyze("DECLARE @t TABLE(Id int); SELECT * FROM @t;").Any(i => i.Code == "VARIABLE-UNDECLARED"), "declared table variable is recognized");
Check(!Engine.Analyze("CREATE PROCEDURE dbo.p @Id int AS BEGIN SELECT @Id; END;").Any(i => i.Code == "VARIABLE-UNDECLARED"), "procedure parameters are recognized as declarations");
Check(!Engine.Analyze("EXEC dbo.p @Id = 1;").Any(i => i.Code == "VARIABLE-UNDECLARED"), "named EXEC argument is not an undeclared variable");
Check(Engine.Analyze("EXEC dbo.p @Id = @missing;").Count(i => i.Code == "VARIABLE-UNDECLARED") == 1, "EXEC argument value requires a declaration");
Check(Engine.Analyze("DECLARE @Id int;\nGO\nSELECT @Id;").Count(i => i.Code == "VARIABLE-UNDECLARED") == 1, "GO resets variable scope");
Check(!Engine.Analyze("SELECT @@ROWCOUNT; -- @ignored\nSELECT '@literal';").Any(i => i.Code == "VARIABLE-UNDECLARED"), "global variables comments and literals are not flagged");
Check(Engine.Analyze("SELECT @Id; DECLARE @Id int;").Any(i => i.Code == "VARIABLE-UNDECLARED"), "declaration after use does not hide an error");
fixSql = "SELECT @missing;";
finding = Engine.Analyze(fixSql).Single(i => i.Code == "VARIABLE-UNDECLARED");
fixResult = Engine.DiagnosticFix(fixSql, finding, objects);
Check(fixResult != null && fixResult.CaretOffset >= 0 && fixResult.Text.Contains("DECLARE @missing ;"), "variable correction asks for type inline without guessing");
fixSql = "SELECT TOP(3) Id FROM dbo.Orders; -- tail";
finding = Engine.Analyze(fixSql).Single(i => i.Code == "TOP-ORDER");
fixResult = Engine.DiagnosticFix(fixSql, finding, objects);
Check(fixResult != null && fixResult.Text.Contains("ORDER BY ") && fixResult.Text[fixResult.CaretOffset] == '\n' && !fixResult.Text.Contains("Id"), "TOP correction inserts ORDER BY with caret ready for user columns");
fixSql = "SELECT Id FROM dbo.Orders WHERE Id==1;";
finding = Engine.Analyze(fixSql).Single(i => i.Code == "SQL-OPERATOR");
fixResult = Engine.DiagnosticFix(fixSql, finding, objects);
Check(fixResult != null && !Engine.Analyze(fixSql.Remove(fixResult.Start, fixResult.Length).Insert(fixResult.Start, fixResult.Text)).Any(i => i.Code == "SYNTAX"), "operator syntax error has explanatory automatic correction");
Check(!Engine.Analyze("SELECT 'WHERE Id == 1'; -- WHERE Id && x").Any(i => i.Code == "SQL-OPERATOR"), "operator diagnostics ignore strings and comments");
var quiet = new CompletionState();
quiet.Escape();
quiet.Edit("\r\n");
quiet.Edit(" ");
Check(!quiet.Allow("SELECT Id;\n\n", 12, false), "Escape stays dismissed through blank line edits");
quiet.Edit("s");
Check(quiet.Allow("s", 1, false), "typing resumes suggestions after Escape");
quiet.Escape();
Check(quiet.Allow("", 0, true), "explicit completion resumes suggestions on empty line");
Check(!new CompletionState().Allow("SELECT 1;\n  ", 12, false), "empty indented lines do not open unsolicited suggestions");
var rowFunctions = Engine.Complete("SELECT rownumber", 16, objects, options: new CompletionOptions { FuzzyMatching = true });
Check(rowFunctions.Any(c => c.Label == "ROW_NUMBER" && c.Insert.Contains("OVER (ORDER BY column)") && c.Placeholders.Count == 1), "rownumber offers window function template with editable ordering");
Check(!Engine.Complete("SELECT Id FROM dbo.Orders WHERE row", 34, objects, options: new CompletionOptions { FuzzyMatching = true }).Any(c => c.Label == "ROW_NUMBER"), "window functions are not offered directly in WHERE");
var bare = Engine.Complete("SELECT * FROM Or", 16, objects, options: new CompletionOptions { UseBrackets = false, TableAliases = true }).Single();
Check(bare.Insert == "dbo.Orders AS o", "optional brackets apply to generated table and alias");
Check(Engine.OptionalBrackets("[dbo].[Order Details].[Id] + '[keep]' -- [comment]", false) == "dbo.[Order Details].Id + '[keep]' -- [comment]", "required quoting strings and comments are preserved");
Check(Engine.OptionalBrackets("[dbo].[SELECT]", false) == "dbo.[SELECT]", "reserved identifiers remain quoted");
Check(Engine.ExpandStar("SELECT * FROM dbo.Orders AS o", 8, objects, false).Text.StartsWith("o.Id,"), "optional brackets apply to star expansion");
sections = "CREATE PROCEDURE dbo.p AS BEGIN SELECT * FROM ; SELECT * FROM ; END;";
Check(Engine.InlineIssues(sections).Where(m => m.Issues.Any(i => i.Code == "SYNTAX")).All(m => sections.Substring(m.Offset, m.Length) != "END") && Engine.InlineIssues(sections).Count(m => m.Issues.Any(i => i.Code == "SYNTAX")) >= 2, "recover broken SELECTs inside procedure without inventing an END error");
Check(!Engine.Analyze("CREATE FUNCTION dbo.f(@Id int) RETURNS int AS BEGIN RETURN @Id; END;").Any(i => i.Code == "VARIABLE-UNDECLARED"), "function parameters are declarations");

var repair = Engine.RequestCompletion("SELECT * FROM dbo.Ordres ", 25, objects, true);
Check(repair.Candidates.Any(c => c.Label == "Orders"), "explicit completion repairs an unmatched object revisited after a space");
Check(repair.Start == 18 && repair.Length == 6, "explicit correction span preserves trailing whitespace");
Check(Engine.RequestCompletion("SELECT * FROM dbo.Ordres ", 25, objects, false).Candidates.All(c => c.Label != "Orders"), "automatic continuation remains context restricted");
repair = Engine.RequestCompletion("SELECT * FROM dbo.Ordres", 21, objects, true);
Check(repair.Candidates.Any(c => c.Label == "Orders") && repair.Length == 6, "explicit repair replaces an entire mistyped identifier at middle caret");
Check(Engine.RequestCompletion("-- FROM Ordres ", 15, objects, true).Candidates.Count == 0, "explicit repair ignores comments");
Check(Engine.RequestCompletion("SELECT 'Ordres ", 15, objects, true).Candidates.Count == 0, "explicit repair ignores strings");
Check(Engine.RequestCompletion("SELECT * FROM ", 14, objects, true).Candidates.Any(c => c.Label == "Orders"), "explicit empty FROM completion retains tables");
Check(Engine.RequestCompletion("SELECT CustmerId ", 17, objects, true).Candidates.Any(c => c.Label == "CustomerId"), "explicit completion repairs a column revisited after whitespace");

Check(Fk("SELECT * FROM dbo.Orders o JOIN sales.Customers c ").Any(c => c.Category == "FK Join" && c.Insert.StartsWith("ON [o].[CustomerId]")), "JOIN source completion offers the actual relation including ON");
Check(!Fk("SELECT * FROM dbo.Orders o CROSS JOIN sales.Customers c ").Any(c => c.Category == "FK Join"), "CROSS JOIN never suggests ON relations");
Check(Fk("SELECT * FROM sales.Customers c JOIN dbo.Orders o ").Any(c => c.Category == "FK Join" && c.Insert.Contains("[o].[CustomerId] = [c].[CustomerKey]")), "pre-ON relation respects reverse binding direction");
Check(Engine.AcceptedText("[dbo].[Orders] AS [o]") == "[dbo].[Orders] AS [o] ", "accepted table alias is separated from subsequent SQL");
Check(Engine.AcceptedText("[o]", "\n") == "[o] ", "accepted alias before a newline still separates subsequent typing");
Check(Engine.AcceptedText("SELECT ") == "SELECT ", "acceptance does not duplicate an existing trailing space");
Check(Engine.AcceptedText("[Id]", " ") == "[Id]", "existing following space is reused");
Check(Engine.AcceptedText("[Id]", ",") == "[Id]" && Engine.AcceptedText("dbo.") == "dbo.", "acceptance preserves punctuation and qualifier adjacency");
orders.ForeignKeys[0].IsDisabled = true;
Check(Fk("SELECT * FROM dbo.Orders o JOIN sales.Customers c ON ").Any(c => c.Category == "FK Join" && c.Detail.Contains("disabled")), "disabled but declared relationship stays visible with its disabled status");
orders.ForeignKeys[0].IsDisabled = false;
var insertTable = new DbObject { Schema = "sales", Name = "Customers", Kind = "USER_TABLE", Columns = new List<string> { "Id", "Name", "Age", "Calculated", "Stamp" }, InsertColumns = new List<InsertColumn> { new InsertColumn { Name = "Name", Type = "nvarchar(100)", BaseType = "nvarchar" }, new InsertColumn { Name = "Age", Type = "int", BaseType = "int" } } };
Candidate InsertAt(string sql, int? caret = null, bool brackets = true) => Engine.Complete(sql, caret ?? sql.Length, new[] { insertTable }, true, new CompletionOptions { UseBrackets = brackets, TableAliases = true }).Single(c => c.Label == "Customers");
var insertBody = InsertAt("INSERT INTO Cus");
Check(insertBody.Insert.StartsWith("[sales].[Customers]\n("), "INSERT target generates column body without alias");
Check(insertBody.Insert.Contains("[Name],\n    [Age]") && !insertBody.Insert.Contains("[Id]") && !insertBody.Insert.Contains("[Calculated]") && !insertBody.Insert.Contains("[Stamp]"), "INSERT uses writable metadata only");
Check(insertBody.Insert.Contains("VALUES\n(") && insertBody.Insert.EndsWith(";"), "INSERT generates complete VALUES statement");
Check(insertBody.Placeholders.Count == 2 && insertBody.Placeholders.All(p => insertBody.Insert.Substring(p.Offset, p.Length) == "NULL"), "INSERT editable values have valid tab-stop offsets");
Check(!Engine.Analyze("INSERT INTO " + insertBody.Insert).Any(i => i.Code == "SYNTAX"), "INSERT template parses as T-SQL");
var bareInsert = InsertAt("INSERT INTO Cus", brackets: false);
Check(bareInsert.Insert.StartsWith("sales.Customers\n(") && bareInsert.Insert.Contains("    Name,"), "INSERT respects optional brackets");
Check(bareInsert.Placeholders.All(p => bareInsert.Insert.Substring(p.Offset, p.Length) == "NULL"), "INSERT tab stops survive bracket removal");
Check(InsertAt("INSERT INTO sales.Cus").Insert.StartsWith("[Customers]\n("), "INSERT preserves existing schema qualifier");
foreach (var tail in new[] { " SELECT Name, Age FROM sales.Other", " VALUES (1, 2)", " (Name) VALUES ('x')", " OUTPUT inserted.Name", " DEFAULT VALUES", " -- keep my continuation\n", " /* existing comment */" })
    Check(InsertAt("INSERT INTO Cus" + tail, "INSERT INTO Cus".Length).Insert == "[sales].[Customers]", "INSERT preserves continuation: " + tail.Trim());
var terminated = InsertAt("INSERT INTO Cus;", "INSERT INTO Cus".Length);
Check(!terminated.Insert.EndsWith(";") && !Engine.Analyze("INSERT INTO " + terminated.Insert + ";").Any(i => i.Code == "SYNTAX"), "INSERT reuses existing semicolon");
Check(InsertAt("SELECT * INTO Cus").Insert == "[sales].[Customers]", "SELECT INTO never generates INSERT body");
Check(InsertAt("UPDATE Cus").Insert == "[sales].[Customers]", "UPDATE never generates INSERT body");
insertTable.InsertColumns = new List<InsertColumn>();
Check(InsertAt("INSERT INTO Cus").Insert == "[sales].[Customers]\nDEFAULT VALUES;", "No writable columns uses DEFAULT VALUES");
insertTable.InsertColumns = null;
Check(InsertAt("INSERT INTO Cus").Insert == "[sales].[Customers]", "Unknown writable metadata never guesses INSERT body");
insertTable.InsertColumns = new List<InsertColumn> { new InsertColumn { Name = "x] /* comment */", Type = "nvarchar(max)", BaseType = "nvarchar" }, new InsertColumn { Name = "select", Type = "int", BaseType = "int" } };
var escapedInsert = InsertAt("INSERT INTO Cus", brackets: false);
Check(!Engine.Analyze("INSERT INTO " + escapedInsert.Insert).Any(i => i.Code == "SYNTAX") && escapedInsert.Placeholders.All(p => escapedInsert.Insert.Substring(p.Offset, p.Length) == "NULL"), "Unusual INSERT identifiers preserve quoting and valid comments");
Check(insertBody.Insert.Contains("/* Name · nvarchar(100) NULL */") && insertBody.Insert.Contains("/* Age · int NULL */"), "INSERT comments include declared type and nullability");
Check(Engine.ColumnType("nvarchar", "sys", false, 200, 0, 0) == "nvarchar(100)" && Engine.ColumnType("nchar", "sys", false, 20, 0, 0) == "nchar(10)", "Unicode column type lengths count characters");
Check(Engine.ColumnType("varchar", "sys", false, -1, 0, 0) == "varchar(max)" && Engine.ColumnType("varbinary", "sys", false, 32, 0, 0) == "varbinary(32)", "Column type preserves MAX and binary lengths");
Check(Engine.ColumnType("decimal", "sys", false, 9, 18, 4) == "decimal(18,4)" && Engine.ColumnType("numeric", "sys", false, 9, 12, 2) == "numeric(12,2)", "Column type preserves decimal precision and scale");
Check(Engine.ColumnType("datetime2", "sys", false, 8, 27, 7) == "datetime2(7)" && Engine.ColumnType("time", "sys", false, 5, 16, 5) == "time(5)" && Engine.ColumnType("float", "sys", false, 8, 53, 0) == "float(53)", "Column type preserves temporal scale and float precision");
Check(Engine.ColumnType("MoneyType", "a]b", true, 9, 18, 2) == "[a]]b].[MoneyType]", "User-defined column type preserves schema and escaping");
foreach (var pair in new[] {
    ("int", "0"), ("bit", "0"), ("bigint", "0"), ("tinyint", "0"), ("smallint", "0"),
    ("decimal", "0"), ("numeric", "0"), ("float", "0"), ("real", "0"), ("money", "0"), ("smallmoney", "0"), ("sql_variant", "0"),
    ("nvarchar", "N''"), ("nchar", "N''"), ("ntext", "N''"), ("varchar", "''"), ("char", "''"), ("text", "''"),
    ("binary", "0x"), ("varbinary", "0x"), ("image", "0x"),
    ("date", "'19000101'"), ("datetime", "'19000101'"), ("smalldatetime", "'19000101'"), ("datetime2", "'19000101'"),
    ("time", "'00:00:00'"), ("datetimeoffset", "'1900-01-01T00:00:00+00:00'"),
    ("uniqueidentifier", "'00000000-0000-0000-0000-000000000000'"), ("xml", "N'<root />'"),
    ("geography", "geography::Point(0, 0, 4326)"), ("geometry", "geometry::Point(0, 0, 0)"), ("hierarchyid", "hierarchyid::GetRoot()") })
{
    insertTable.InsertColumns = new List<InsertColumn> { new InsertColumn { Name = "Required", Type = pair.Item1, BaseType = pair.Item1.ToUpperInvariant(), Nullable = false } };
    var template = InsertAt("INSERT INTO Cus");
    var stop = template.Placeholders.Single();
    Check(template.Insert.Substring(stop.Offset, stop.Length) == pair.Item2 && template.Insert.Contains(" NOT NULL */") && !Engine.Analyze("INSERT INTO " + template.Insert).Any(i => i.Code == "SYNTAX"), "Required INSERT initial value: " + pair.Item1);
}
insertTable.InsertColumns = new List<InsertColumn> {
    new InsertColumn { Name = "Count", Type = "int", BaseType = "int", Nullable = false, HasDefault = true, DefaultExpression = "((42))" },
    new InsertColumn { Name = "Title", Type = "nvarchar(100)", BaseType = "nvarchar", HasDefault = true, DefaultExpression = "(N'سلام')" },
    new InsertColumn { Name = "Created", Type = "datetime2(7)", BaseType = "datetime2", Nullable = false, HasDefault = true, DefaultExpression = "(getdate())" },
    new InsertColumn { Name = "LegacyDefault", Type = "int", BaseType = "int", Nullable = false, HasDefault = true },
    new InsertColumn { Name = "Optional", Type = "int", BaseType = "int", Nullable = true },
    new InsertColumn { Name = "Alias", Type = "[dbo].[MoneyType]", BaseType = "decimal", Nullable = false }
};
var defaultTemplate = InsertAt("INSERT INTO Cus");
Check(defaultTemplate.Placeholders.Select(p => defaultTemplate.Insert.Substring(p.Offset, p.Length)).SequenceEqual(new[] { "((42))", "(N'سلام')", "(getdate())", "DEFAULT", "NULL", "0" }), "INSERT defaults outrank nullability and type initial values, including bound defaults");
Check(!Engine.Analyze("INSERT INTO " + defaultTemplate.Insert).Any(i => i.Code == "SYNTAX"), "Mixed INSERT default expressions remain valid SQL");
Check(defaultTemplate.Insert.Contains("/* Alias · [dbo].[MoneyType] NOT NULL */"), "Alias type is displayed while underlying type supplies initial value");
var bareDefaults = InsertAt("INSERT INTO Cus", brackets: false);
Check(bareDefaults.Placeholders.Select(p => bareDefaults.Insert.Substring(p.Offset, p.Length)).SequenceEqual(defaultTemplate.Placeholders.Select(p => defaultTemplate.Insert.Substring(p.Offset, p.Length))), "Variable-length INSERT placeholders survive bracket removal");
insertTable.InsertColumns = new List<InsertColumn> { new InsertColumn { Name = "Custom", Type = "[dbo].[ClrType]", BaseType = "ClrType", Nullable = false } };
var unknownRequired = InsertAt("INSERT INTO Cus");
Check(unknownRequired.Insert.Contains("@required_value") && unknownRequired.Insert.Contains("enter a required value") && !unknownRequired.Insert.Contains("NULL,"), "Unknown required CLR type requests an editable value instead of NULL");


// Statement selection never splits inside literals, CTEs or guarded blocks.
foreach (var pair in new[] {
    Tuple.Create("SELECT 1;\nSELECT 2;", "SELECT 2;"),
    Tuple.Create("SELECT 1\nSELECT 2", "SELECT 2"),
    Tuple.Create("SELECT 1;\nGO\nSELECT 2;", "SELECT 2;"),
    Tuple.Create("SELECT N'a;b'; SELECT 2;", "SELECT 2;"),
    Tuple.Create("/* SELECT 0; */ SELECT 2; -- tail", "SELECT 2;"),
    Tuple.Create("SELECT 1; WITH c AS (SELECT 2 AS n) SELECT n FROM c;", "WITH c AS (SELECT 2 AS n) SELECT n FROM c;"),
    Tuple.Create("IF 1=0 BEGIN SELECT 2; END ELSE SELECT 3;", "IF 1=0 BEGIN SELECT 2; END ELSE SELECT 3;"),
    Tuple.Create("BEGIN SELECT 2; SELECT 3; END;", "BEGIN SELECT 2; SELECT 3; END;"),
    Tuple.Create("CREATE PROCEDURE dbo.p AS SELECT 2; SELECT 3;", "CREATE PROCEDURE dbo.p AS SELECT 2; SELECT 3;"),
    Tuple.Create("SELECT * FROM (SELECT 2 AS n) x; SELECT 3;", "SELECT * FROM (SELECT 2 AS n) x;") })
{
    int caret = pair.Item1.IndexOf("2", StringComparison.Ordinal);
    var unit = Engine.StatementAtCaret(pair.Item1, caret, out var why);
    Check(unit != null && unit.Text == pair.Item2 && pair.Item1.Substring(unit.Start, unit.Length) == unit.Text && why == null, "Execution boundary: " + pair.Item1.Replace('\n', ' '));
}
foreach (string text in new[] { "", "-- SELECT 2;", "/* SELECT 2; */", "SELECT 1;\n\n", "GO\n", "SELECT FROM;" })
    Check(Engine.StatementAtCaret(text, text.Length, out var reason) == null && !string.IsNullOrEmpty(reason), "No executable statement at blank/comment/invalid cursor: " + text.Replace('\n', ' '));
Check(Engine.StatementAtCaret("SELECT 1;SELECT 2;", 9, out _)?.Text == "SELECT 2;", "Adjacent statements use the one starting at the cursor");
Check(Engine.StatementAtCaret("SELECT 1;", 9, out _)?.Text == "SELECT 1;", "Trailing statement edge remains executable");
Check(Engine.StatementAtCaret("SELECT 1;", -1, out _) == null && Engine.StatementAtCaret("SELECT 1;", 50, out _) == null, "Execution rejects out-of-range caret");


foreach (string word in new[] { "select", "from", "where", "insert", "into", "update", "delete", "join", "on", "and", "order", "by", "group", "distinct", "SeLeCt", "null", "case", "when", "then", "end" })
{
    string text = word + " ";
    var fix = Engine.KeywordCaseOnSpace(text, text.Length);
    Check(fix != null && fix.Offset == 0 && fix.Length == word.Length && fix.Replacement == word.ToUpperInvariant(), "Space uppercases complete SQL keyword: " + word);
}
foreach (string text in new[] { "-- select ", "/* from ", "SELECT N'select ", "SELECT [select ", "SELECT [where] ", "SELECT \"where\" ", "SELECT @from ", "SELECT #from ", "SELECT myselect ", "SELECT sel ", "SELECT ", "select  ", "select\t" })
    Check(Engine.KeywordCaseOnSpace(text, text.Length) == null, "Keyword casing preserves comment/literal/identifier/partial text: " + text);
var hoveredTable = new DbObject { Schema = "sales", Name = "Orders", Kind = "USER_TABLE", Columns = new List<string> { "Id", "Total" }, ColumnDetails = new List<InsertColumn> { new InsertColumn { Name = "Id", Type = "int", Nullable = false, Identity = true }, new InsertColumn { Name = "Total", Type = "decimal(18,2)", Nullable = false, HasDefault = true, DefaultExpression = "((0))" } } };
var hoverCatalog = new[] { hoveredTable };
MetadataDescription Describe(string sql, string word) => Engine.DescribeAt(sql, sql.IndexOf(word, StringComparison.Ordinal), hoverCatalog);
Check(Describe("SELECT * FROM sales.Orders o", "Orders").Title == "[sales].[Orders]", "Hover resolves schema-qualified table");
Check(Describe("SELECT * FROM sales.Orders o", "sales").Body.Contains("IDENTITY"), "Table hover includes generated columns and identity metadata");
Check(Describe("SELECT o.Total FROM sales.Orders o", "Total").Body.Contains("decimal(18,2) NOT NULL · DEFAULT ((0))"), "Column hover resolves alias and shows type/nullability/default");
Check(Describe("SELECT Total FROM sales.Orders", "Total").Title == "[sales].[Orders].[Total]", "Unqualified column hover binds only current source");
Check(Describe("SELECT [o].[Total] FROM sales.Orders [o]", "Total").Title.EndsWith("[Total]"), "Hover supports bracketed identifiers");
Check(Describe("SELECT x.Total FROM sales.Orders o", "Total") == null, "Unknown qualifier does not guess a column owner");
Check(Describe("-- Total\nSELECT 1", "Total") == null && Describe("SELECT N'Total'", "Total") == null, "Metadata hover ignores comments and literals");
Check(Describe("SELECT 1; SELECT Total FROM sales.Orders", "Total") != null, "Hover scopes a later statement");
var otherHover = new DbObject { Schema = "dbo", Name = "Other", Kind = "USER_TABLE", Columns = new List<string> { "Total" } };
string ambiguousHover = "SELECT Total FROM sales.Orders o JOIN dbo.Other b ON o.Total=b.Total";
Check(Engine.DescribeAt(ambiguousHover, 7, new[] { hoveredTable, otherHover }) == null, "Ambiguous unqualified hover is suppressed");
string missingType = "SELECT b.Total FROM dbo.Other b";
Check(Engine.DescribeAt(missingType, 9, new[] { otherHover }).Body.Contains("unavailable"), "Missing type metadata is explicit rather than guessed");
Check(Engine.DescribeAt("SELECT 1", -1, hoverCatalog) == null && Engine.DescribeAt("SELECT 1", 90, hoverCatalog) == null, "Hover rejects out-of-range positions");
string aliasHover = "SELECT o FROM sales.Orders o";
Check(Engine.DescribeAt(aliasHover, 7, hoverCatalog)?.Title == "[sales].[Orders]", "Hover on table alias shows its table metadata");
Check(Describe("SELECT * FROM otherdb.sales.Orders", "Orders") == null, "Cross-database table hover never substitutes current-database metadata");
Check(Engine.KeywordCaseOnSpace("select customer", 7)?.Replacement == "SELECT", "Pending Space casing remains valid after the next word is typed");
Check(Engine.KeywordCaseOnSpace("-- select customer", 10) == null, "Pending casing is rejected if surrounding edits move the token into a comment");
var oldDetails = hoveredTable.ColumnDetails;
hoveredTable.ColumnDetails = new List<InsertColumn> { new InsertColumn { Name = "Total", Type = "int", HasDefault = true, DefaultExpression = new string('x', 10000), Computed = true } };
var boundedHover = Describe("SELECT Total FROM sales.Orders", "Total");
Check(boundedHover.Body.Length < 500 && boundedHover.Body.Contains("COMPUTED"), "Hover bounds long default expressions and shows computed metadata");
hoveredTable.ColumnDetails = oldDetails;
string quotedHover = "SELECT [o].[Total] FROM sales.Orders [o]";
var quotedDescription = Engine.DescribeAt(quotedHover, quotedHover.IndexOf("Total", StringComparison.Ordinal), hoverCatalog);
Check(quotedHover.Substring(quotedDescription.Start, quotedDescription.Length) == "[Total]", "Hover tracks the whole quoted token for stable pointer bounds");
var schemaDescription = Engine.DescribeAt("SELECT * FROM sales.Orders", 15, hoverCatalog);
Check(schemaDescription.Start == 14 && schemaDescription.Length == 5, "Schema hover anchors its own token rather than the mouse position");
foreach (string head in new[] { "UPDATE dbo.Orders SET CustomerId=1 WHERE ", "DELETE FROM dbo.Orders WHERE " })
{
    foreach (string prior in new[] { "", "SELECT * FROM dbo.Orders o\n" })
        foreach (string suffix in new[] { "", "\nSELECT * FROM dbo.Orders unrelated" })
        {
            string query = prior + head + suffix;
            var rows = Engine.Complete(query, prior.Length + head.Length, objects, true, new CompletionOptions { QualifyColumns = true, TableAliases = true }).Where(c => c.Category == "Column").ToList();
            Check(rows.Count == 2 && rows.All(c => c.Insert == DbObject.Quote(c.Label)), "DML WHERE never inherits aliases from adjacent statements: " + query.Replace('\n', ' '));
        }
}
var deleteTarget = Engine.Complete("DELETE FROM Ord", 15, objects, options: new CompletionOptions { TableAliases = true }).Single();
Check(!deleteTarget.Insert.Contains(" AS "), "Direct DELETE target does not receive an invalid AS alias");
string joinedUpdate = "UPDATE o SET CustomerId=1 FROM dbo.Orders o WHERE ";
var updateAliasColumns = Engine.Complete(joinedUpdate, joinedUpdate.Length, objects, true, new CompletionOptions { QualifyColumns = true }).Where(c => c.Category == "Column").ToList();
Check(updateAliasColumns.Count == 2 && updateAliasColumns.All(c => c.Insert.StartsWith("[o].")), "UPDATE FROM retains explicitly declared source aliases");
string joinedDelete = "DELETE o FROM dbo.Orders o WHERE ";
var deleteAliasColumns = Engine.Complete(joinedDelete, joinedDelete.Length, objects, true, new CompletionOptions { QualifyColumns = true }).Where(c => c.Category == "Column").ToList();
Check(deleteAliasColumns.Count == 2 && deleteAliasColumns.All(c => c.Insert.StartsWith("[o].")), "DELETE alias FROM retains explicitly declared source aliases");
Console.WriteLine($"{checks} checks passed.");
