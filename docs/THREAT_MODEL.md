# Threat Model

A short threat model for the Film Kiralama desktop application. The application stores accounts with passwords,
e-mail addresses and phone numbers, the shop's films and stock, and the rentals of every account with their
prices and late fees. User names, e-mail addresses and phone numbers, and the rentals linked to them, are personal
data under the Turkish Personal Data Protection Law (KVKK, Law No. 6698). This document lists technical measures
only; it is not a legal assessment.

## Scope and assets

- **Application:** a Windows Forms client with customer and admin sign-in that talks directly to SQL Server and
  downloads film posters from the web.
- **Assets:**
  - Accounts: user names, e-mail addresses, phone numbers, roles, passwords and their stored hashes; sign-in
    counters and lockout times.
  - Films and their stock (copies owned, copies on the shelf); rentals with dates, prices and late fees.
  - Setup keys issued by the database owner (`KurulumAnahtarlari`, stored as SHA-256 hashes).
  - The connection string (server, database, and possibly SQL credentials).
  - Log files under `%LOCALAPPDATA%\FilmKiralama\logs`.
  - Database backups made by the operator (`.bak` files).
- **Trust boundaries:** the Windows user session that runs the application; the network link to SQL Server; the
  network link to poster hosts; the local file system (settings, logs, backups); the boundary between a signed-in
  account and the data of other accounts; the boundary between the application's SQL login and the database owner.

## Assumptions

- The application runs on trusted workstations; people sign in on them with their own accounts.
- Every instance connects with the same SQL Server login, so the database itself does not tell accounts apart:
  roles and ownership are enforced by the application.
- The database owner (db_owner or sysadmin) is a different, more trusted principal than the application's login,
  and is the only one who creates schema and setup keys.
- The operator is the data controller and is responsible for consent, retention, deletion requests and the
  security of the server, its logins and its backups.
- A new database contains no account. Demo accounts (fictional, passwords published in the README) are created
  only when demo mode is switched on explicitly with `FILM_DEMO=1` or `Seed:Demo=true`, and only in a completely
  empty database; demo mode is never used with real data.

## Identity and authentication

| Threat | Mitigation | Status |
|---|---|---|
| Passwords read from the database or a backup | PBKDF2-HMAC-SHA256 with 600,000 iterations and a 16 byte random salt per password; compared in constant time | Mitigated |
| Plain text passwords of the original application | The legacy upgrade marks them as legacy plain text in their own format column; the first successful sign-in replaces each with a PBKDF2 hash in the same conditional update | Partly mitigated (see residual risks) |
| A stored value read in the wrong format | The format comes from the format column, never from the shape of the value | Mitigated |
| Weak new passwords | Every new password (sign-up, setup, password change, admin reset) goes through the same validator: 8 to 64 characters | Mitigated (length policy only) |
| Online password guessing | 5 failed attempts lock the account for 15 minutes; the counter is increased in one atomic `UPDATE`, attempts during a lock are neither counted nor extend it, and a success is written only while the account is still unlocked and unchanged. A wrong current password in the password change counts as a failed sign-in | Mitigated |
| Account enumeration through sign-in answers or timing | Unknown accounts, wrong passwords and locked accounts get the same message, and each costs one key derivation | Mitigated |
| Account enumeration through sign-up | The sign-up form names the taken values, as the original form did; anyone at the sign-up screen can learn whether a user name, e-mail address or phone number is registered | Accepted |
| Lockout used to block a known account (denial of service) | The lock ends by itself after 15 minutes; no permanent lock exists, and an admin's password reset lifts it | Accepted |
| Accounts with published passwords in a default installation | The migrations insert no rows; demo data is added only in demo mode and only to an empty database | Mitigated |

## Roles, sessions and ownership

