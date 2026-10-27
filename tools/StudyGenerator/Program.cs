// studygen: writes the chapter studies in studies/<BOOK>/<chapter>.json using the Claude Message Batches API.
//
//   studygen one JHN 4                 Generate one chapter right away (try the prompt before a batch)
//   studygen submit --books GEN,EXO    Queue a batch for chapters that have no study yet
//   studygen submit --all [--limit 50] Queue every missing chapter (optionally only the first N)
//   studygen status [batchId]          Show progress (defaults to the last batch submitted)
//   studygen collect [batchId]         Download finished results and write the study files
//   studygen index                     Rebuild studies/index.json from the files on disk
//   studygen review JHN 3              Mark a study as reviewed (or --undo)
//   studygen dryrun JHN 4              Write the request for one chapter to request-JHN-4.json without calling the API
//
// Options: --model <id> (default claude-opus-5-5), --force (regenerate chapters that already have a study).
// Needs ANTHROPIC_API_KEY in the environment for one, submit, status and collect.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

var opts = Options.Parse(args);
var repo = Repo.Find();
var app = new App(repo, opts);

try
{
    return opts.Command switch
    {
        "one" => await app.One(),
        "submit" => await app.Submit(),
        "status" => await app.Status(),
        "collect" => await app.Collect(),
        "index" => app.Index(),
        "review" => app.Review(),
        "dryrun" => app.DryRun(),
        _ => Usage()
    };
}
catch (UserError e)
{
    Console.Error.WriteLine("Error: " + e.Message);
    return 1;
}

static int Usage()
{
    Console.WriteLine("""
        studygen one <BOOK> <chapter>        Generate one chapter now
        studygen submit --books GEN,EXO      Queue missing chapters in these books
        studygen submit --all [--limit N]    Queue every missing chapter
        studygen status [batchId]            Batch progress
        studygen collect [batchId]           Write finished studies to disk
        studygen index                       Rebuild studies/index.json
        studygen review <BOOK> <chapter>     Mark a study reviewed (--undo to revert)
        studygen dryrun <BOOK> <chapter>     Save the request JSON without calling the API

        Options: --model <id>  --force
        Book codes are in studies/canon.json (GEN, PSA, JHN, 1CO, ...).
        """);
    return 1;
}

sealed class UserError(string message) : Exception(message);

sealed record Book(string Code, string Name, int Chapters, string Testament);

sealed class Options
{
    public string Command = "";
    public List<string> Positional = [];
    public string Model = "claude-opus-5-5";
    public bool Force, All, Undo;
    public int? Limit;
    public HashSet<string> Books = [];

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new UserError($"{a} needs a value");
            switch (a)
            {
                case "--model": o.Model = Next(); break;
                case "--force": o.Force = true; break;
                case "--all": o.All = true; break;
                case "--undo": o.Undo = true; break;
                case "--limit": o.Limit = int.Parse(Next()); break;
                case "--books":
                    foreach (var b in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        o.Books.Add(b.ToUpperInvariant());
                    break;
                default:
                    if (o.Command == "") o.Command = a.ToLowerInvariant(); else o.Positional.Add(a);
                    break;
            }
        }
        return o;
    }
}

sealed class Repo
{
    public required string Root;
    public string Studies => Path.Combine(Root, "studies");
    public string Tool => Path.Combine(Root, "tools", "StudyGenerator");
    public string BatchLog => Path.Combine(Tool, "batches.json");
    public string StudyPath(string code, int ch) => Path.Combine(Studies, code, ch + ".json");

    // Walk up from the working directory (or the binary) until studies/canon.json is found.
    public static Repo Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "studies", "canon.json")))
                    return new Repo { Root = d.FullName };
        }
        throw new UserError("Run this from inside the BibleHistory repo (could not find studies/canon.json).");
    }
}

