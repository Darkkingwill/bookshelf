import { get, set } from 'lodash';

// Bookshelf changed a few view defaults (cards no longer repeat "Monitored" and the quality profile, the book table
// shows Series and an estimated Length instead of Pages, and the always-empty "Formats" column is hidden). Browsers that
// already saved the old defaults would never see that, so reset just those options once per browser. The marker lives in
// its own localStorage key because the persisted-state key is rewritten from a whitelist on every save and would lose
// it. Bump VERSION when the list below changes.
const MARKER_KEY = 'bookshelfDefaultsVersion';
const VERSION = '2';

const COLUMN_VISIBILITY = {
  'books.columns': { series: true, length: true, pageCount: false },
  'history.columns': { customFormats: false },
  'queue.columns': { customFormats: false },
  'blocklist.columns': { customFormats: false }
};

export default function migrateBookshelfDefaults(persistedState) {
  if (localStorage.getItem(MARKER_KEY) === VERSION) {
    return;
  }

  if (persistedState) {
    if (get(persistedState, 'bookIndex.posterOptions')) {
      set(persistedState, 'bookIndex.posterOptions.showMonitored', false);
      set(persistedState, 'bookIndex.posterOptions.showQualityProfile', false);
    }

    Object.keys(COLUMN_VISIBILITY).forEach((path) => {
      const columns = get(persistedState, path);

      if (Array.isArray(columns)) {
        columns.forEach((column) => {
          if (column.name in COLUMN_VISIBILITY[path]) {
            column.isVisible = COLUMN_VISIBILITY[path][column.name];
          }
        });
      }
    });
  }

  localStorage.setItem(MARKER_KEY, VERSION);
}