| Threat | Mitigation | Status |
|---|---|---|
| A customer managing films, accounts or every rental | These calls require the admin role in the service; a denied call touches no repository and is audited as `access.denied` | Mitigated |
| A stale session: an account deleted or demoted while it is signed in | The session's role only decides what it may ask for. Every privileged operation and every rental operation checks the stored account and its role again inside its own statement or transaction; when the account is gone or lost the role, nothing is written and the user is asked to sign in again (`access.session_revoked`). Tested on SQLite and on SQL Server | Mitigated |
| Self-promotion to admin | Sign-up always creates the user role; a customer's session cannot change roles; the first admin is created only on an empty database, or with a setup key from the database owner while no admin exists, atomically under a table lock | Mitigated |
| Losing the last admin | The last admin cannot be demoted, also when two admins demote each other at the same time (tested on SQL Server); admins cannot be deleted | Mitigated |
| IDOR: returning or listing another account's rental | A customer's rentals are read and returned only with the session's own account id in the statement; another account's rental, a missing one and one already returned get the same answer and leave the same log entries, so rental ids cannot be probed. Admins may return any rental | Mitigated |

## Stock and locks

| Threat | Mitigation | Status |
|---|---|---|
| The shelf going below zero or more copies rented than owned | A rental and a return change the copies on the shelf in the same transaction as the rental row, under an exclusive `sp_getapplock` lock per film; check constraints keep the copies between zero and the copies owned. Races for the last copy and for two copies are tested on SQL Server | Mitigated |
| The same film rented twice by one account | A filtered unique index allows one active rental per account and film; a concurrent second request of the same account is refused | Mitigated |
| A rental at a price the customer did not see | The quote's daily price is compared with the stored price inside the transaction; a changed price refuses the rental | Mitigated |
| A return counted twice | The return updates only an active rental, so two concurrent returns put one copy back (tested on SQL Server) | Mitigated |
| A held lock making operations hang | The lock wait is bounded; a request that cannot get the film's lock ends with a "busy" message instead of a command timeout | Mitigated |
| Late fees or totals overflowing | Totals and fees are computed in `decimal` and capped at the largest amount the money column can hold; rental length and the fee per day are limited by validated settings | Mitigated |

## Poster addresses

| Threat | Mitigation | Status |
|---|---|---|
| Server-side request forgery from every customer's computer: a poster address pointing to localhost, the local network or a cloud metadata address | Poster addresses must be absolute http or https addresses of at most 255 characters when an admin saves them. When a poster is shown, the address must carry no user information and every address the host name resolves to must be public (no loopback, private, link-local, shared, multicast or reserved address, also not in an IPv6 form), and the connection goes to the address that was checked; redirects are followed by hand under the same rules, at most 3 | Mitigated |
| Local files or network shares opened through a poster address | Only http and https are fetched; anything else shows the application's own placeholder without opening it | Mitigated |
| Large or malicious images | At most 2 MB of an image content type, at most 4096 × 4096 pixels, within 10 seconds; no proxy, cookies or credentials are sent | Mitigated |
| A poster host learning which customers look at which film | Posters are fetched by the customer's computer, so the host sees its address and the time | Accepted |

## Setup key and the accepted limit

| Threat | Mitigation | Status |
|---|---|---|
| A client creating its own authority to become the first admin | The application has no way to create a key. Only the database owner runs `docs/issue-setup-key.sql`, which takes 16 random bytes, stores only the SHA-256 hash and shows the key once; the script refuses when an admin exists | Mitigated |
| The application's login forging, extending or deleting keys | `docs/grant-app-login.sql` grants `SELECT` on the key table and `UPDATE` only of `Tuketildi` and `TuketimTarihi`, and denies `INSERT`, `DELETE` and `UPDATE` of the id, hash, issue time and expiry; tested with that login on SQL Server | Mitigated |
| Guessing a key | 128-bit keys; every refused key is followed by a fixed 1 second pause; stored keys are compared in constant time. Wrong attempts are not counted, so nobody can revoke the founder's key by guessing | Mitigated |
| Reusing a key or using it after its 24 hours | A key is used up by a conditional `UPDATE` in the same transaction that writes the admin, and its expiry is decided by the database's clock; a failed write rolls the use back; unused keys are refused once an admin exists | Mitigated |
| Keys in logs or settings | The key is never logged or stored by the application; `FILM_SETUP_KEY` only prefills the field and a malformed value is not shown | Mitigated |
| **Accepted limit:** whoever holds the application login's credentials | That login may read and write the account, film and rental tables (deleting accounts and films, never rentals), read the setup key hashes and mark keys used. Anyone with the connection string can therefore read all personal data, change rows directly, raise an account's role in the table, or spend an unused key without creating an admin; they cannot create or extend a key. Protect the connection string and grant the login nothing more than `docs/grant-app-login.sql` | Accepted |

