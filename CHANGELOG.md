# Changelog

🇬🇧 **English** · 🇹🇷 [Türkçe](CHANGELOG.tr.md)

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Catalog search by title, genre, year range, minimum rating and availability, paged in the service (12 films a
  page for customers, 50 for admins) in a stable order, with the latest answer shown on both screens; the admin's
  film grid searches every film, unlisted ones included.
- Watchlists: customers keep up to 100 films to rent later, see which are available and rent them from the list;
  a rental takes the film off the list. New table `IzlemeListesi` (migration `AddWatchlist`).
- Ratings and reviews: 1 to 5 stars and a plain text comment of at most 500 characters for films the customer has
  returned, one per film, changeable and deletable; averages with one decimal on the cards and the rental screen;
  admins hide and show reviews. New table `Degerlendirmeler` (migration `AddReviews`).
- Admin reports of the most rented films, overdue rentals and late fees for a period, saved as spreadsheet safe
  CSV, and genre based recommendations for customers.
- Audit codes `review.saved`, `review.deleted`, `review.moderated`, `watchlist.changed` and `report.exported`.
- Daily rolling file logs with Serilog under `%LOCALAPPDATA%\FilmKiralama\logs`: files older than 30 days are
  deleted and at most 30 files are kept (tests check this with dated files). Error entries keep only exception
  types, HResult codes and method names.
- Anonymous audit entries in the log file: a fixed code and a role only, never an account or a film. Every refused
  sign-in, also one with an empty field, writes the same code without a role, and a new account and a sign-up with
  taken values write the same neutral code, `auth.account.registration_submitted`.
- Central FluentValidation validators for sign-in, sign-up, password changes, the first admin, the admin's account
  form, the film form and the rental days; the rules and the Turkish messages are unchanged.
- More Moq and SQLite tests: the shelf at zero, one and every copy, the last copy, the first late day, the longest
  rental, the lock length, refused input that writes nothing, audit codes, and tests that the log files carry no
  personal data, setup keys or outcomes that tell accounts or rentals apart.
- English and Turkish README with an architecture diagram, the legacy upgrade and the first-run setup, a threat
  model (`docs/THREAT_MODEL.md`) and this changelog.
- Release job in CI: on `v*` tags the tag name is checked, a win-x64 build is published and uploaded as a zip
  artifact together with the READMEs, the changelogs and `docs/*.sql`.

### Changed

- Errors are written to the log file instead of the trace output; the error dialogs are unchanged. Refused
  operations leave no log entry of their own.
- Sign-in locks and rental, due and return days follow the database server's UTC clock instead of the computer's;
  the rental and return confirmations show dates and late fees from the same clock.
- Customers also see listed films without a copy on the shelf when "Yalnızca müsait" is off; they cannot be rented
  but can go on the watchlist.
- `docs/grant-app-login.sql` grants the rights on the review and watchlist tables.

### Fixed

- Poster sizes are checked from the file header before decoding, so a small file that declares a huge bitmap is
  refused without being decoded; at most three posters download at a time.
- A decoded poster is kept only at card size (at most 480 × 720) instead of at its full size, so the memory a page
  of cards holds no longer grows with the size of the images.
- Poster downloads refuse private addresses hidden in IPv4-translated and local-use NAT64 IPv6 addresses, and
  documentation and discard-only addresses.
- Poster redirects from https to http are refused.
- The catalog searches a typed title once the typing pauses (300 ms or Enter) instead of on every key.
- `report.exported` is written only after the CSV file is saved; a file that cannot be written gets its own message
  instead of the general error, and the overdue file is named with the day its rows were counted on.
- CSV fields that start with a formula character after leading spaces or line breaks are prefixed too.
- The genre search has its own length message and refuses hidden characters.
- A watchlist entry of a film unlisted since shows only its title and "Kaldırıldı".

### Removed

- The screenshots of the original application, which showed third-party film posters; the design update brings
  new ones.

### Security

- A review an admin hid is never removed: when its author deletes it, its comment is cleared and it stays hidden
  out of every list, so writing it again leaves it hidden (column `Silindi`, migration `AddReviewDeletion`).
- Sign-in reads the database clock before it looks the account up, so known and unknown names take the same
  round trips.
- A deleted account's open session can no longer search the catalog, read reviews or get recommendations, and a
  retried concurrent review save checks the account and the eligibility again.
- User names, e-mail addresses, phone numbers, passwords, setup keys, film titles and exception messages are never
  written to the log.
- Review comments and searches are never written to the log; another account's review or watchlist entry gets
  the answer of a missing one and leaves the same entries. Review authors are masked for other customers.

## Foundation

The rewrite of the original application, merged before this changelog was started.

### Added

- Layered .NET 10 solution (Domain, Business, Data, UI) with EF Core, dependency injection and automatic
  migrations; the original database is upgraded in place, keeping its data.
- Film stock with copies on the shelf, one active rental per film and account, returns with a configurable daily
  late fee, filters for the catalog and the admin's rental list.
- First-run setup for an empty database; for a database with accounts but no admin, a single use setup key that
  only the database owner issues with `docs/issue-setup-key.sql`, and example rights for the application's login
  in `docs/grant-app-login.sql`.
- Optional demo mode (`FILM_DEMO`, `Seed:Demo`), off by default.
- Original artwork in place of third-party images, and poster downloads limited to public addresses.
- Tests for the services on SQLite and for races, setup keys and the legacy upgrade on SQL Server; CI with a SQL
  Server job.

### Security

- PBKDF2 password hashes with an upgrade of plain text passwords on the next sign-in; lockout after 5 failed
  attempts for 15 minutes with the same answer for every failure.
- Role and owner checks in every service, with the acting account's stored role checked inside every privileged
  operation; another account's rental gets the same answer as a missing one.
- Rentals and returns under a per-film lock, so the shelf never goes below zero.
- No account with a known password in a default installation.