sealed class App(Repo repo, Options opts)
{
    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep Hebrew, Greek and quotes readable in the files
    };

    readonly List<Book> canon = LoadCanon(repo);
    Dictionary<string, Book> ByCode => canon.ToDictionary(b => b.Code);

    static List<Book> LoadCanon(Repo repo)
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Studies, "canon.json")))!;
        return doc["books"]!.AsArray()
            .Select(b => new Book((string)b!["code"]!, (string)b["name"]!, (int)b["chapters"]!, (string)b["testament"]!))
            .ToList();
    }

    (Book book, int ch) ChapterArg()
    {
        if (opts.Positional.Count < 2) throw new UserError("Give a book code and chapter, e.g. JHN 3");
        var code = opts.Positional[0].ToUpperInvariant();
        if (!ByCode.TryGetValue(code, out var book)) throw new UserError($"Unknown book code {code}. See studies/canon.json.");
        if (!int.TryParse(opts.Positional[1], out int ch) || ch < 1 || ch > book.Chapters)
            throw new UserError($"{book.Name} has chapters 1-{book.Chapters}.");
        return (book, ch);
    }

    static string Label(Book book, int ch) => book.Name == "Psalms" ? $"Psalm {ch}" : $"{book.Name} {ch}";

    // ---------------- prompt and tool ----------------

    string SystemPrompt()
    {
        var template = File.ReadAllText(Path.Combine(repo.Tool, "prompt.md"));
        var examplePath = repo.StudyPath("JHN", 3);
        var example = File.Exists(examplePath) ? File.ReadAllText(examplePath) : "(no example available)";
        return template.Replace("{{EXAMPLE}}", example);
    }

    // The tool schema is the study schema minus the fields this program fills in (ref, book, status, ...).
    JsonObject ToolSchema()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Studies, "study.schema.json")))!.AsObject();
        var defs = schema["$defs"]!.AsObject();
        var props = schema["properties"]!.AsObject();
        var keep = new[] { "title", "theme", "keyVerses", "steps" };
        var outProps = new JsonObject();
        foreach (var k in keep) outProps[k] = Resolve(props[k]!.DeepClone(), defs);
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("title", "theme", "keyVerses", "steps"),
            ["properties"] = outProps
        };
    }

    // Inline local $refs and drop keywords the API does not need.
    static JsonNode Resolve(JsonNode node, JsonObject defs)
    {
        if (node is JsonObject o)
        {
            if (o["$ref"] is JsonValue r && ((string)r!).StartsWith("#/$defs/"))
                return Resolve(defs[((string)r!)["#/$defs/".Length..]]!.DeepClone(), defs);
            var copy = new JsonObject();
            foreach (var (k, v) in o)
                if (k is not ("format" or "$schema" or "$id") && v != null) copy[k] = Resolve(v.DeepClone(), defs);
            return copy;
        }
        if (node is JsonArray a)
        {
            var copy = new JsonArray();
            foreach (var v in a) copy.Add(v == null ? null : Resolve(v.DeepClone(), defs));
            return copy;
        }
        return node.DeepClone();
    }

    JsonObject MessageParams(Book book, int ch, string system, JsonObject toolSchema)
    {
        string label = Label(book, ch);
        return new JsonObject
        {
            ["model"] = opts.Model,
            ["max_tokens"] = 12000,
            // The system prompt is identical for every chapter, so cache it.
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = system,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
            }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = "record_study",
                ["description"] = "Record the finished 12-step study of one Bible chapter.",
                ["input_schema"] = toolSchema.DeepClone()
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = "record_study" },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = $"Write the chapter study for {label} ({book.Testament}, {book.Name} has {book.Chapters} chapter{(book.Chapters == 1 ? "" : "s")}). Record it with record_study."
            })
        };
    }

    // ---------------- commands ----------------

    public async Task<int> One()
    {
        var (book, ch) = ChapterArg();
        if (File.Exists(repo.StudyPath(book.Code, ch)) && !opts.Force)
            throw new UserError($"{book.Code}.{ch} already has a study. Use --force to replace it.");
        Api.RequireKey();
        Console.WriteLine($"Generating {Label(book, ch)} with {opts.Model}...");
        var resp = await Api.Post("/v1/messages", MessageParams(book, ch, SystemPrompt(), ToolSchema()));
        var usage = resp["usage"];
        Console.WriteLine($"Tokens: {usage?["input_tokens"]} in, {usage?["cache_read_input_tokens"] ?? 0} cached, {usage?["output_tokens"]} out");
        return Save(book, ch, resp) ? Index() : 1;
    }

    public int DryRun()
    {
        var (book, ch) = ChapterArg();
        var path = Path.Combine(repo.Tool, $"request-{book.Code}-{ch}.json");
        File.WriteAllText(path, MessageParams(book, ch, SystemPrompt(), ToolSchema()).ToJsonString(Pretty));
        Console.WriteLine($"Request saved to {Path.GetRelativePath(repo.Root, path)}");
        return 0;
    }

    public async Task<int> Submit()
    {
        if (!opts.All && opts.Books.Count == 0) throw new UserError("Choose chapters with --books GEN,EXO or --all.");
        Api.RequireKey();
        foreach (var b in opts.Books)
            if (!ByCode.ContainsKey(b)) throw new UserError($"Unknown book code {b}.");

        var todo = canon.Where(b => opts.All || opts.Books.Contains(b.Code))
            .SelectMany(b => Enumerable.Range(1, b.Chapters).Select(ch => (book: b, ch)))
            .Where(x => opts.Force || !File.Exists(repo.StudyPath(x.book.Code, x.ch)))
            .ToList();
        if (opts.Limit is int n) todo = todo.Take(n).ToList();
        if (todo.Count == 0) { Console.WriteLine("Nothing to do: every chosen chapter already has a study."); return 0; }

        var system = SystemPrompt();
        var schema = ToolSchema();
        var requests = new JsonArray();
        foreach (var (book, ch) in todo)
            requests.Add(new JsonObject
            {
                ["custom_id"] = $"{book.Code}_{ch}", // custom_id allows only letters, digits, _ and -
                ["params"] = MessageParams(book, ch, system, schema)
            });

        Console.WriteLine($"Submitting {todo.Count} chapter{(todo.Count == 1 ? "" : "s")} with {opts.Model}...");
        var batch = await Api.Post("/v1/messages/batches", new JsonObject { ["requests"] = requests });
        var id = (string)batch["id"]!;
        LogBatch(id, todo.Count);
        Console.WriteLine($"Batch {id} submitted. Most batches finish within an hour; check with:  studygen status");
        return 0;
    }

    public async Task<int> Status()
    {
        var id = BatchArg();
        var b = await Api.Get($"/v1/messages/batches/{id}");
        var c = b["request_counts"]!;
        Console.WriteLine($"Batch {id}: {b["processing_status"]}");
        Console.WriteLine($"  processing {c["processing"]}, succeeded {c["succeeded"]}, errored {c["errored"]}, canceled {c["canceled"]}, expired {c["expired"]}");
        if ((string?)b["processing_status"] == "ended") Console.WriteLine("Finished. Write the files with:  studygen collect");
        return 0;
    }

    public async Task<int> Collect()
    {
        var id = BatchArg();
        var b = await Api.Get($"/v1/messages/batches/{id}");
        if ((string?)b["processing_status"] != "ended")
            throw new UserError($"Batch {id} is still {b["processing_status"]}. Try again later.");

        var jsonl = await Api.GetText((string)b["results_url"]!);
        int ok = 0, failed = 0;
        var failures = new List<string>();
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var row = JsonNode.Parse(line)!;
            var cid = (string)row["custom_id"]!;
            var parts = cid.Split('_');
            if (parts.Length != 2 || !ByCode.TryGetValue(parts[0], out var book) || !int.TryParse(parts[1], out int ch))
            { failures.Add($"{cid}: unrecognized id"); failed++; continue; }

            var result = row["result"]!;
            if ((string?)result["type"] != "succeeded")
            {
                failures.Add($"{cid}: {result["type"]} {result["error"]?.ToJsonString()}");
                failed++; continue;
            }
            if (Save(book, ch, result["message"]!, failures)) ok++; else failed++;
        }

        Console.WriteLine($"Wrote {ok} stud{(ok == 1 ? "y" : "ies")}; {failed} failed.");
        if (failures.Count > 0)
        {
            var path = Path.Combine(repo.Tool, $"failures-{id}.txt");
            File.WriteAllLines(path, failures);
            Console.WriteLine($"Failures listed in {Path.GetRelativePath(repo.Root, path)}. Resubmit them with the same submit command; finished chapters are skipped.");
        }
        Index();
        return failed == 0 ? 0 : 2;
    }

    public int Index()
    {
        var index = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var book in canon)
            for (int ch = 1; ch <= book.Chapters; ch++)
            {
                var path = repo.StudyPath(book.Code, ch);
                if (!File.Exists(path)) continue;
                try
                {
                    var status = (string?)JsonNode.Parse(File.ReadAllText(path))?["status"] ?? "draft";
                    index[$"{book.Code}.{ch}"] = status;
                }
                catch (JsonException e) { Console.Error.WriteLine($"Skipping {book.Code}.{ch}: {e.Message}"); }
            }
        var json = new JsonObject();
        foreach (var (k, v) in index) json[k] = v;
        File.WriteAllText(Path.Combine(repo.Studies, "index.json"), json.ToJsonString(Pretty) + "\n");
        int reviewed = index.Values.Count(v => v == "reviewed");
        Console.WriteLine($"Index: {index.Count} of 1,189 chapters ({reviewed} reviewed).");
        return 0;
    }

    public int Review()
    {
        var (book, ch) = ChapterArg();
        var path = repo.StudyPath(book.Code, ch);
        if (!File.Exists(path)) throw new UserError($"{book.Code}.{ch} has no study yet.");
        var doc = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        doc["status"] = opts.Undo ? "draft" : "reviewed";
        doc["updated"] = DateTime.Now.ToString("yyyy-MM-dd");
        File.WriteAllText(path, doc.ToJsonString(Pretty) + "\n");
        Console.WriteLine($"{Label(book, ch)} marked {doc["status"]}.");
        return Index();
    }

    // ---------------- saving ----------------

    bool Save(Book book, int ch, JsonNode message, List<string>? failures = null)
    {
        void Fail(string why) { if (failures != null) failures.Add($"{book.Code}_{ch}: {why}"); else Console.Error.WriteLine(why); }

        if ((string?)message["stop_reason"] == "max_tokens") { Fail("ran out of tokens before finishing"); return false; }
        var input = message["content"]?.AsArray()
            .FirstOrDefault(c => (string?)c?["type"] == "tool_use" && (string?)c["name"] == "record_study")?["input"];
        if (input is not JsonObject body) { Fail("no record_study call in the response"); return false; }

        var problems = Validate(body);
        if (problems.Count > 0) { Fail("schema problems: " + string.Join("; ", problems)); return false; }

        // Fixed fields first so every file reads the same way.
        var study = new JsonObject
        {
            ["ref"] = $"{book.Code}.{ch}",
            ["book"] = book.Name,
            ["chapter"] = ch,
            ["title"] = body["title"]!.DeepClone(),
            ["status"] = "draft",
            ["source"] = opts.Model,
            ["updated"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["theme"] = body["theme"]!.DeepClone(),
            ["keyVerses"] = body["keyVerses"]?.DeepClone() ?? new JsonArray(),
            ["steps"] = body["steps"]!.DeepClone()
        };

        var path = repo.StudyPath(book.Code, ch);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, study.ToJsonString(Pretty) + "\n");
        if (failures == null) Console.WriteLine($"Wrote {Path.GetRelativePath(repo.Root, path)}");
        return true;
    }

    // A light check of the parts the page depends on. The API already enforces most of the schema.
    static List<string> Validate(JsonObject s)
    {
        var p = new List<string>();
        foreach (var k in new[] { "title", "theme" })
            if (s[k] is not JsonValue v || string.IsNullOrWhiteSpace((string?)v)) p.Add($"missing {k}");
        if (s["steps"] is not JsonObject st) { p.Add("missing steps"); return p; }

        foreach (var k in new[] { "language", "usage", "voices", "translations", "application", "misapplications", "contrasts", "features", "questions" })
            if (st[k] is not JsonArray a || a.Count == 0) p.Add($"step {k} is empty");
        foreach (var k in new[] { "gospel", "summary" })
            if (st[k] is not JsonValue v || string.IsNullOrWhiteSpace((string?)v)) p.Add($"step {k} is empty");
        if (st["context"]?["flow"] is not JsonArray { Count: > 0 }) p.Add("context.flow is empty");
        if (st["crossRefs"] is not JsonObject cr || cr["tier1"] is not JsonArray || cr["tier2"] is not JsonArray || cr["tier3"] is not JsonArray)
            p.Add("crossRefs needs tier1, tier2 and tier3");
        if (st["metaArc"]?["book"] is null || st["metaArc"]?["redemptive"] is null) p.Add("metaArc incomplete");
        if (st["questions"] is JsonArray qs)
            foreach (var k in new[] { "observe", "interpret", "apply" })
                if (!qs.Any(q => (string?)q?["kind"] == k)) p.Add($"questions need at least one '{k}' question");

        // Guard against long quotations of copyrighted translations.
        if (st["translations"] is JsonArray tr)
            foreach (var t in tr)
                foreach (var r in t?["renderings"]?.AsArray() ?? [])
                    if (((string?)r?["text"])?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 14 && (string?)r?["version"] != "KJV")
                        p.Add($"{r?["version"]} rendering is too long");
        return p;
    }

    // ---------------- batch log ----------------

    void LogBatch(string id, int count)
    {
        var log = File.Exists(repo.BatchLog) ? JsonNode.Parse(File.ReadAllText(repo.BatchLog))!.AsArray() : new JsonArray();
        log.Add(new JsonObject { ["id"] = id, ["chapters"] = count, ["model"] = opts.Model, ["submitted"] = DateTime.Now.ToString("s") });
        File.WriteAllText(repo.BatchLog, log.ToJsonString(Pretty) + "\n");
    }

    string BatchArg()
    {
        if (opts.Positional.Count > 0) return opts.Positional[0];
        if (!File.Exists(repo.BatchLog)) throw new UserError("No batches submitted yet. Give a batch id or run submit first.");
        var log = JsonNode.Parse(File.ReadAllText(repo.BatchLog))!.AsArray();
        return (string)log[^1]!["id"]!;
    }
}

static class Api
{
    const string Base = "https://api.anthropic.com";
    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (!string.IsNullOrEmpty(key)) http.DefaultRequestHeaders.Add("x-api-key", key);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        return http;
    }

    public static void RequireKey()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            throw new UserError("Set ANTHROPIC_API_KEY first (create a key at console.anthropic.com).");
    }

    public static async Task<JsonNode> Post(string path, JsonNode body)
    {
        RequireKey();
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var resp = await Http.PostAsync(Base + path, content);
        return await Read(resp);
    }

    public static async Task<JsonNode> Get(string path)
    {
        RequireKey();
        using var resp = await Http.GetAsync(Base + path);
        return await Read(resp);
    }

    public static async Task<string> GetText(string url)
    {
        RequireKey();
        using var resp = await Http.GetAsync(url);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new UserError($"API {(int)resp.StatusCode}: {text}");
        return text;
    }

    static async Task<JsonNode> Read(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var msg = JsonNode.Parse(text)?["error"]?["message"]?.ToString() ?? text;
            throw new UserError($"API {(int)resp.StatusCode}: {msg}");
        }
        return JsonNode.Parse(text)!;
    }
}
