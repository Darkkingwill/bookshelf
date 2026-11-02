import migrateAddAuthorDefaults from './migrateAddAuthorDefaults';
import migrateAuthorSortKey from './migrateAuthorSortKey';
import migrateBlacklistToBlocklist from './migrateBlacklistToBlocklist';
import migrateBookshelfDefaults from './migrateBookshelfDefaults';

export default function migrate(persistedState) {
  migrateAddAuthorDefaults(persistedState);
  migrateAuthorSortKey(persistedState);
  migrateBlacklistToBlocklist(persistedState);
  migrateBookshelfDefaults(persistedState);
}
