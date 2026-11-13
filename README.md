# Bible History

Ten linked pages for studying the Bible's story:

- **Timeline** (`index.html`): the major events of the Bible, from Creation to Revelation, followed by major events of church history from AD 100 to today.
- **Books** (`books.html`): all 66 books on one zoomable time axis, showing when each book's events take place or when it was written. Filter by group or by individual book, zoom by pinch, Ctrl/⌘ + scroll, or buttons, and tap a book for its dates, author, and summary.
- **Kings and Prophets** (`kings.html`): every king of Israel and Judah and the 39 prophets of their time, from Samuel through Malachi, on the same zoomable time axis. Below the chart, every prophet in the Bible, from Noah to the two witnesses of Revelation. Each king is colored by the Bible's verdict on him, co-reigns are marked, and tapping anyone highlights everyone who lived at the same time.
- **People** (`people.html`): Jesus' family line from Adam, following both Matthew 1 and Luke 3. The line forks into two columns where the Gospels trace different sons of David, and joins where they meet. Tap anyone for notes, life spans, the women Matthew names, and links to the Timeline and Kings pages.
- **Covenants** (`covenants.html`): the covenants with Adam, Noah, Abraham, at Sinai, with David, and the New Covenant, showing how each builds on the last and is fulfilled in Christ. Follow one promise (the offspring, God's dwelling, the nations, the king, the heart, the sign) through all six.
- **Prophecy** (`prophecy.html`): 45 Old Testament promises beside their New Testament fulfillments, in the order of Christ's life, each marked as quoted as fulfilled or echoed, with the centuries between them and links to the Timeline and both books.
- **Plagues** (`plagues.html`): the ten plagues of Egypt, with the sign of the staff before them and the sea after, laid out in Exodus's pattern of three rounds of three. Each shows the warning, the magicians, whether Israel was spared, who hardened Pharaoh's heart, its echoes through Scripture to Revelation, and its significance.
- **Church Family Tree** (`church.html`): how the one church became today's denominations, drawn as a family tree from the early church to today, with what makes each distinct; and the heresies and sects outside orthodoxy, from Gnosticism to Jehovah's Witnesses, each measured against the historic creeds. A third tab sets the world's other religions beside the gospel: what each teaches in its own terms, the common ground, and the gospel's answer. A map shows where each began.
- **Maps** (`maps.html`): the places of the Bible on a zoomable map drawn from Natural Earth coastlines, with journeys traced out (Abraham, Jacob, Joseph, the Exodus, the exile, the flight to Egypt, Paul's three journeys and his voyage to Rome, the seven churches of Revelation). Tap any place for what happened there, with links to read it and to the Timeline.
- **Word and Deed** (`word-and-deed.html`): every episode in the Gospels and Acts where Jesus or his followers preach or do a good work, scored for whether the two happen together.
- **Study** (`study.html`): a study of any of the 1,189 chapters, worked through a 12-step method: original language, usage elsewhere, context, cross-references in three tiers, theological voices, translation comparison, gospel connection, application, misapplications, contrasts, unique features, and a summary, followed by questions to consider for personal study or a small group. Pick a book and chapter from the grid; chapters with a dot have a study. Each book on the Books page links to its studies.

Gospel and Acts events on the timeline link to their Word and Deed entries, and those entries link back.

## Timeline

- Ten Bible eras, from Beginnings through the Early Church, on a scaled overview bar
- Five church history eras (Ancient, Medieval, Reformation, Puritans & Awakening, Modern) on a second bar, each event with a "Why it matters" note, most with a key Scripture text
- Search across events, people, and books; filter by Old Testament, New Testament, or Church history
- Every church named in Acts, marked on the timeline, with a "Churches in Acts only" filter
- Tap any event for a summary, the people involved, and a link to the passage on BibleGateway

## Chapter studies

Each study is a JSON file at `studies/<BOOK>/<chapter>.json` (for example `studies/JHN/3.json`), and `study.html` loads only the chapter being read.

- `studies/canon.json`: the 66 books with their codes and chapter counts
- `studies/study.schema.json`: the shape every study follows
- `studies/index.json`: which chapters have a study and whether it is a draft or reviewed (rebuilt by the generator)

Studies start as **drafts**, shown with a badge and a "Report a problem" link. Review a study before removing the badge, paying most attention to the theological voices (views are summarized, never quoted, and must actually be that person's), the translation comparison (KJV in full, only a few words from copyrighted versions), and the Hebrew and Greek words. Genesis 1, Psalm 23 and John 3 are hand-written pilots and the model for the rest.

### Generating studies

`tools/StudyGenerator` is a .NET 8 console app that writes studies with the Claude Message Batches API. Set `ANTHROPIC_API_KEY`, then from the repo:

```
cd tools/StudyGenerator
dotnet run -- one ROM 8              # one chapter now, to check the prompt
dotnet run -- submit --books MAT,MRK # queue a batch of missing chapters
dotnet run -- submit --all           # or every missing chapter
dotnet run -- status                 # progress of the last batch
dotnet run -- collect                # write finished studies and rebuild the index
dotnet run -- review ROM 8           # mark a study reviewed (--undo to revert)
```

Chapters that already have a study are skipped unless you pass `--force`; `--model` picks the model (default `claude-opus-5-5`) and `--limit N` caps a batch. `dryrun ROM 8` saves the request without calling the API. The prompt is in `tools/StudyGenerator/prompt.md`, with John 3 included as the worked example. Run a few chapters with `one` and read them before submitting a large batch.

All are static pages: open `index.html` in a browser (the Study page needs a web server to load its data, for example `python3 -m http.server`), or turn on GitHub Pages for this repo (Settings → Pages → deploy from `main`).

## Install as an app

The site is a progressive web app. On GitHub Pages (or any HTTPS host), use **Add to Home Screen** on iPhone or **Install app** on Android and desktop Chrome. It opens full screen with its own icon and works offline after the first visit. `sw.js` saves the pages; bump its `VERSION` when adding or renaming files.

Dates follow a conservative chronology (Exodus c. 1446 BC) and are approximate.

## How the pages fit together

Every page is a single HTML file with its own content, styles and data. What they share lives in two files:

- `site.css`: the color palette (light and dark), fonts, and the nav bar, phone menu and light/dark button.
- `site.js`: builds the nav on every page, remembers the light/dark choice, registers the offline worker, and provides small helpers pages use (`Site.esc`, `Site.gw` for BibleGateway links, `Site.splitRefs`).

Each page loads both in its `<head>` and marks where the nav goes with `<nav class="sitenav" aria-label="Site"></nav><script>Site.nav()</script>`.

To add a page: copy the `<head>` of an existing page, add one line for it to `PAGES` in `site.js` (it then appears in the nav and phone menu on every page), and add the file to `CORE` in `sw.js` and bump `VERSION`.

## Running the ASP.NET Core app

The site is moving to ASP.NET Core (.NET 10). The app serves the same pages and adds Google sign-in and **Ask**, where approved readers ask Bible questions answered by Claude. GitHub Pages keeps publishing the static pages from `main` until the switch; there, the Ask tab and Sign in simply don't appear.

```
dotnet run --project src/BibleHistory.Web --launch-profile https   # https://localhost:7104
dotnet test BibleHistory.slnx
```

Setting up Google sign-in, the Claude key and Azure: [docs/azure-setup.md](docs/azure-setup.md).

- `src/BibleHistory.Web`: the app.
  - `SiteFiles.cs` serves the public site from the folder in `Site:Root` (the repository root in development, a `site` folder in a published build). Only those files are served; source, tools and `.git` never are.
  - `Auth/`: Google sign-in and the cookie that keeps readers signed in. New accounts wait for approval; emails in `Admin:Emails` are admins.
  - `Ask/`: the Ask API and the Claude call (`AskModel.cs`), with the instructions Claude follows in `ask-system.md`. Each reader gets a daily limit (`Ask:DailyLimit`, default 20) and one answer at a time. Every question, answer, token count and estimated cost is saved.
  - `Admin/`: approve or ban readers, change limits, and read every question (the `admin.html` page).
  - `Data/`: the SQLite database (EF Core, with migrations applied at start-up). It lives outside the app folder, in `/home/data` on Azure, so deploys never touch it.
- `ask.html` and `admin.html`: the pages for Ask and its admin, built like the rest of the site.
- `tests/BibleHistory.Web.Tests`: every page and offline file is served, nothing else is; sign-in, approval, limits, streaming, logging and admin rules (using a stand-in for Claude, so tests cost nothing).
- `.github/workflows/dotnet.yml`: builds and tests every push; deploys `main` to Azure once it is set up.
