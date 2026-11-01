import { get, set } from 'lodash';

// Bookshelf changed a few view defaults (cards no longer repeat "Monitored" and the quality profile, the book table
// shows Series and an estimated Length instead of Pages). Browsers that already saved the old defaults would never see
// that, so reset just those options once per browser. The marker lives in its own localStorage key because the
// persisted-state key is rewritten from a whitelist on every save and would lose it.
const MARKER_KEY = 'bookshelfDefaultsVersion';
const VERSION = '1';

export default function migrateBookshelfDefaults(persistedState) {
  if (localStorage.getItem(MARKER_KEY) === VERSION) {
    return;
  }

  if (persistedState) {
    if (get(persistedState, 'bookIndex.posterOptions')) {
      set(persistedState, 'bookIndex.posterOptions.showMonitored', false);
      set(persistedState, 'bookIndex.posterOptions.showQualityProfile', false);
    }

    const columns = get(persistedState, 'books.columns');

    if (Array.isArray(columns)) {
      const visibility = { series: true, length: true, pageCount: false };

      columns.forEach((column) => {
        if (column.name in visibility) {
          column.isVisible = visibility[column.name];
        }
      });
    }
  }

  localStorage.setItem(MARKER_KEY, VERSION);
}
