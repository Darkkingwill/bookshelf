# bookshelf

This is a revival of [Readarr](https://github.com/Readarr/Readarr). The images
published are configured to use working Goodreads or Hardcover metadata out of
the box.

Bookshelf is an ebook and audiobook collection manager for Usenet and BitTorrent
users. It can monitor multiple RSS feeds for new books from your favorite
authors and will grab, sort, and rename them. Note that only one type of a
given book is supported. If you want both an audiobook and ebook of a given
book you will need multiple instances.

## Getting Started

The container listens on port 8787 and expects a volume mounted at `/config`.

    docker run -p 8787:8787 -v ~/.config/bookshelf:/config ghcr.io/darkkingwill/bookshelf:hardcover

The `softcover` tags use [Goodreads](https://www.goodreads.com) as the metadata
provider. The quality of this metadata is generally poor and contains a lot of
slop. However, it is backward-compatible with existing Readarr databases and
functionality like Goodreads list imports should continue to work normally.

The `hardcover` tags use [Hardcover](https://hardcover.app/home) as a metadata
provider. This metadata is higher quality but isn't backward-compatible; if
you're already running Readarr you'll need to redeploy this from scratch.
Goodreads list imports haven't been tested and likely don't work.

### Metadata search providers

Settings → Metadata now includes a configurable list of fallback metadata
search providers (Google Books, Open Library, Hardcover, rreading-glasses,
Audible, or a custom Audiobookshelf-compatible endpoint). When the primary
search fails or returns nothing, these are tried in priority order when
searching for a new author or book to add. Google Books and Open Library
work with no configuration; the others need an API key/URL, entered
directly in the settings UI.

### Per-book series overrides

If a metadata provider links a book to the wrong series, or a series title
is junk (e.g. a boxset entry with an ASIN baked into the name), you can
override the series title, position, or which linked series is treated as
primary from that book's Edit modal, without waiting on the provider to fix
its data.

### Convert to M4B

A book's toolbar has a **Convert to M4B** button (next to Preview Rename /
Preview Retag) that merges every file currently on disk for that book into a
single chaptered `.m4b`, with cover art embedded and chapter titles taken
from the original filenames. ffmpeg is already bundled in the Docker image,
no extra setup needed.

- If the audiobook came as multiple parts, import the download normally
  first so all the part-files show up under the book, then run the
  conversion - it merges whatever files are currently attached to the book,
  in track order.
- This is destructive: once the merge succeeds, the original files are
  deleted and replaced by the new `.m4b`. You'll get a confirmation prompt
  first.
- If your download client hardlinks into the library (the default for most
  setups), deleting the original files here only removes bookshelf's link to
  them - the copy your download client is still seeding is a separate,
  independent hardlink and is untouched. It's safe to convert a book while
  its download is still seeding.
- Before doing the (potentially long) encode, bookshelf checks that it can
  actually write to every folder involved and fails immediately with no
  changes made if it can't, rather than finishing a long encode and then
  failing partway through cleanup.

### MyAnonaMouse narrator & series

The built-in MyAnonaMouse indexer includes narrator and series information
(when MAM provides it) directly in release titles, so you can tell them
apart in the interactive search results without needing to open each
release on MAM's site.

## Support

This project won't use Discord for support. If you have a problem please file
an issue or start a discussion.

## Contributors & Developers

Help is very welcome. Priority is on fixing quality of life issues

- [ ] Monitor series.
- [ ] Support ebook and audio files in the same root.

Already done

- [x] Native support for MyAnonaMouse without Prowlarr, including narrator
      and series info in release titles.
- [x] Convert multi-file audiobooks to a single chaptered M4B, with cover
      art, from the book page.
- [x] Hardcover list import.
- [x] Improved matching.
- [x] Metadata is no longer cached locally.
- [x] Removed servarr analytics spyware.
- [x] Supports selfhosted metadata (UI or `METADATA_URL` env var).
- [x] Goodreads requests are rate-limited with automatic backoff, and a
      health check flags it if the API key stops working.

## Sponsors

If you ever donated to [this](https://opencollective.com/readarr) project you
should request a refund. Those people don't deserve your money.

### License

The is a derivative work of the [Readarr](https://github.com/Readarr/Readarr)
and [Prowlarr](https://github.com/Prowlarr/Prowlarr) projects which are both
licensed [GPLv3](http://www.gnu.org/licenses/gpl.html). This project is
therefore also licensed under the terms of GPLv3.

Copyright 2025-2026
