import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';

// Lets the user override a book's series title/position, or force which
// linked series is treated as primary, when the metadata provider gets it
// wrong (junk boxset titles, ASINs baked into series names, mismatched
// series links, etc). Overrides are saved directly via the API and are not
// part of the standard book edit form/save flow.
class SeriesLinkEditor extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isFetching: false,
      isPopulated: false,
      error: null,
      isSaving: false,
      saveError: null,
      saveSuccess: false,
      links: []
    };
  }

  componentDidMount() {
    this.fetchLinks();
  }

  //
  // Control

  fetchLinks() {
    const {
      bookId
    } = this.props;

    this.setState({ isFetching: true, error: null });

    const { request } = createAjaxRequest({
      url: `/series/link?bookIds=${bookId}`,
      method: 'GET',
      dataType: 'json'
    });

    request.done((data) => {
      this.setState({
        isFetching: false,
        isPopulated: true,
        links: data
      });
    });

    request.fail((xhr) => {
      this.setState({
        isFetching: false,
        isPopulated: false,
        error: xhr
      });
    });
  }

  updateLink(index, changes) {
    this.setState((prevState) => {
      const links = [...prevState.links];
      links[index] = { ...links[index], ...changes };

      return { links, saveSuccess: false };
    });
  }

  //
  // Listeners

  onTitleOverrideChange = ({ value }, index) => {
    this.updateLink(index, { titleOverride: value === '' ? null : value });
  };

  onPositionOverrideChange = ({ value }, index) => {
    this.updateLink(index, { positionOverride: value === '' ? null : value });
  };

  onPrimaryOverrideChange = (event, index) => {
    const raw = event.target.value;
    const isPrimaryOverride = raw === '' ? null : raw === 'true';

    this.updateLink(index, { isPrimaryOverride });
  };

  onSavePress = () => {
    const {
      links
    } = this.state;

    this.setState({ isSaving: true, saveError: null, saveSuccess: false });

    const { request } = createAjaxRequest({
      url: '/series/link',
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify(links)
    });

    request.done((data) => {
      this.setState({
        isSaving: false,
        saveSuccess: true,
        links: data
      });
    });

    request.fail((xhr) => {
      this.setState({
        isSaving: false,
        saveError: xhr
      });
    });
  };

  //
  // Render

  render() {
    const {
      isFetching,
      isPopulated,
      error,
      links,
      isSaving,
      saveError,
      saveSuccess
    } = this.state;

    if (isFetching) {
      return <LoadingIndicator />;
    }

    if (error) {
      return (
        <Alert kind={kinds.DANGER}>
          Unable to load series links for this book
        </Alert>
      );
    }

    if (!isPopulated || !links.length) {
      return (
        <Alert kind={kinds.INFO}>
          This book is not linked to any series
        </Alert>
      );
    }

    return (
      <div>
        <p style={{ opacity: 0.8, marginBottom: 10 }}>
          Override the series title, position, or which linked series is used for renaming, when the metadata provider gets it wrong. Leave a field blank to fall back to the provider value. Overrides survive future metadata refreshes.
        </p>

        {
          links.map((link, index) => {
            const effectivePosition = link.effectivePosition;
            const effective = effectivePosition ? `${link.effectiveTitle} #${effectivePosition}` : link.effectiveTitle;

            return (
              <div
                key={link.id}
                style={{
                  border: '1px solid rgba(128, 128, 128, 0.4)',
                  borderRadius: 4,
                  padding: 10,
                  marginBottom: 10
                }}
              >
                <div style={{ fontWeight: 'bold', marginBottom: 8 }}>
                  {link.seriesTitle}
                  {
                    link.isPrimary &&
                      <span style={{ fontWeight: 'normal', opacity: 0.7 }}> (provider marks this primary)</span>
                  }
                </div>

                <div style={{ display: 'flex', gap: 10, marginBottom: 8, flexWrap: 'wrap' }}>
                  <label style={{ flex: '2 1 200px' }}>
                    Title override
                    <TextInput
                      name={`titleOverride-${index}`}
                      value={link.titleOverride || ''}
                      placeholder={link.seriesTitle || ''}
                      onChange={(payload) => this.onTitleOverrideChange(payload, index)}
                    />
                  </label>

                  <label style={{ flex: '1 1 100px' }}>
                    Position override
                    <TextInput
                      name={`positionOverride-${index}`}
                      value={link.positionOverride || ''}
                      placeholder={link.position || ''}
                      onChange={(payload) => this.onPositionOverrideChange(payload, index)}
                    />
                  </label>

                  <label style={{ flex: '1 1 150px' }}>
                    Use as primary series
                    <select
                      style={{ width: '100%', height: 30 }}
                      value={link.isPrimaryOverride === null || link.isPrimaryOverride === undefined ? '' : String(link.isPrimaryOverride)}
                      onChange={(event) => this.onPrimaryOverrideChange(event, index)}
                    >
                      <option value="">Automatic</option>
                      <option value="true">Force primary</option>
                      <option value="false">Force not primary</option>
                    </select>
                  </label>
                </div>

                <div style={{ fontSize: 12, opacity: 0.7 }}>
                  Will rename as: {effective}
                </div>
              </div>
            );
          })
        }

        {
          saveError &&
            <Alert kind={kinds.DANGER}>
              Unable to save series overrides
            </Alert>
        }

        {
          saveSuccess &&
            <Alert kind={kinds.SUCCESS}>
              Series overrides saved. Use Rename Files / Rename Author to apply them on disk.
            </Alert>
        }

        <SpinnerButton
          isSpinning={isSaving}
          onPress={this.onSavePress}
        >
          Save Series Overrides
        </SpinnerButton>
      </div>
    );
  }
}

SeriesLinkEditor.propTypes = {
  bookId: PropTypes.number.isRequired
};

export default SeriesLinkEditor;
