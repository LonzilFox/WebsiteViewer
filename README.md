# WebsiteViewer

WebsiteViewer is a Windows desktop tool for exploring the structure of a website.

It starts from a specified URL, discovers reachable internal HTML pages, and presents the results in a structured interface. It is designed for quickly estimating website size while also showing URL hierarchy, sections, parent-child relationships, and page response performance.

## Features

- Crawl internal HTML pages starting from a specified URL.
- Show each discovered page in the interface while the crawl is running.
- Display HTTP status, crawl depth, section, request time, HTML parsing time, title, and URL.
- Group pages by top-level URL section and show section statistics.
- Display a URL hierarchy tree with parent-child relationships.
- Show the inferred parent URL and discovery source for a selected page.
- Display canonical URL and crawl error information when available.
- Show live crawl statistics:
  - Pages scanned
  - Successful and failed pages
  - Discovered URLs
  - Queue size
  - Crawl speed
  - Elapsed time
  - Estimated remaining time
  - Average request time
  - Average parsing time
- Stop a crawl without losing the pages already collected.
- Filter the page list by URL, title, section, or status.
- Export crawl results to CSV and JSON files.
- Support `robots.txt`.
- Optionally remove URL query parameters when normalizing URLs.
- Optionally include subdomains.
- Configure maximum pages, maximum crawl depth, request delay, and concurrency.
- Use a dark, card-based interface with adaptive layouts and high-DPI support.
- The application starts with all settings and page details expanded and adapts its initial window size to the available screen area. The layout becomes more compact on smaller displays. Vertical scrolling is available if the complete view cannot fit. Settings and details can also be collapsed manually.
- Dark title bars and system scrollbars are available where supported by Windows.

## User Interface

The application is organized into four main views:

### Pages

Shows the discovered pages in a virtualized table.

Available information includes:

- HTTP status
- Crawl depth
- Section
- Request time
- Parse time
- Page title
- URL

Selecting a page displays additional information in the detail area. Long URLs can be selected and copied from the read-only fields. Double-click a page to open it in the default browser.

### Sections

Shows page counts grouped by top-level URL section, including successful and failed page counts.

### Structure

Displays the discovered URL hierarchy as a collapsible tree. Selecting a branch can be used to inspect the related pages.

### Logs

Shows crawl activity and runtime information.

## Crawl Settings

The main configuration area provides:

- **Target URL** — The website entry point.
- **Maximum Pages** — Maximum number of pages to process.
- **Maximum Depth** — Maximum link depth from the starting URL.
- **Request Delay** — Delay between crawl requests.
- **Concurrency** — Number of concurrent crawl workers.
- **Ignore URL Query** — Removes query parameters when normalizing URLs.
- **Respect robots.txt** — Prevents crawling URLs disallowed by `robots.txt`.
- **Include Subdomains** — Allows crawling matching subdomains.

Numeric fields support direct typing, the plus and minus buttons, and the Up and Down keys.

The crawler stays within the configured website scope and ignores common non-HTML resources such as images, scripts, stylesheets, archives, and media files.

## Exported Files

The application can export the following files:

### `urls.csv`

Contains page-level information such as:

- URL
- Title
- Status
- Depth
- Section
- Parent URL
- Source URL
- Canonical URL
- Crawl time
- Parse time
- Error information

### `edges.csv`

Contains discovered internal link relationships:

```text
source_url,target_url
```

This can be used to analyze website connectivity or import the relationships into graph visualization tools.

### `section_counts.csv`

Contains the number of pages discovered in each top-level section.

### `tree.json`

Contains the discovered URL hierarchy in JSON format.

### `summary.json`

Contains crawl summary information and aggregate timing statistics.

## Output Directory

By default, crawl results are written to the configured output directory.

The application also stores crash diagnostics under:

```text
%LOCALAPPDATA%\WebsiteViewer\logs\
```

If the application terminates unexpectedly, check:

```text
latest_crash.log
```

for diagnostic information.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK for development and local builds

Check the installed SDK with:

```bat
dotnet --version
```

## Development

Run the application from the project directory:

```bat
run-dev.bat
```

Or use the .NET CLI:

```powershell
dotnet run
```

The development script builds the project before launching the application. If the application exits unexpectedly, the command window remains open so that the exit code and diagnostic information can be inspected.

## Release Build

Build and publish a self-contained Windows x64 application:

```bat
build-release.bat
```

The executable is placed under:

```text
publish\win-x64\WebsiteViewer.exe
```

The self-contained build does not require the target computer to have the .NET runtime installed.

## GitHub

The project can be uploaded directly to a GitHub repository.

The repository includes a GitHub Actions workflow for automated Windows builds.

The following local build and generated directories should not be committed:

```text
bin/
obj/
publish/
out/
.vs/
```

These paths are already covered by `.gitignore`.

## Crawl Behavior and Limitations

WebsiteViewer discovers pages by following internal links found in the HTML returned by the server.

It does not execute client-side JavaScript.

As a result, the following types of pages may not be discovered automatically:

- Pages generated only after JavaScript execution
- Pages requiring authentication
- Pages that have no reachable internal links
- Pages exposed only through application state
- Content loaded dynamically through API requests

The reported page count should therefore be understood as the number of pages that are actually discoverable from the selected entry point under the current crawl settings, rather than an absolute count of every route implemented by the website.

Use reasonable concurrency and request delays, and follow the target website's terms of use, `robots.txt`, and applicable policies.
