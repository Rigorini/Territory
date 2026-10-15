# Territory Register

A territory management system for Jehovah's Witnesses congregations. It keeps track of municipalities, territories and publishers, assigns and returns territories, and records the full assignment history. 



## Why This Software?

There are many cloud-based solutions available for managing congregation territories, and I was asked whether we should consider using one of them. This raised a few questions for me.

Who provides these services? Where is the data stored? What permissions are granted to the service providers, and what legal guarantees do they offer?

In Europe, privacy regulations are strict. Publishers give congregations written authorization to use their personal data, but they do not grant such authorization to the brothers who provide these services. Furthermore, there is no guarantee that someone providing a cloud service will always remain a faithful brother or that the information collected through the service will never be used against the congregation.

With the goal of giving every congregation access to a simple and modern territory management solution, while at the same time allowing each congregation to retain full control over and responsibility for its own data, I decided to create software that would be as simple as possible and that each congregation could download, install wherever it prefers, and use independently.

Anyone who would like to contribute to the project is welcome, provided they share the core principles described above.

## Smartphone interface for publishers

The software provides publishers with a **mobile-friendly web interface for their smartphone**, showing the assigned territory on a **map with GPS**, so they can see their own position while working the territory.

## Territory maps (Google My Maps)

The software does not draw maps itself. Each territory map must be created with **[Google My Maps](https://www.google.com/maps/d/)**, shared so that the people who need it can view it, and then its **link must be pasted into the territory** in the application.

## Technology and hosting

- **Backend**: .NET 10, ASP.NET Core minimal API.
- **Frontend**: Angular with Angular Material.
- **Database**: **PostgreSQL**.

The application is built with ASP.NET, Angular and Postgres, and it can be installed on any ASP.NET hosting, **even a free one such as [MonsterASP.NET](https://www.monsterasp.net/)**, as long as a PostgreSQL database is reachable.

## Database setup

To create the database structure, run the included script **`db/001_schema.sql`**. It is idempotent: it is the only script to run, and it can be run again on an existing database to add new columns. EF migrations are not used. The script must be executed with the table owner (for example `postgres`); the application user only needs read/write access to the tables.

On first start, with an empty database, the `/setup` page lets you create the first administrator.

## Configuration

Files are in `src/Territory.Api`.

> **Warning – use Environment Variables.** `ConnectionStrings` and `PersonalData:Key` contain secrets. The correct way to provide them is through **environment variables** (or your hosting panel's settings, user-secrets in development). **Do not put them in `appsettings.json`** and never commit them. For local development only, `appsettings.Development.json` can be used (it is excluded from the deploy).

In environment variables, the `:` separator of the configuration keys is written as a double underscore `__`.

### ConnectionStrings

Npgsql connection string to the PostgreSQL database.

```powershell
# Windows PowerShell
$env:ConnectionStrings__Territory = "Host=localhost;Port=5432;Database=territory;Username=territory_app;Password=<your-password>"
```

```bash
# Linux / macOS
export ConnectionStrings__Territory="Host=localhost;Port=5432;Database=territory;Username=territory_app;Password=<your-password>"
```

On a hosting provider, create a variable named `ConnectionStrings__Territory` with the host, database, user and password provided by the provider (add `SSL Mode=Require` if the provider requires it).

### PersonalData (the key)

Personal data (first and last names of users and publishers) is encrypted with AES-GCM in the database. The application **does not start without a key**, and the key must never be stored in the repository or in the database.

> **Important – names in the database are encrypted. If you lose the key, they can no longer be read.** Keep a safe copy of the key (for example in a password manager), and never change it without re-encrypting the data first. The key must be the same on all instances.

**Step 1 – Generate the key** (a 256-bit key encoded in base64, i.e. 44 characters). Pick one option.

Option A – using the application itself (recommended):

```powershell
dotnet run --project src\Territory.Api -- generate-personal-data-key
```

Option B – PowerShell only:

```powershell
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
Write-Output ([Convert]::ToBase64String($bytes))
```

Option C – Linux / macOS:

```bash
openssl rand -base64 32
```

Example output (do not reuse it): `q3p0V0m1uQ0kYc8bX1o0r7yQ2oZpGmJt8m3kqf9eN0U=`

**Step 2 – Provide the key** as an environment variable:

```powershell
$env:PersonalData__Key = "<base64-key>"
```

Alternatively, store it in a file, by default `keys/personal-data.key` (relative to the app folder, configurable with `PersonalData:KeyFile`). The command `generate-personal-data-key --file` creates it with restricted permissions.



### AllowedHosts

ASP.NET Core only answers requests whose `Host` header matches this list; any other host receives `400 Bad Request`. It protects against host-header spoofing. The default value in `appsettings.json` is `*` (any host), which is fine for local development but **should be restricted in production**.

Rules:

- Separate multiple hosts with `;`.
- Host name only: no scheme (`https://`), no path, no port.
- Wildcards are allowed only as a leading `*.` (for example `*.example.com`, which does not match `example.com` itself).
- List every domain users will actually type, including `www` and any custom domain.

Examples:

```json
"AllowedHosts": "mycongregation.runasp.net"
"AllowedHosts": "territories.example.com;www.territories.example.com"
"AllowedHosts": "*.example.com"
```

As an environment variable (preferred in production; not a secret, but it is environment-specific):

```powershell
$env:AllowedHosts = "mycongregation.runasp.net"
```

Restart or republish after the change. If the site suddenly returns `400` after setting it, the domain in the browser is missing from the list.

### S13

Official S-13 registers in Word, one per language, read at runtime as templates:

```json
"S13": {
  "Folder": "s13",
  "Templates": {
    "it": "S-13_I.docx",
    "en": "S-13_E.docx"
  }
}
```

- `Folder`: templates folder. If relative, it is looked up in the app folder and then in the working folder. The `.docx` files in `src/Territory.Api/s13` are automatically copied to the output and to the published files.
- `Templates`: for each language code, the **file name only** of the template (paths are not allowed). If the requested language is missing, English is used; if that is missing too, the export fails.
- The register is per service year (September–August, selected by the **assignment** date): one page per municipality, with extra pages when more rows or columns are needed. Export it from *Export register → Export S-13*.

## Adding a new language (developer guide)

Example: adding French (`fr`).

1. **Client – translations**: copy `client/public/i18n/en.json` to `fr.json` and translate all the keys (including `prefs.languages.fr`, the language name, which must also be added to the other `*.json` files).

2. **Client – language list**: in `client/src/app/core/language.service.ts` add `'fr'` to `SUPPORTED_LANGUAGES`. The language menu, the browser-based choice and the profile saving already use it. Check that date formatting and plural handling (`Intl`) are correct for the new language.

3. **Server – language list**: in `src/Territory.Application/Common/Languages.cs` add the constant and include it in `Supported` (the server rejects unlisted languages when saving the profile). The comment states that it must match the client.

4. **Server – culture**: in `src/Territory.Api/Program.cs` add `new CultureInfo("fr")` to `supportedCultures`.

5. **Server – Excel/Word register texts**: in `src/Territory.Application/Export/RegisterTexts.cs` add a `French` instance with the translated headers and return it from `RegisterTexts.For(...)` (currently everything that is not English falls back to Italian).

6. **S-13**: obtain the official French S-13 template (same structure as the existing ones: header table, register table with 2 title rows and data rows in pairs, 4 assignment columns), copy it to `src/Territory.Api/s13/` and register it in `appsettings.json`:
   
   ```json
   "Templates": { "it": "S-13_I.docx", "en": "S-13_E.docx", "fr": "S-13_F.docx" }
   ```
   
   No change to the export code is needed: available rows and columns are read from the template. The template is filled respecting the contents of the first table (service year and municipality) and the `dd/MM/yy` date format.

7. **Tests**: update/add the relevant tests (for example `language.service.spec.ts`, `Application/DocxS13WriterTests.cs`) and try the S-13 export by opening the generated file in Word.

The database needs no changes: the user's language is stored as a text code.

## License

**License:** This software is provided exclusively for non-commercial use for the benefit of congregations affiliated with Jehovah's Witnesses. See the LICENSE file for details.
