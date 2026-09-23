import createAjaxRequest from 'Utilities/createAjaxRequest';

// Sources a lookup can be pinned to: Goodreads (direct client) plus every enabled metadata
// provider from Settings > Metadata. The key is what the search API takes as `source`.
export default function fetchSearchSources() {
  return new Promise((resolve) => {
    const { request } = createAjaxRequest({
      url: '/config/metadatasource',
      dataType: 'json'
    });

    request.then((providers) => {
      resolve([
        { key: 'goodreads', name: 'Goodreads' },
        ...providers
          .filter((p) => p.enabled)
          .map((p) => ({ key: p.key, name: p.displayName }))
      ]);
    }).fail(() => {
      resolve([{ key: 'goodreads', name: 'Goodreads' }]);
    });
  });
}
