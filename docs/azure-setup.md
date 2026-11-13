# Putting the site on Azure

One-time setup for the ASP.NET Core app: Google sign-in, the Claude API key, the App Service, and automatic deploys.
Allow about 30 minutes. Nothing secret goes in the repository; every key is entered in Google, Anthropic, Azure or
GitHub settings.

## 1. Create the App Service

In the [Azure portal](https://portal.azure.com): **Create a resource > Web App**.

| Setting | Value |
|---|---|
| Publish | Code |
| Runtime stack | .NET 10 (LTS) |
| Operating system | Linux |
| Pricing plan | Basic B1 (about $13/month) or higher. The free tier sleeps and has no Always On. |

After it is created, in the app:

- **Settings > Configuration > General settings**: Always on **On**. Under **Platform settings**, turn on
  **SCM Basic Auth Publishing Credentials** (needed for the publish profile in step 5).
- **Monitoring > Health check**: enable, path `/healthz`.
- **Scale out**: keep **1 instance**. The database is SQLite, a single file, which one instance must own.

Note the app's address, `https://<your-app>.azurewebsites.net`. You need it for Google.

## 2. Google sign-in

In the [Google Cloud console](https://console.cloud.google.com), create a project (for example "Bible History"), then:

1. **APIs & Services > OAuth consent screen**: user type **External**; app name, your support email, and the app's
   address as the home page. Scopes: only the default `openid`, `email`, `profile`. Then **Publish app**, so anyone
   with a Google account can sign in. These basic scopes need no Google review.
2. **APIs & Services > Credentials > Create credentials > OAuth client ID**: type **Web application**.
   Authorized redirect URIs:
   - `https://<your-app>.azurewebsites.net/signin-google`
   - `https://<your-domain>/signin-google` (if you add your own domain later)
   - `https://localhost:7104/signin-google` (for running it on your machine)
3. Copy the **Client ID** and **Client secret**.

## 3. Claude API key

At [console.anthropic.com](https://console.anthropic.com): add billing, create an API key, and under **Limits** set a
monthly spend limit you are comfortable with. Ask uses Claude Opus 5.5; a typical answer costs 2 to 3 cents.

## 4. App settings

In the App Service: **Settings > Environment variables > App settings**, add:

| Name | Value |
|---|---|
| `Authentication__Google__ClientId` | from step 2 |
| `Authentication__Google__ClientSecret` | from step 2 |
| `Anthropic__ApiKey` | from step 3 |
| `Admin__Emails` | your Google email (comma-separate to add more admins) |

Optional: `Ask__DailyLimit` (questions per reader per day, default 20), `Ask__Effort` (`low`, `medium` (default) or
`high`), `Ask__Model` (default `claude-opus-5-5`).

The database and sign-in keys are stored in `/home/data` automatically on App Service. `/home` is the app's
permanent storage, and deploys only replace `/home/site/wwwroot`, so redeploying never touches them. To back the
database up, download `/home/data/biblehistory.db` from the app's Kudu console
(`https://<your-app>.scm.azurewebsites.net`), or turn on **Backups** in the portal.

## 5. Automatic deploys from GitHub

1. In the App Service **Overview**, click **Download publish profile**.
2. In the GitHub repository: **Settings > Secrets and variables > Actions**:
   - **Secrets > New repository secret**: name `AZURE_WEBAPP_PUBLISH_PROFILE`, value: the whole downloaded file.
   - **Variables > New repository variable**: name `AZURE_WEBAPP_NAME`, value: the App Service name.
3. Push to `master` (or re-run the latest **.NET build, test and deploy** workflow on `master`). The deploy job now
   runs after the tests pass. Pushes to `main` (the GitHub Pages site) never deploy.

## 6. First sign-in

Open `https://<your-app>.azurewebsites.net/ask.html` and sign in with the Google account in `Admin__Emails`. You are
approved automatically, and the account menu (top right) has **Admin**, where you approve everyone else.

When you are happy with it, point your domain at the App Service (**Custom domains**, with a free managed
certificate), add that domain's `/signin-google` address in Google, and turn off GitHub Pages.

## Branches

- `main`: the static site GitHub Pages publishes. Content changes to the pages can go here and go live at once.
- `master`: the ASP.NET Core version, deployed to Azure. Merge `main` into it to bring page changes across.

## Running it on your machine

```
dotnet user-secrets --project src/BibleHistory.Web set "Authentication:Google:ClientId" "<client id>"
dotnet user-secrets --project src/BibleHistory.Web set "Authentication:Google:ClientSecret" "<client secret>"
dotnet user-secrets --project src/BibleHistory.Web set "Anthropic:ApiKey" "<api key>"
dotnet user-secrets --project src/BibleHistory.Web set "Admin:Emails" "<your google email>"
dotnet run --project src/BibleHistory.Web --launch-profile https
```

Then open `https://localhost:7104/ask.html`. Use the https address: Google sign-in needs it. The local database is
`src/BibleHistory.Web/App_Data/biblehistory.db`, which git ignores.