## Logging

| Threat | Mitigation | Status |
|---|---|---|
| Personal data written to log files | Error entries contain only exception types, HResult codes and method names, without messages or file paths; refused operations leave no entry; audit entries are a fixed code and a role, with no account id, user name, e-mail address, phone number, password, setup key, film or amount. Tests run whole sessions on SQLite and check the files | Mitigated |
| Account existence or lock state disclosed through the log | Every refused sign-in, from an empty field to a locked account, writes the same `auth.sign_in.failed unknown` entry; a new account and a sign-up with taken values write the same `auth.account.registration_submitted user` entry; a foreign, a missing and a returned rental leave the same entries. Tests compare the files of each outcome | Mitigated |
| Log files read by other Windows users | Logs are under the user's own local application data folder | Mitigated by OS permissions |
| Log files read by another application account on the same Windows account | The files hold only anonymous codes, roles and exception types; counts and times of events remain visible | Mitigated (residual) |
| Log files growing without bound | Daily rolling files; files dated more than 30 days ago and any beyond the newest 30 are deleted. Tests create dated files and check which remain | Mitigated |
| Personal data in error dialogs | Users see fixed Turkish messages; raw exception text is never shown | Mitigated |

## Backups

| Threat | Mitigation | Status |
|---|---|---|
| Backups exposing personal data and legacy plain text passwords | `*.bak`, `*.mdf` and `*.ldf` are git-ignored. Backups taken before the upgrade, or while legacy accounts remain, contain plain text passwords: encrypt them (SQL Server backup encryption or an encrypted volume), restrict access and delete old ones | Documented |
| Legacy upgrade altering or exposing existing rows | The upgrade runs in one transaction under a checked `sp_getapplock` lock and refuses before any change when rows have no certain meaning; the refusal names counts only, never row contents | Mitigated |

## Connection security

| Threat | Mitigation | Status |
|---|---|---|
| Connection string leaked through the repository | `appsettings.json` is git-ignored; user secrets and the `FILM_CONNECTION` environment variable are supported; only `appsettings.example.json` is committed, without a real server name | Mitigated |
| Interception of the database connection | `TrustServerCertificate=True` in the example is for a local development server only; for any other server use `Encrypt=True` with a certificate the client trusts and leave `TrustServerCertificate` off | Documented |
| SQL injection | All access goes through EF Core with parameters; the raw statements are constant lock and schema statements built without user input | Mitigated |
| Excessive rights of the application's login | Use `docs/grant-app-login.sql`: no DDL, no deleted rentals, restricted key table rights. Do the first start and every migration with the owner's connection; prefer Windows authentication | Documented |

## KVKK

- Personal data (user name, e-mail address, phone number, rentals) is kept only in the configured SQL Server
  database and never written to log files; passwords are never shown or read back.
- The application has no retention feature; returned rentals stay as history and accounts with rentals cannot be
  deleted from the application. The operator deletes data in the database when required.

## Residual risks

- Accounts migrated from the original application keep their plain text password until their next successful
  sign-in; an admin can force the change by setting a new password for those accounts.
- The sign-up form tells whether a user name, e-mail address or phone number is taken (accepted above).
- On a Windows account shared by several application users, each of them can read when sign-ins, sign-ups and
  rentals happened from the log file, though not by whom.

## Out of scope

- Payments (the rental screen takes none), network services, web access, multi-factor authentication, encryption
  of data at rest inside the application, automated backups and retention policies, and a per-user audit trail of
  who read which record.
