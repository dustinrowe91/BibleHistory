# The Scarlet Thread

A Bible-history study website: *Bible history from Creation to the Church.* The name is the "scarlet thread of
redemption", the one story of salvation running through Scripture (Joshua 2:18 to the cross). The site is live and
people use it, so treat `main` as production.

## Theological lens

Reformed in substance, but not obviously Reformed. Teach Scripture-first within historic, orthodox Protestant
Christianity (the ancient creeds and Reformation confessions): God's sovereignty and grace, grace alone through faith
alone in Christ alone, one covenant purpose across both Testaments, the Old Testament read toward Christ. Present this
as the plain teaching of the Bible in ordinary words. Don't label it with tradition names or argue against other
traditions unless asked. Where faithful Christians differ (baptism, millennium, church government, gifts, creation
timelines), give the main views fairly. Measure heresies and sects against the creeds, accurately and charitably.
Quotations follow the ESV; never invent a quotation.

## Branches (important)

- `main`: the static site GitHub Pages publishes. **Pushing to `main` changes the live site.** Page and content
  changes go here.
- `master`: the ASP.NET Core (.NET 10) version. All .NET work goes here. It is the only branch that deploys to Azure.
  Merge `main` into `master` regularly to bring page changes across.
- `main` also contains the .NET code from before the branches split. Leave it there (the owner's decision); it does
  not affect GitHub Pages.
- Commit straight to the branch; no pull requests unless asked.

## The static site

Plain HTML pages with inline CSS and JS, no build step: `index.html` (Timeline), `books.html`, `kings.html` (Kings &
Prophets), `people.html` (line of Jesus), `covenants.html`, `prophecy.html`, `plagues.html`, `church.html`
(denominations, heresies/sects, world religions), `maps.html`, `word-and-deed.html`, `study.html` (not in the nav),
plus `ask.html` and `admin.html` (need the .NET server). Pages cross-link to each other ("Also on this site").

- Shared code: `site.css` (palette, fonts, nav) and `site.js` (builds the nav, theme toggle, phone menu, helpers
  `Site.esc`, `Site.gw` for BibleGateway ESV links, `Site.splitRefs` for "Genesis 15:5; 22:18", `Site.markdown`,
  `Site.me`). Each page loads both in `<head>` and has `<nav class="sitenav" aria-label="Site"></nav><script>Site.nav()</script>`.
- To add a page: copy an existing page's `<head>`, add one line to `PAGES` in `site.js`, add the file to `CORE` in
  `sw.js` and bump `VERSION`.
- Style: palette tokens on `:root` with dark mode (`--lapis #2747a3`, `--ochre #a8701a`, `--crimson`); fonts Cinzel
  (display), Alegreya (body), Alegreya Sans (UI). Each page's `<style>` opens with a short layout comment.
- Must work on phones: 16px side gutters, no horizontal scroll, nav switches to a Menu button below 820px (and when
  the tabs would wrap). Keep everything working offline (PWA, `sw.js`, network-first pages).
- Maps use an equirectangular SVG from Natural Earth data, simplified and inlined as `const GEO=`.

## The ASP.NET Core app (`master`)

`BibleHistory.slnx`, `src/BibleHistory.Web`, `tests/BibleHistory.Web.Tests` (see README "Running the ASP.NET Core
app" and `docs/azure-setup.md`).

- Serves the static pages unchanged (`SiteFiles.cs`, an allow-list: nothing else in the repo is ever served).
- Google sign-in only (cookie, 30 days). New accounts are Pending until an admin approves them; emails in
  `Admin:Emails` are admins and approved automatically. Admin page: approve/ban, per-person daily limits, every
  question with its cost.
- Ask: approved readers ask Bible questions; Claude answers, streamed as NDJSON. Claude Opus 5.5
  (`claude-opus-5-5`), effort medium, server-side refusal fallback (`fallbacks: default`), cached system prompt in
  `Ask/ask-system.md`. Daily limit 20 by default, one answer at a time, 1,000-character questions, JSON-only POSTs.
  Every question, answer, tokens and estimated cost is logged. Web search is off (possible later).
- Data: SQLite via EF Core, migrations applied at start-up, rollback journal (not WAL: Azure's /home is a network
  share). Database and sign-in keys live outside the app folder (`/home/data` on Azure, `App_Data` locally) so
  deploys never overwrite them. Keep the App Service at one instance.
- Secrets never go in the repo: Google client ID/secret, `Anthropic:ApiKey` and `Admin:Emails` are App Settings in
  Azure (user-secrets locally).
- Tests use a fake Claude and a test sign-in, so they cost nothing: `dotnet test BibleHistory.slnx`.
- CI (`.github/workflows/dotnet.yml`) builds and tests every push; deploys `master` to Azure once the repo has the
  `AZURE_WEBAPP_NAME` variable and `AZURE_WEBAPP_PUBLISH_PROFILE` secret.

## Checking work

- Static pages: open every changed page in a browser at desktop and phone widths, light and dark; no console errors,
  no horizontal scroll, nav and phone menu working, Scripture links valid.
- .NET: `dotnet test BibleHistory.slnx`; for the pages that need the server, run the app and fake `/api/*` responses
  in the browser rather than spending real Claude calls.

## Status and next steps

- Azure: the web app **BibleAi** exists (created in the portal). `.github/workflows/master_bibleai.yml`, generated
  by Azure's Deployment Center, deploys every push to `master` (federated sign-in through GitHub secrets). The deploy
  job in `dotnet.yml` is unused (it needs `AZURE_WEBAPP_NAME`, which is not set). Still to do: Google OAuth client
  (redirect `https://<app host>/signin-google`), Anthropic API key with a spend limit, and the App Settings. Real
  Google sign-in and real Claude answers have not been tested yet.
- Later: upgrade to B1 (Always On, custom domain), point a domain at it, turn off GitHub Pages; maybe web search for
  Ask; maybe rename the repo and the `BibleHistory.*` .NET names to match the new site name.
- `tools/StudyGenerator`: a .NET console tool that writes the chapter studies in `studies/` with the Claude Batches API.

Creating an App Service from the CLI instead (Azure Cloud Shell), for reference:

```bash
RG=scarletthread-rg; LOC=centralus; PLAN=scarletthread-plan; APP=scarletthread   # APP must be globally unique
az group create --name $RG --location $LOC
az appservice plan create --name $PLAN --resource-group $RG --location $LOC --is-linux --sku F1
az webapp create --name $APP --resource-group $RG --plan $PLAN --runtime "DOTNETCORE:10.0"
az webapp update --name $APP --resource-group $RG --https-only true
az webapp config set --name $APP --resource-group $RG --ftps-state Disabled --generic-configurations '{"healthCheckPath": "/healthz"}'
az resource update --resource-group $RG --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies \
  --parent sites/$APP --name scm --set properties.allow=true
az webapp deployment list-publishing-profiles --name $APP --resource-group $RG --xml   # into the GitHub secret
```
