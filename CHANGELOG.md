# Changelog

🇬🇧 **English** · 🇹🇷 [Türkçe](CHANGELOG.tr.md)

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

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

### Removed

- The screenshots of the original application, which showed third-party film posters; the design update brings
  new ones.

### Security

- User names, e-mail addresses, phone numbers, passwords, setup keys, film titles and exception messages are never
  written to the log.

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
