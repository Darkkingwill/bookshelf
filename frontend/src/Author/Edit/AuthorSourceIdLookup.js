import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';

// Google Books has no real author id - GoogleBooksProvider.GetAuthorInfo treats the id as
// the author's exact name, so there's nothing to look up, just type the name directly.
const SOURCES_WITHOUT_LOOKUP = ['googlebooks'];

// Lets the user search the currently-selected Metadata Source by name and click a result to
// fill in the Foreign Author ID field above, instead of hand-finding and pasting a provider's
// id themselves. Each source has different id semantics under the hood (see
// AuthorMetadataSourceLookupController.cs on the backend), which this hides from the user -
// they just see name-based results either way.
class AuthorSourceIdLookup extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      term: props.authorName || '',
      isSearching: false,
      hasSearched: false,
      error: false,
      results: []
    };
  }

  componentDidUpdate(prevProps) {
    if (prevProps.metadataSource !== this.props.metadataSource) {
      this.setState({ hasSearched: false, results: [], error: false });
    }
  }

  //
  // Listeners

  onTermChange = ({ value }) => {
    this.setState({ term: value });
  };

  onSearchPress = () => {
    const { metadataSource } = this.props;
    const { term } = this.state;

    if (!term.trim()) {
      return;
    }

    this.setState({ isSearching: true, hasSearched: false, error: false, results: [] });

    const { request } = createAjaxRequest({
      url: `/author/lookup/source?source=${encodeURIComponent(metadataSource)}&term=${encodeURIComponent(term)}`,
      method: 'GET',
      dataType: 'json'
    });

    request.done((data) => {
      this.setState({
        isSearching: false,
        hasSearched: true,
        results: data
      });
    });

    request.fail(() => {
      this.setState({
        isSearching: false,
        hasSearched: true,
        error: true
      });
    });
  };

  onResultPress = (foreignAuthorId) => {
    this.props.onSelect(foreignAuthorId);
    this.setState({ results: [], hasSearched: false });
  };

  //
  // Render

  render() {
    const { metadataSource } = this.props;
    const { term, isSearching, hasSearched, error, results } = this.state;

    if (!metadataSource || SOURCES_WITHOUT_LOOKUP.includes(metadataSource)) {
      return null;
    }

    return (
      <div style={{ marginTop: 6 }}>
        <div style={{ display: 'flex', gap: 8 }}>
          <div style={{ flex: 1 }}>
            <TextInput
              name="authorSourceLookupTerm"
              value={term}
              placeholder="Name to search for"
              onChange={this.onTermChange}
            />
          </div>

          <SpinnerButton
            isSpinning={isSearching}
            onPress={this.onSearchPress}
          >
            Look Up ID
          </SpinnerButton>
        </div>

        {
          error &&
            <Alert kind={kinds.DANGER} style={{ marginTop: 8 }}>
              Lookup failed - the provider may be unreachable or misconfigured.
            </Alert>
        }

        {
          hasSearched && !error && !results.length &&
            <Alert kind={kinds.WARNING} style={{ marginTop: 8 }}>
              No matches found for that name on this source.
            </Alert>
        }

        {
          !!results.length &&
            <div
              style={{
                marginTop: 8,
                border: '1px solid rgba(128, 128, 128, 0.4)',
                borderRadius: 4,
                maxHeight: 220,
                overflowY: 'auto'
              }}
            >
              {
                results.map((result, index) => {
                  return (
                    <div
                      key={`${result.foreignAuthorId}-${index}`}
                      onClick={() => this.onResultPress(result.foreignAuthorId)}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 10,
                        padding: '6px 10px',
                        cursor: 'pointer',
                        borderBottom: index === results.length - 1 ? 'none' : '1px solid rgba(128, 128, 128, 0.2)'
                      }}
                    >
                      {
                        result.imageUrl &&
                          <img
                            src={result.imageUrl}
                            style={{ width: 24, height: 24, objectFit: 'cover', borderRadius: 2 }}
                          />
                      }

                      <span style={{ flex: 1 }}>{result.name}</span>

                      <span style={{ opacity: 0.6, fontSize: 12 }}>{result.foreignAuthorId}</span>
                    </div>
                  );
                })
              }
            </div>
        }
      </div>
    );
  }
}

AuthorSourceIdLookup.propTypes = {
  metadataSource: PropTypes.string,
  authorName: PropTypes.string,
  onSelect: PropTypes.func.isRequired
};

export default AuthorSourceIdLookup;
