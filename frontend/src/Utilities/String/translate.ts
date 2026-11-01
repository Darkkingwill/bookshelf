import createAjaxRequest from 'Utilities/createAjaxRequest';

function getTranslations() {
  return createAjaxRequest({
    global: false,
    dataType: 'json',
    url: '/localization',
  }).request;
}

let translations: Record<string, string> = {};

export async function fetchTranslations(): Promise<boolean> {
  return new Promise(async (resolve) => {
    try {
      const data = await getTranslations();
      translations = data.Strings;

      resolve(true);
    } catch (error) {
      resolve(false);
    }
  });
}

export default function translate(
  key: string,
  tokens: Record<string, string | number | boolean> = {}
) {
  const { isProduction = true } = window.Readarr;

  if (!isProduction && !(key in translations)) {
    console.warn(`Missing translation for key: ${key}`);
  }

  const translation = translations[key] || key;

  tokens.appName = 'Bookshelf';

  // Fallback to the old behaviour for translations not yet updated to use named tokens
  Object.values(tokens).forEach((value, index) => {
    tokens[index] = value;
  });

  // The server's language files still say "Readarr" in about 50 strings; show the product's real name until those
  // files are updated.
  return translation
    .replace(/\{([a-z0-9]+?)\}/gi, (match, tokenMatch) =>
      String(tokens[tokenMatch] ?? match)
    )
    .replace(/Readarr/g, 'Bookshelf');
}
